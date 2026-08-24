#ifndef VIEWER_CONFIG_H
#define VIEWER_CONFIG_H

#include <Arduino.h>

// Pinos SPI compartilhados para os 3 Módulos UWB DW1000
#define UWB_SPI_MOSI 23
#define UWB_SPI_MISO 19
#define UWB_SPI_SCK  18

// Chip Selects para as 3 Âncoras UWB
#define UWB_CS_ANCHOR_1 5
#define UWB_CS_ANCHOR_2 4
#define UWB_CS_ANCHOR_3 15

#define UWB_IRQ_PIN 27
#define UWB_RST_PIN 14

// Posições das 3 âncoras na base física (em metros)
struct Vector3D {
    float x, y, z;
};

const Vector3D ANCHOR_1_POS = {0.0f, 0.0f, 0.0f};
const Vector3D ANCHOR_2_POS = {1.0f, 0.0f, 0.0f};
const Vector3D ANCHOR_3_POS = {0.5f, 1.0f, 0.0f};

// Estrutura de dados enviada para o Unity via TCP/Serial
#pragma pack(push, 1)
struct UwbPositionPacket {
    float tagX;
    float tagY;
    float tagZ;
    uint32_t timestampMs;
};
#pragma pack(pop)

#endif // VIEWER_CONFIG_H
