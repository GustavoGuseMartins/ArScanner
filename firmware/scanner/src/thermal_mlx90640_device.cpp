#include "thermal_mlx90640_device.h"
#include "thermal_eeprom_diagnostics.h"
#include <Arduino.h>
#include <math.h>
#include <string.h>

#ifndef THERMAL_EEPROM_DIAGNOSTICS_ENABLED
#define THERMAL_EEPROM_DIAGNOSTICS_ENABLED 0
#endif
#ifndef THERMAL_SERIAL_FRAME_DIAGNOSTIC
#define THERMAL_SERIAL_FRAME_DIAGNOSTIC 0
#endif

namespace {
constexpr uint8_t Address = 0x33;
constexpr uint16_t StatusRegister = 0x8000, ControlRegister = 0x800D;
constexpr uint32_t TransferDeadlineMs = 300;
// Arduino-ESP32 Wire buffers 128 bytes. Calibration reads keep 32-word
// transfers; the per-subpage RAM copy uses the full buffer to halve the
// number of transactions and keep margin at 16 subpages/s (8 fps).
constexpr uint16_t MaxWordsPerTransfer = 64;
constexpr uint16_t RamWordsPerTransfer = 64;
#if THERMAL_EEPROM_DIAGNOSTICS_ENABLED
void logEepromWords(const uint16_t *words, const char *read, uint32_t clock, int primaryExtractionError, unsigned chunkWords = 32, bool extracted = false) {
    auto summary = ThermalEepromDiagnostics::summarize(words);
    Serial.printf("@MLXEE:BEGIN:read=%s words=832 origin=2400 order=big_endian hz=%lu primary_extract=%d extracted=%u chunk_words=%u crc32=%08lX broken=%u outlier=%u type_word=%04X type_bit=%u\n",
        read, (unsigned long)clock, primaryExtractionError, (unsigned)extracted, chunkWords, (unsigned long)summary.crc32,
        (unsigned)summary.broken, (unsigned)summary.outlier,
        (unsigned)words[10], (unsigned)((words[10] >> 6) & 1U));
    for (unsigned offset = 0; offset < ThermalEepromDiagnostics::WordCount; offset += 32) {
        Serial.printf("@MLXEE:%03u:", offset);
        for (unsigned i = offset; i < offset + 32; ++i) Serial.printf("%04X", (unsigned)words[i]);
        Serial.println("");
    }
    Serial.printf("@MLXEE:END:read=%s crc32=%08lX\n", read, (unsigned long)summary.crc32);
}
#endif
}

