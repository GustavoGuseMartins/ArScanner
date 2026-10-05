#pragma once
#include <Wire.h>
#include "thermal_mlx90640_math.h"
#include "thermal_frame_assembler.h"
#include "thermal_calibration_mask.h"

// Small register acquisition layer. Melexis temperature/calibration maths are
// kept separate; no dependence on Adafruit's private or unbounded frame API.
class ThermalMlxDevice {
public:
    // Called only after a complete Wire transaction has been consumed. The
    // caller may service another device synchronously on this same task/bus;
    // it must not recurse into this device or change the bus clock.
    using CooperativeReadHook = void (*)(void *context);
    void setCooperativeReadHook(CooperativeReadHook hook, void *context = nullptr) {
        cooperativeReadHook = hook; cooperativeReadContext = context;
    }
    int begin(TwoWire &bus, uint8_t refreshCode);
    int setRefreshRate(uint8_t refreshCode);
    int readSubpage(float *result, uint8_t &page, uint32_t &sampleMs);
    void correctBadPixels(float *result);
    int rawError() const { return lastRawError; }
    uint32_t readDurationMs() const { return durationMs; }
    uint32_t ramReadDurationMs() const { return ramDurationMs; }
    const uint8_t *validityMask() const { return validPixels; }
    uint16_t maskedPixelCount() const { return maskedCount; }
    bool partialCalibration() const { return partial; }
    int calibrationWarning() const { return warning; }
private:
    CooperativeReadHook cooperativeReadHook = nullptr;
    void *cooperativeReadContext = nullptr;
    TwoWire *wire = nullptr;
    ThermalMlxMath::paramsMLX90640 params = {};
    uint16_t raw[834] = {};
    int lastRawError = 0;
    uint32_t durationMs = 0;
    uint32_t ramDurationMs = 0;
    bool boundedOperation = false;
    uint32_t operationStartedMs = 0;
    bool calibrationDiagnosticLogged = false;
    uint8_t validPixels[ThermalCalibration::MaskBytes] = {};
    uint16_t maskedCount = 0;
    bool partial = false;
    int warning = 0;
    uint8_t diagnosticSamples = 0;
    uint32_t shortByteReads = 0;
    uint32_t negativeByteReads = 0;
    void logCalibrationFailure(int extractionError, uint32_t crcBeforeExtraction);
    int readWords(uint16_t address, uint16_t count, uint16_t *data, uint16_t chunkWords = 32);
    int writeWord(uint16_t address, uint16_t value, bool verify = true);
};
