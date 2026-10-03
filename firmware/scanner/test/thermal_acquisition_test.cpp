// Native regression: link ThermalSensor + device + Melexis maths with thermal_stubs.
#include "../src/thermal_mlx90640.h"
#include "../include/thermal_eeprom_diagnostics.h"
#include "fixtures/thermal_real_eeprom.h"
#include <cassert>
#include <cstdio>
#include <cstring>
#include <limits>

static bool validBit(const uint8_t *mask, unsigned pixel) {
    return (mask[pixel >> 3] & (1U << (pixel & 7U))) != 0;
}

static void realFixture(int extraMaskedPixel = -1) {
    Wire.fixture();
    uint16_t raw[832]; uint8_t validity[96] = {};
    std::memcpy(raw, RealThermalEeprom, sizeof raw);
    if (extraMaskedPixel >= 0) raw[64 + extraMaskedPixel] = 0xFFFF;
    for (unsigned i = 0; i < 832; ++i) Wire.registers[0x2400+i] = raw[i];
    for (unsigned p = 0; p < 768; ++p)
        if (raw[64+p] && !(raw[64+p] & 1U)) validity[p >> 3] |= uint8_t(1U << (p & 7U));
    ThermalMlxMath::paramsMLX90640 params{};
    // Construct RAM corresponding approximately to ambient 25 C using the real
    // immutable coefficients, not invented calibration. This is synthetic RAM.
    assert(ThermalMlxMath::MLX90640_ExtractParameters(raw, &params, validity) == 0);
    assert(ThermalMlxMath::MLX90640_ValidGlobalParameters(&params));
    Wire.registers[0x0400+778] = uint16_t(params.gainEE);
    Wire.registers[0x0400+810] = uint16_t(params.vdd25);
    const float ptat = 1000;
    Wire.registers[0x0400+800] = uint16_t(ptat);
    Wire.registers[0x0400+768] = uint16_t(std::lround(
        ptat * 262144.f / params.vPTAT25 - ptat * params.alphaPTAT));
    Wire.registers[0x0400+776] = uint16_t(params.cpOffset[0]);
    Wire.registers[0x0400+808] = uint16_t(params.cpOffset[1]);
    for (unsigned p = 0; p < 768; ++p)
        Wire.registers[0x0400+p] = uint16_t(params.offset[p] + int(p % 17U) * 20);
}

static bool samplePixel(ThermalSensor &sensor, float col, float row, float &temperature) {
    float horizontal = THERMAL_SENSOR_FOV_Y_DEG*.5f - row*THERMAL_SENSOR_FOV_Y_DEG/23.f;
    float vertical = THERMAL_SENSOR_FOV_X_DEG*.5f - col*THERMAL_SENSOR_FOV_X_DEG/31.f;
    return sensor.tryGetPointTemperature(horizontal, vertical, temperature);
}

static bool completeFrame(ThermalSensor &sensor) {
    Wire.ready(0);
    assert(!sensor.updateFrame());
    fakeMillis += 125; Wire.ready(1);
    return sensor.updateFrame();
}

uint32_t fakeMillis = 1000;
FakeSerial Serial;
TwoWire Wire;