void ThermalMlxDevice::logCalibrationFailure(int extractionError, uint32_t crcBeforeExtraction) {
    if (calibrationDiagnosticLogged) return;
    calibrationDiagnosticLogged = true;
#if THERMAL_EEPROM_DIAGNOSTICS_ENABLED
    uint32_t originalClock = wire->getClock();
    uint32_t crcAfterExtraction = ThermalEepromDiagnostics::crc32(raw);
    Serial.printf("@MLXEE:MATH:before_crc32=%08lX after_crc32=%08lX unchanged=%u primary_extract=%d\n",
        (unsigned long)crcBeforeExtraction, (unsigned long)crcAfterExtraction,
        (unsigned)(crcBeforeExtraction == crcAfterExtraction), extractionError);
    logEepromWords(raw, "primary", originalClock, extractionError, 32, true);
    uint16_t slow[ThermalEepromDiagnostics::WordCount] = {};
    wire->setClock(100000);
    int readError = readWords(0x2400, ThermalEepromDiagnostics::WordCount, slow);
    wire->setClock(originalClock); // Preserve runtime configuration and the shared IMU bus.
    if (readError) {
        Serial.printf("@MLXEE:COMPARE:read=100k hz=100000 error=%d comparable=0 extracted=0\n", readError);
    } else {
        auto secondary = ThermalEepromDiagnostics::summarize(slow);
        unsigned different = ThermalEepromDiagnostics::differences(raw, slow);
        Serial.printf("@MLXEE:COMPARE:read=100k hz=100000 error=0 comparable=1 crc32=%08lX primary_crc32=%08lX different_words=%u broken=%u outlier=%u extracted=0\n",
            (unsigned long)secondary.crc32, (unsigned long)crcAfterExtraction, different,
            (unsigned)secondary.broken, (unsigned)secondary.outlier);
        if (different) logEepromWords(slow, "100k", 100000, extractionError);
    }

    // A different transfer size tests deterministic buffer/chunk artifacts.
    // This snapshot remains untouched by calibration extraction.
    wire->setClock(400000);
    readError = readWords(0x2400, ThermalEepromDiagnostics::WordCount, slow, 8);
    if (readError) {
        Serial.printf("@MLXEE:COMPARE:read=chunks8 hz=400000 chunk_words=8 error=%d comparable=0 extracted=0\n", readError);
    } else {
        auto smaller = ThermalEepromDiagnostics::summarize(slow);
        unsigned different = ThermalEepromDiagnostics::differences(raw, slow);
        Serial.printf("@MLXEE:COMPARE:read=chunks8 hz=400000 chunk_words=8 error=0 comparable=1 crc32=%08lX primary_crc32=%08lX different_words=%u broken=%u outlier=%u extracted=0\n",
            (unsigned long)smaller.crc32, (unsigned long)crcAfterExtraction, different,
            (unsigned)smaller.broken, (unsigned)smaller.outlier);
        logEepromWords(slow, "chunks8", 400000, extractionError, 8);
    }

    // Read every suspicious all-ones word and both neighbors with separate
    // address + 2-byte requests, including suspicious header coefficients.
    unsigned pointReads = 0, pointErrors = 0, pointDifferences = 0;
    for (unsigned suspect = 0; suspect < ThermalEepromDiagnostics::WordCount; ++suspect) {
        if (raw[suspect] != 0xFFFFU) continue;
        unsigned first = suspect ? suspect - 1 : suspect;
        unsigned last = suspect + 1 < ThermalEepromDiagnostics::WordCount ? suspect + 1 : suspect;
        for (unsigned offset = first; offset <= last; ++offset) {
            uint16_t value = 0;
            int error = readWords(uint16_t(0x2400U + offset), 1, &value, 1);
            ++pointReads;
            if (error) ++pointErrors;
            else if (value != raw[offset]) ++pointDifferences;
            Serial.printf("@MLXEE:POINT:hz=400000 chunk_words=1 suspect=%u offset=%u address=%04X primary=%04X value=%04X error=%d comparable=%u\n",
                suspect, offset, (unsigned)(0x2400U + offset), (unsigned)raw[offset], (unsigned)value, error, (unsigned)(error == 0));
        }
    }
    wire->setClock(originalClock);
    Serial.printf("@MLXEE:POINT_SUMMARY:reads=%u errors=%u different_words=%u short_byte_reads=%lu negative_byte_reads=%lu\n",
        pointReads, pointErrors, pointDifferences, (unsigned long)shortByteReads, (unsigned long)negativeByteReads);
    Serial.println("@MLXEE:DONE:factory_eeprom_unchanged calibration_still_rejected");
#else
    Serial.printf("MLX90640 calibration rejected: raw=%d crc32=%08lX masked=%u model_word=%04X\n",
        extractionError, (unsigned long)crcBeforeExtraction, (unsigned)maskedCount, (unsigned)raw[10]);
#endif
}

