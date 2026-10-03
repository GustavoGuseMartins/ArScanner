#ifndef VIEWER_CONFIG_H
#define VIEWER_CONFIG_H

#include <Arduino.h>
#include "../../common/uwb_twr.h"

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

// Library fallback values. Each radio needs its own measured antenna delay;
// the DWM1000 datasheet section 2.1.3 does not prescribe a universal value.
#define UWB_ANCHOR_1_ANTENNA_DELAY_TICKS 16384U
#define UWB_ANCHOR_2_ANTENNA_DELAY_TICKS 16384U
#define UWB_ANCHOR_3_ANTENNA_DELAY_TICKS 16384U

// Configuração de Wi-Fi e UDP para transmissão sem fio ao App Unity
#define WIFI_SSID           "ArScanner_Net"
#define WIFI_PASSWORD       "scanner123"
#define UDP_BROADCAST_PORT  9999

// Centros das antenas informados para a PCB montada em 26/09/2026:
// base 175 mm, lados iguais de 100 mm. Altura = sqrt(100^2 - 87.5^2) = 48.412 mm.
// DWM3 = esquerda, DWM4 = direita, DWM2 = topo. Confirmar com paquímetro.
struct Vector3D {
    float x;
    float y;
    float z;
};

const Vector3D ANCHOR_1_POS = {0.0875f, 0.0484123f, 0.0f}; // DWM2 (Topo)
const Vector3D ANCHOR_2_POS = {0.0f, 0.0f, 0.0f};         // DWM3 (Esquerda)
const Vector3D ANCHOR_3_POS = {0.175f, 0.0f, 0.0f};        // DWM4 (Direita)
// These are measured physical centres, still an approximation to RF phase centres.
// Three ranges have two mirror solutions. This selects an OPERATING assumption.
#define UWB_PLANE_SIDE 1.0f
#define UWB_RANGE_SIGMA_M 0.10f // Modelo provisório de ruído (1-sigma ~10cm)
#define UWB_MAX_POSITION_SIGMA_M 0.50f // Distances remain visible when position uncertainty is too large.

// Protocolo Two-Way Ranging (TWR) entre Âncoras e Tag
#pragma pack(push, 1)

// Estrutura de dados de posição tridimensional transmitida para o Unity
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
