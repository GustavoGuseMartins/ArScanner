#ifndef CONFIG_H
#define CONFIG_H

#include <Arduino.h>

// ==========================================
// PINOUT ESP32-S3 (ESP32-S3-DEVKITC-1-N16R8V)
// Documentação: Assets/gemini-code-1787612425528.md
// ==========================================

// Barramento I2C Compartilhado (MLX90640 + MPU6050)
// Pull-ups de 4.7k (R2 e R4) para 3.3V no hardware
#define I2C_SDA_PIN         47
#define I2C_SCL_PIN         48
#define I2C_FREQ            1000000L  // 1MHz I2C Fast Mode Plus

// Barramento SPI (Módulo UWB Decawave DWM1000 - DWM1 Tag)
#define UWB_MOSI_PIN        4   // GPIO4 - SPIMOSI
#define UWB_MISO_PIN        5   // GPIO5 - SPIMISO
#define UWB_SCK_PIN         41  // GPIO41 (MTDI) - SPICLK
#define UWB_CS_PIN          42  // GPIO42 (MTMS) - ~SPICS
#define UWB_IRQ_PIN         2   // GPIO2 - IRQ (Interrupção de rádio)
#define UWB_RST_PIN         -1  // Conectado ao RST global com pull-up

// Comunicação Serial LiDAR (Conector J3)
#define LIDAR_TX_PIN        1   // GPIO1 - TX do ESP32 -> RX LiDAR
#define LIDAR_RX_PIN        3   // GPIO3 - RX do ESP32 <- TX LiDAR
#define LIDAR_BAUD          115200

// Chaveamento de Carga / Motor LiDAR (MOSFET IRLZ44N Q1)
#define LIDAR_MOSFET_GATE_PIN 21 // GPIO21 - PWM / Chaveamento do Motor LiDAR

// Driver do Motor de Passo (TMC2209 U3)
#define STEPPER_STEP_PIN    45  // GPIO45 - STEP (Passo do motor)
#define STEPPER_DIR_PIN     46  // GPIO46 - DIR (Sentido de rotação)
#define STEPPER_MICROSTEPS  16
#define STEPS_PER_REV       (200 * STEPPER_MICROSTEPS)

// Câmera RGB OV2640 (Freenove WROOM CAM DVP)
#define PWDN_GPIO_NUM       -1
#define RESET_GPIO_NUM      -1
#define XCLK_GPIO_NUM       15
#define SIOD_GPIO_NUM       4
#define SIOC_GPIO_NUM       5
#define Y9_GPIO_NUM         16
#define Y8_GPIO_NUM         17
#define Y7_GPIO_NUM         18
#define Y6_GPIO_NUM         12
#define Y5_GPIO_NUM         10
#define Y4_GPIO_NUM         8
#define Y3_GPIO_NUM         9
#define Y2_GPIO_NUM         11
#define VSYNC_GPIO_NUM      6
#define HREF_GPIO_NUM       7
#define PCLK_GPIO_NUM       13

// Geometria das Câmeras no Suporte Frontal
// Câmera Superior: OV2640 RGB | Câmera Inferior: MLX90640 Térmica
#define CAMERA_LENS_OFFSET_Y_MM   18.0f // Deslocamento vertical entre centros das lentes (mm)
#define THERMAL_FOV_H_DEG         110.0f // MLX90640ESF-BAA Wide Angle
#define THERMAL_FOV_V_DEG         75.0f

// Configurações de Rede TCP / Wi-Fi SoftAP
#define WIFI_SSID           "ArScanner_Net"
#define WIFI_PASS           "scanner123"
#define TCP_PORT            8888
#define POINTS_PER_BATCH    15   // Agrupamento de 15 pontos por pacote TCP anti-latência

// ==========================================
// ESTRUTURA DE DADOS ENVIADA VIA TCP (28 BYTES)
// ==========================================

#pragma pack(push, 1)
struct ScanPointPacket {
    float posX_mm;           // Coordenada X calculada no ESP32-S3 (mm)
    float posY_mm;           // Coordenada Y calculada no ESP32-S3 (mm)
    float posZ_mm;           // Coordenada Z calculada no ESP32-S3 (mm)
    float temperatureC;      // Temperatura da MLX90640 no ponto (°C)
    uint8_t r, g, b;         // Cor real amostrada da OV2640
    uint8_t surfaceFlags;    // Flags: 0=Ponto livre, 1=Superficie planar (parede), 2=Hotspot (>28C)
    int16_t pitchCentiDeg;   // Pitch do drone em centigraus (MPU6050 com inversão de montagem)
    int16_t rollCentiDeg;    // Roll do drone em centigraus (MPU6050)
    uint32_t timestampMs;    // Timestamp sincronizado do pacote
};
#pragma pack(pop)

#endif // CONFIG_H
