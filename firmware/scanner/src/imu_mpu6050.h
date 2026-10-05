#ifndef IMU_MPU6050_H
#define IMU_MPU6050_H

#include "config.h"
#include <Wire.h>
#include <string.h>

struct ImuRawData {
    float accelX, accelY, accelZ;   // g
    float gyroX, gyroY, gyroZ;      // °/s
    float angleX, angleY, angleZ;   // ° (integrado)
    uint32_t sampleIntervalUs, integrationGaps;
};

struct ImuHealth {
    const char *state = "not_initialized";
    int16_t identity = -1; // WHO_AM_I, -1 when the register could not be read.
    uint32_t initAttempts = 0, readErrors = 0;
    bool ready = false, biasCalibrated = false;
};

// Orientação relativa da cabeça em torno da gravidade. O MPU6050 não possui
// magnetômetro, portanto o primeiro registro é uma referência manual e o yaw
// permanece relativo a ela. O campo de yaw já inclui o pan mecânico medido no
// instante da referência; isso permite ao firmware substituir, sem somar duas
// vezes, o ângulo do motor quando a base inteira é girada.
struct ImuOrientationSnapshot {
    const char *state = "disabled";
    bool referenceValid = false, enabled = false, stationary = false, gravityValid = false;
    uint32_t generation = 0, ageMs = UINT32_MAX, gaps = 0, stationaryMs = 0;
    float relativeHeadYawDeg = 0, relativeBaseYawDeg = 0;
    float pitchDeg = 0, rollDeg = 0, yawUncertaintyDeg = 0;
    float qw = 1, qx = 0, qy = 0, qz = 0;
    // Last reference invalidation persists across explicit references/mode
    // changes and initialization retries. Reset only on a new driver instance
    // (a scanner reboot), so a read failure survives the automatic recovery.
    const char *invalidReason = "none";
    uint32_t invalidations = 0, invalidSampleUs = 0, invalidSampleIntervalUs = 0;
    bool invalidSampleValid = false;
    float invalidGravityNormG = 0, invalidAccelX = 0, invalidAccelY = 0, invalidAccelZ = 0;
    bool canApplyYaw(uint32_t maximumAgeMs = 150) const {
        return enabled && referenceValid && gaps == 0 && ageMs <= maximumAgeMs &&
            state && strcmp(state, "ready") == 0;
    }
};

class ImuSensor {
private:
    ImuRawData latest = {};
    float gyroBias[3] = {};
    uint32_t lastUpdateUs = 0;
    unsigned failedReads = 0;
    bool initialized = false;
    ImuHealth health;
    bool readSample(ImuRawData &sample);
    bool orientationEnabled = false;
    bool orientationReferenceValid = false;
    bool orientationCollecting = false;
    bool orientationGravityValid = false;
    bool orientationStationary = false, orientationSampleSeen = false;
    bool orientationReferenceTimedOut = false;
    uint32_t orientationReferenceStartedMs = 0, orientationReferenceGoodUs = 0;
    uint32_t orientationLastUpdateUs = 0, orientationGeneration = 0;
    uint32_t orientationGaps = 0;
    float orientationReferencePanDeg = 0, orientationReferenceYawDeg = 0;
    float orientationLastPanDeg = 0;
    float orientationRelativeYawDeg = 0;
    float orientationReferencePitchDeg = 0, orientationReferenceRollDeg = 0;
    float orientationPitchDeg = 0, orientationRollDeg = 0;
    float orientationYawUncertaintyDeg = 0;
    float orientationGravity[3] = {0, 1, 0};
    float orientationReferenceGravity[3] = {0, 1, 0};
    // Orientation-only warm-up residual, in the bias-corrected sensor frame.
    // Keep the startup driver bias and raw diagnostic readings unchanged.
    float orientationGyroResidualDps[3] = {};
    float orientationPreviousGyroDps[3] = {};
    double orientationReferenceGyroIntegral[3] = {};
    void resetOrientationReferenceWindow();
    void invalidateOrientationReference(const char *reason, const ImuRawData *sample,
        uint32_t sampleUs, uint32_t intervalUs);
    const char *orientationInvalidReason = "none";
    uint32_t orientationInvalidations = 0, orientationInvalidSampleUs = 0;
    uint32_t orientationInvalidSampleIntervalUs = 0;
    bool orientationInvalidSampleValid = false;
    float orientationInvalidGravityNormG = 0;
    float orientationInvalidAccel[3] = {};
    float filteredTiltPitch = 0.0f;
    float filteredTiltRoll = 0.0f;

public:
    ImuSensor();
    bool begin();
    bool update(); // true only after a complete I2C transaction
    float getPitch();
    float getRoll();
    float getFilteredTiltPitch();
    float getFilteredTiltRoll();
    float getYaw();
    ImuRawData getRawAxes();
    uint32_t getSampleTimestampUs() const { return lastUpdateUs; }
    void updateOrientation(float panDegrees);
    bool requestOrientationReference(float panDegrees);
    bool setOrientationEnabled(bool enabled);
    ImuOrientationSnapshot getOrientation() const;
    bool isInitialized() const { return initialized; }
    ImuHealth getHealth() const { return health; }
};

#endif // IMU_MPU6050_H
