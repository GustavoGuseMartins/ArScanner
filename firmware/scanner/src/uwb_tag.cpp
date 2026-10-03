#include "uwb_tag.h"
#include "../../common/uwb_radio_exchange.h"

UwbTag::UwbTag() {}


bool UwbTag::begin() {
    initialized = false;
    stats.stage = 1;
    Serial.println("\n===== INICIALIZANDO UWB TAG DWM1000 (SCANNER) =====");
    
    // Configura CS da Tag em HIGH
    pinMode(UWB_CS_PIN, OUTPUT);
    digitalWrite(UWB_CS_PIN, HIGH);
    delay(10);

    // Inicializa DW1000 com pino de IRQ (GPIO 2)
    DW1000.begin(UWB_IRQ_PIN, UWB_RST_PIN);
    // O protocolo usa polling explícito. O ISR da biblioteca limpa os mesmos
    // status e não pode concorrer com as transações SPI deste fluxo.
    detachInterrupt(digitalPinToInterrupt(UWB_IRQ_PIN));
    DW1000.select(UWB_CS_PIN);

    char msg[128];
    DW1000.getPrintableDeviceIdentifier(msg);
    Serial.print("   [DW1000 Lib] Tag ID: ");
    Serial.println(msg);

    if (strstr(msg, "DECA") != NULL) {
        Serial.println(">>> [SUCESSO TOTAL] DW1000 Tag reconhecido e operacional! <<<");
    } else {
        Serial.println("[UWB] Tag indisponivel: DEV_ID nao retornou DECA. Confira alimentacao e SPI.");
        return false;
    }

    // Configuração do rádio DWM1000
    DW1000.setDefaults();
    DW1000.setDeviceAddress(1);  // Endereço da Tag móvel = 1
    DW1000.setNetworkId(10);     // Rede do sistema = 10
    
    // Configura IRQ para Push-Pull Active-High e modo de dados padrão
    DW1000.enableMode(DW1000.MODE_LONGDATA_RANGE_ACCURACY);
    // enableMode não altera o preâmbulo 4 deixado por setDefaults (PRF 16 MHz).
    // Canal 5 com PRF 64 MHz exige um código compatível, igual nas duas pontas.
    DW1000.setChannel(DW1000.CHANNEL_5);
    DW1000.setPreambleCode(DW1000.PREAMBLE_CODE_64MHZ_10);
    DW1000.setAntennaDelay(UWB_TAG_ANTENNA_DELAY_TICKS);
    DW1000.interruptOnSent(false);
    DW1000.interruptOnReceived(false);
    DW1000.interruptOnReceiveFailed(false);
    DW1000.interruptOnAutomaticAcknowledgeTrigger(false);
    DW1000.setReceiverAutoReenable(false);
    DW1000.commitConfiguration();

    // Coloca o rádio em modo de escuta permanente
    DW1000.newReceive();
    DW1000.setDefaults();
    DW1000.receivePermanently(false);
    DW1000.startReceive();

    initialized = true;
    stats.stage = 0;
    Serial.println("UWB Tag inicializado com sucesso (Modo Escuta TWR ativo).\n");
    return true;
}

void UwbTag::updateRanging() {
    if (!initialized) return;
    DW1000.reselect(UWB_CS_PIN);
    DW1000.readSystemEventStatusRegister();

    if (DW1000.isReceiveDone()) {
        uint16_t len = DW1000.getDataLength();
        if (len == sizeof(UwbTwrPollPacket)) {
            UwbTwrPollPacket pollPkt;
            DW1000.getData((byte*)&pollPkt, sizeof(pollPkt));

            // Valida se é um frame de POLL para esta Tag (ID 1)
            if (pollPkt.msgType == UWB_TWR_MSG_POLL && pollPkt.tagAddr == 1 &&
                pollPkt.anchorAddr >= 2 && pollPkt.anchorAddr <= 4) {
                DW1000Time pollRxTime;
                ++stats.polls;
                stats.lastPollMs = millis();
                stats.stage = 2;
                DW1000.getReceiveTimestamp(pollRxTime);
                DW1000.clearReceiveStatus();

                UwbTwrResponsePacket respPkt;
                respPkt.msgType = UWB_TWR_MSG_RESPONSE;
                respPkt.anchorAddr = pollPkt.anchorAddr;
                respPkt.tagAddr = 1;
                respPkt.seq = pollPkt.seq;
                DW1000Time responseTx, finalRx;
                UwbTwrFinalPacket finalPacket;
                stats.stage = 3;
                bool sent = uwbSend(respPkt, responseTx);
                if (sent) { ++stats.responses; stats.stage = 4; }
                if (sent && uwbReceive(finalPacket, finalRx) &&
                    uwbMatches(finalPacket, pollPkt, UWB_TWR_MSG_FINAL)) {
                    ++stats.finals;
                    stats.stage = 5;
                    float distance;
                    if (uwbDistance(finalPacket.round1,
                            (responseTx-pollRxTime).wrap().getTimestamp(),
                            (finalRx-responseTx).wrap().getTimestamp(), finalPacket.reply2, distance)) {
                        UwbTwrReportPacket report = {UWB_TWR_MSG_REPORT,
                            pollPkt.anchorAddr, 1, pollPkt.seq, distance};
                        DW1000Time reportTx;
                        stats.stage = 6;
                        if (uwbSend(report, reportTx)) { ++stats.reports; stats.stage = 7; }
                    }
                }
            } else {
                DW1000.clearReceiveStatus();
            }
        } else {
            DW1000.clearReceiveStatus();
        }

        // Reativa escuta para o próximo ciclo de interrogação
        DW1000.newReceive();
        DW1000.setDefaults();
        DW1000.clearAllStatus();
        DW1000.receivePermanently(false);
        DW1000.startReceive();
    } else if (DW1000.isReceiveFailed() || DW1000.isReceiveTimeout()) {
        ++stats.rxErrors;
        DW1000.clearReceiveStatus();
        DW1000.newReceive();
        DW1000.setDefaults();
        DW1000.receivePermanently(false);
        DW1000.startReceive();
    }
}
