// Compile with -I firmware/scanner/test/imu_stubs before the real include path,
// linking src/imu_mpu6050.cpp. No hardware or third-party driver is involved.
#include "../src/imu_mpu6050.h"
#include <cassert>
#include <cstdio>
#include <cstring>
#include <cmath>
uint32_t fakeMicros = 1000;
FakeSerial Serial;
FakeWire Wire;

static void sampleOrientation(ImuSensor &sensor, uint32_t intervalUs, float pan = 37.f) {
    fakeMicros += intervalUs;
    assert(sensor.update());
    sensor.updateOrientation(pan);
}

static void collectReference(ImuSensor &sensor, float pan = 37.f, uint32_t intervalUs = 5955U) {
    Wire.gyroX = Wire.gyroY = Wire.gyroZ = 0;
    sampleOrientation(sensor,10000U,pan);
    assert(sensor.requestOrientationReference(pan));
    for (unsigned i=0; i<12000 && !sensor.getOrientation().referenceValid; ++i) {
        sampleOrientation(sensor,intervalUs,pan);
        assert(strcmp(sensor.getOrientation().state,"reference_timeout") != 0);
    }
    const auto reference = sensor.getOrientation();
    assert(reference.referenceValid && !reference.enabled && reference.stationary);
    assert(strcmp(reference.state,"referenced") == 0);
    assert(reference.stationaryMs >= 2999U && reference.stationaryMs <= 3100U);
}

static void validateOrientationReference() {
    // Installed mounting: sensor -X is head up. Exercise the production driver,
    // including its actual reference state machine, instead of the math helper.
    Wire.accelX = -16384; Wire.accelY = Wire.accelZ = 0;
    ImuSensor sensor;
    assert(sensor.begin());
    collectReference(sensor);
    assert(sensor.getOrientation().generation == 1);
    assert(sensor.setOrientationEnabled(true));
    assert(strcmp(sensor.getOrientation().state,"ready") == 0);
    // Head pan -12 degrees/s, base physically fixed: measured head yaw must
    // subtract that same pan without inventing a 24 degrees/s base rotation.
    Wire.gyroX = -786; // -12 dps at +/-500 dps scale.
    for (unsigned i=1; i<=100; ++i) sampleOrientation(sensor,10000U,37.f-.12f*i);
    auto orientation = sensor.getOrientation();
    assert(orientation.referenceValid && !orientation.stationary);
    assert(fabsf(orientation.relativeHeadYawDeg-25.f) < .002f);
    assert(fabsf(orientation.relativeBaseYawDeg) < .002f);
    // Delayed processing does not extend a gyro reading across work that
    // happened after acquisition, and its age still reflects that work.
    fakeMicros += 10000U;
    assert(sensor.update());
    fakeMicros += 50000U;
    sensor.updateOrientation(24.88f);
    orientation = sensor.getOrientation();
    assert(orientation.referenceValid && orientation.ageMs == 50U);
    assert(fabsf(orientation.relativeHeadYawDeg-24.88f) < .002f);
    fakeMicros += 10000U;
    sensor.updateOrientation(24.88f); // No new I2C sample: no second integration.
    assert(fabsf(sensor.getOrientation().relativeHeadYawDeg-24.88f) < .002f);
    assert(sensor.getOrientation().ageMs == 60U);

    // A true long acquisition gap still invalidates yaw and cannot be enabled.
    sampleOrientation(sensor,200000U,24.88f);
    assert(!sensor.getOrientation().referenceValid && sensor.getOrientation().gaps == 1);
    assert(strcmp(sensor.getOrientation().state,"reference_invalid") == 0);
    assert(!sensor.setOrientationEnabled(true));
    collectReference(sensor,24.88f,500U); // Fractional ms must not be rounded away.
    assert(sensor.getOrientation().generation == 2);
    Wire.failRead = true;
    fakeMicros += 10000U;
    assert(!sensor.update()); // Even a short missing transaction loses continuity.
    assert(!sensor.getOrientation().referenceValid && sensor.getOrientation().gaps == 1);
    Wire.failRead = false;
    sampleOrientation(sensor,10000U);
    assert(!sensor.getOrientation().referenceValid);

    collectReference(sensor);
    Wire.accelX = -8192; // Invalid gravity must not silently skip a yaw sample.
    sampleOrientation(sensor,10000U);
    assert(!sensor.getOrientation().referenceValid && !sensor.getOrientation().gravityValid);
    Wire.accelX = -16384;
    sampleOrientation(sensor,10000U);
    assert(!sensor.getOrientation().referenceValid);

    // Missing time cannot be called stationary, even if the next sample is
    // quiet; continuous movement remains rejected with the original 2 dps gate.
    assert(sensor.requestOrientationReference(37.f));
    for (unsigned i=0; i<150; ++i) sampleOrientation(sensor,10000U);
    assert(sensor.getOrientation().stationaryMs >= 1490U);
    sampleOrientation(sensor,3000000U);
    assert(!sensor.getOrientation().referenceValid && sensor.getOrientation().stationaryMs == 0);
    Wire.gyroX = 1965; // 30 dps.
    for (unsigned i=0; i<160; ++i) sampleOrientation(sensor,10000U);
    assert(!sensor.getOrientation().referenceValid && sensor.getOrientation().stationaryMs == 0);
    assert(strcmp(sensor.getOrientation().state,"reference_timeout") == 0);
    collectReference(sensor);
    assert(strcmp(sensor.getOrientation().state,"referenced") == 0);
    collectReference(sensor,37.f,60000U); // Normal 50 ms thermal read + Aux delay.
    puts("IMU reference PASS: acquired-sample clock, precise stationary duration, referenced -> ready, signed pan removal, delayed/duplicate processing, permanent read/gravity/gap invalidation, moving rejection and explicit retry after timeout.");
}

