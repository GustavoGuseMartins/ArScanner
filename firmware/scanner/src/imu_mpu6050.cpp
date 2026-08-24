#include "imu_mpu6050.h"

ImuSensor::ImuSensor() : mpu(Wire) {}

bool ImuSensor::begin() {
    mpu.begin();
    mpu.calcGyroOffsets(false);
    return true;
}

void ImuSensor::update() {
    mpu.update();
}

float ImuSensor::getPitch() {
    return mpu.getAngleX();
}

float ImuSensor::getRoll() {
    return mpu.getAngleY();
}

float ImuSensor::getYaw() {
    return mpu.getAngleZ();
}
