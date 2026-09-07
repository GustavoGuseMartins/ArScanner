#ifndef VIEWER_CONFIG_H
#define VIEWER_CONFIG_H

#include <Arduino.h>

// ==========================================
// PINOUT ESP32-WROOM-32 (NodeMCU-32S / DevKit V1)
// Documentação: Assets/gemini-code-1787612425528.md
// ==========================================

// Barramento SPI Compartilhado para os 3 Módulos UWB DWM1000
#define UWB_SPI_SCK         18  // IO18 (Pino 30) - SPICLK compartilhado
#define UWB_SPI_MISO        19  // IO19 (Pino 31) - SPIMISO compartilhado
#define UWB_SPI_MOSI        23  // IO23 (Pino 37) - SPIMOSI compartilhado
#define UWB_GLOBAL_RST_PIN  4   // IO4  (Pino 26) - ~RST Global ligado em paralelo

// Âncora 1: DWM2 (Anchor 1)
#define UWB_CS_ANCHOR_1     5   // IO5  (Pino 29) - ~SPICS Anchor 1
#define UWB_IRQ_ANCHOR_1    34  // IO34 (Pino 5)  - IRQ Anchor 1 (Entrada exclusiva GPI)

// Âncora 2: DWM3 (Anchor 2)
#define UWB_CS_ANCHOR_2     17  // IO17 (Pino 28) - ~SPICS Anchor 2
#define UWB_IRQ_ANCHOR_2    35  // IO35 (Pino 6)  - IRQ Anchor 2 (Entrada exclusiva GPI)

// Âncora 3: DWM4 (Anchor 3)
#define UWB_CS_ANCHOR_3     16  // IO16 (Pino 27) - ~SPICS Anchor 3
#define UWB_IRQ_ANCHOR_3    39  // GPIO39 / SENSOR_VN (Pino 4) - IRQ Anchor 3 (Entrada exclusiva GPI)

// Posições espaciais das 3 âncoras na base física de referência (em metros)
struct Vector3D {
    float x;
    float y;
    float z;
};

// Layout padrão das âncoras na base triangular de trilateração
const Vector3D ANCHOR_1_POS = {0.0f, 0.0f, 0.0f};
const Vector3D ANCHOR_2_POS = {1.0f, 0.0f, 0.0f};
const Vector3D ANCHOR_3_POS = {0.5f, 1.0f, 0.0f};

// Estrutura de dados de posição tridimensional transmitida para o Unity
#pragma pack(push, 1)
struct UwbPositionPacket {
    float tagX;
    float tagY;
    float tagZ;
    float distAnchor1;
    float distAnchor2;
    float distAnchor3;
    uint32_t timestampMs;
};
#pragma pack(pop)

#endif // VIEWER_CONFIG_H
