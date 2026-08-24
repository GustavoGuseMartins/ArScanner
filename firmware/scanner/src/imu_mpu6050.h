#ifndef IMU_MPU6050_H
#define IMU_MPU6050_H

#include "config.h"
#include <MPU6050_tockn.h>

class ImuSensor {
private:
    MPU6050 mpu;

public:
    ImuSensor();
    bool begin();
    void update();
    float getPitch();
    float getRoll();
    float getYaw();
};

#endif // IMU_MPU6050_H
