#pragma once
#include "Arduino.h"
#include <array>
#include <deque>
#include <vector>

class TwoWire {
public:
    std::array<uint16_t, 65536> registers = {};
    std::vector<uint8_t> tx;
    std::deque<uint8_t> rx;
    std::vector<uint8_t> requestSizes;
    std::vector<uint16_t> requestAddresses;
    std::vector<uint32_t> probeClocks, eepromStartClocks;
    uint16_t address = 0;
    uint32_t clock = 100000, requestMs = 1, requests = 0;
    uint16_t timeout = 100;
    unsigned eepromWrites = 0, readCalls = 0, negativeReadAt = UINT32_MAX;
    bool unreportedShortRead = false;
    bool differingSmallEepromRead = false;
    bool failFastEepromRead = false, failSlowEepromRead = false, failFastSmallEepromRead = false;
    bool failFastProbe = false, failFastControlRead = false, mismatchControlWrite = false;
    uint32_t fastEepromRequestMs = 0;
    bool failRead = false, failWrite = false, shortRead = false, continuousReady = false;
    void fixture() {
        registers.fill(0); rx.clear(); tx.clear();
        requestSizes.clear(); requestAddresses.clear(); eepromWrites = 0; readCalls = 0; negativeReadAt = UINT32_MAX;
        probeClocks.clear(); eepromStartClocks.clear();
        unreportedShortRead = differingSmallEepromRead = false;
        failFastEepromRead = failSlowEepromRead = failFastSmallEepromRead = false;
        failFastProbe = failFastControlRead = mismatchControlWrite = false;
        fastEepromRequestMs = 0;
        failRead = failWrite = shortRead = continuousReady = false;
        requestMs = 1; requests = 0;
        for (unsigned i = 0; i < 832; ++i) registers[0x2400 + i] = 0x1000;
        registers[0x2400 + 17] = 1000;
        registers[0x2400 + 33] = 1000;
        registers[0x2400 + 48] = 1000;
        registers[0x2400 + 49] = 29000;
        registers[0x2400 + 50] = 64;
        registers[0x2400 + 51] = 0x0100;
        registers[0x2400 + 52] = 0;
        registers[0x2400 + 57] = 100; // Positive, finite compensation-pixel sensitivity.
        registers[0x2400 + 58] = 0;
        registers[0x2400 + 61] = registers[0x2400 + 62] = 0; // Valid range correction denominators.
        for (unsigned i = 0; i < 768; ++i) registers[0x0400 + i] = 10000;
        registers[0x0400 + 768] = 1000;
        registers[0x0400 + 776] = registers[0x0400 + 808] = 0;
        registers[0x0400 + 778] = 1000;
        registers[0x0400 + 800] = 1000;
        registers[0x0400 + 810] = uint16_t(-16384);
        registers[0x800D] = 0x1000; // Chess mode with automatic subpage operation disabled at startup.
    }
    bool simulateConversion() {
        const uint16_t control = registers[0x800D];
        if (!(control & 1U) || (control & 4U)) return false;
        // This deliberately models the control bits rather than forcing the
        // page IDs desired by the test. RAM values themselves stay synthetic.
        const unsigned page = control & 8U ? ((control >> 4) & 1U) : ((registers[0x8000] & 1U) ^ 1U);
        ready(page);
        return true;
    }
    void ready(unsigned page) { registers[0x8000] = uint16_t(8 | page); }
    void setTimeOut(uint16_t value) { timeout = value; }
    void setClock(uint32_t value) { clock = value; }
    uint32_t getClock() { return clock; }
    void beginTransmission(uint8_t) { tx.clear(); }
    void write(uint8_t value) { tx.push_back(value); }
    uint8_t endTransmission(bool = true) {
        if (tx.empty()) {
            probeClocks.push_back(clock);
            if (failFastProbe && clock == 400000) return 4;
        }
        if (failWrite) return 4;
        if (tx.size() >= 2) address = uint16_t((tx[0] << 8) | tx[1]);
        if (tx.size() == 4) {
            if (address >= 0x2400 && address < 0x2740) ++eepromWrites;
            uint16_t value = uint16_t((tx[2] << 8) | tx[3]);
            if (address == 0x8000) registers[address] &= uint16_t(~8U);
            else registers[address] = address == 0x800D && mismatchControlWrite ? uint16_t(value ^ 1U) : value;
        }
        return 0;
    }
    uint8_t requestFrom(uint8_t, uint8_t count) {
        const bool eeprom = address >= 0x2400 && address < 0x2740;
        if (address == 0x2400) eepromStartClocks.push_back(clock);
        ++requests; fakeMillis += eeprom && clock == 400000 && fastEepromRequestMs ? fastEepromRequestMs : requestMs;
        rx.clear(); readCalls = 0; requestSizes.push_back(count); requestAddresses.push_back(address);
        if (failRead) return 0;
        if (eeprom && ((clock == 400000 && (failFastEepromRead || (failFastSmallEepromRead && count == 16))) ||
                      (clock == 100000 && failSlowEepromRead))) return 0;
        if (address == 0x800D && failFastControlRead && clock == 400000) return 0;
        for (unsigned i = 0; i < count / 2; ++i) {
            uint16_t value = registers[uint16_t(address + i)];
            if (differingSmallEepromRead && count == 16 && uint16_t(address+i) == 0x2445)
                value ^= 2; // A transport-dependent discrepancy, not an EEPROM mutation.
            rx.push_back(uint8_t(value >> 8)); rx.push_back(uint8_t(value));
        }
        if (continuousReady && address >= 0x0400 && address < 0x0400 + 832)
            registers[0x8000] |= 8;
        if (shortRead) { rx.pop_back(); return count - 1; }
        if (unreportedShortRead) rx.pop_back();
        return count;
    }
    int available() { return int(rx.size()); }
    int read() {
        if (readCalls++ == negativeReadAt) return -1;
        if (rx.empty()) return -1;
        int result = rx.front(); rx.pop_front(); return result;
    }
};
extern TwoWire Wire;