static void validateAssembler() {
    using ThermalAcquisition::FrameAssembler;
    FrameAssembler pair;
    float pixels[768];
    for (float &value : pixels) value = NAN;
    bool duplicate;
    assert(!pair.accept(0, 100, duplicate) && !duplicate && pair.mask() == 1);
    for (unsigned i = 0; i < 768; ++i) if (((i / 32) ^ i) % 2 == 0) pixels[i] = 20;
    assert(!FrameAssembler::finiteFrame(pixels));
    assert(!pair.accept(0, 400, duplicate) && duplicate && pair.mask() == 1);
    assert(pair.expired(601)); // A repeated half never extends the original deadline.
    pair.reset();
    for (float &value : pixels) value = NAN;
    assert(!pair.accept(1, 602, duplicate) && pair.mask() == 2);
    for (unsigned i = 0; i < 768; ++i) if (((i / 32) ^ i) % 2 == 1) pixels[i] = 21;
    assert(!FrameAssembler::finiteFrame(pixels)); // Expired page0 cannot leak into page1.
    assert(pair.accept(0, 727, duplicate));
    for (unsigned i = 0; i < 768; ++i) if (((i / 32) ^ i) % 2 == 0) pixels[i] = 22;
    assert(FrameAssembler::finiteFrame(pixels) && pair.spanMs() == 125 && pair.timestampMs() == 664);
    pixels[321] = NAN;
    assert(!FrameAssembler::finiteFrame(pixels));
    uint8_t validity[96]; std::memset(validity, 0xFF, sizeof validity);
    validity[321 >> 3] &= uint8_t(~(1U << (321 & 7U)));
    assert(FrameAssembler::finiteFrame(pixels, validity));
    pixels[321] = 0;
    assert(!FrameAssembler::finiteFrame(pixels, validity)); // A masked pixel must remain absent, not fabricated.
    pixels[321] = NAN; pixels[0] = NAN;
    assert(!FrameAssembler::finiteFrame(pixels, validity)); // Missing good pixel is a broken complete frame.
    std::memset(validity, 0, sizeof validity);
    for (float &value : pixels) value = NAN;
    assert(!FrameAssembler::finiteFrame(pixels, validity)); // No usable measurements cannot form a frame.
    pair.reset();
    assert(!pair.accept(0, UINT32_MAX - 40, duplicate));
    assert(pair.accept(1, 59, duplicate) && pair.spanMs() == 100 && pair.timestampMs() == 9);
    assert(!pair.accept(2, 60, duplicate));
}

static void validateEepromSummary() {
    uint16_t first[832] = {}, second[832] = {};
    auto zero = ThermalEepromDiagnostics::summarize(first);
    assert(zero.crc32 == 0xBFEF95C0U && zero.broken == 768 && zero.outlier == 0);
    for (unsigned i = 0; i < 832; ++i) first[i] = second[i] = uint16_t(i);
    auto sequential = ThermalEepromDiagnostics::summarize(first);
    assert(sequential.crc32 == 0x243C5ED5U && sequential.outlier == 384 && sequential.broken == 0);
    assert(ThermalEepromDiagnostics::differences(first, second) == 0);
    second[75] = 0;
    auto changed = ThermalEepromDiagnostics::summarize(second);
    assert(changed.crc32 != sequential.crc32 && changed.broken == 1 && changed.outlier == 383);
    assert(ThermalEepromDiagnostics::differences(first, second) == 1);
}

