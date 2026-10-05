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

void ImuSensor::resetOrientationReferenceWindow() {
    orientationReferenceGoodUs = 0;
    for (double &value : orientationReferenceGyroIntegral) value = 0;
}

void ImuSensor::invalidateOrientationReference(const char *reason, const ImuRawData *sample,
                                               uint32_t sampleUs, uint32_t intervalUs) {
    // Count the transition once; a later failing read must not overwrite the
    // sample that actually invalidated an otherwise usable reference.
    if (!orientationReferenceValid) return;
    orientationReferenceValid = false;
    ++orientationGaps;
    ++orientationInvalidations;
    orientationYawUncertaintyDeg = 180.f;
    orientationInvalidReason = reason;
    orientationInvalidSampleUs = sampleUs;
    orientationInvalidSampleIntervalUs = intervalUs;
    orientationInvalidSampleValid = sample != nullptr;
    orientationInvalidAccel[0] = sample ? sample->accelX : 0;
    orientationInvalidAccel[1] = sample ? sample->accelY : 0;
    orientationInvalidAccel[2] = sample ? sample->accelZ : 0;
    orientationInvalidGravityNormG = sample
        ? vectorNorm(orientationInvalidAccel) : 0;
}

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
    orientationSampleSeen = false;
    orientationReferenceTimedOut = false;
    resetOrientationReferenceWindow();
    for (float &value : orientationGyroResidualDps) value = 0;
    for (float &value : orientationPreviousGyroDps) value = 0;
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
        const uint32_t failedSampleUs = micros();
        invalidateOrientationReference("read_failed", nullptr, failedSampleUs,
            lastUpdateUs ? uint32_t(failedSampleUs-lastUpdateUs) : 0);
        orientationStationary = false;
        if (orientationCollecting) resetOrientationReferenceWindow();
        if (orientationCollecting && uint32_t(millis()-orientationReferenceStartedMs) >= 6000U) {
            orientationCollecting = false;
            orientationReferenceTimedOut = true;
            resetOrientationReferenceWindow();
        }
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
    // Integrate/collect once per acquired sample, using its acquisition clock.
    // HTTP work or a repeated call must neither add still time nor age a sample.
    if (orientationSampleSeen && orientationLastUpdateUs == lastUpdateUs) return;
    const uint32_t intervalUs = orientationSampleSeen
        ? uint32_t(lastUpdateUs - orientationLastUpdateUs) : 0U;
    const bool continuous = intervalUs > 0U && intervalUs <= 100000U &&
        latest.sampleIntervalUs > 0U && latest.sampleIntervalUs <= 100000U;
    const bool previousStationary = orientationStationary;
    orientationLastUpdateUs = lastUpdateUs;
    orientationSampleSeen = true;
    const ImuRawData sample = latest;
    const float gyroSensor[3] = { sample.gyroX, sample.gyroY, sample.gyroZ };
    // Physical mapping validated on the assembled head: sensor +X is down,
    // +Y is right and +Z points toward the rear. This is a proper right-handed
    // head frame: Xhead=Ysensor, Yhead=-Xsensor, Zhead=Zsensor.
    float up[3] = { sample.accelY, -sample.accelX, sample.accelZ };
    float norm = vectorNorm(up);
    orientationGravityValid = isfinite(norm) && norm >= .85f && norm <= 1.15f;
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
    const float dt = intervalUs * 1e-6f;

    if (orientationCollecting) {
        // A blind interval cannot join two separate stationary windows.
        if (!continuous) resetOrientationReferenceWindow();
        if (orientationStationary && previousStationary && continuous) {
            // Retain sub-millisecond time; frequent reads otherwise round away
            // a substantial part of the observed 3-second stationary window.
            const uint32_t acceptedUs = min<uint32_t>(intervalUs, 6000000U-orientationReferenceGoodUs);
            orientationReferenceGoodUs += acceptedUs;
            // Trapezoidal, acquisition-time weighted mean. Unequal thermal
            // intervals and repeated processing must not bias the residual.
            for (int i=0; i<3; ++i)
                orientationReferenceGyroIntegral[i] +=
                    .5 * (double(orientationPreviousGyroDps[i]) + double(gyroSensor[i])) * acceptedUs;
            const float blend = orientationReferenceGoodUs <= acceptedUs ? 1.f : .04f;
            for (int i=0; i<3; ++i)
                orientationReferenceGravity[i] = (1.f-blend)*orientationReferenceGravity[i] + blend*orientationGravity[i];
            float gnorm = vectorNorm(orientationReferenceGravity);
            if (gnorm > .001f) for (float &value : orientationReferenceGravity) value /= gnorm;
        }
        uint32_t elapsed = uint32_t(millis() - orientationReferenceStartedMs);
        if (elapsed >= 3000 && orientationReferenceGoodUs >= 2400000U && orientationStationary) {
            for (int i=0; i<3; ++i)
                orientationGyroResidualDps[i] = float(orientationReferenceGyroIntegral[i] / orientationReferenceGoodUs);
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
            orientationReferenceTimedOut = true;
            resetOrientationReferenceWindow();
        }
    } else if (orientationReferenceValid) {
        if (!continuous || !orientationGravityValid || !isfinite(gyroNorm)) {
            const char *reason = !continuous ? "sample_gap"
                : !orientationGravityValid ? "invalid_gravity" : "invalid_gyro";
            invalidateOrientationReference(reason, &sample, lastUpdateUs, intervalUs);
        } else {
            // Remove the warm stationary residual collected at this explicit
            // reference, then project on the measured up direction. The raw
            // driver readings retain their independently calibrated bias.
            const float correctedGyroHead[3] = {
                gyroHead[0] - orientationGyroResidualDps[1],
                gyroHead[1] + orientationGyroResidualDps[0],
                gyroHead[2] - orientationGyroResidualDps[2]
            };
            // Project the bias-corrected gyro on the measured up direction;
            // this remains valid with the observed non-level PCB mounting.
            // Head axes are right handed; clockwise Unity/pan yaw has the
            // opposite sign to the right-handed rotation around measured up.
            float rate = -(correctedGyroHead[0]*orientationGravity[0] +
                correctedGyroHead[1]*orientationGravity[1] + correctedGyroHead[2]*orientationGravity[2]);
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

        // Inclinação filtrada nos eixos da cabeça, independente do toggle yaw.
        // Os offsets nominais não substituem calibração física da montagem.
        float rawPitch = orientationPitchDeg - IMU_MOUNT_NOMINAL_PITCH_DEG;
        float rawRoll  = orientationRollDeg  - IMU_MOUNT_NOMINAL_ROLL_DEG;
        filteredTiltPitch = filteredTiltPitch * 0.92f + rawPitch * 0.08f;
        filteredTiltRoll  = filteredTiltRoll  * 0.92f + rawRoll  * 0.08f;
    }
    for (int i=0; i<3; ++i) orientationPreviousGyroDps[i] = gyroSensor[i];
    (void)panDegrees;
}

