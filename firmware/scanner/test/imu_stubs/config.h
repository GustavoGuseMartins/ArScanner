#pragma once
#include <stdint.h>
#include <algorithm>
using std::min;
#define PI 3.14159265358979323846f
#define IMU_PITCH_AXIS 0
#define IMU_ROLL_AXIS 1
#define IMU_PITCH_SIGN (-1.f)
#define IMU_ROLL_SIGN 1.f
#define IMU_MOUNT_NOMINAL_PITCH_DEG 16.2f
#define IMU_MOUNT_NOMINAL_ROLL_DEG  (-1.1f)
extern uint32_t fakeMicros;
inline uint32_t micros() { return fakeMicros; }
inline uint32_t millis() { return fakeMicros / 1000U; }
inline void delay(unsigned ms) { fakeMicros += ms*1000U; }
struct FakeSerial { void println(const char*) {} };
extern FakeSerial Serial;
