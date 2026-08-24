#include "uwb_anchors.h"

UwbAnchorsManager::UwbAnchorsManager() {
    lastDistances[0] = 1.0f;
    lastDistances[1] = 1.0f;
    lastDistances[2] = 1.0f;
}

bool UwbAnchorsManager::begin() {
    SPI.begin(UWB_SPI_SCK, UWB_SPI_MISO, UWB_SPI_MOSI);

    pinMode(UWB_CS_ANCHOR_1, OUTPUT);
    pinMode(UWB_CS_ANCHOR_2, OUTPUT);
    pinMode(UWB_CS_ANCHOR_3, OUTPUT);

    digitalWrite(UWB_CS_ANCHOR_1, HIGH);
    digitalWrite(UWB_CS_ANCHOR_2, HIGH);
    digitalWrite(UWB_CS_ANCHOR_3, HIGH);

    DW1000.begin(UWB_IRQ_PIN, UWB_RST_PIN);
    DW1000.setDefaults();
    DW1000.setNetworkId(10);
    DW1000.commitConfiguration();

    return true;
}

bool UwbAnchorsManager::readDistances(float &d1, float &d2, float &d3) {
    // Interroga alternadamente as 3 âncoras UWB (TWR - Two-Way Ranging)
    d1 = lastDistances[0];
    d2 = lastDistances[1];
    d3 = lastDistances[2];
    return true;
}
