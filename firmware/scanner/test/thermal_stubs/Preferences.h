#pragma once
#include <stdint.h>
class Preferences {
public:
    bool begin(const char*, bool) { return true; }
    bool isKey(const char*) { return false; }
    int getInt(const char*, int value) { return value; }
    unsigned putInt(const char*, int) { return sizeof(int32_t); }
    void end() {}
};