static void validateDriverBounds() {
    Wire.fixture();
    ThermalMlxDevice device;
    assert(device.begin(Wire, 4) == 0 && Wire.timeout == 25);
    assert(!device.partialCalibration() && !device.maskedPixelCount() && !device.calibrationWarning());
    for (unsigned p = 0; p < 768; ++p) assert(validBit(device.validityMask(), p));
    float frame[768];
    for (float &value : frame) value = NAN;
    uint8_t page = 0; uint32_t stamp = 0;
    uint32_t requests = Wire.requests, started = fakeMillis;
    assert(device.readSubpage(frame, page, stamp) == ThermalAcquisition::NotReady);
    assert(Wire.requests == requests + 1 && fakeMillis - started == 1); // No ready polling loop.
    Wire.ready(0); Wire.continuousReady = true;
    requests = Wire.requests;
    assert(device.readSubpage(frame, page, stamp) == -8);
    assert(Wire.requests - requests <= 28); // One RAM read, never the old 5 retries.
    Wire.continuousReady = false; Wire.requestMs = 20; Wire.ready(0);
    started = fakeMillis;
    assert(device.readSubpage(frame, page, stamp) == ThermalAcquisition::ReadDeadline);
    assert(fakeMillis - started <= 325); // Total subpage I/O bound, not per-block only.
    Wire.requestMs = 1; Wire.shortRead = true; Wire.ready(0);
    assert(device.readSubpage(frame, page, stamp) == -1 && Wire.available() == 0);
    Wire.fixture(); Wire.unreportedShortRead = true;
    assert(device.begin(Wire, 4) == -103 && Wire.available() == 0);
    Wire.fixture(); Wire.negativeReadAt = 4;
    assert(device.begin(Wire, 4) == -103 && Wire.available() == 0);
    Wire.fixture();
    for (unsigned i = 0; i < 832; ++i) Wire.registers[0x2400 + i] = 0;
    assert(device.begin(Wire, 4) == ThermalAcquisition::InvalidCalibration);
    Wire.fixture(); Wire.registers[0x2400 + 10] |= 0x0040; // MLX90641 must not use the 90640 calibration map.
    assert(device.begin(Wire, 4) == ThermalAcquisition::InvalidCalibration);
    Wire.fixture(); Wire.registers[0x2400 + 48] = 0; // Missing global gain cannot be masked as a pixel.
    assert(device.begin(Wire, 4) == ThermalAcquisition::InvalidCalibration);
    Wire.fixture(); Wire.registers[0x2400 + 57] = 1000; // Negative CP sensitivity remains invalid.
    assert(device.begin(Wire, 4) == ThermalAcquisition::InvalidCalibration);
    Wire.fixture(); Wire.registers[0x2400 + 61] = 0x1000; // Invalid temperature-range denominator remains invalid.
    assert(device.begin(Wire, 4) == ThermalAcquisition::InvalidCalibration);
    Wire.fixture();
    Wire.registers[0x2400 + 64 + 37] = 0; // One factory-listed broken pixel is supported.
    assert(device.begin(Wire, 4) == 0);
    Wire.ready(0);
    assert(device.readSubpage(frame, page, stamp) == 0);
    Wire.ready(1);
    assert(device.readSubpage(frame, page, stamp) == 0);
    frame[37] = NAN;
    device.correctBadPixels(frame);
    assert(ThermalAcquisition::FrameAssembler::finiteFrame(frame));
    Wire.registers[0x2400 + 64 + 38] = 0; // Adjacent broken pixels remain absent in partial mode.
    Wire.clock = 400000;
    uint32_t clockBeforeDiagnostic = Wire.clock;
    assert(device.begin(Wire, 4) == 0 && device.partialCalibration() && device.maskedPixelCount() == 2);
    assert(Wire.clock == clockBeforeDiagnostic); // A slower diagnostic read cannot change the shared runtime bus.

    Wire.fixture(); Wire.clock = 400000;
    for (unsigned i : {41U, 69U, 97U, 125U, 153U, 181U}) Wire.registers[0x2400 + i] = 0xFFFF;
    ThermalMlxDevice corrupted;
    assert(corrupted.begin(Wire, 4) == 0 && corrupted.partialCalibration() && corrupted.calibrationWarning() == -4);
    unsigned smallerTransfers = 0;
    for (uint8_t size : Wire.requestSizes) {
        if (size == 16) ++smallerTransfers;
    }
    assert(smallerTransfers >= 104); // Partial acquisition requires the independent small-chunk reread.
    assert(Wire.clock == 400000 && Wire.eepromWrites == 0);
    for (unsigned i : {41U, 69U, 97U, 125U, 153U, 181U}) assert(Wire.registers[0x2400 + i] == 0xFFFF);
}

