#ifndef UWB_TAG_H
#define UWB_TAG_H

#include "config.h"
#include <SPI.h>
#include <DW1000.h>

class UwbTag {
public:
    UwbTag();
    bool begin();
    void updateRanging();
};

#endif // UWB_TAG_H
