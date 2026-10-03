#ifndef IMU_MPU6050_H
#define IMU_MPU6050_H

#include "config.h"
#include <Wire.h>

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
    bool orientationStationary = false;
    uint32_t orientationReferenceStartedMs = 0, orientationReferenceGoodMs = 0;
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

public:
    ImuSensor();
    bool begin();
    bool update(); // true only after a complete I2C transaction
    float getPitch();
    float getRoll();
    float getYaw();
    ImuRawData getRawAxes();
    void updateOrientation(float panDegrees);
    bool requestOrientationReference(float panDegrees);
    bool setOrientationEnabled(bool enabled);
    ImuOrientationSnapshot getOrientation() const;
    bool isInitialized() const { return initialized; }
    ImuHealth getHealth() const { return health; }
};

#endif // IMU_MPU6050_H