static void validateAutomaticSubpageControl() {
    // Figure 12 control fields: alternate subpages (B0=1, hold/repeat/selection
    // cleared), chess B12, ADC B11..10=2, refresh B9..7=4. Preserve reserved
    // B1 and B13..15, including the actual boot configuration 0x7F39.
    for (uint16_t initial : {uint16_t(0x7F39), uint16_t(0x1000), uint16_t(0xFFFF), uint16_t(0xE002), uint16_t(0)}) {
        Wire.fixture(); Wire.registers[0x800D] = initial;
        ThermalMlxDevice device;
        assert(device.begin(Wire, 4) == 0);
        const uint16_t expected = uint16_t((initial & 0xE002U) | 0x1A01U);
        assert(Wire.registers[0x800D] == expected);
        assert((Wire.registers[0x800D] & 0xE002U) == (initial & 0xE002U));
        if (initial == 0x7F39) assert(Wire.registers[0x800D] == 0x7A01);
        assert(device.setRefreshRate(3) == 0);
        assert(Wire.registers[0x800D] == uint16_t((expected & ~0x0380U) | (3U << 7)));
        assert((Wire.registers[0x800D] & 0x007DU) == 1U); // Rate fallback cannot re-enable repeat or hold.
        float frame[768]; for (float &value : frame) value = NAN;
        uint8_t page; uint32_t stamp; bool duplicate;
        ThermalAcquisition::FrameAssembler pair;
        pair.setDeadline(900);
        Wire.registers[0x8000] = 1; // Last converted page1 before configuration.
        assert(Wire.simulateConversion());
        assert(device.readSubpage(frame, page, stamp) == 0 && page == 0);
        assert(!pair.accept(page, stamp, duplicate) && !duplicate);
        fakeMillis += 250;
        assert(Wire.simulateConversion());
        assert(device.readSubpage(frame, page, stamp) == 0 && page == 1);
        assert(pair.accept(page, stamp, duplicate) && !duplicate && pair.mask() == 3);
        device.correctBadPixels(frame);
        assert(ThermalAcquisition::FrameAssembler::finiteFrame(frame, device.validityMask()));
    }

    // Re-enabling repeat after initialization must be rejected immediately,
    // before a half-frame can become a published measurement.
    realFixture(); Wire.registers[0x800D] = 0x7F39;
    ThermalSensor sensor;
    assert(sensor.begin() && Wire.registers[0x800D] == 0x7A01);
    Wire.registers[0x800D] |= 0x0018U;
    fakeMillis += 125;
    assert(Wire.simulateConversion() && (Wire.registers[0x8000] & 1U) == 1);
    assert(!sensor.updateFrame());
    const auto invalidControl = sensor.acquisitionHealth();
    assert(!std::strcmp(invalidControl.state, "invalid_configuration") &&
        invalidControl.rawError == ThermalAcquisition::InvalidConfiguration &&
        !invalidControl.subpageMask && !invalidControl.frameReady && !invalidControl.duplicateSubpages);
    assert(sensor.begin() && Wire.registers[0x800D] == 0x7A01);

    // Fault injection: a stalled sensor reporting only page1 despite correct
    // control bits still cannot form a frame. These forced ready IDs model the
    // fault, while healthy pairs above/below use control-driven conversion.
    for (unsigned i = 0; i < 3; ++i) {
        fakeMillis += 125;
        Wire.ready(1);
        assert(!sensor.updateFrame());
    }
    auto repeated = sensor.acquisitionHealth();
    assert(repeated.subpageMask == 2 && repeated.duplicateSubpages == 2 && !repeated.frameReady);
    Wire.registers[0x800D] = uint16_t((Wire.registers[0x800D] & ~1U) | 4U);
    assert(!Wire.simulateConversion()); // Disabled or held output cannot generate the missing page.
    assert(sensor.begin() && (Wire.registers[0x800D] & 0x007DU) == 1U);
    fakeMillis += 125; assert(Wire.simulateConversion());
    assert((Wire.registers[0x8000] & 1U) == 0 && !sensor.updateFrame());
    fakeMillis += 125; assert(Wire.simulateConversion());
    assert((Wire.registers[0x8000] & 1U) == 1 && sensor.updateFrame());
    const auto restored = sensor.acquisitionHealth();
    assert(restored.frameReady && restored.partialCalibration && restored.maskedPixels == 18);
}

static void validateMaskedNormalizationInvariant() {
    uint16_t original[832], altered[832]; uint8_t validity[96] = {};
    std::memcpy(original, RealThermalEeprom, sizeof original);
    std::memcpy(altered, RealThermalEeprom, sizeof altered);
    unsigned absent = 0;
    for (unsigned p = 0; p < 768; ++p) {
        if (!(original[64+p] & 1U)) validity[p >> 3] |= uint8_t(1U << (p & 7U));
        else {
            // Arbitrary missing coefficients, even values with their factory flag
            // cleared, cannot affect normalization when the explicit mask excludes them.
            const uint16_t replacements[] = {0, 0xFFFF, 0xFFFE};
            altered[64+p] = replacements[absent++ % 3];
        }
    }
    assert(absent == 18);
    ThermalMlxMath::paramsMLX90640 before{}, after{};
    assert(ThermalMlxMath::MLX90640_ExtractParameters(original, &before, validity) == 0);
    assert(ThermalMlxMath::MLX90640_ExtractParameters(altered, &after, validity) == 0);
    assert(before.alphaScale == after.alphaScale && before.ktaScale == after.ktaScale && before.kvScale == after.kvScale);
    for (unsigned p = 0; p < 768; ++p) {
        assert(before.alpha[p] == after.alpha[p] && before.offset[p] == after.offset[p] &&
            before.kta[p] == after.kta[p] && before.kv[p] == after.kv[p]);
        if (!validBit(validity, p))
            assert(!after.alpha[p] && !after.offset[p] && !after.kta[p] && !after.kv[p]);
    }
    for (unsigned i = 0; i < 5; ++i)
        assert(before.brokenPixels[i] == 0xFFFF && before.outlierPixels[i] == 0xFFFF &&
            after.brokenPixels[i] == 0xFFFF && after.outlierPixels[i] == 0xFFFF);
    assert(!std::memcmp(original, RealThermalEeprom, sizeof original));
}

