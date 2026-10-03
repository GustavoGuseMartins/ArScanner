#pragma once
#include <math.h>
#include <stdint.h>

// Raw MPU axes and the math here are RIGHT HANDED. Head RH axes are:
// +X right, +Y up, +Z backward. Unity/ScanGeometry use +Z forward instead.
// A sensor-to-head mounting must be a proper rotation, never a reflection.
namespace ImuOrientationMath {
constexpr float radians = 0.017453292519943295f;
constexpr float degrees = 57.29577951308232f;
struct Vec3 { float x, y, z; };
inline bool finite(Vec3 v) { return isfinite(v.x) && isfinite(v.y) && isfinite(v.z); }
inline float dot(Vec3 a, Vec3 b) { return a.x*b.x + a.y*b.y + a.z*b.z; }
inline Vec3 scaled(Vec3 v, float k) { return {v.x*k, v.y*k, v.z*k}; }

// Polar vectors (acceleration/position) and axial vectors (angular velocity)
// transform differently when the coordinate convention changes handedness.
inline Vec3 unityPolar(Vec3 headRH) { return {headRH.x, headRH.y, -headRH.z}; }
inline Vec3 unityAxial(Vec3 headRH) { return {-headRH.x, -headRH.y, headRH.z}; }

struct SensorToHeadRH {
    // Identity is storage initialization, NOT a known physical mounting.
    float m[3][3] = {{1,0,0}, {0,1,0}, {0,0,1}};
    bool validated = false;

    bool isProperRotation() const {
        const float tolerance = .001f;
        for (int i=0; i<3; ++i) {
            for (int j=0; j<3; ++j) {
                float product = 0;
                for (int k=0; k<3; ++k) {
                    if (!isfinite(m[i][k]) || !isfinite(m[j][k])) return false;
                    product += m[i][k]*m[j][k];
                }
                if (fabsf(product - (i==j ? 1.f : 0.f)) > tolerance) return false;
            }
        }
        const float determinant =
            m[0][0]*(m[1][1]*m[2][2]-m[1][2]*m[2][1]) -
            m[0][1]*(m[1][0]*m[2][2]-m[1][2]*m[2][0]) +
            m[0][2]*(m[1][0]*m[2][1]-m[1][1]*m[2][0]);
        return fabsf(determinant-1.f) <= tolerance;
    }
    Vec3 map(Vec3 sensor) const {
        return {
            m[0][0]*sensor.x + m[0][1]*sensor.y + m[0][2]*sensor.z,
            m[1][0]*sensor.x + m[1][1]*sensor.y + m[1][2]*sensor.z,
            m[2][0]*sensor.x + m[2][1]*sensor.y + m[2][2]*sensor.z
        };
    }
};

struct Options {
    float minimumGravityG = .9f, maximumGravityG = 1.1f;
    float maximumIntervalSeconds = .1f;
    // Projected gyro integration is a yaw estimate only for vertical motion,
    // e.g. an upright scanner with a pan axis confirmed vertical. It is NOT
    // general Euler yaw during simultaneous tilt. Enable after that validation.
    bool verticalYawMotionValidated = false;
};

enum class TiltStatus : uint8_t {
    MountingUnvalidated, InvalidMounting, InvalidAcceleration, EulerSingularity, Available
};
enum class YawStatus : uint8_t {
    MotionUnvalidated, NoReference, Available, ReferenceInvalid
};
struct Result {
    TiltStatus tiltStatus = TiltStatus::MountingUnvalidated;
    YawStatus yawStatus = YawStatus::MotionUnvalidated;
    bool tiltAvailable = false, relativeYawAvailable = false;
    // These signs and Euler order match ScanGeometry::tilt and ::yaw.
    float pitchDegrees = 0, rollDegrees = 0;
    float relativeYawDegrees = 0, verticalRateClockwiseDegreesPerSecond = 0;
    uint32_t integrationGaps = 0;
};

class OrientationEstimator {
    SensorToHeadRH mounting;
    Options options;
    bool referenced = false, referenceInvalid = false;
    float accumulatedYaw = 0;
    uint32_t gaps = 0;

