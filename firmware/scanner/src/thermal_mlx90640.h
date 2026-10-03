#ifndef THERMAL_MLX90640_H
#define THERMAL_MLX90640_H

#include "config.h"
#include <Wire.h>
#include "thermal_mlx90640_device.h"
#include <freertos/FreeRTOS.h>
#include <freertos/portmacro.h>

struct ThermalAcquisitionHealth {
    bool frameReady;
    const char *state;
    uint8_t subpageMask, refreshHz;
    int lastSubpage, rawError;
    uint32_t duplicateSubpages, frameTimeouts, readErrors, overruns, invalidFrames;
    uint32_t i2cHz, readDurationMs, frameSpanMs;
    bool partialCalibration;
    uint16_t maskedPixels;
    int calibrationWarning;
    uint8_t requestedFrameRateHz, targetFrameRateHz;
    float measuredFrameRateHz;
    uint32_t frameRateWindowMs;
};

class ThermalSensor {
private:
    ThermalMlxDevice mlx;
    ThermalAcquisition::FrameAssembler assembler;
    float frame[768] = {}; // Último quadro completo publicado
    uint8_t frameValidity[96] = {}; // Published atomically with temperatures.
    float nextFrame[768] = {}; // Aquisição I2C, somente na tarefa de sensores
    portMUX_TYPE frameMux = portMUX_INITIALIZER_UNLOCKED;
    bool initialized = false;
    bool frameValid = false;
    uint32_t frameTimestampMs = 0;
    int lastError = -100; // -101=invalid COMPLETE frame, -104=subpage deadline
    uint32_t frameCount = 0;
    uint8_t failedReads = 0;
    uint8_t overrunStreak = 0;
    uint8_t refreshHz = 8; // Subpages/s; two distinct subpages form a complete frame.
    uint8_t requestedFullFps = 4;
    uint32_t i2cHz = I2C_FAST_FREQ;
    uint32_t lastSubpageMs = 0, lastTimeoutMs = 0, lastCompleteMs = 0;
    uint32_t rateWindowStartedMs = 0, rateWindowFrames = 0;
    ThermalAcquisitionHealth diagnostic = {false, "not_initialized", 0, 8, -1, 0,
        0, 0, 0, 0, 0, I2C_FAST_FREQ, 0, 0, false, 0, 0, 4, 4, 0, 0};
    volatile uint8_t orientationProfile = THERMAL_ORIENTATION_PROFILE_DEFAULT;
    void discardPartial();
    void setError(const char *state, int error, int rawError);
    bool reduceRate(bool busError);
    uint8_t refreshCode() const { return refreshHz == 16 ? 5 : refreshHz == 8 ? 4 : 3; }
    uint32_t pairDeadlineMs() const { return refreshHz == 16 ? 300 : refreshHz == 8 ? 500 : 900; }
    void resetRateWindow();

public:
    ThermalSensor();
    bool begin();
    bool updateFrame();
    // Called only by the Aux task that owns I2C. Explicit selection retries the
    // fast bus; automatic fallback never oscillates back up by itself.
    bool requestFrameRate(uint8_t fullFps);
    float getPointTemperature(float angleHorizDeg, float angleVertDeg = 0.0f);
    bool tryGetPointTemperature(float angleHorizDeg, float angleVertDeg, float &temperatureC,
                                uint32_t sampleTimeMs = UINT32_MAX);
    bool getNormalizedFrame(uint8_t *dest, float &minT, float &maxT,
                            uint8_t *validityMask = nullptr);
    bool isInitialized();
    uint8_t getOrientationProfile() const { return orientationProfile; }
    void loadOrientationProfile();
    bool saveOrientationProfile(uint8_t profile);
    void health(int &error, uint32_t &frames, uint32_t &ageMs);
    ThermalAcquisitionHealth acquisitionHealth();
};

#endif // THERMAL_MLX90640_H
