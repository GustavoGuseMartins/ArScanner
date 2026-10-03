#include "config.h"
#include "uwb_anchors.h"
#include "trilateration_3d.h"

UwbAnchorsManager uwbManager;
Trilateration3D trilateration;
bool allAnchorsReady = false;
unsigned long lastLogMs = 0;
uint32_t lastInitRetryMs = 0, lastStatusMs = 0;
int positionState = 0; // 0=radios missing, 1=incomplete, 2=geometry, 3=uncertainty, 4=valid
float positionSigma = -1;

void publishStatus() {
    // Fresh ranges are already acquired as one TWR cycle; publish each cycle
    // up to 10 Hz so moving the scanner does not wait on a 2 Hz UI feed.
    if (millis()-lastStatusMs < 100) return;
    lastStatusMs = millis();
    char body[400];
    int n = snprintf(body, sizeof(body),
        "{\"kind\":\"uwb_status\",\"version\":2,\"timestampMs\":%lu,\"radioMask\":%u,"
        "\"state\":%d,\"d1\":%.4f,\"d2\":%.4f,\"d3\":%.4f,\"sigma\":%.3f,"
        "\"s1\":%u,\"s2\":%u,\"s3\":%u,\"cycleValid\":%s,"
        "\"t1Ms\":%lu,\"t2Ms\":%lu,\"t3Ms\":%lu}", (unsigned long)millis(),
        uwbManager.initializedMask(), positionState, uwbManager.latestRanges[0],
        uwbManager.latestRanges[1], uwbManager.latestRanges[2], positionSigma,
        uwbManager.stages[0], uwbManager.stages[1], uwbManager.stages[2],
        uwbManager.cycleValid ? "true" : "false", (unsigned long)uwbManager.rangeTimeMs[0],
        (unsigned long)uwbManager.rangeTimeMs[1], (unsigned long)uwbManager.rangeTimeMs[2]);
    if (n <= 0 || (size_t)n >= sizeof(body)) return;
    Serial.println(body); // One framed JSON line; logs cannot become position bytes.
}

void setup() {
    Serial.begin(115200);
    delay(1500);
    Serial.println("\n==============================================");
    Serial.println("Iniciando Base Receptora UWB (Visualizador)...");
    Serial.println("==============================================");

    // Os três alcances seguem diretamente para o telefone pelo CP210x/USB-C.
    // Somente o scanner usa Wi-Fi nesta montagem.
    Serial.println("[USB] Base UWB serial 115200 pronta para o celular.");

    allAnchorsReady = uwbManager.begin();
    if (!allAnchorsReady) {
        Serial.println("\n[AVISO] Uma ou mais ancoras UWB nao responderam na inicializacao.");
        Serial.println("[DIAGNOSTICO] Repetindo teste de leitura a cada 5 segundos...");
    } else {
        Serial.println("\n[OK] Todas as ancoras UWB inicializadas com sucesso!");
    }
}

void loop() {
    if (!allAnchorsReady) {
        if (millis()-lastInitRetryMs >= 5000) {
            lastInitRetryMs = millis();
            allAnchorsReady = uwbManager.retryMissing();
        }
        if (uwbManager.initializedMask() == 0) {
            positionState = 0;
            publishStatus();
            delay(20);
            return;
        }
    }

    float d1 = 0.0f, d2 = 0.0f, d3 = 0.0f;
    positionState = 1;
    positionSigma = -1;
    if (uwbManager.readDistances(d1, d2, d3)) {
        Vector3D tagPos;
        if (trilateration.calculatePosition(d1, d2, d3, tagPos)) {
            positionState = 4;
            positionSigma = trilateration.estimatedPositionSigma;
            UwbPositionPacket pkt;
            pkt.tagX = tagPos.x;
            pkt.tagY = tagPos.y;
            pkt.tagZ = tagPos.z;
            pkt.distAnchor1 = d1;
            pkt.distAnchor2 = d2;
            pkt.distAnchor3 = d3;
            pkt.timestampMs = millis();

            // 1. Serial com enquadramento ASCII; a bridge decodifica os 28 bytes.
            Serial.print("@UWB28:");
            for (size_t i=0;i<sizeof(pkt);++i) Serial.printf("%02X", ((const uint8_t*)&pkt)[i]);
            Serial.println();

            // Log diagnóstico periódico no monitor serial.
            if (millis() - lastLogMs > 1000) {
                lastLogMs = millis();
                Serial.printf("[UWB-TRACK] d1=%.2fm, d2=%.2fm, d3=%.2fm => POS(X=%.2f, Y=%.2f, Z=%.2f)m | USB serial\n",
                              d1, d2, d3, tagPos.x, tagPos.y, tagPos.z);
            }
        }
        else {
            positionSigma = isfinite(trilateration.estimatedPositionSigma)
                ? trilateration.estimatedPositionSigma : -1;
            positionState = positionSigma < 0 ? 2 : 3;
        }
    } else {
        trilateration.reset();
        if (millis()-lastLogMs > 1000) {
            lastLogMs = millis();
            Serial.println("[UWB-REJECT] ciclo incompleto; sem reutilizar distancias antigas.");
        }
    }

    publishStatus();
    delay(20); // Pausa entre ciclos; taxa inclui as quatro mensagens por ancora.
}
