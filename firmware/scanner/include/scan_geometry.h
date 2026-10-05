#pragma once
#include <math.h>
#include <stdint.h>

// Unity left-handed coordinates: X right, Y up, Z forward; metres/degrees.
// Positive yaw turns forward towards right (clockwise viewed from above).
namespace ScanGeometry {
constexpr float radians = 0.017453292519943295f;
struct Vec3 { float x, y, z; };
inline Vec3 add(Vec3 a, Vec3 b) { return {a.x+b.x, a.y+b.y, a.z+b.z}; }
inline Vec3 subtract(Vec3 a, Vec3 b) { return {a.x-b.x, a.y-b.y, a.z-b.z}; }
inline Vec3 yaw(Vec3 p, float degrees) {
    const float c = cosf(degrees*radians), s = sinf(degrees*radians);
    return {c*p.x+s*p.z, p.y, -s*p.x+c*p.z};
}
inline Vec3 tilt(Vec3 p, float pitch, float roll) {
    float c = cosf(roll*radians), s = sinf(roll*radians);
    p = {c*p.x-s*p.y, s*p.x+c*p.y, p.z};
    c = cosf(pitch*radians); s = sinf(pitch*radians);
    return {p.x, c*p.y-s*p.z, s*p.y+c*p.z};
}
// The optical origin orbits the pan axis; translate BEFORE pan rotation.
inline Vec3 inHead(float distance, float angle, float mountYaw, Vec3 opticalOffset) {
    const float a = angle*radians;
    return add(yaw({0, distance*sinf(a), distance*cosf(a)}, mountYaw), opticalOffset);
}
struct HeadDebug { Vec3 point; float angleDeg, mountYawDeg; };
inline HeadDebug inHeadDebug(float distance, float rawAngle, float angleSign,
                            float zeroDeg, float mountYaw, Vec3 opticalOffset) {
    const float angle = angleSign*rawAngle + zeroDeg;
    return {inHead(distance, angle, mountYaw, opticalOffset), angle, mountYaw};
}
inline Vec3 project(Vec3 headPoint, float pan, float pitch, float roll,
                    bool imuOnHead, Vec3 tagOffset, bool tagOnHead) {
    Vec3 bodyPoint = yaw(headPoint, pan);
    Vec3 tag = tagOnHead ? yaw(tagOffset, pan) : tagOffset;
    bodyPoint = subtract(bodyPoint, tag);
    // A rotating IMU's tilt is expressed in head axes, not fixed base axes.
    // Pitch/roll application is independent of which source supplies pan yaw.
    return imuOnHead ? yaw(tilt(yaw(bodyPoint, -pan), pitch, roll), pan)
                     : tilt(bodyPoint, pitch, roll);
}
// Thermal orientation profile: bit 0 selects +X instead of -X as optical
// forward; bit 1 mirrors image horizontally. Profile 0 is the historical
// -X/90-degree-clockwise mapping; runtime defaults are selected in config.h.
constexpr uint8_t thermalForwardPlusX = 0x01;
constexpr uint8_t thermalMirrorHorizontal = 0x02;
inline bool validThermalOrientation(uint8_t profile) { return profile <= 3; }
inline bool thermalAngles(Vec3 headPoint, Vec3 lidarOrigin, Vec3 thermalOffset,
                          bool invertLidarY, float &horizontalDeg, float &verticalDeg,
                          uint8_t orientationProfile = 0) {
    if (!validThermalOrientation(orientationProfile)) return false;
    if (invertLidarY) headPoint.y = 2.0f*lidarOrigin.y-headPoint.y;
    Vec3 fromLens = subtract(headPoint, add(lidarOrigin, thermalOffset));
    const float forwardSign = (orientationProfile & thermalForwardPlusX) ? 1.0f : -1.0f;
    const float forward = forwardSign*fromLens.x;
    if (!isfinite(forward) || forward <= 0.05f) return false;
    // Image right is +Z while looking -X, and -Z while looking +X.
    const float horizontal = atan2f(-forwardSign*fromLens.z, forward);
    const float vertical = atan2f(fromLens.y, hypotf(forward, fromLens.z));
    horizontalDeg = horizontal/radians;
    verticalDeg = vertical/radians;
    return isfinite(horizontalDeg) && isfinite(verticalDeg);
}
// Both sensors orbit the same pan axis. Re-express the physical LiDAR hit
// (including its eccentric optical origin) in the head pose of the image.
// Rotating only the ray or subtracting the current lens first loses parallax.
inline bool thermalAnglesAtFramePose(Vec3 headPoint, float lidarPanDeg,
                                     float framePanDeg, Vec3 lidarOrigin,
                                     Vec3 thermalOffset, bool invertLidarY,
                                     float &horizontalDeg, float &verticalDeg,
                                     uint8_t orientationProfile = 0) {
    if (!isfinite(lidarPanDeg) || !isfinite(framePanDeg)) return false;
    return thermalAngles(yaw(headPoint, lidarPanDeg-framePanDeg), lidarOrigin,
        thermalOffset, invertLidarY, horizontalDeg, verticalDeg, orientationProfile);
}
// The native 32 x 24 image must be rotated 90 degrees clockwise for display:
// native column grows toward head-down and native row toward head-left.
// The upright display is 24 x 32, with its right at the native top.
inline bool thermalRawPixelForRightCorrection90(float horizontalDeg, float verticalDeg,
                                                float nativeFovXDeg, float nativeFovYDeg,
                                                float &column, float &row,
                                                uint8_t orientationProfile = 0) {
    if (!validThermalOrientation(orientationProfile)) return false;
    const float headHorizontalFov = nativeFovYDeg;
    const float headVerticalFov = nativeFovXDeg;
    if (!isfinite(horizontalDeg) || !isfinite(verticalDeg) ||
        !isfinite(headHorizontalFov) || !isfinite(headVerticalFov) ||
        headHorizontalFov <= 0 || headVerticalFov <= 0 ||
        fabsf(horizontalDeg) > headHorizontalFov*0.5f ||
        fabsf(verticalDeg) > headVerticalFov*0.5f) return false;
    column = (headVerticalFov*0.5f-verticalDeg)/headVerticalFov*31.0f;
    const float imageHorizontal = (orientationProfile & thermalMirrorHorizontal)
        ? -horizontalDeg : horizontalDeg;
    row = (headHorizontalFov*0.5f-imageHorizontal)/headHorizontalFov*23.0f;
    return true;
}
}
