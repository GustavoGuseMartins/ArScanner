#include "uwb_anchors.h"
#include "../../common/uwb_radio_exchange.h"

UwbAnchorsManager::UwbAnchorsManager() {
    anchors[0] = {UWB_CS_ANCHOR_1, UWB_IRQ_ANCHOR_1, 2, false}; // DWM2 (Anchor 1)
    anchors[1] = {UWB_CS_ANCHOR_2, UWB_IRQ_ANCHOR_2, 3, false}; // DWM3 (Anchor 2)
    anchors[2] = {UWB_CS_ANCHOR_3, UWB_IRQ_ANCHOR_3, 4, false}; // DWM4 (Anchor 3)

    seqNum = 0;
}

void UwbAnchorsManager::performGlobalHardwareReset() {
    Serial.println("[UWB-DIAG] Verificando pino de Reset Global (IO4)...");
    pinMode(UWB_GLOBAL_RST_PIN, INPUT_PULLUP);
    delay(5);
    int rstBefore = digitalRead(UWB_GLOBAL_RST_PIN);
    Serial.printf("[UWB-DIAG] Estado inicial do IO4 (INPUT_PULLUP): %s (GPIO %d = %d)\n", 
                  rstBefore ? "HIGH (3.3V)" : "LOW (0V - Atenção: rádio pode estar travado em reset)", 
                  UWB_GLOBAL_RST_PIN, rstBefore);

    Serial.println("[UWB] Executando Hardware Reset Global via IO4...");
    pinMode(UWB_GLOBAL_RST_PIN, OUTPUT);
    digitalWrite(UWB_GLOBAL_RST_PIN, LOW);
    delay(5); // 5 ms em nível lógico baixo
    pinMode(UWB_GLOBAL_RST_PIN, INPUT_PULLUP); // Retorna com pullup interno habilitado
    delay(20); // Aguarda estabilização pós-reset

    int rstAfter = digitalRead(UWB_GLOBAL_RST_PIN);
    Serial.printf("[UWB-DIAG] Estado pós-reset do IO4 (INPUT_PULLUP): %s (GPIO %d = %d)\n", 
                  rstAfter ? "HIGH (OK, fora de reset)" : "LOW (ERRO: Linha presa em 0V!)", 
                  UWB_GLOBAL_RST_PIN, rstAfter);
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

static void testPinsState() {
    Serial.println("\n===== TESTE ELETRICO DOS PINOS DO BARRAMENTO =====");
    
    // Testa pino de Reset (IO4)
    pinMode(UWB_GLOBAL_RST_PIN, INPUT_PULLUP);
    int rstPullup = digitalRead(UWB_GLOBAL_RST_PIN);
    pinMode(UWB_GLOBAL_RST_PIN, INPUT_PULLDOWN);
    int rstPulldown = digitalRead(UWB_GLOBAL_RST_PIN);
    pinMode(UWB_GLOBAL_RST_PIN, INPUT_PULLUP); // Mantém pullup
    Serial.printf("IO4 (RST): PullUp=%d, PullDown=%d\n", rstPullup, rstPulldown);

    // Testa pino MISO (IO19)
    pinMode(UWB_SPI_MISO, INPUT_PULLUP);
    int misoPullup = digitalRead(UWB_SPI_MISO);
    pinMode(UWB_SPI_MISO, INPUT_PULLDOWN);
    int misoPulldown = digitalRead(UWB_SPI_MISO);
    pinMode(UWB_SPI_MISO, INPUT);
    int misoFloating = digitalRead(UWB_SPI_MISO);
    Serial.printf("IO19 (MISO): PullUp=%d, PullDown=%d, Float=%d\n", misoPullup, misoPulldown, misoFloating);

    // Testa pinos IRQ (Pinos GPI 34, 35, 39)
    pinMode(UWB_IRQ_ANCHOR_1, INPUT);
    pinMode(UWB_IRQ_ANCHOR_2, INPUT);
    pinMode(UWB_IRQ_ANCHOR_3, INPUT);
    Serial.printf("IRQs: DWM2(IO34)=%d, DWM3(IO35)=%d, DWM4(IO39)=%d\n",
                  digitalRead(UWB_IRQ_ANCHOR_1),
                  digitalRead(UWB_IRQ_ANCHOR_2),
                  digitalRead(UWB_IRQ_ANCHOR_3));
    Serial.println("=================================================");
}

static uint32_t rawReadDevId(uint8_t csPin, uint32_t clockFreq, uint8_t spiMode) {
    SPISettings diagSettings(clockFreq, MSBFIRST, spiMode);
    SPI.beginTransaction(diagSettings);
    digitalWrite(csPin, LOW);
    delayMicroseconds(5);

    SPI.transfer(0x00);
    uint8_t b0 = SPI.transfer(0x00);
    uint8_t b1 = SPI.transfer(0x00);
    uint8_t b2 = SPI.transfer(0x00);
    uint8_t b3 = SPI.transfer(0x00);

    digitalWrite(csPin, HIGH);
    SPI.endTransaction();

    return ((uint32_t)b3 << 24) | ((uint32_t)b2 << 16) | ((uint32_t)b1 << 8) | b0;
}

static uint32_t bitbangReadDevId(uint8_t csPin) {
    pinMode(UWB_SPI_SCK, OUTPUT);
    pinMode(UWB_SPI_MOSI, OUTPUT);
    pinMode(UWB_SPI_MISO, INPUT);

    digitalWrite(UWB_CS_ANCHOR_1, HIGH);
    digitalWrite(UWB_CS_ANCHOR_2, HIGH);
    digitalWrite(UWB_CS_ANCHOR_3, HIGH);

    digitalWrite(UWB_SPI_SCK, LOW);
    digitalWrite(UWB_SPI_MOSI, LOW);
    delayMicroseconds(20);

    // Inicia transação
    digitalWrite(csPin, LOW);
    delayMicroseconds(20);

    // Envia byte 0x00 (comando de leitura do registrador DEV_ID 0x00)
    uint8_t cmd = 0x00;
    for (int i = 7; i >= 0; i--) {
        digitalWrite(UWB_SPI_MOSI, (cmd >> i) & 1);
        delayMicroseconds(10);
        digitalWrite(UWB_SPI_SCK, HIGH);
        delayMicroseconds(10);
        digitalWrite(UWB_SPI_SCK, LOW);
        delayMicroseconds(10);
    }

    // No Modo 0, o DW1000 coloca o primeiro bit na linha MISO logo após o último falling edge do comando.
    // Lemos o bit atual no nível baixo/subida e pulsamos o clock para o DW1000 avançar para o próximo bit.
    uint8_t bytes[4] = {0, 0, 0, 0};
    for (int b = 0; b < 4; b++) {
        for (int i = 7; i >= 0; i--) {
            int bit = digitalRead(UWB_SPI_MISO);
            bytes[b] = (bytes[b] << 1) | bit;
            digitalWrite(UWB_SPI_SCK, HIGH);
            delayMicroseconds(10);
            digitalWrite(UWB_SPI_SCK, LOW);
            delayMicroseconds(10);
        }
    }

    digitalWrite(csPin, HIGH);
    delayMicroseconds(20);

    uint32_t id = ((uint32_t)bytes[3] << 24) | ((uint32_t)bytes[2] << 16) | ((uint32_t)bytes[1] << 8) | bytes[0];
    Serial.printf("   [BitBang SPI CS:IO%d] Bytes: %02X %02X %02X %02X -> 0x%08X\n", 
                  csPin, bytes[0], bytes[1], bytes[2], bytes[3], id);
    return id;
}

bool UwbAnchorsManager::initSingleAnchor(int index) {
    if (index < 0 || index >= 3) return false;

    uint8_t cs = anchors[index].csPin;
    uint8_t irq = anchors[index].irqPin;
    uint16_t addr = anchors[index].address;
    anchors[index].isInitialized = false;

    Serial.printf("\n--- Testando Ancora %d (CS: IO%d, IRQ: IO%d, Addr: %d) ---\n", index + 1, cs, irq, addr);

    // 1. Teste Bit-Bang de validacao direta
    bitbangReadDevId(cs); // Logs the electrical test separately from operational SPI.

    // 2. Reinicializa periférico de Hardware SPI (necessário SPI.end() no ESP32 para reatrelar os pinos)
    SPI.end();
    SPI.begin(UWB_SPI_SCK, UWB_SPI_MISO, UWB_SPI_MOSI);
    delayMicroseconds(20);

    // 3. Teste apenas de leitura nos quatro modos: bit-bang lê DECA, mas o
    // periférico SPI vinha amostrando o mesmo DEV_ID com deslocamento.
    const uint8_t modes[] = {SPI_MODE0, SPI_MODE1, SPI_MODE2, SPI_MODE3};
    for (uint8_t mode = 0; mode < 4; ++mode) {
        uint32_t hwId = rawReadDevId(cs, 500000L, modes[mode]);
        Serial.printf("   [Hardware SPI @ 500k MODE%u] 0x%08lX\n", mode, (unsigned long)hwId);
    }

#ifdef ARSCANNER_VIEWER_SOFT_SPI
    SPI.end();
    pinMode(UWB_SPI_SCK, OUTPUT);
    pinMode(UWB_SPI_MOSI, OUTPUT);
    pinMode(UWB_SPI_MISO, INPUT);
    digitalWrite(UWB_SPI_SCK, LOW);
    digitalWrite(UWB_SPI_MOSI, LOW);
#endif

    // 4. Inicializa com a biblioteca DW1000 oficial
    DW1000.begin(irq, -1);
    // Um único driver atende os três CS sequencialmente. Não deixe um IRQ
    // de outra âncora acessar o rádio atualmente selecionado.
    detachInterrupt(digitalPinToInterrupt(irq));
    DW1000.select(cs);

    char msg[128];
    DW1000.getPrintableDeviceIdentifier(msg);
    Serial.printf("   [Biblioteca DW1000] Ancora %d Device ID: %s\n", index + 1, msg);

    // The operational driver must read the device; a bit-bang-only success is diagnostic.
    if (strstr(msg, "DECA") != NULL) {
        Serial.printf(">>> [SUCESSO TOTAL] Ancora %d identificada pelo driver SPI; ranging ainda precisa responder. <<<\n", index + 1);

        DW1000.setDefaults();
        DW1000.setDeviceAddress(addr);
        DW1000.setNetworkId(10);
        DW1000.enableMode(DW1000.MODE_LONGDATA_RANGE_ACCURACY);
        DW1000.setChannel(DW1000.CHANNEL_5);
        DW1000.setPreambleCode(DW1000.PREAMBLE_CODE_64MHZ_10);
        const uint16_t antennaDelays[3] = {
            UWB_ANCHOR_1_ANTENNA_DELAY_TICKS,
            UWB_ANCHOR_2_ANTENNA_DELAY_TICKS,
            UWB_ANCHOR_3_ANTENNA_DELAY_TICKS};
        DW1000.setAntennaDelay(antennaDelays[index]);
        DW1000.interruptOnSent(false);
        DW1000.interruptOnReceived(false);
        DW1000.interruptOnReceiveFailed(false);
        DW1000.interruptOnAutomaticAcknowledgeTrigger(false);
        DW1000.setReceiverAutoReenable(false);
        DW1000.commitConfiguration();

        anchors[index].isInitialized = true;
        return true;
    }

    return false;
}

bool UwbAnchorsManager::begin() {
    // Configura pinos de Chip Select como saídas em nível HIGH (todos desabilitados)
    for (int i = 0; i < 3; i++) {
        pinMode(anchors[i].csPin, OUTPUT);
        digitalWrite(anchors[i].csPin, HIGH);
        pinMode(anchors[i].irqPin, INPUT);
    }

    // Executa sequência de hardware reset global nos 3 módulos
    performGlobalHardwareReset();

    // Executa teste elétrico das linhas
    testPinsState();

    // Inicializa barramento SPI compartilhado
    Serial.printf("[UWB-DIAG] Inicializando SPI (SCK: IO%d, MISO: IO%d, MOSI: IO%d)...\n", 
                  UWB_SPI_SCK, UWB_SPI_MISO, UWB_SPI_MOSI);
    SPI.begin(UWB_SPI_SCK, UWB_SPI_MISO, UWB_SPI_MOSI);

    // Lê os pinos MISO e MOSI estaticamente
    Serial.printf("[UWB-DIAG] Estado em repouso - MISO (IO%d): %d | MOSI (IO%d): %d | SCK (IO%d): %d\n",
                  UWB_SPI_MISO, digitalRead(UWB_SPI_MISO),
                  UWB_SPI_MOSI, digitalRead(UWB_SPI_MOSI),
                  UWB_SPI_SCK, digitalRead(UWB_SPI_SCK));

    // Inicializa individualmente cada âncora SEM forçar CS em LOW antes
    bool allSuccess = true;
    for (int i = 0; i < 3; i++) {
        deselectAll();
        if (!initSingleAnchor(i)) {
            allSuccess = false;
        }
        deselectAll();
    }

    Serial.println("\n[UWB] Finalizado ciclo de inicialização das 3 âncoras.");
    return allSuccess;
}

bool UwbAnchorsManager::pollAnchor(int index, float &measuredDistance) {
    if (index < 0 || index >= 3) return false;
    stages[index] = 1;
    if (!anchors[index].isInitialized) return false;

    uint8_t cs = anchors[index].csPin;
    uint8_t anchorAddr = (uint8_t)anchors[index].address;

    // 1. Seleciona exclusivamente o rádio desta âncora
    DW1000.reselect(cs);
    // Recarrega os registradores deste rádio nos caches globais da biblioteca.
    DW1000.newConfiguration();
    // The library stores this delay globally and uses it in delayed FINAL TX.
    // Restore the selected module's value before ranging another anchor.
    const uint16_t antennaDelays[3] = {
        UWB_ANCHOR_1_ANTENNA_DELAY_TICKS,
        UWB_ANCHOR_2_ANTENNA_DELAY_TICKS,
        UWB_ANCHOR_3_ANTENNA_DELAY_TICKS};
    DW1000.setAntennaDelay(antennaDelays[index]);
    DW1000.receivePermanently(false);
    DW1000.clearAllStatus();

    auto finish = [this](bool success) {
        DW1000.idle();
        DW1000.clearAllStatus();
        deselectAll();
        return success;
    };

    // 2. Prepara e transmite pacote POLL
    UwbTwrPollPacket pollPkt;
    pollPkt.msgType = UWB_TWR_MSG_POLL;
    pollPkt.anchorAddr = anchorAddr;
    pollPkt.tagAddr = 1;
    pollPkt.seq = ++seqNum;

    DW1000.newTransmit();
    DW1000.setDefaults();
    DW1000.setData((byte*)&pollPkt, sizeof(pollPkt));
    DW1000.startTransmit();

    stages[index] = 2;

    // Inclui preâmbulo longo de 2048 símbolos e polling SPI.
    unsigned long txStart = millis();
    do {
        DW1000.readSystemEventStatusRegister();
        if (DW1000.isTransmitDone()) break;
        delayMicroseconds(10);
    } while (millis() - txStart < 10);

    if (!DW1000.isTransmitDone()) {
        DW1000.clearTransmitStatus();
        return finish(false);
    }

    DW1000Time pollTxTime;
    DW1000.getTransmitTimestamp(pollTxTime);
    DW1000.clearTransmitStatus();

    // 3. Coloca o rádio em modo de recepção da resposta da Tag
    DW1000.newReceive();
    DW1000.setDefaults();
    DW1000.receivePermanently(false);
    DW1000.startReceive();

    stages[index] = 3;

    // Aguarda RESPONSE da Tag, incluindo seu intervalo de atendimento.
    unsigned long rxStart = millis();
    do {
        DW1000.readSystemEventStatusRegister();
        if (DW1000.isReceiveDone() || DW1000.isReceiveFailed() || DW1000.isReceiveTimeout()) break;
        delayMicroseconds(20);
    } while (millis() - rxStart < 20);

    if (!DW1000.isReceiveDone()) {
        DW1000.clearReceiveStatus();
        return finish(false);
    }

    // 4. Lê dados do pacote RESPONSE recebido
    UwbTwrResponsePacket respPkt;
    stages[index] = 4;
    uint16_t dataLen = DW1000.getDataLength();
    if (dataLen != sizeof(respPkt)) {
        DW1000.clearReceiveStatus();
        return finish(false);
    }

    DW1000.getData((byte*)&respPkt, sizeof(respPkt));

    DW1000Time respRxTime;
    DW1000.getReceiveTimestamp(respRxTime);
    DW1000.clearReceiveStatus();

    // Validação de integridade do cabeçalho
    if (respPkt.msgType != UWB_TWR_MSG_RESPONSE || 
        respPkt.anchorAddr != anchorAddr || 
        respPkt.tagAddr != 1 || respPkt.seq != pollPkt.seq) {
        return finish(false);
    }

    // 5. DS-TWR: FINAL reports anchor intervals; tag uses its own two intervals.
    // Do not subtract durations from unsynchronized crystals (old SS-TWR).
    DW1000.newTransmit();
    DW1000.setDefaults();
    DW1000Time finalTx = DW1000.setDelay(DW1000Time(5000, DW1000Time::MICROSECONDS));
    UwbTwrFinalPacket finalPkt = {UWB_TWR_MSG_FINAL, anchorAddr, 1, pollPkt.seq,
        (respRxTime-pollTxTime).wrap().getTimestamp(),
        (finalTx-respRxTime).wrap().getTimestamp()};
    DW1000.setData((byte*)&finalPkt, sizeof(finalPkt));
    DW1000.startTransmit();
    stages[index] = 5;
    if (!uwbWaitSent()) return finish(false);
    DW1000Time actualTx;
    DW1000.getTransmitTimestamp(actualTx);
    DW1000.clearTransmitStatus();
    stages[index] = 6;
    if (actualTx.getTimestamp() != finalTx.getTimestamp()) return finish(false);
    stages[index] = 7;
    UwbTwrReportPacket report;
    DW1000Time reportRx;
    if (!uwbReceive(report, reportRx) || !uwbMatches(report, pollPkt, UWB_TWR_MSG_REPORT) ||
        !isfinite(report.distanceMeters) || report.distanceMeters < 0.08f || report.distanceMeters > 35.0f)
        return finish(false);
    measuredDistance = report.distanceMeters;
    stages[index] = 0;
    return finish(true);
}

uint8_t UwbAnchorsManager::initializedMask() const {
    uint8_t mask = 0;
    for (int i=0;i<3;++i) if (anchors[i].isInitialized) mask |= 1U << i;
    return mask;
}

bool UwbAnchorsManager::retryMissing() {
    for (int i=0;i<3;++i) {
        if (!anchors[i].isInitialized) {
            deselectAll();
            initSingleAnchor(i);
            deselectAll();
        }
    }
    return initializedMask() == 7;
}

bool UwbAnchorsManager::readDistances(float &d1, float &d2, float &d3) {
    int validCount = 0;
    cycleValid = false;
    // Ciclo sequencial de interrogação TWR com a Tag móvel (ID = 1)
    for (int i = 0; i < 3; i++) {
        latestRanges[i] = -1;
        rangeTimeMs[i] = 0;
        float measured = 0.0f;
        if (pollAnchor(i, measured)) {
            latestRanges[i] = measured;
            rangeTimeMs[i] = millis();
            validCount++;
        }
        delayMicroseconds(500); // Guarda de estabilização do barramento
    }

    // Never report a fresh position assembled from old ranges after a failed poll.
    if (validCount < 3 || (uint32_t)(rangeTimeMs[2]-rangeTimeMs[0]) > 100) return false;
    d1 = latestRanges[0];
    d2 = latestRanges[1];
    d3 = latestRanges[2];
    cycleValid = true;

    return true;
}
