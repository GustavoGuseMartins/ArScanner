#pragma once
#include <stdint.h>
#include <algorithm>
#include <cstdio>
extern uint32_t fakeMillis;
inline uint32_t millis() { return fakeMillis; }
inline void delay(unsigned value) { fakeMillis += value; }
using std::min;
using std::max;
template <typename T> T constrain(T value, T low, T high) { return value < low ? low : value > high ? high : value; }
struct FakeSerial {
    void println(const char *value) { puts(value); }
    template <typename... Args> void printf(const char *format, Args... args) { std::printf(format, args...); }
};
extern FakeSerial Serial;
