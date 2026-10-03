// Compile with -I firmware/scanner/test/imu_stubs before the real include path,
// linking src/imu_mpu6050.cpp. No hardware or third-party driver is involved.
#include "../src/imu_mpu6050.h"
#include <cassert>
#include <cstdio>
#include <cstring>
uint32_t fakeMicros = 1000;
FakeSerial Serial;
FakeWire Wire;

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
}
