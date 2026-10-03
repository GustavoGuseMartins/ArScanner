#pragma once
#include <stdint.h>
#define PI 3.14159265358979323846f
#define IMU_PITCH_AXIS 0
#define IMU_ROLL_AXIS 1
#define IMU_PITCH_SIGN (-1.f)
#define IMU_ROLL_SIGN 1.f
extern uint32_t fakeMicros;
inline uint32_t micros() { return fakeMicros; }
inline void delay(unsigned ms) { fakeMicros += ms*1000U; }
struct FakeSerial { void println(const char*) {} };
extern FakeSerial Serial;
