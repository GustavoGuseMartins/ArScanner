#ifndef UWB_TAG_H
#define UWB_TAG_H

#include "config.h"
#include <SPI.h>
#include <DW1000.h>
#include <DW1000Ranging.h>

class UwbTag {
private:
    static void handleReceived();
    static void handleSent();

public:
    UwbTag();
    bool begin();
    void updateRanging();
};

#endif // UWB_TAG_H
