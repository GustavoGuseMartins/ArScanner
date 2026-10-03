#ifndef UWB_TAG_H
#define UWB_TAG_H

#include "config.h"
#include <SPI.h>
#include <DW1000.h>
#include <DW1000Ranging.h>

struct UwbTagDiagnostics {
    uint32_t polls = 0, responses = 0, finals = 0, reports = 0, rxErrors = 0;
    uint32_t lastPollMs = 0;
    uint8_t stage = 1; // 1=absent, 0=listening, 2=poll, 3=response TX, 4=final RX, 5=range, 6=report TX, 7=complete
};

class UwbTag {
private:
    bool initialized = false;
    UwbTagDiagnostics stats;

public:
    UwbTag();
    bool begin();
    bool isInitialized() const { return initialized; }
    void updateRanging();
    UwbTagDiagnostics diagnostics() const { return stats; } // Owner task only; publish snapshot for HTTP.
};

#endif // UWB_TAG_H