int ThermalMlxDevice::readWords(uint16_t address, uint16_t count, uint16_t *data, uint16_t chunkWords) {
    if (!chunkWords || chunkWords > MaxWordsPerTransfer) return -1;
    uint32_t started = boundedOperation ? operationStartedMs : millis();
    while (count) {
        if (uint32_t(millis() - started) > TransferDeadlineMs) return ThermalAcquisition::ReadDeadline;
        uint16_t words = count < chunkWords ? count : chunkWords;
        uint8_t bytes = uint8_t(words * 2);
        wire->beginTransmission(Address);
        wire->write(uint8_t(address >> 8)); wire->write(uint8_t(address));
        if (wire->endTransmission(false) != 0) return -1;
        if (uint32_t(millis() - started) > TransferDeadlineMs) return ThermalAcquisition::ReadDeadline;
        if (wire->requestFrom(Address, bytes) != bytes || wire->available() < bytes) {
            ++shortByteReads;
            while (wire->available()) wire->read();
            return -1;
        }
        for (uint16_t i = 0; i < words; ++i) {
            int hi = wire->read(), lo = wire->read();
            // A missing byte returns -1, which must never masquerade as FFFF.
            if (hi < 0 || lo < 0) {
                ++negativeByteReads;
                while (wire->available()) wire->read();
                return -1;
            }
            data[i] = uint16_t((unsigned(hi) << 8) | unsigned(lo));
        }
        address += words; data += words; count -= words;
        // RAM may take ~170 ms at the fallback 100 kHz. Service the IMU
        // between chunks, after consuming this response and before beginning
        // another transaction, without concurrent Wire access or a second task.
        if (cooperativeReadHook) cooperativeReadHook(cooperativeReadContext);
    }
    return uint32_t(millis() - started) > TransferDeadlineMs ? ThermalAcquisition::ReadDeadline : 0;
}

int ThermalMlxDevice::writeWord(uint16_t address, uint16_t value, bool verify) {
    wire->beginTransmission(Address);
    wire->write(uint8_t(address >> 8)); wire->write(uint8_t(address));
    wire->write(uint8_t(value >> 8)); wire->write(uint8_t(value));
    if (wire->endTransmission() != 0) return -1;
    if (!verify) return 0;
    uint16_t actual = 0;
    int error = readWords(address, 1, &actual);
    return error ? error : actual == value ? 0 : -2;
}

