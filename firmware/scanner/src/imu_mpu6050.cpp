#include "imu_mpu6050.h"
#include "imu_sample_math.h"
#include <math.h>

namespace {
constexpr uint8_t Address = 0x68;
bool readRegisters(uint8_t reg, uint8_t* data, uint8_t count) {
    Wire.beginTransmission(Address);
    Wire.write(reg);
    if (Wire.endTransmission(false) != 0 || Wire.requestFrom(Address,count) != count) {
        while (Wire.available()) Wire.read();
        return false;
    }
    for (uint8_t i=0; i<count; ++i) data[i] = Wire.read();
    return true;
}
bool writeRegister(uint8_t reg, uint8_t value) {
    Wire.beginTransmission(Address);
    Wire.write(reg); Wire.write(value);
    return Wire.endTransmission() == 0;
}
void gravityAngles(const ImuRawData &sample, float &x, float &y) {
    // Sensor-frame diagnostics only, not 6-DoF pose. Map the upright PCB before tilt.
    x = atan2f(sample.accelY, sample.accelZ + fabsf(sample.accelX))*180.f/PI;
    y = atan2f(-sample.accelX, sample.accelZ + fabsf(sample.accelY))*180.f/PI;
}
}

ImuSensor::ImuSensor() {}

bool ImuSensor::readSample(ImuRawData &sample) {
    uint8_t bytes[14];
    if (!readRegisters(0x3B,bytes,sizeof(bytes))) {
        ++health.readErrors;
        return false;
    }
    using ImuSampleMath::signedWord;
    sample.accelX = signedWord(bytes,16384.f);
    sample.accelY = signedWord(bytes+2,16384.f);
    sample.accelZ = signedWord(bytes+4,16384.f);
    sample.gyroX = signedWord(bytes+8,65.5f);
    sample.gyroY = signedWord(bytes+10,65.5f);
    sample.gyroZ = signedWord(bytes+12,65.5f);
    return true;
}

bool ImuSensor::begin() {
    initialized = false;
    health.ready = health.biasCalibrated = false;
    health.identity = -1;
    ++health.initAttempts;
    lastUpdateUs = 0;
    failedReads = 0;
    uint8_t identity;
    if (!readRegisters(0x75,&identity,1)) {
        ++health.readErrors;
        health.state = "identity_read_failed";
        Serial.println("[IMU] Sem MPU6050 em 0x68. GY-25 em SDA/SCL exige jumper I2C; confira alimentacao e modo.");
        return false;
    }
    health.identity = identity;
    if (identity != 0x68) {
        health.state = "unexpected_identity";
        Serial.println("[IMU] Dispositivo em 0x68 nao confirmou identidade MPU6050.");
        return false;
    }
    // Gyro +/-500 dps, accel +/-2g, DLPF 44/42 Hz, sample rate 100 Hz.
    if (!writeRegister(0x6B,0x01) || !writeRegister(0x1A,0x03) ||
        !writeRegister(0x19,9) || !writeRegister(0x1B,0x08) || !writeRegister(0x1C,0)) {
        health.state = "configuration_failed";
        return false;
    }
    delay(100);
    Serial.println("[IMU] Calibrando bias por 3 s. Mantenha toda a cabeca imovel.");
    ImuSampleMath::CalibrationWindow window;
    ImuRawData sample = {};
    for (unsigned i=0; i<300; ++i) {
        if (!readSample(sample)) {
            health.state = "calibration_read_failed";
            return false;
        }
        const float values[] = {sample.accelX,sample.accelY,sample.accelZ,
            sample.gyroX,sample.gyroY,sample.gyroZ};
        if (!window.add(values)) {
            health.state = "calibration_moving";
            Serial.println("[IMU] Calibracao recusada: movimento/aceleracao. Nova tentativa quando parado.");
            return false;
        }
        delay(10);
    }
    if (!window.stable()) {
        health.state = "calibration_unstable";
        Serial.println("[IMU] Calibracao recusada: leituras instaveis.");
        return false;
    }
    for (int i=0; i<3; ++i) gyroBias[i] = window.mean[i+3];
    latest = sample;
    latest.gyroX -= gyroBias[0]; latest.gyroY -= gyroBias[1]; latest.gyroZ -= gyroBias[2];
    gravityAngles(latest,latest.angleX,latest.angleY);
    latest.angleZ = 0;
    lastUpdateUs = micros();
    initialized = true;
    health.state = "ready";
    health.ready = health.biasCalibrated = true;
    return true;
}

bool ImuSensor::update() {
    if (!initialized) return false;
    ImuRawData sample = {};
    if (!readSample(sample)) {
        if (++failedReads >= 3) initialized = false;
        health.state = "sample_read_failed";
        health.ready = false;
        if (!initialized) health.biasCalibrated = false;
        lastUpdateUs = 0;
        return false;
    }
    failedReads = 0;
    sample.gyroX -= gyroBias[0]; sample.gyroY -= gyroBias[1]; sample.gyroZ -= gyroBias[2];
    const uint32_t now = micros();
    const float dt = lastUpdateUs ? uint32_t(now-lastUpdateUs)*1e-6f : 0.f;
    float ax, ay;
    gravityAngles(sample,ax,ay);
    // The thermal task can block. Do not extend one gyro sample over that gap.
    const bool continuous = dt > 0.f && dt <= .1f;
    sample.sampleIntervalUs = lastUpdateUs ? uint32_t(now-lastUpdateUs) : 0;
    sample.integrationGaps = latest.integrationGaps + (continuous ? 0U : 1U);
    const float alpha = continuous ? expf(-dt/.5f) : 0.f;
    sample.angleX = alpha*(latest.angleX+sample.gyroX*dt)+(1.f-alpha)*ax;
    sample.angleY = alpha*(latest.angleY+sample.gyroY*dt)+(1.f-alpha)*ay;
    sample.angleZ = latest.angleZ + (continuous ? sample.gyroZ*dt : 0.f);
    latest = sample;
    lastUpdateUs = now;
    health.state = "ready";
    health.ready = true;
    return true;
}

float ImuSensor::getPitch() {
    if (!initialized) return 0;
    return IMU_PITCH_SIGN*(IMU_PITCH_AXIS==0 ? latest.angleX : IMU_PITCH_AXIS==1 ? latest.angleY : latest.angleZ);
}
float ImuSensor::getRoll() {
    if (!initialized) return 0;
    return IMU_ROLL_SIGN*(IMU_ROLL_AXIS==0 ? latest.angleX : IMU_ROLL_AXIS==1 ? latest.angleY : latest.angleZ);
}
float ImuSensor::getYaw() { return initialized ? latest.angleZ : 0; }
ImuRawData ImuSensor::getRawAxes() { return initialized ? latest : ImuRawData{}; }
