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
    float lastDistances[3];

    void performGlobalHardwareReset();
    bool initSingleAnchor(int index);

public:
    UwbAnchorsManager();
    bool begin();
    bool readDistances(float &d1, float &d2, float &d3);
    void selectAnchor(int index);
    void deselectAll();
};

#endif // UWB_ANCHORS_H
