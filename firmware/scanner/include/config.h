#ifndef CONFIG_H
#define CONFIG_H

#include <Arduino.h>

// ==========================================
// PINOUT ESP32-S3 (Freenove WROOM CAM N16R8)
// ==========================================

// Barramento I2C (Compartilhado entre Câmera Térmica MLX90640 e IMU MPU6050)
#define I2C_SDA_PIN         47
#define I2C_SCL_PIN         48
#define I2C_FREQ            400000L  // 400kHz Fast Mode

// Barramento SPI (Módulo UWB BU01 / DW1000 Tag)
#define UWB_MOSI_PIN        39
#define UWB_MISO_PIN        40
#define UWB_SCK_PIN         41
#define UWB_CS_PIN          42
#define UWB_IRQ_PIN         2
#define UWB_RST_PIN         -1  // Conectado ao RST global ou pull-up

// Comunicação LiDAR (UART + PWM do Motor do LiDAR)
#define LIDAR_RX_PIN        1
#define LIDAR_TX_PIN        3
#define LIDAR_PWM_PIN       21
#define LIDAR_BAUD          115200

// Driver Motor de Passo (TMC2209)
#define STEPPER_STEP_PIN    45
#define STEPPER_DIR_PIN     46
#define STEPPER_MICROSTEPS  16
#define STEPS_PER_REV       (200 * STEPPER_MICROSTEPS)

// Configurações de Rede TCP
#define WIFI_SSID           "ArScanner_Net"
#define WIFI_PASS           "scanner123"
#define TCP_PORT            8888

// ==========================================
// ESTRUTURA DE DADOS ENVIADA VIA TCP
// ==========================================

#pragma pack(push, 1)
struct ScanPointPacket {
    float baseAngleDeg;      // Ângulo de rotação do NEMA 14 (0° a 360°)
    float lidarAngleDeg;     // Ângulo interno da cabeça do LiDAR
    float distanceMm;        // Distância medida pelo LiDAR em mm
    float temperatureC;      // Temperatura associada do ponto (MLX90640)
    uint8_t r, g, b;         // Cor RGB da câmera OV2640 no ponto
    uint32_t timestampMs;    // Timestamp do pacote
};
#pragma pack(pop)

#endif // CONFIG_H
