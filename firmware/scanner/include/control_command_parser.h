#ifndef CONTROL_COMMAND_PARSER_H
#define CONTROL_COMMAND_PARSER_H

#include <stdint.h>
#include <string.h>

// TCP is a byte stream: a command's payload may arrive in a later read.
struct ControlCommand {
    uint8_t type;
    uint8_t payload[4];
};

class ControlCommandParser {
public:
    ControlCommandParser() { reset(); }

    void reset() { pending = {}; received = 0; expected = 0; }

    bool feed(uint8_t value, ControlCommand &result) {
        if (expected == 0) {
            pending = {};
            pending.type = value;
            received = 0;
            if (value == 0x04) expected = 4;       // SET_SPEED: float
            else if (value == 0x05 || value == 0x06 || value == 0x07) expected = 1; // mode, LiDAR PWM, pan enabled
            else if ((value >= 0x01 && value <= 0x03) || value == 0x08 || value == 0x09) {
                result = pending;
                return true;
            }
            return false;
        }

        pending.payload[received++] = value;
        if (received < expected) return false;
        result = pending;
        reset();
        return true;
    }

private:
    ControlCommand pending;
    uint8_t received;
    uint8_t expected;
};

#endif