int ThermalMlxDevice::begin(TwoWire &bus, uint8_t refreshCode) {
    wire = &bus;
    partial = false; warning = 0; maskedCount = 0;
    diagnosticSamples = 0;
    memset(validPixels, 0, sizeof validPixels);
    memset(&params, 0, sizeof params);
    // One transaction has its own bound; multi-transfer reads also have a total
    // deadline. A 64-byte transfer at the fallback 100 kHz fits within 25 ms.
    wire->setTimeOut(25);
    lastRawError = readWords(0x2400, 832, raw);
    if (lastRawError) return -103;
    // Prevent invalid/blank EEPROM from entering scale-normalization loops.
    if ((raw[10] & 0x0040U) || raw[33] == 0 || raw[48] == 0 || raw[50] == 0 || raw[51] == 0) {
        lastRawError = ThermalAcquisition::InvalidCalibration;
        logCalibrationFailure(lastRawError, ThermalEepromDiagnostics::crc32(raw));
        return ThermalAcquisition::InvalidCalibration;
    }
    uint32_t crcBeforeExtraction = ThermalEepromDiagnostics::crc32(raw);
    int factoryWarning = ThermalMlxMath::MLX90640_ClassifyDeviatingPixels(raw, &params);
    bool recoverPartial = factoryWarning >= -6 && factoryWarning <= -3;
    if (recoverPartial) {
        maskedCount = ThermalCalibration::buildValidityMask(raw, validPixels);
        if (!ThermalCalibration::withinPartialLimit(maskedCount)) {
            lastRawError = factoryWarning;
            logCalibrationFailure(factoryWarning, crcBeforeExtraction);
            memset(validPixels, 0, sizeof validPixels);
            return ThermalAcquisition::InvalidCalibration;
        }
        // Require a fresh, complete byte-identical snapshot with different
        // transfer boundaries. Host CRC alone is not factory integrity proof.
        uint16_t verification[ThermalEepromDiagnostics::WordCount];
        lastRawError = readWords(0x2400, ThermalEepromDiagnostics::WordCount, verification, 8);
        if (lastRawError || memcmp(raw, verification, sizeof verification) != 0) {
            if (!lastRawError) lastRawError = ThermalAcquisition::InvalidCalibration;
            logCalibrationFailure(lastRawError, crcBeforeExtraction);
            memset(validPixels, 0, sizeof validPixels);
            return ThermalAcquisition::InvalidCalibration;
        }
    } else if (factoryWarning) {
        lastRawError = factoryWarning;
        logCalibrationFailure(factoryWarning, crcBeforeExtraction);
        return ThermalAcquisition::InvalidCalibration;
    }
    lastRawError = ThermalMlxMath::MLX90640_ExtractParameters(raw, &params, recoverPartial ? validPixels : nullptr);
    if (lastRawError) {
        logCalibrationFailure(lastRawError, crcBeforeExtraction);
        memset(validPixels, 0, sizeof validPixels);
        return ThermalAcquisition::InvalidCalibration;
    }
    if (!ThermalMlxMath::MLX90640_ValidGlobalParameters(&params)) {
        lastRawError = ThermalAcquisition::InvalidCalibration;
        logCalibrationFailure(lastRawError, crcBeforeExtraction);
        memset(validPixels, 0, sizeof validPixels);
        return ThermalAcquisition::InvalidCalibration;
    }
    for (unsigned i = 0; i < 768; ++i) {
        if (recoverPartial && !ThermalCalibration::valid(validPixels, i)) continue;
        bool factoryBad = false;
        for (unsigned bad = 0; bad < 5; ++bad)
            if (params.brokenPixels[bad] == i || params.outlierPixels[bad] == i) factoryBad = true;
        if (params.alpha[i] == 0 && (!factoryBad || recoverPartial)) {
            lastRawError = ThermalAcquisition::InvalidCalibration;
            logCalibrationFailure(lastRawError, crcBeforeExtraction);
            memset(validPixels, 0, sizeof validPixels);
            return ThermalAcquisition::InvalidCalibration;
        }
    }
    if (!recoverPartial) memset(validPixels, 0xFF, sizeof validPixels);
    uint16_t control = 0;
    lastRawError = readWords(ControlRegister, 1, &control);
    if (!lastRawError) {
        uint16_t originalControl = control;
        // Continuous alternating subpages: enable bit0; no data hold(bit2),
        // repeat(bit3), or forced selection(bits6..4). EEPROM may set repeat,
        // so configuring only rate/ADC/chess can leave one half forever absent.
        // Preserve reserved bit1 and bits15..13. Datasheet Figure12/Table6.
        control = uint16_t((control & ~uint16_t(0x1F80 | 0x007D)) | 1U | 0x1000 | (2U << 10) |
                           (uint16_t(refreshCode & 7U) << 7));
        lastRawError = writeWord(ControlRegister, control);
#if THERMAL_SERIAL_FRAME_DIAGNOSTIC
        uint16_t readback = 0;
        int readbackError = lastRawError ? lastRawError : readWords(ControlRegister, 1, &readback);
        Serial.printf("@MLXCONTROL:original=%04X configured=%04X readback=%04X error=%d enable=%u hold=%u repeat=%u selection=%u\n",
            (unsigned)originalControl, (unsigned)control, (unsigned)readback, readbackError,
            (unsigned)(readback & 1U), (unsigned)((readback >> 2) & 1U),
            (unsigned)((readback >> 3) & 1U), (unsigned)((readback >> 4) & 7U));
        if (readbackError) lastRawError = readbackError;
#else
        (void)originalControl;
#endif
    }
    if (lastRawError) {
        memset(validPixels, 0, sizeof validPixels);
        return ThermalAcquisition::InvalidConfiguration;
    }
    partial = recoverPartial;
    warning = recoverPartial ? factoryWarning : 0;
    if (partial) Serial.printf("MLX90640 partial calibration: warning=%d masked=%u valid=%u crc32=%08lX stable=1\n",
        warning, (unsigned)maskedCount, (unsigned)(768 - maskedCount), (unsigned long)crcBeforeExtraction);
    return 0;
}

int ThermalMlxDevice::setRefreshRate(uint8_t refreshCode) {
    uint16_t control = 0;
    lastRawError = readWords(ControlRegister, 1, &control);
    if (!lastRawError) lastRawError = writeWord(ControlRegister,
        uint16_t((control & ~uint16_t(0x0380)) | (uint16_t(refreshCode & 7U) << 7)));
    return lastRawError;
}