static void validatePartialRealEeprom() {
    realFixture();
    const auto initial = ThermalEepromDiagnostics::summarize(RealThermalEeprom);
    assert(initial.crc32 == 0x127F0F51U && initial.outlier == 18 && !initial.broken);
    ThermalMlxDevice device;
    assert(device.begin(Wire, 4) == 0 && device.partialCalibration() &&
        device.maskedPixelCount() == 18 && device.calibrationWarning() == -4);
    unsigned usable = 0;
    for (unsigned p = 0; p < 768; ++p) {
        bool expected = (RealThermalEeprom[64+p] & 1U) == 0;
        assert(validBit(device.validityMask(), p) == expected);
        usable += expected;
    }
    assert(usable == 750 && Wire.eepromWrites == 0);
    float frame[768]; for (float &value : frame) value = NAN;
    uint8_t page; uint32_t stamp;
    Wire.ready(0); assert(device.readSubpage(frame, page, stamp) == 0);
    fakeMillis += 125; Wire.ready(1); assert(device.readSubpage(frame, page, stamp) == 0);
    assert(ThermalAcquisition::FrameAssembler::finiteFrame(frame, device.validityMask()));
    float original[768]; std::memcpy(original, frame, sizeof frame);
    device.correctBadPixels(frame); // The partial path must never interpolate the absent 18 measurements.
    for (unsigned p = 0; p < 768; ++p) {
        if (validBit(device.validityMask(), p)) assert(isfinite(frame[p]) && frame[p] == original[p]);
        else assert(isnan(frame[p]) && isnan(original[p]));
    }
    uint16_t stored[832];
    for (unsigned i = 0; i < 832; ++i) stored[i] = Wire.registers[0x2400+i];
    assert(!std::memcmp(stored, RealThermalEeprom, sizeof stored) &&
        ThermalEepromDiagnostics::summarize(stored).crc32 == initial.crc32 && Wire.eepromWrites == 0);

    realFixture(); ThermalSensor sensor;
    assert(sensor.begin() && completeFrame(sensor));
    auto health = sensor.acquisitionHealth();
    assert(health.frameReady && health.partialCalibration && health.maskedPixels == 18 &&
        health.calibrationWarning == -4 && !std::strcmp(health.state, "ready_partial"));
    uint8_t normalized[768], mask[96]; float low, high;
    assert(!sensor.getNormalizedFrame(normalized, low, high)); // Legacy payload cannot hide missing pixels.
    assert(sensor.getNormalizedFrame(normalized, low, high, mask) && isfinite(low) && isfinite(high) && high > low);
    for (unsigned p = 0; p < 768; ++p) assert(validBit(mask, p) == ((RealThermalEeprom[64+p] & 1U) == 0));
    float temperature = -999;
    assert(!samplePixel(sensor, 4.5f, 0.f, temperature) && temperature == -999); // Pixel 5 contributes positive weight.
    assert(samplePixel(sensor, 0.f, 0.f, temperature) && isfinite(temperature)); // Exact border is usable.

    realFixture(0); ThermalSensor missingFirst;
    assert(missingFirst.begin() && completeFrame(missingFirst));
    assert(missingFirst.getNormalizedFrame(normalized, low, high, mask) &&
        !validBit(mask, 0) && isfinite(low) && isfinite(high) && high > low);
    assert(missingFirst.acquisitionHealth().maskedPixels == 19); // Min/max must not initialize from masked pixel zero.

    realFixture(1); ThermalSensor zeroWeight;
    assert(zeroWeight.begin() && completeFrame(zeroWeight));
    temperature = -999;
    assert(samplePixel(zeroWeight, 0.f, 0.f, temperature) && isfinite(temperature)); // Masked pixel 1 has exactly zero weight.
    temperature = -999;
    assert(!samplePixel(zeroWeight, .5f, 0.f, temperature) && temperature == -999);

    realFixture(); ThermalSensor unexpected;
    assert(unexpected.begin());
    Wire.registers[0x0400] = 0x8000; // Finite RAM producing an invalid radiance radicand in a GOOD pixel.
    assert(!completeFrame(unexpected));
    assert(unexpected.acquisitionHealth().invalidFrames == 1 && !unexpected.acquisitionHealth().frameReady &&
        !unexpected.getNormalizedFrame(normalized, low, high, mask));

    realFixture(); Wire.differingSmallEepromRead = true;
    ThermalMlxDevice disagreeing;
    assert(disagreeing.begin(Wire, 4) == ThermalAcquisition::InvalidCalibration);
    assert(Wire.eepromWrites == 0); // Changed transport data cannot be rehabilitated as bad physical pixels.
    realFixture(); Wire.registers[0x2400+48] = 0;
    ThermalMlxDevice missingGlobal;
    assert(missingGlobal.begin(Wire, 4) == ThermalAcquisition::InvalidCalibration);
    realFixture();
    for (unsigned p = 0; p < 39; ++p) Wire.registers[0x2400+64+p] = 0xFFFF;
    ThermalMlxDevice excessive;
    assert(excessive.begin(Wire, 4) == ThermalAcquisition::InvalidCalibration);
    assert(Wire.eepromWrites == 0);
}

