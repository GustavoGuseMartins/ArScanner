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

// Inversão dos eixos decorrente da montagem física no verso da placa (de costas para o LiDAR):
float ImuSensor::getPitch() {
    return -mpu.getAngleX(); // Inversão para manter correspondência com inclinação frontal do drone
}

float ImuSensor::getRoll() {
    return mpu.getAngleY();
}

float ImuSensor::getYaw() {
    return mpu.getAngleZ();
}