int ThermalMlxDevice::readSubpage(float *result, uint8_t &page, uint32_t &sampleMs) {
    uint32_t started = millis();
    ramDurationMs = 0;
    boundedOperation = true;
    operationStartedMs = started;
    auto finish = [this, started](int error) {
        durationMs = uint32_t(millis() - started);
        boundedOperation = false;
        return error;
    };
    uint16_t status = 0, after = 0;
    lastRawError = readWords(StatusRegister, 1, &status);
    if (!lastRawError && !(status & 8U)) {
        return finish(ThermalAcquisition::NotReady); // Yield instead of polling forever.
    }
#if THERMAL_SERIAL_FRAME_DIAGNOSTIC
    bool trace = !lastRawError && diagnosticSamples < 6;
    uint16_t cleared = 0;
#endif
    if (!lastRawError) lastRawError = writeWord(StatusRegister, 0x0030, false);
#if THERMAL_SERIAL_FRAME_DIAGNOSTIC
    if (trace && !lastRawError) lastRawError = readWords(StatusRegister, 1, &cleared);
#endif
    if (!lastRawError) {
        // Measure the deadline-sensitive copy separately from temperature math.
        // Keep shared-bus MPU service and task preemption inside this elapsed
        // time: both consume the margin before the next thermal conversion.
        const uint32_t ramStarted = millis();
        lastRawError = readWords(0x0400, 832, raw, RamWordsPerTransfer);
        ramDurationMs = uint32_t(millis() - ramStarted);
    }
    if (!lastRawError) lastRawError = readWords(StatusRegister, 1, &after);
    // If another subpage arrived while RAM was being copied, discard the torn
    // read. The next scheduled attempt reads the newly ready subpage once.
    if (!lastRawError && (after & 8U)) lastRawError = -8;
    if (!lastRawError) lastRawError = readWords(ControlRegister, 1, &raw[832]);
#if THERMAL_SERIAL_FRAME_DIAGNOSTIC
    if (trace) {
        ++diagnosticSamples;
        Serial.printf("@MLXSUBPAGE:sample=%u before=%04X cleared=%04X after=%04X control=%04X page_before=%u page_after=%u error=%d read_ms=%lu\n",
            (unsigned)diagnosticSamples, (unsigned)status, (unsigned)cleared, (unsigned)after,
            (unsigned)(lastRawError ? 0 : raw[832]), (unsigned)(status & 1U), (unsigned)(after & 1U),
            lastRawError, (unsigned long)(millis() - started));
    }
#endif
    if (lastRawError) return finish(lastRawError);
    if (!(raw[832] & 0x1000) || (raw[832] & 0x007D) != 1U) {
        lastRawError = ThermalAcquisition::InvalidConfiguration;
        return finish(lastRawError);
    }
    // Preserve the page ID that made RAM ready, before clearing data-ready.
    // The current official Melexis API also associates RAM with this snapshot.
    raw[833] = status & 1U;
    if (raw[778] == 0) {
        lastRawError = ThermalAcquisition::InvalidFrame;
        return finish(lastRawError);
    }
    float ambient = ThermalMlxMath::MLX90640_GetTa(raw, &params);
    if (!isfinite(ambient)) {
        lastRawError = ThermalAcquisition::InvalidFrame;
        return finish(lastRawError);
    }
    ThermalMlxMath::MLX90640_CalculateTo(raw, &params, .95f, ambient - 8.f, result, partial ? validPixels : nullptr);
    page = uint8_t(raw[833]);
    sampleMs = started;
    return finish(0);
}

void ThermalMlxDevice::correctBadPixels(float *result) {
    if (partial) return;
    ThermalMlxMath::MLX90640_BadPixelsCorrection(params.brokenPixels, result, 1, &params);
    ThermalMlxMath::MLX90640_BadPixelsCorrection(params.outlierPixels, result, 1, &params);
}