static void finishWarmReference(ImuSensor &sensor, float pan = 37.f) {
    for (unsigned i=0; i<700 && !sensor.getOrientation().referenceValid; ++i)
        sampleOrientation(sensor,10000U,pan);
    assert(sensor.getOrientation().referenceValid && sensor.getOrientation().stationary);
    assert(sensor.setOrientationEnabled(true));
}

static void validateWarmOrientationResidual() {
    Wire.gyroX = Wire.gyroY = Wire.gyroZ = 0;
    Wire.accelX = -16384; Wire.accelY = Wire.accelZ = 0;
    ImuSensor sensor;
    assert(sensor.begin()); // Cold startup bias is zero.
    // A warm sensor retains a small bias on all axes after startup calibration.
    Wire.gyroX = -20; Wire.gyroY = 12; Wire.gyroZ = 9;
    sampleOrientation(sensor,10000U);
    assert(sensor.requestOrientationReference(37.f));
    finishWarmReference(sensor);
    for (unsigned i=0; i<2000; ++i) sampleOrientation(sensor,10000U);
    assert(fabsf(sensor.getOrientation().relativeHeadYawDeg-37.f) < .0002f);
    const auto raw = sensor.getRawAxes();
    assert(fabsf(raw.gyroX+20.f/65.5f) < .00001f);
    assert(fabsf(raw.gyroY-12.f/65.5f) < .00001f);
    assert(fabsf(raw.gyroZ-9.f/65.5f) < .00001f);
    assert(raw.angleZ > 2.f); // Diagnostic integration retains the boot bias.
    // Compensation is a sensor-frame vector, not one scalar measured at the
    // reference tilt: changing gravity direction still cancels the same bias.
    Wire.accelX = -11585; Wire.accelY = 0; Wire.accelZ = 11585;
    for (unsigned i=0; i<100; ++i) sampleOrientation(sensor,10000U);
    assert(fabsf(sensor.getOrientation().relativeHeadYawDeg-37.f) < .0002f);
    Wire.accelX = -16384; Wire.accelZ = 0;
    Wire.gyroX = -20 + 786; // Residual plus a real +12 dps pan rotation.
    for (unsigned i=1; i<=100; ++i) sampleOrientation(sensor,10000U,37.f+.12f*i);
    assert(fabsf(sensor.getOrientation().relativeHeadYawDeg-49.f) < .002f);
    assert(fabsf(sensor.getOrientation().relativeBaseYawDeg) < .002f);

    // Re-reference at a different warm residual. Only the window after a
    // failed read or long gap contributes; pre-gap residual must be discarded.
    for (unsigned fault=0; fault<2; ++fault) {
        Wire.gyroX = -60; Wire.gyroY = -20; Wire.gyroZ = 30;
        sampleOrientation(sensor,10000U);
        assert(sensor.requestOrientationReference(37.f));
        for (unsigned i=0; i<80; ++i) sampleOrientation(sensor,10000U);
        Wire.gyroX = 15; Wire.gyroY = 8; Wire.gyroZ = -10;
        if (fault == 0) {
            Wire.failRead = true;
            fakeMicros += 10000U;
            assert(!sensor.update());
            Wire.failRead = false;
        } else sampleOrientation(sensor,200000U);
        assert(sensor.getOrientation().stationaryMs == 0);
        finishWarmReference(sensor);
        for (unsigned i=0; i<500; ++i) sampleOrientation(sensor,10000U);
        assert(fabsf(sensor.getOrientation().relativeHeadYawDeg-37.f) < .0002f);
    }
    puts("Warm IMU residual PASS: explicit per-axis reference removes stationary drift, keeps raw diagnostics and real pan, and clears time/bias accumulators after failed reads and gaps.");
}

