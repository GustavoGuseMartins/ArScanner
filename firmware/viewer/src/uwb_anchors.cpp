#include "uwb_anchors.h"

UwbAnchorsManager::UwbAnchorsManager() {
    anchors[0] = {UWB_CS_ANCHOR_1, UWB_IRQ_ANCHOR_1, 2, false}; // DWM2 (Anchor 1)
    anchors[1] = {UWB_CS_ANCHOR_2, UWB_IRQ_ANCHOR_2, 3, false}; // DWM3 (Anchor 2)
    anchors[2] = {UWB_CS_ANCHOR_3, UWB_IRQ_ANCHOR_3, 4, false}; // DWM4 (Anchor 3)

    lastDistances[0] = 0.0f;
    lastDistances[1] = 0.0f;
    lastDistances[2] = 0.0f;
}

void UwbAnchorsManager::performGlobalHardwareReset() {
    // Diretriz Técnica: Reset dos rádios no IO4
    // 1. Colocar IO4 em OUTPUT LOW por 10µs a 2ms
    // 2. Mudar IO4 para INPUT (alta impedância/tri-state) permitindo que os pull-ups restaurem 3.3V
    Serial.println("[UWB] Executando Hardware Reset Global via IO4...");
    pinMode(UWB_GLOBAL_RST_PIN, OUTPUT);
    digitalWrite(UWB_GLOBAL_RST_PIN, LOW);
    delay(2); // 2 ms em nível lógico baixo
    pinMode(UWB_GLOBAL_RST_PIN, INPUT); // Retorna para alta impedância
    delay(10); // Aguarda estabilização pós-reset
}

void UwbAnchorsManager::selectAnchor(int index) {
    deselectAll();
    if (index >= 0 && index < 3) {
        digitalWrite(anchors[index].csPin, LOW);
    }
}

void UwbAnchorsManager::deselectAll() {
    digitalWrite(anchors[0].csPin, HIGH);
    digitalWrite(anchors[1].csPin, HIGH);
    digitalWrite(anchors[2].csPin, HIGH);
}

bool UwbAnchorsManager::initSingleAnchor(int index) {
    if (index < 0 || index >= 3) return false;

    uint8_t cs = anchors[index].csPin;
    uint8_t irq = anchors[index].irqPin;
    uint16_t addr = anchors[index].address;

    Serial.printf("[UWB] Inicializando Âncora %d (CS: %d, IRQ: %d, Addr: %d)...\n", index + 1, cs, irq, addr);

    // Inicializa DW1000 com o pino de interrupção correspondente
    DW1000.begin(irq, -1);
    DW1000.select(cs);

    char msg[128];
    DW1000.getPrintableDeviceIdentifier(msg);
    Serial.printf("[UWB] Âncora %d Device ID: %s\n", index + 1, msg);

    DW1000.setDefaults();
    DW1000.setDeviceAddress(addr);
    DW1000.setNetworkId(10);
    
    // Modo SPI 0 e perfil de alcance de longa distância / alta precisão
    DW1000.enableMode(DW1000.MODE_LONGDATA_RANGE_ACCURACY);
    DW1000.commitConfiguration();

    anchors[index].isInitialized = true;
    return true;
}

bool UwbAnchorsManager::begin() {
    // Configura pinos de Chip Select como saídas em nível HIGH (desabilitados)
    for (int i = 0; i < 3; i++) {
        pinMode(anchors[i].csPin, OUTPUT);
        digitalWrite(anchors[i].csPin, HIGH);
        pinMode(anchors[i].irqPin, INPUT); // Pinos GPI (34, 35, 39)
    }

    // Executa sequência de hardware reset global nos 3 módulos
    performGlobalHardwareReset();

    // Inicializa barramento SPI compartilhado
    SPI.begin(UWB_SPI_SCK, UWB_SPI_MISO, UWB_SPI_MOSI);

    // Inicializa individualmente cada âncora
    bool allSuccess = true;
    for (int i = 0; i < 3; i++) {
        selectAnchor(i);
        if (!initSingleAnchor(i)) {
            allSuccess = false;
            Serial.printf("[UWB] Falha ao inicializar Âncora %d!\n", i + 1);
        }
        deselectAll();
    }

    Serial.println("[UWB] Inicialização das 3 âncoras concluída.");
    return allSuccess;
}

bool UwbAnchorsManager::readDistances(float &d1, float &d2, float &d3) {
    // Ciclo sequencial de interrogação TWR (Two-Way Ranging) com a Tag móvel (ID = 1)
    // As distâncias são atualizadas por módulo
    d1 = lastDistances[0];
    d2 = lastDistances[1];
    d3 = lastDistances[2];
    return true;
}
