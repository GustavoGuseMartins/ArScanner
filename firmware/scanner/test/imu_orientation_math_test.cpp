#include "../include/imu_orientation_math.h"
#include "../include/scan_geometry.h"
#include <cassert>
#include <cstdio>
#include <limits>

using namespace ImuOrientationMath;
static bool near(float actual, float expected, float tolerance = .001f) {
    return fabsf(actual-expected) <= tolerance;
}
static Vec3 sensorVector(const SensorToHeadRH &mount, Vec3 head) {
    // Inverse of a proper mounting rotation is its transpose.
    return {mount.m[0][0]*head.x+mount.m[1][0]*head.y+mount.m[2][0]*head.z,
            mount.m[0][1]*head.x+mount.m[1][1]*head.y+mount.m[2][1]*head.z,
            mount.m[0][2]*head.x+mount.m[1][2]*head.y+mount.m[2][2]*head.z};
}
static Vec3 headGravity(float pitch, float roll) {
    return {sinf(roll*radians)*cosf(pitch*radians),
            cosf(roll*radians)*cosf(pitch*radians), sinf(pitch*radians)};
}

int main() {
    SensorToHeadRH unknown;
    Options verticalPan;
    verticalPan.verticalYawMotionValidated = true;
    OrientationEstimator uncommissioned(unknown, verticalPan);
    auto result = uncommissioned.initializeReference({0,1,0});
    assert(!result.tiltAvailable && !result.relativeYawAvailable);
    assert(result.tiltStatus == TiltStatus::MountingUnvalidated);

    SensorToHeadRH identity;
    identity.validated = true;
    assert(identity.isProperRotation());
    OrientationEstimator verticalBoard(identity, verticalPan);
    // Vertical PCB: gravity on MPU Y must not become a spurious 90-degree tilt.
    result = verticalBoard.initializeReference({0,1,0});
    assert(result.tiltAvailable && result.relativeYawAvailable);
    assert(near(result.pitchDegrees,0) && near(result.rollDegrees,0));
    // With sensor/head RH Y up, negative gyro Y is clockwise Unity yaw.
    for (int i=0; i<100; ++i) result = verticalBoard.update({0,1,0},{0,-90,0},.01f);
    assert(result.relativeYawAvailable && near(result.relativeYawDegrees,90,.002f));
    const auto forward = ScanGeometry::yaw({0,0,1},result.relativeYawDegrees);
    assert(near(forward.x,1) && near(forward.z,0));
    for (int i=0; i<100; ++i) result = verticalBoard.update({0,1,0},{0,90,0},.01f);
    assert(near(result.relativeYawDegrees,0,.002f));

    // A physically possible 90-degree mounting maps sensor -Z onto head +Y.
    SensorToHeadRH rotated;
    const float rotation[3][3] = {{1,0,0},{0,0,-1},{0,1,0}};
    for (int i=0; i<3; ++i) for (int j=0; j<3; ++j) rotated.m[i][j]=rotation[i][j];
    rotated.validated = true;
    assert(rotated.isProperRotation());
    OrientationEstimator differentMount(rotated, verticalPan);
    result = differentMount.initializeReference({0,0,-1});
    assert(result.tiltAvailable && near(result.pitchDegrees,0) && near(result.rollDegrees,0));
    for (int i=0; i<100; ++i) result = differentMount.update({0,0,-1},{0,0,90},.01f);
    assert(near(result.relativeYawDegrees,90,.002f));

    // All combinations verify Euler signs/order against actual point geometry.
    const float pitches[] = {-35,0,35}, rolls[] = {-25,0,25};
    for (float pitch : pitches) for (float roll : rolls) {
        OrientationEstimator estimator(rotated);
        const Vec3 gravityRH = headGravity(pitch,roll);
        result = estimator.initializeReference(sensorVector(rotated,gravityRH));
        assert(result.tiltAvailable);
        assert(!result.relativeYawAvailable && result.yawStatus == YawStatus::MotionUnvalidated);
        assert(near(result.pitchDegrees,pitch) && near(result.rollDegrees,roll));
        const Vec3 gravityUnity = unityPolar(gravityRH);
        const auto corrected = ScanGeometry::tilt({gravityUnity.x,gravityUnity.y,gravityUnity.z},
                                                  result.pitchDegrees,result.rollDegrees);
        assert(near(corrected.x,0) && near(corrected.y,1) && near(corrected.z,0));
    }

    // The gyro transforms as an axial vector across a handedness change.
    const Vec3 polar = unityPolar({2,3,4}), axial = unityAxial({2,3,4});
    assert(polar.x==2 && polar.y==3 && polar.z==-4);
    assert(axial.x==-2 && axial.y==-3 && axial.z==4);
    SensorToHeadRH reflection = identity;
    reflection.m[2][2] = -1;
    assert(!reflection.isProperRotation());
    OrientationEstimator reflected(reflection,verticalPan);
    result = reflected.initializeReference({0,1,0});
    assert(!result.tiltAvailable && result.tiltStatus==TiltStatus::InvalidMounting);
    SensorToHeadRH scaledMount = identity;
    scaledMount.m[0][0] = 2;
    assert(!scaledMount.isProperRotation());

    // Only the gyro component on observed gravity contributes to this yaw.
    OrientationEstimator projected(identity,verticalPan);
    const Vec3 tiltedGravity = headGravity(15,20);
    projected.initializeReference(tiltedGravity);
    result = projected.update(tiltedGravity,scaled(tiltedGravity,-30),.1f);
    assert(near(result.verticalRateClockwiseDegreesPerSecond,30));
    assert(near(result.relativeYawDegrees,3));

    // Long gaps cannot be filled with the last reading or silently repaired.
    result = differentMount.update({0,0,-1},{0,0,90},.3f);
    assert(result.tiltAvailable && !result.relativeYawAvailable && result.integrationGaps==1);
    assert(result.yawStatus==YawStatus::ReferenceInvalid && near(result.relativeYawDegrees,90,.002f));
    result = differentMount.update({0,0,-1},{0,0,90},.01f);
    assert(!result.relativeYawAvailable && near(result.relativeYawDegrees,90,.002f));
    differentMount.initializeReference({0,0,-1});
    result = differentMount.update({0,0,-1},{0,0,90},.01f,false);
    assert(!result.relativeYawAvailable && result.integrationGaps==1);
    differentMount.initializeReference({0,0,-1});
    differentMount.markDiscontinuity();
    result = differentMount.update({0,0,-1},{0,0,90},.01f);
    assert(!result.relativeYawAvailable && result.integrationGaps==1);

    // Valid fresh tilt may recover; lost yaw stays invalid until a new zero.
    differentMount.initializeReference({0,0,-1});
    result = differentMount.update({0,0,-2},{0,0,90},.01f);
    assert(!result.tiltAvailable && !result.relativeYawAvailable);
    assert(result.tiltStatus==TiltStatus::InvalidAcceleration);
    result = differentMount.update({0,0,-1},{0,0,90},.01f);
    assert(result.tiltAvailable && !result.relativeYawAvailable);
    differentMount.initializeReference({0,0,-1});
    result = differentMount.update({0,0,-1},{0,0,std::numeric_limits<float>::quiet_NaN()},.01f);
    assert(result.tiltAvailable && !result.relativeYawAvailable);
    OrientationEstimator singular(identity,verticalPan);
    result = singular.initializeReference({0,0,1});
    assert(!result.tiltAvailable && result.tiltStatus==TiltStatus::EulerSingularity);

    // Bias removal is a caller requirement; an uncorrected bias still drifts.
    OrientationEstimator residualBias(identity,verticalPan);
    residualBias.initializeReference({0,1,0});
    for (int i=0; i<1000; ++i) result = residualBias.update({0,1,0},{0,-.1f,0},.01f);
    assert(near(result.relativeYawDegrees,1,.002f));
    puts("IMU orientation PASS: explicit mounting, vertical PCB, tilt signs/order, RH/LH axial mapping, relative vertical yaw, invalid frames, permanent gap invalidation and gyro bias drift.");
}
