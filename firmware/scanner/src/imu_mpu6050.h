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

class ImuSensor {
private:
    ImuRawData latest = {};
    float gyroBias[3] = {};
    uint32_t lastUpdateUs = 0;
    unsigned failedReads = 0;
    bool initialized = false;
    ImuHealth health;
    bool readSample(ImuRawData &sample);

public:
    ImuSensor();
    bool begin();
    bool update(); // true only after a complete I2C transaction
    float getPitch();
    float getRoll();
    float getYaw();
    ImuRawData getRawAxes();
    bool isInitialized() const { return initialized; }
    ImuHealth getHealth() const { return health; }
};

#endif // IMU_MPU6050_H
