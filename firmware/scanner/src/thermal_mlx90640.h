#ifndef THERMAL_MLX90640_H
#define THERMAL_MLX90640_H

#include "config.h"
#include "scan_geometry.h"
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
    uint32_t i2cHz, readDurationMs, ramReadDurationMs, frameSpanMs;
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
    uint8_t refreshHz = THERMAL_DEFAULT_FULL_FPS * 2; // Two distinct subpages form a complete frame.
    uint8_t requestedFullFps = THERMAL_DEFAULT_FULL_FPS;
    uint32_t i2cHz = I2C_FAST_FREQ;
    uint32_t lastSubpageMs = 0, lastTimeoutMs = 0, lastCompleteMs = 0;
    uint32_t rateWindowStartedMs = 0, rateWindowFrames = 0;
    ThermalAcquisitionHealth diagnostic = {false, "not_initialized", 0, THERMAL_DEFAULT_FULL_FPS * 2, -1, 0,
        0, 0, 0, 0, 0, I2C_FAST_FREQ, 0, 0, 0, false, 0, 0,
        THERMAL_DEFAULT_FULL_FPS, THERMAL_DEFAULT_FULL_FPS, 0, 0};
    volatile uint8_t orientationProfile = THERMAL_ORIENTATION_PROFILE_DEFAULT;
    void discardPartial();
    void setError(const char *state, int error, int rawError);
    bool reduceRate(bool busError);
    uint8_t refreshCode() const { return refreshHz == 16 ? 5 : refreshHz == 8 ? 4 : 3; }
    uint32_t pairDeadlineMs() const { return refreshHz == 16 ? 300 : refreshHz == 8 ? 500 : 900; }
    void resetRateWindow();
    bool tryGetPointTemperatureForFrame(float angleHorizDeg, float angleVertDeg,
        float &temperatureC, uint32_t sampleTimeMs, uint8_t projectionProfile,
        bool requireFrameMatch, uint32_t projectionFrameCount);

public:
    // The callback must use one valid pose history/reference for both times.
    // A GY-25 yaw must never fall back to mechanical pan if its history is absent.
    // Callbacks run outside frameMux and return false for unavailable poses.
    using PanAtTimestamp = bool (*)(void *context, uint32_t timestampMs, float &panDeg);
    void setCooperativeReadHook(ThermalMlxDevice::CooperativeReadHook hook, void *context = nullptr) {
        mlx.setCooperativeReadHook(hook, context);
    }
    ThermalSensor();
    bool begin();
    bool updateFrame();
    // Compatibility/recovery request, only 8 fps is accepted. Called by Aux;
    // retrying restores the fast bus without exposing a frame-rate selector.
    bool requestFrameRate(uint8_t fullFps);
    float getPointTemperature(float angleHorizDeg, float angleVertDeg = 0.0f);
    bool tryGetPointTemperature(float angleHorizDeg, float angleVertDeg, float &temperatureC,
                                uint32_t sampleTimeMs = UINT32_MAX);
    bool tryGetHeadPointTemperature(ScanGeometry::Vec3 headPoint,
        ScanGeometry::Vec3 lidarOrigin, ScanGeometry::Vec3 thermalOffset,
        bool invertLidarY, float &temperatureC, uint32_t sampleTimeMs,
        PanAtTimestamp panAtTimestamp, void *poseContext = nullptr);
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
