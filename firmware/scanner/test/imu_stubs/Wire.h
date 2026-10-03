#pragma once
#include <stdint.h>
struct FakeWire {
    bool failRead = false, failWrite = false;
    uint8_t identity = 0x68;
    bool failConfiguration = false;
    uint8_t reg = 0, written = 0;
    int availableBytes = 0, index = 0;
    int16_t gyroY = 0;
    void beginTransmission(uint8_t) { written = 0; }
    void write(uint8_t value) { if (written++ == 0) reg = value; }
    uint8_t endTransmission(bool = true) { return failWrite || (failConfiguration && written > 1) ? 4 : 0; }
    uint8_t requestFrom(uint8_t, uint8_t count) {
        index = 0;
        availableBytes = failRead ? count-1 : count;
        return availableBytes;
    }
    int available() { return availableBytes; }
    int read() {
        --availableBytes;
        if (reg == 0x75) return identity;
        const uint8_t data[14] = {0,0,0x40,0,0,0,0,0,0,0,
            uint8_t(uint16_t(gyroY)>>8), uint8_t(gyroY),0,0};
        return data[index++];
    }
};
extern FakeWire Wire;