static void validateSensorPublication() {
    Wire.fixture();
    ThermalSensor sensor;
    assert(sensor.begin());
    uint8_t normalized[768]; float low, high;
    assert(!sensor.getNormalizedFrame(normalized, low, high));
    Wire.ready(0);
    assert(!sensor.updateFrame());
    auto health = sensor.acquisitionHealth();
    assert(!health.frameReady && health.subpageMask == 1 && health.rawError == 0);
    fakeMillis += 125; Wire.ready(0);
    assert(!sensor.updateFrame() && sensor.acquisitionHealth().duplicateSubpages == 1);
    fakeMillis += 125; Wire.ready(1);
    assert(sensor.updateFrame());
    health = sensor.acquisitionHealth();
    assert(health.frameReady && health.subpageMask == 0 && health.frameSpanMs > 0);
    assert(sensor.getNormalizedFrame(normalized, low, high) && isfinite(low) && isfinite(high));
    int error; uint32_t frames, age;
    sensor.health(error, frames, age);
    assert(error == 0 && frames == 1 && age > 0); // Midpoint timestamp, not publication time.
    fakeMillis += 1001;
    assert(!sensor.acquisitionHealth().frameReady && !sensor.getNormalizedFrame(normalized, low, high));
    Wire.ready(0);
    assert(!sensor.updateFrame());
    fakeMillis += 501; Wire.ready(1);
    assert(!sensor.updateFrame());
    health = sensor.acquisitionHealth();
    assert(health.frameTimeouts == 1 && health.subpageMask == 2 && !health.frameReady);
    assert(sensor.begin()); // Reset must discard partial pages from the previous configuration.
    Wire.ready(1);
    assert(!sensor.updateFrame() && sensor.acquisitionHealth().subpageMask == 2);
    fakeMillis += 1100;
    assert(!sensor.updateFrame());
    sensor.health(error, frames, age);
    assert(error == ThermalAcquisition::FrameTimeout && sensor.isInitialized());
    Wire.failRead = true;
    for (unsigned i = 0; i < 5; ++i) assert(!sensor.updateFrame());
    health = sensor.acquisitionHealth();
    // The third transport failure triggers reconfiguration, which also fails
    // on this disconnected bus; it must stop acquisition for backed-off retry.
    assert(health.readErrors == 3 && health.refreshHz == 4 && health.i2cHz == 100000 && !sensor.isInitialized());
    Wire.fixture();
    assert(sensor.begin());
    fakeMillis += 1100;
    assert(!sensor.updateFrame() && sensor.isInitialized());
    fakeMillis += 4001;
    assert(!sensor.updateFrame() && !sensor.isInitialized());
    sensor.health(error, frames, age);
    assert(error == ThermalAcquisition::FrameTimeout);
}