bool ImuSensor::requestOrientationReference(float panDegrees) {
    if (!initialized) return false;
    orientationEnabled = false;
    orientationReferenceValid = false;
    orientationCollecting = true;
    orientationReferenceTimedOut = false;
    orientationReferenceStartedMs = millis();
    resetOrientationReferenceWindow();
    for (float &value : orientationGyroResidualDps) value = 0;
    orientationPreviousGyroDps[0] = latest.gyroX;
    orientationPreviousGyroDps[1] = latest.gyroY;
    orientationPreviousGyroDps[2] = latest.gyroZ;
    orientationReferencePanDeg = panDegrees;
    orientationReferenceGravity[0] = orientationGravity[0];
    orientationReferenceGravity[1] = orientationGravity[1];
    orientationReferenceGravity[2] = orientationGravity[2];
    // Synchronize to the last acquired sample. The next new reading starts
    // collection; processing the same reading twice must not count as rest.
    orientationLastUpdateUs = lastUpdateUs;
    orientationSampleSeen = true;
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
    result.stationaryMs = orientationReferenceGoodUs / 1000U;
    result.relativeHeadYawDeg = orientationReferencePanDeg + orientationRelativeYawDeg;
    result.relativeBaseYawDeg = wrapDegrees(result.relativeHeadYawDeg - orientationLastPanDeg);
    result.pitchDeg = orientationPitchDeg - orientationReferencePitchDeg;
    result.rollDeg = orientationRollDeg - orientationReferenceRollDeg;
    result.yawUncertaintyDeg = orientationYawUncertaintyDeg;
    result.invalidReason = orientationInvalidReason;
    result.invalidations = orientationInvalidations;
    result.invalidSampleUs = orientationInvalidSampleUs;
    result.invalidSampleIntervalUs = orientationInvalidSampleIntervalUs;
    result.invalidSampleValid = orientationInvalidSampleValid;
    result.invalidGravityNormG = orientationInvalidGravityNormG;
    result.invalidAccelX = orientationInvalidAccel[0];
    result.invalidAccelY = orientationInvalidAccel[1];
    result.invalidAccelZ = orientationInvalidAccel[2];
    float half = result.relativeHeadYawDeg * .5f / DegreesPerRadian;
    result.qw = cosf(half); result.qy = sinf(half); result.qx = result.qz = 0;
    result.ageMs = orientationSampleSeen ? uint32_t(micros() - orientationLastUpdateUs) / 1000U : UINT32_MAX;
    if (orientationCollecting) result.state = "reference_collecting";
    else if (orientationReferenceTimedOut) result.state = "reference_timeout";
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
float ImuSensor::getFilteredTiltPitch() {
    if (!initialized) return 0;
    return filteredTiltPitch;
}
float ImuSensor::getFilteredTiltRoll() {
    if (!initialized) return 0;
    return filteredTiltRoll;
}
float ImuSensor::getYaw() { return initialized ? latest.angleZ : 0; }
ImuRawData ImuSensor::getRawAxes() { return initialized ? latest : ImuRawData{}; }
