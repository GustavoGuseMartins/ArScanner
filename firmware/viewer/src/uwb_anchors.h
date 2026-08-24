#ifndef UWB_ANCHORS_H
#define UWB_ANCHORS_H

#include "config.h"
#include <SPI.h>
#include <DW1000.h>

class UwbAnchorsManager {
private:
    float lastDistances[3];

public:
    UwbAnchorsManager();
    bool begin();
    bool readDistances(float &d1, float &d2, float &d3);
};

#endif // UWB_ANCHORS_H
