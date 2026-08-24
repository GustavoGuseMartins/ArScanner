#include "uwb_tag.h"

UwbTag::UwbTag() {}

bool UwbTag::begin() {
    SPI.begin(UWB_SCK_PIN, UWB_MISO_PIN, UWB_MOSI_PIN, UWB_CS_PIN);
    DW1000.begin(UWB_IRQ_PIN, UWB_RST_PIN);
    DW1000.select(UWB_CS_PIN);

    char msg[128];
    DW1000.getPrintableDeviceIdentifier(msg);
    Serial.print("DW1000 Tag ID: ");
    Serial.println(msg);

    DW1000.setDefaults();
    DW1000.setDeviceAddress(1); // Tag ID = 1
    DW1000.setNetworkId(10);
    DW1000.commitConfiguration();

    return true;
}

void UwbTag::updateRanging() {
    // Loop de resposta TWR (Two-Way Ranging) para responder às 3 âncoras da base receptora
}
