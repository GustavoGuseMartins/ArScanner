#include "config.h"
#include "uwb_anchors.h"
#include "trilateration_3d.h"

UwbAnchorsManager uwbManager;

void setup() {
    Serial.begin(115200);
    delay(1000);
    Serial.println("Iniciando Base Receptora UWB (Visualizador)...");

    uwbManager.begin();
}

void loop() {
    float d1, d2, d3;
    if (uwbManager.readDistances(d1, d2, d3)) {
        Vector3D tagPos;
        Trilateration3D::calculatePosition(d1, d2, d3, tagPos);

        UwbPositionPacket pkt;
        pkt.tagX = tagPos.x;
        pkt.tagY = tagPos.y;
        pkt.tagZ = tagPos.z;
        pkt.timestampMs = millis();

        // Envia pacote serial/TCP para o Unity
        Serial.write((const uint8_t*)&pkt, sizeof(UwbPositionPacket));
    }

    delay(20); // 50Hz de amostragem de posição espacial UWB
}
