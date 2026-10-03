#ifndef UWB_ANCHORS_H
#define UWB_ANCHORS_H

#include "config.h"
#include <SPI.h>
#include <DW1000.h>
#include <DW1000Ranging.h>

struct AnchorModule {
    uint8_t csPin;
    uint8_t irqPin;
    uint16_t address;
    bool isInitialized;
};

class UwbAnchorsManager {
private:
    AnchorModule anchors[3];
    uint8_t seqNum;

    void performGlobalHardwareReset();
    bool initSingleAnchor(int index);
    bool pollAnchor(int index, float &measuredDistance);

public:
    // 0=ready/range OK, 1=radio absent, 2=poll TX, 3=response RX,
    // 4=response header, 5=final TX, 6=final timestamp, 7=report RX/range.
    uint8_t stages[3] = {1,1,1};
    float latestRanges[3] = {-1,-1,-1};
    uint32_t rangeTimeMs[3] = {0,0,0}; // End of each successful exchange, viewer clock.
    bool cycleValid = false;
    uint8_t initializedMask() const;
    UwbAnchorsManager();
    bool begin();
    bool retryMissing();
    bool readDistances(float &d1, float &d2, float &d3);
    void selectAnchor(int index);
    void deselectAll();
};

#endif // UWB_ANCHORS_H