static void validateTimeWeightedResidual() {
    Wire.gyroX = Wire.gyroY = Wire.gyroZ = 0;
    Wire.accelX = -16384; Wire.accelY = Wire.accelZ = 0;
    ImuSensor sensor;
    assert(sensor.begin());
    sampleOrientation(sensor,10000U);
    assert(sensor.requestOrientationReference(37.f));
    const uint32_t intervals[3] = {5000U,15000U,60000U};
    const int16_t readings[3] = {-10,-60,-30};
    double integral = 0, previous = 0;
    uint32_t observedUs = 0;
    for (unsigned i=0; i<700 && !sensor.getOrientation().referenceValid; ++i) {
        const unsigned slot = i % 3;
        Wire.gyroX = readings[slot];
        const double current = float(Wire.gyroX)/65.5f;
        integral += .5*(previous+current)*intervals[slot];
        observedUs += intervals[slot];
        previous = current;
        sampleOrientation(sensor,intervals[slot]);
        const auto progress = sensor.getOrientation().stationaryMs;
        // Processing a reading again must change neither progress nor bias.
        sensor.updateOrientation(37.f);
        assert(sensor.getOrientation().stationaryMs == progress);
    }
    assert(sensor.getOrientation().referenceValid && sensor.setOrientationEnabled(true));
    assert(sensor.getOrientation().stationaryMs == observedUs/1000U);
    Wire.gyroX = 0;
    for (unsigned i=0; i<100; ++i) sampleOrientation(sensor,10000U);
    const float expected = 37.f-float(integral/observedUs);
    assert(fabsf(sensor.getOrientation().relativeHeadYawDeg-expected) < .0002f);
    puts("Warm IMU weighting PASS: irregular acquisition intervals use elapsed-time trapezoidal weighting; duplicate processing contributes nothing.");
}

static void validateOrientationInvalidationDiagnostics() {
    Wire.failRead = false;
    Wire.gyroX = Wire.gyroY = Wire.gyroZ = 0;
    Wire.accelX = -16384; Wire.accelY = Wire.accelZ = 0;
    ImuSensor sensor;
    assert(sensor.begin());
    auto record = sensor.getOrientation();
    assert(strcmp(record.invalidReason,"none") == 0 && record.invalidations == 0);
    assert(!record.invalidSampleValid && record.invalidSampleUs == 0);

    collectReference(sensor);
    assert(sensor.setOrientationEnabled(true));
    Wire.accelX = -8192; Wire.accelY = 4096; // Complete sample, but only ~0.56 g.
    sampleOrientation(sensor,10000U);
    record = sensor.getOrientation();
    assert(!record.referenceValid && !record.canApplyYaw());
    assert(strcmp(record.invalidReason,"invalid_gravity") == 0 && record.invalidations == 1);
    assert(record.invalidSampleValid && record.invalidSampleUs == fakeMicros);
    assert(record.invalidSampleIntervalUs == 10000U);
    assert(record.invalidAccelX == -.5f && record.invalidAccelY == .25f && record.invalidAccelZ == 0);
    assert(fabsf(record.invalidGravityNormG-sqrtf(.3125f)) < .00001f);
    const auto gravityRecord = record;
    Wire.accelX = -16384; Wire.accelY = 0;
    sampleOrientation(sensor,10000U); // Good polling sample cannot erase the bad one.
    assert(sensor.getOrientation().invalidSampleUs == gravityRecord.invalidSampleUs);
    assert(sensor.getOrientation().invalidAccelX == gravityRecord.invalidAccelX);
    collectReference(sensor);
    assert(sensor.getOrientation().referenceValid);
    assert(sensor.getOrientation().invalidations == 1);
    assert(strcmp(sensor.getOrientation().invalidReason,"invalid_gravity") == 0);

    sampleOrientation(sensor,100001U); // Time gap, despite physically valid gravity.
    record = sensor.getOrientation();
    assert(!record.referenceValid && record.invalidations == 2);
    assert(strcmp(record.invalidReason,"sample_gap") == 0);
    assert(record.invalidSampleValid && record.invalidSampleUs == fakeMicros);
    assert(record.invalidSampleIntervalUs == 100001U && record.invalidGravityNormG == 1.f);
    collectReference(sensor);
    Wire.failRead = true;
    fakeMicros += 10000U;
    assert(!sensor.update());
    record = sensor.getOrientation();
    assert(!record.referenceValid && record.invalidations == 3);
    assert(strcmp(record.invalidReason,"read_failed") == 0);
    assert(!record.invalidSampleValid && record.invalidSampleUs == fakeMicros);
    assert(record.invalidSampleIntervalUs == 10000U);
    assert(record.invalidGravityNormG == 0 && record.invalidAccelX == 0 &&
        record.invalidAccelY == 0 && record.invalidAccelZ == 0);
    const uint32_t firstFailedUs = record.invalidSampleUs;
    fakeMicros += 10000U;
    assert(!sensor.update());
    fakeMicros += 10000U;
    assert(!sensor.update() && !sensor.isInitialized());
    assert(sensor.getOrientation().invalidations == 3);
    assert(sensor.getOrientation().invalidSampleUs == firstFailedUs);
    Wire.failRead = false;
    assert(sensor.begin()); // Automatic sensor recovery retains the actual cause.
    assert(sensor.getOrientation().invalidations == 3);
    assert(strcmp(sensor.getOrientation().invalidReason,"read_failed") == 0);
    assert(sensor.getOrientation().invalidSampleUs == firstFailedUs);
    puts("IMU invalidation diagnostics PASS: failed-read/gap/gravity reasons retain the triggering timestamp and sample, count one transition, and survive new references and initialization retries.");
}

