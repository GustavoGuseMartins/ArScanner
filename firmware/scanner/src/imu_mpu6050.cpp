#include "imu_mpu6050.h"
#include "imu_sample_math.h"
#include <math.h>

namespace {
constexpr uint8_t Address = 0x68;
constexpr float DegreesPerRadian = 57.29577951308232f;
// The assembled sensor shows roughly 0.1–1.3 dps of stationary noise. Keep
// reference collection strict enough to reject a hand movement, while leaving
// the active scan path free to rotate at the pan rate.
constexpr float ReferenceGyroStillDps = 2.0f;
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
float wrapDegrees(float value) {
    while (value > 180.f) value -= 360.f;
    while (value < -180.f) value += 360.f;
    return value;
}
float vectorNorm(const float *v) {
    return sqrtf(v[0]*v[0] + v[1]*v[1] + v[2]*v[2]);
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
    orientationEnabled = false;
    orientationReferenceValid = false;
    orientationCollecting = false;
    orientationGravityValid = false;
    orientationStationary = false;
    orientationGaps = 0;
    orientationGeneration = 0;
    orientationLastUpdateUs = 0;
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

void ImuSensor::updateOrientation(float panDegrees) {
    if (!initialized) return;
    orientationLastPanDeg = panDegrees;
    const ImuRawData sample = latest;
    // Physical mapping validated on the assembled head: sensor +X is down,
    // +Y is right and +Z points toward the rear. This is a proper right-handed
    // head frame: Xhead=Ysensor, Yhead=-Xsensor, Zhead=Zsensor.
    float up[3] = { sample.accelY, -sample.accelX, sample.accelZ };
    float norm = vectorNorm(up);
    orientationGravityValid = isfinite(norm) && norm >= .75f && norm <= 1.25f;
    if (orientationGravityValid) {
        up[0] /= norm; up[1] /= norm; up[2] /= norm;
        for (int i=0; i<3; ++i) orientationGravity[i] = up[i];
    }
    const float gyroHead[3] = { sample.gyroY, -sample.gyroX, sample.gyroZ };
    const float gyroNorm = sqrtf(gyroHead[0]*gyroHead[0] + gyroHead[1]*gyroHead[1] + gyroHead[2]*gyroHead[2]);
    // Reference collection needs a genuinely still head. Once referenced, a
    // pan-rate gyro value is expected and must not invalidate the scan.
    orientationStationary = orientationGravityValid && isfinite(gyroNorm) &&
        gyroNorm <= ReferenceGyroStillDps;
    uint32_t nowUs = micros();
    float dt = orientationLastUpdateUs ? uint32_t(nowUs - orientationLastUpdateUs) * 1e-6f : 0.f;
    orientationLastUpdateUs = nowUs;
    uint32_t dtMs = uint32_t(dt * 1000.f);

    if (orientationCollecting) {
        if (orientationStationary) {
            orientationReferenceGoodMs = min<uint32_t>(6000U, orientationReferenceGoodMs + dtMs);
            const float blend = orientationReferenceGoodMs <= dtMs ? 1.f : .04f;
            for (int i=0; i<3; ++i)
                orientationReferenceGravity[i] = (1.f-blend)*orientationReferenceGravity[i] + blend*orientationGravity[i];
            float gnorm = vectorNorm(orientationReferenceGravity);
            if (gnorm > .001f) for (float &value : orientationReferenceGravity) value /= gnorm;
        }
        uint32_t elapsed = uint32_t(millis() - orientationReferenceStartedMs);
        if (elapsed >= 3000 && orientationReferenceGoodMs >= 2400) {
            orientationReferencePanDeg = panDegrees;
            orientationRelativeYawDeg = 0;
            orientationReferenceValid = true;
            orientationCollecting = false;
            orientationGeneration++;
            orientationGaps = 0;
            orientationYawUncertaintyDeg = 0;
            orientationReferencePitchDeg = atan2f(orientationReferenceGravity[2],
                hypotf(orientationReferenceGravity[0], orientationReferenceGravity[1])) * DegreesPerRadian;
            orientationReferenceRollDeg = atan2f(orientationReferenceGravity[0], orientationReferenceGravity[1]) * DegreesPerRadian;
        } else if (elapsed >= 6000) {
            orientationCollecting = false;
            orientationReferenceValid = false;
            orientationReferenceGoodMs = 0;
        }
    } else if (orientationReferenceValid && orientationGravityValid) {
        if (dt <= 0.f || dt > .1f) {
            orientationGaps++;
            orientationReferenceValid = false;
            orientationYawUncertaintyDeg = 180.f;
        } else {
            // Project the bias-corrected gyro on the measured up direction;
            // this remains valid with the observed non-level PCB mounting.
            float rate = gyroHead[0]*orientationGravity[0] +
                gyroHead[1]*orientationGravity[1] + gyroHead[2]*orientationGravity[2];
            if (isfinite(rate)) {
                orientationRelativeYawDeg += rate * dt;
                orientationYawUncertaintyDeg = min(180.f,
                    orientationYawUncertaintyDeg + fabsf(rate)*dt*.012f + .002f);
            }
        }
    }
    if (orientationGravityValid) {
        orientationPitchDeg = atan2f(orientationGravity[2],
            hypotf(orientationGravity[0], orientationGravity[1])) * DegreesPerRadian;
        orientationRollDeg = atan2f(orientationGravity[0], orientationGravity[1]) * DegreesPerRadian;
    }
    (void)panDegrees;
}

bool ImuSensor::requestOrientationReference(float panDegrees) {
    if (!initialized) return false;
    orientationEnabled = false;
    orientationReferenceValid = false;
    orientationCollecting = true;
    orientationReferenceStartedMs = millis();
    orientationReferenceGoodMs = 0;
    orientationReferencePanDeg = panDegrees;
    orientationReferenceGravity[0] = orientationGravity[0];
    orientationReferenceGravity[1] = orientationGravity[1];
    orientationReferenceGravity[2] = orientationGravity[2];
    orientationLastUpdateUs = micros();
    return true;
}

bool ImuSensor::setOrientationEnabled(bool enabled) {
    if (enabled && !orientationReferenceValid) return false;
    orientationEnabled = enabled;
    return true;
}

ImuOrientationSnapshot ImuSensor::getOrientation() const {
    ImuOrientationSnapshot result;
    result.referenceValid = orientationReferenceValid;
    result.enabled = orientationEnabled;
    result.gravityValid = orientationGravityValid;
    result.stationary = orientationStationary;
    result.generation = orientationGeneration;
    result.gaps = orientationGaps;
    result.stationaryMs = orientationReferenceGoodMs;
    result.relativeHeadYawDeg = orientationReferencePanDeg + orientationRelativeYawDeg;
    result.relativeBaseYawDeg = wrapDegrees(result.relativeHeadYawDeg - orientationLastPanDeg);
    result.pitchDeg = orientationPitchDeg - orientationReferencePitchDeg;
    result.rollDeg = orientationRollDeg - orientationReferenceRollDeg;
    result.yawUncertaintyDeg = orientationYawUncertaintyDeg;
    float half = result.relativeHeadYawDeg * .5f / DegreesPerRadian;
    result.qw = cosf(half); result.qy = sinf(half); result.qx = result.qz = 0;
    result.ageMs = orientationLastUpdateUs ? uint32_t(micros() - orientationLastUpdateUs) / 1000U : UINT32_MAX;
    if (orientationCollecting) result.state = "reference_collecting";
    else if (!orientationReferenceValid) result.state = orientationGaps ? "reference_invalid" : "no_reference";
    else if (orientationEnabled) result.state = "ready";
    else result.state = "referenced";
    return result;
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