    Result tilt(Vec3 accelerationSensorG, Vec3 &upHeadRH) const {
        Result result;
        result.relativeYawDegrees = accumulatedYaw;
        result.integrationGaps = gaps;
        result.yawStatus = !options.verticalYawMotionValidated ? YawStatus::MotionUnvalidated
            : referenceInvalid ? YawStatus::ReferenceInvalid
            : referenced ? YawStatus::Available : YawStatus::NoReference;
        if (!mounting.validated) return result;
        if (!mounting.isProperRotation()) {
            result.tiltStatus = TiltStatus::InvalidMounting;
            return result;
        }
        const float norm2 = dot(accelerationSensorG, accelerationSensorG);
        if (!finite(accelerationSensorG) || !isfinite(norm2) || norm2 <= 0.f ||
            norm2 < options.minimumGravityG*options.minimumGravityG ||
            norm2 > options.maximumGravityG*options.maximumGravityG) {
            result.tiltStatus = TiltStatus::InvalidAcceleration;
            return result;
        }
        upHeadRH = mounting.map(scaled(accelerationSensorG, 1.f/sqrtf(norm2)));
        // R_tilt = Rx(pitch) Rz(roll) in ScanGeometry's Unity convention.
        // g_unity = (sin(roll)cos(pitch), cos(roll)cos(pitch), -sin(pitch)).
        const float horizontal2 = upHeadRH.x*upHeadRH.x + upHeadRH.y*upHeadRH.y;
        if (horizontal2 < .000001f) {
            result.tiltStatus = TiltStatus::EulerSingularity;
            return result;
        }
        result.pitchDegrees = atan2f(upHeadRH.z, sqrtf(horizontal2))*degrees;
        result.rollDegrees = atan2f(upHeadRH.x, upHeadRH.y)*degrees;
        result.tiltStatus = TiltStatus::Available;
        result.tiltAvailable = true;
        return result;
    }
    void invalidateReference(bool integrationGap) {
        if (referenced) referenceInvalid = true;
        referenced = false;
        if (integrationGap) ++gaps;
    }

public:
    OrientationEstimator(const SensorToHeadRH &sensorToHead,
                         const Options &configuration = Options())
        : mounting(sensorToHead), options(configuration) {}

    // Explicit relative zero; caller must have observed a valid fresh reading.
    // This creates no heading in AR, north, or any other external reference.
    Result initializeReference(Vec3 accelerationSensorG) {
        referenced = referenceInvalid = false;
        accumulatedYaw = 0;
        gaps = 0;
        Vec3 up = {};
        Result result = tilt(accelerationSensorG, up);
        if (result.tiltAvailable && options.verticalYawMotionValidated) {
            referenced = true;
            result.yawStatus = YawStatus::Available;
            result.relativeYawAvailable = true;
        }
        return result;
    }

    // Gyro input must already have stationary bias removed. Acceleration must
    // represent gravity: a near-1g magnitude alone cannot prove that in motion.
    // 'continuous' must be false after a failed/missing device read, even if dt
    // looks small. A gap permanently invalidates yaw until explicit reference.
    Result update(Vec3 accelerationSensorG, Vec3 biasCorrectedGyroSensorDps,
                  float intervalSeconds, bool continuous = true) {
        Vec3 up = {};
        Result result = tilt(accelerationSensorG, up);
        const bool intervalValid = continuous && isfinite(intervalSeconds) &&
            intervalSeconds > 0.f && intervalSeconds <= options.maximumIntervalSeconds;
        const bool sampleValid = result.tiltAvailable && finite(biasCorrectedGyroSensorDps);
        if (!intervalValid || !sampleValid) {
            invalidateReference(!intervalValid);
        } else {
            // Gyro is axial. Both sensor and head here are RH, so the proper
            // mounting rotation maps it normally. Clockwise Unity yaw = -RH Y.
            const Vec3 gyro = mounting.map(biasCorrectedGyroSensorDps);
            result.verticalRateClockwiseDegreesPerSecond = -dot(gyro, up);
            if (referenced && options.verticalYawMotionValidated)
                accumulatedYaw += result.verticalRateClockwiseDegreesPerSecond*intervalSeconds;
        }
        result.relativeYawDegrees = accumulatedYaw;
        result.relativeYawAvailable = referenced && options.verticalYawMotionValidated;
        result.yawStatus = !options.verticalYawMotionValidated ? YawStatus::MotionUnvalidated
            : referenceInvalid ? YawStatus::ReferenceInvalid
            : referenced ? YawStatus::Available : YawStatus::NoReference;
        result.integrationGaps = gaps;
        return result;
    }

    // Call as soon as a bus/sample failure is known; do not wait for recovery.
    void markDiscontinuity() { invalidateReference(true); }
};
}