static void validateInitializationFallback() {
    // Healthy boot keeps the fast rate and probes only after setting its clock.
    realFixture(); ThermalSensor healthy;
    assert(healthy.begin() && completeFrame(healthy));
    auto health = healthy.acquisitionHealth();
    assert(health.i2cHz == 400000 && health.refreshHz == 8 && !health.readErrors);
    assert(Wire.probeClocks == std::vector<uint32_t>{400000});
    assert((Wire.registers[0x800D] & 0x0380U) == (4U << 7));

    // Realistic fault: ACK succeeds but the primary EEPROM transfer at 400 kHz
    // fails. A fresh 100 kHz calibration read and independent verification must
    // both pass before publishing the same 750 usable/18 absent measurements.
    realFixture(); Wire.failFastEepromRead = true; ThermalSensor recovered;
    assert(recovered.begin());
    health = recovered.acquisitionHealth();
    assert(health.i2cHz == 100000 && health.refreshHz == 4 && health.readErrors == 1 && !health.rawError);
    assert((Wire.probeClocks == std::vector<uint32_t>{400000, 100000}));
    assert((Wire.eepromStartClocks == std::vector<uint32_t>{400000, 100000, 100000}));
    assert((Wire.registers[0x800D] & 0x0380U) == (3U << 7));
    Wire.ready(0); assert(!recovered.updateFrame());
    fakeMillis += 650; Wire.ready(1); // The slower pair uses its 900 ms deadline.
    assert(recovered.updateFrame());
    health = recovered.acquisitionHealth();
    assert(health.frameReady && health.partialCalibration && health.maskedPixels == 18 &&
        health.calibrationWarning == -4 && !std::strcmp(health.state, "ready_partial"));
    uint8_t normalized[768], mask[96]; float low, high;
    assert(!recovered.getNormalizedFrame(normalized, low, high));
    assert(recovered.getNormalizedFrame(normalized, low, high, mask));
    unsigned usable = 0;
    for (unsigned p = 0; p < 768; ++p) {
        assert(validBit(mask, p) == ((RealThermalEeprom[64+p] & 1U) == 0));
        usable += validBit(mask, p);
    }
    assert(usable == 750 && Wire.eepromWrites == 0);
    const size_t probes = Wire.probeClocks.size();
    assert(recovered.begin() && Wire.probeClocks.size() == probes + 1 && Wire.probeClocks.back() == 100000);
    assert(recovered.acquisitionHealth().readErrors == 1); // Successful retries do not erase the failed attempt.

    // Failure at both speeds is bounded, reports both failed attempts, and the
    // next externally delayed begin stays at 100 kHz rather than repeating fast.
    realFixture(); Wire.failFastEepromRead = Wire.failSlowEepromRead = true;
    ThermalSensor unavailable;
    assert(!unavailable.begin() && !unavailable.updateFrame());
    health = unavailable.acquisitionHealth();
    int error; uint32_t frames, age;
    unavailable.health(error, frames, age);
    assert(error == -103 && !frames && age == UINT32_MAX && !unavailable.isInitialized());
    assert(health.i2cHz == 100000 && health.refreshHz == 4 && health.readErrors == 2 &&
        health.rawError == -1 && !health.frameReady && !std::strcmp(health.state, "i2c_error"));
    assert((Wire.eepromStartClocks == std::vector<uint32_t>{400000, 100000}));
    fakeMillis += 2000;
    assert(!unavailable.begin() && unavailable.acquisitionHealth().readErrors == 3);
    assert((Wire.eepromStartClocks == std::vector<uint32_t>{400000, 100000, 100000}));

    // The same one-time fallback handles a failed fast ACK, a total read
    // deadline, a verification transfer, or configuration transport failure.
    for (unsigned fault = 0; fault < 4; ++fault) {
        realFixture();
        Wire.failFastProbe = fault == 0;
        Wire.fastEepromRequestMs = fault == 1 ? 20 : 0;
        Wire.failFastSmallEepromRead = fault == 2;
        Wire.failFastControlRead = fault == 3;
        const uint32_t started = fakeMillis;
        ThermalSensor alternateFault;
        assert(alternateFault.begin() && fakeMillis - started < 650);
        health = alternateFault.acquisitionHealth();
        assert(health.i2cHz == 100000 && health.refreshHz == 4 && health.readErrors == 1 &&
            health.partialCalibration && health.maskedPixels == 18 && !health.rawError);
        assert((Wire.probeClocks == std::vector<uint32_t>{400000, 100000}));
        assert(completeFrame(alternateFault) && Wire.eepromWrites == 0);
    }

    // Invalid values are not transport faults: reject global calibration,
    // inconsistent EEPROM snapshots, and register verification mismatch.
    for (unsigned invalid = 0; invalid < 3; ++invalid) {
        realFixture();
        if (invalid == 0) Wire.registers[0x2400 + 48] = 0;
        Wire.differingSmallEepromRead = invalid == 1;
        Wire.mismatchControlWrite = invalid == 2;
        ThermalSensor rejected;
        assert(!rejected.begin() && !rejected.updateFrame());
        health = rejected.acquisitionHealth();
        assert(health.i2cHz == 400000 && health.refreshHz == 8 && !health.readErrors && !health.frameReady);
        assert(Wire.probeClocks == std::vector<uint32_t>{400000} && Wire.eepromWrites == 0);
        rejected.health(error, frames, age);
        assert(error == (invalid == 2 ? ThermalAcquisition::InvalidConfiguration : ThermalAcquisition::InvalidCalibration));
        assert(health.rawError == (invalid == 2 ? -2 : ThermalAcquisition::InvalidCalibration));
    }

    // Runtime overruns lower the rate while retaining 400 kHz. Reinitialization
    // still needs transport fallback even though fastMode is already false.
    realFixture(); ThermalSensor rateReduced;
    assert(rateReduced.begin());
    Wire.continuousReady = true;
    for (unsigned i = 0; i < 3; ++i) {
        Wire.ready(i & 1U);
        assert(!rateReduced.updateFrame());
    }
    health = rateReduced.acquisitionHealth();
    assert(health.refreshHz == 4 && health.i2cHz == 400000 && health.overruns == 3 && !health.readErrors);
    Wire.continuousReady = false; Wire.failFastEepromRead = true;
    assert(rateReduced.begin() && rateReduced.acquisitionHealth().i2cHz == 100000 &&
        rateReduced.acquisitionHealth().refreshHz == 4 && rateReduced.acquisitionHealth().readErrors == 1);
    assert(completeFrame(rateReduced));
    puts("Thermal startup fallback PASS: fast boot retained, bounded 400/100 kHz transport retries, slow retries persist, honest counters, 750/18 mask preserved, calibration/configuration values still rejected.");
}

