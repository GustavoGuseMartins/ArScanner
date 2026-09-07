#include "uwb_tag.h"

UwbTag::UwbTag() {}

bool UwbTag::begin() {
    // Inicializa barramento SPI com pinos definidos no esquemático do Scanner
    SPI.begin(UWB_SCK_PIN, UWB_MISO_PIN, UWB_MOSI_PIN, UWB_CS_PIN);

    // Inicializa DW1000 com pino de IRQ (GPIO 2)
    DW1000.begin(UWB_IRQ_PIN, UWB_RST_PIN);
    DW1000.select(UWB_CS_PIN);

    char msg[128];
    DW1000.getPrintableDeviceIdentifier(msg);
    Serial.print("DW1000 Tag ID: ");
    Serial.println(msg);

    // Configuração do rádio DWM1000
    DW1000.setDefaults();
    DW1000.setDeviceAddress(1);  // Endereço da Tag móvel = 1
    DW1000.setNetworkId(10);     // Rede do sistema = 10
    
    // Configura IRQ para Push-Pull Active-High e modo de dados padrão
    DW1000.enableMode(DW1000.MODE_LONGDATA_RANGE_ACCURACY);
    DW1000.commitConfiguration();

    Serial.println("UWB Tag inicializado com sucesso (SPI Mode 0, IRQ Active-High).");
    return true;
}

void UwbTag::updateRanging() {
    // Rotina de resposta TWR (Two-Way Ranging) com as âncoras da base receptora
}