int main() {
    ImuSensor sensor;
    assert(sensor.getHealth().identity == -1 && !sensor.getHealth().biasCalibrated);
    assert(sensor.begin());
    assert(sensor.isInitialized());
    assert(sensor.getHealth().identity == 0x68 && sensor.getHealth().biasCalibrated);
    delay(10);
    assert(sensor.update());
    assert(sensor.getRawAxes().accelY == 1.f);
    assert(sensor.getRawAxes().sampleIntervalUs == 10000);
    Wire.failRead = true;
    assert(!sensor.update());
    assert(strcmp(sensor.getHealth().state,"sample_read_failed") == 0);
    assert(sensor.getHealth().readErrors == 1);
    assert(Wire.available() == 0); // Drain the truncated response.
    assert(sensor.getRawAxes().sampleIntervalUs == 10000); // Not replaced by garbage.
    Wire.failRead = false;
    delay(10);
    assert(sensor.update());
    assert(strcmp(sensor.getHealth().state,"ready") == 0);
    assert(sensor.getRawAxes().integrationGaps == 1);
    delay(300); // Model a blocking thermal acquisition.
    assert(sensor.update());
    assert(sensor.getRawAxes().integrationGaps == 2);
    Wire.failRead = true;
    assert(!sensor.update() && !sensor.update() && !sensor.update());
    assert(!sensor.isInitialized());
    assert(!sensor.getHealth().ready && !sensor.getHealth().biasCalibrated);
    Wire.failRead = false;
    Wire.gyroY = 655; // 10 degrees/s during bias calibration must be refused.
    assert(!sensor.begin());
    assert(strcmp(sensor.getHealth().state,"calibration_moving") == 0);
    Wire.gyroY = 0;
    Wire.failWrite = true;
    assert(!sensor.begin());
    assert(strcmp(sensor.getHealth().state,"identity_read_failed") == 0);
    assert(sensor.getHealth().identity == -1);
    Wire.failWrite = false;
    Wire.identity = 0x70;
    assert(!sensor.begin());
    assert(strcmp(sensor.getHealth().state,"unexpected_identity") == 0);
    assert(sensor.getHealth().identity == 0x70);
    Wire.identity = 0x68;
    Wire.failConfiguration = true;
    assert(!sensor.begin());
    assert(strcmp(sensor.getHealth().state,"configuration_failed") == 0);
    Wire.failConfiguration = false;
    assert(sensor.begin());
    assert(sensor.getHealth().initAttempts == 6 && sensor.getHealth().readErrors == 5);
    puts("IMU driver PASS: short reads, freshness, gaps, moving calibration, identity/configuration health, retry counters.");
    validateOrientationReference();
    validateWarmOrientationResidual();
    validateTimeWeightedResidual();
    validateOrientationInvalidationDiagnostics();
}