static void validatePhysicalEeprom(const char *path) {
    FILE *file = std::fopen(path, "rb");
    assert(file);
    uint8_t bytes[1664];
    assert(std::fread(bytes, 1, sizeof bytes, file) == sizeof bytes && std::fgetc(file) == EOF);
    std::fclose(file);
    uint16_t raw[832], original[832];
    for (unsigned i = 0; i < 832; ++i) raw[i] = original[i] = uint16_t((unsigned(bytes[i * 2]) << 8) | bytes[i * 2 + 1]);
    auto before = ThermalEepromDiagnostics::summarize(raw);
    ThermalMlxMath::paramsMLX90640 params = {};
    int extraction = ThermalMlxMath::MLX90640_ExtractParameters(raw, &params);
    auto after = ThermalEepromDiagnostics::summarize(raw);
    assert(before.crc32 == 0x127F0F51U && before.outlier == 18 && before.broken == 0);
    assert(extraction == -4 && after.crc32 == before.crc32 && std::memcmp(raw, original, sizeof raw) == 0);
    assert(raw[41] == 0xFFFF);
    std::printf("Physical EEPROM PASS: crc32=%08lX extraction=%d outliers=%u before/after byte-identical; header EE[41]=FFFF.\n",
        (unsigned long)before.crc32, extraction, (unsigned)before.outlier);
}

int main(int argc, char **argv) {
    validateEepromSummary();
    validateAssembler();
    validateDriverBounds();
    validateAutomaticSubpageControl();
    validateSensorPublication();
    validateMaskedNormalizationInvariant();
    validatePartialRealEeprom();
    validateInitializationFallback();
    if (argc == 2) validatePhysicalEeprom(argv[1]);
    puts("Thermal acquisition PASS: automatic control configuration and reserved bits, synthetic I2C subpages, repeated/expired halves, real EEPROM with 750 usable pixels and 18 absent pixels, masked normalization invariance, bilinear positive/zero weights, invalid good pixel and global calibration rejection, bounded I2C and immutable calibration.");
}
