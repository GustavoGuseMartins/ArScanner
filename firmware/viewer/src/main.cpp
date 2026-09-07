#include "config.h"
#include "uwb_anchors.h"
#include "trilateration_3d.h"

UwbAnchorsManager uwbManager;

void setup() {
    Serial.begin(115200);
    delay(1000);
    Serial.println("==============================================");
    Serial.println("Iniciando Base Receptora UWB (Visualizador)...");
    Serial.println("==============================================");

    if (!uwbManager.begin()) {
        Serial.println("[AVISO] Uma ou mais âncoras UWB não responderam na inicialização.");
    } else {
        Serial.println("[OK] Todas as âncoras UWB inicializadas com sucesso.");
    }
}

void loop() {
    float d1 = 0.0f, d2 = 0.0f, d3 = 0.0f;
    if (uwbManager.readDistances(d1, d2, d3)) {
        Vector3D tagPos;
        Trilateration3D::calculatePosition(d1, d2, d3, tagPos);

        UwbPositionPacket pkt;
        pkt.tagX = tagPos.x;
        pkt.tagY = tagPos.y;
        pkt.tagZ = tagPos.z;
        pkt.distAnchor1 = d1;
        pkt.distAnchor2 = d2;
        pkt.distAnchor3 = d3;
        pkt.timestampMs = millis();

        // Envia pacote binário formatado via Serial/TCP para o visualizador Unity
        Serial.write((const uint8_t*)&pkt, sizeof(UwbPositionPacket));
    }

    delay(20); // Taxa de amostragem de 50Hz para posicionamento espacial suave
}
