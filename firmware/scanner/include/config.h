#ifndef CONFIG_H
#define CONFIG_H

#include <Arduino.h>
#include "../../common/uwb_twr.h"

// ==========================================
// PINOUT ESP32-S3 (ESP32-S3-DEVKITC-1-N16R8V)
// Documentação: gemini-code-1787612425528.md
// ==========================================

// Barramento I2C Compartilhado (MLX90640 + MPU6050)
// Pull-ups de 4.7k (R2 e R4) para 3.3V no hardware
#define I2C_SDA_PIN         47
#define I2C_SCL_PIN         48
#define I2C_FREQ            100000L  // Inicio conservador; a termica acelera apos inicializar.
#define I2C_FAST_FREQ       400000L  // MLX90640 ate 16 subpaginas/s; volta a 100 kHz se as leituras falharem.

// Barramento SPI (Módulo UWB Decawave DWM1000 - DWM1 Tag)
#define UWB_MOSI_PIN        4   // GPIO4 - SPIMOSI
#define UWB_MISO_PIN        5   // GPIO5 - SPIMISO
#define UWB_SCK_PIN         41  // GPIO41 (MTDI) - SPICLK
#define UWB_CS_PIN          42  // GPIO42 (MTMS) - ~SPICS
#define UWB_IRQ_PIN         2   // GPIO2 - IRQ (Interrupção de rádio)
#define UWB_RST_PIN         -1  // DWM1 reset is not routed to the ESP32 in tcc.net.
// 16384 is the DW1000 library fallback, not a measured delay for this module.
// Replace only after a known-distance antenna-delay calibration (DWM1000 datasheet 2.1.3).
#define UWB_TAG_ANTENNA_DELAY_TICKS 16384U
#ifndef UWB_TAG_ENABLED
#define UWB_TAG_ENABLED 1
#endif

// Comunicação Serial LiDAR LDS02RR (Conector J3; modelo informado pelo autor)
// Conforme esquemático tcc.net:
// J3 Pino 2 = GPIO1 (TX do LDS02RR -> RX do ESP32-S3)
// J3 Pino 3 = GPIO3 (RX do LDS02RR <- TX do ESP32-S3)
#define LIDAR_RX_PIN        1   // GPIO1 - RX do ESP32 <- TX LiDAR (J3 Pino 2)
#define LIDAR_TX_PIN        3   // GPIO3 - TX do ESP32 -> RX LiDAR (J3 Pino 3)
#define LIDAR_BAUD          115200

// Chaveamento de Carga / Motor LiDAR (MOSFET IRLZ44N Q1)
#define LIDAR_MOSFET_GATE_PIN 21 // GPIO21 - PWM / Chaveamento do Motor LiDAR
#define LIDAR_PWM_CHANNEL     0 // Camera XCLK reserves channel 7 / timer 3.

// Driver do Motor de Passo (TMC2209 U3)
#define STEPPER_STEP_PIN    45  // GPIO45 - STEP (Passo do motor)
#define STEPPER_DIR_PIN     46  // GPIO46 - DIR (Sentido de rotação)
// Nominal scanner_v2: fixed 180T gear, 10T pinion carried by rotating head.
// MS1/MS2 open in tcc.net => chip pull-downs select 8 (verify module jumpers/OTP).
// These constants describe STEP INPUT pulses, not the internal 256 interpolation.
#define STEPPER_MICROSTEPS  8
#define STEPPER_FULL_STEPS  200
#define STEPPER_GEAR_RATIO  18
#define STEPS_PER_REV       (STEPPER_FULL_STEPS * STEPPER_MICROSTEPS * STEPPER_GEAR_RATIO)
#define STEPPER_PAN_SIGN    (-1.0f) // Motor anti-horário visto de cima; inverte para convenção Unity (CW+).
#define STEPPER_ZERO_DEG    0.0f // Physical zero confirmed by the operator; no homing/encoder feedback.
// Bump when the mechanical reference, gearing, pulse scale or convention changes.
#define PAN_REFERENCE_CALIBRATION_VERSION 1U

// Medidas REAIS da montagem física (23/09/2026):
// LiDAR vertical: ângulo 0° aponta para CIMA (+Y). Motor DC sem controle de posição.
// Eixo óptico 50 mm acima do pan. A referência física de 23/09 informou 90 mm
// à frente; o código usa 79 mm nominais do modelo V2, ainda a conferir na unidade.
#define LIDAR_ORIGIN_X_M    0.0f
#define LIDAR_ORIGIN_Y_M    0.050f   // 50 mm acima do eixo pan (medido)
#define LIDAR_ORIGIN_Z_M    0.079f   // 79 mm nominal no modelo 3D scanner_v2_montado.blend (centro da fenda Y=-79mm)
#define LIDAR_MOUNT_YAW_DEG 90.0f    // Plano XY vertical; orientação de montagem separada da origem óptica.
#define LIDAR_ANGLE_SIGN    1.0f     // Provisorio: +1 faz raw 90 apontar -X; -1 faz apontar +X. Validar em bancada.
#define LIDAR_ZERO_DEG      90.0f    // Ângulo 0° do protocolo aponta para cima (+Y); +90° corrige.
#define LIDAR_LATENCY_US    0U       // Latência adicional do sensor, medir experimentalmente.
#define LIDAR_MAX_AGE_US    200000U
// The TCP point frame is the pan axis, so no tag translation is applied to
// points here. The viewer subtracts the measured radio offset from the UWB
// tag position to recover this pan-axis origin.
#define TAG_OFFSET_X_M      0.0f
#define TAG_OFFSET_Y_M      0.0f
#define TAG_OFFSET_Z_M      0.0f
#define TAG_PHYSICAL_OFFSET_X_M (-0.0135f) // Deslocamento lateral medido no .blend (blindagem DWM1000 a X=-13.5mm)
#define TAG_PHYSICAL_OFFSET_Y_M 0.120f // 120 mm above the pan axis, measured.
#define TAG_PHYSICAL_OFFSET_Z_M 0.020f // 20 mm forward; rotates with the head.
#define TAG_ON_ROTATING_HEAD true
#define IMU_ON_ROTATING_HEAD true
// A aplicação de pitch/roll é independente do acompanhamento yaw GY-25 no app.
// Os ângulos nominais abaixo ainda exigem calibração física e ensaio da nuvem.
#define IMU_APPLY_TILT      true // Aplica a inclinação filtrada expressa nos eixos da cabeça.
#define IMU_MOUNT_NOMINAL_PITCH_DEG 16.2f // Referência nominal da PCB montada na cabeça.
#define IMU_MOUNT_NOMINAL_ROLL_DEG  (-1.1f)
#define CAMERA_EXTRINSICS_CALIBRATED false // RGB ainda sem alinhamento/pinagem simultânea.

// Mapeamento dos eixos do MPU6050 (módulo azul, montado atrás do LiDAR no PCB).
// Ajustar sinais após diagnóstico de /status com imuRaw.
#define IMU_PITCH_SIGN      (-1.0f) // Inversão da montagem traseira (verificar)
#define IMU_ROLL_SIGN       1.0f    // Verificar com tilt lateral
// 0=X, 1=Y, 2=Z. Provisional mapping; photo does not establish the axes.
// Z is gyro-integrated and drifts; axis selection alone is not 3D calibration.
#define IMU_PITCH_AXIS      0
#define IMU_ROLL_AXIS       1
#ifndef CALIBRATION_DIAGNOSTIC_MODE
#define CALIBRATION_DIAGNOSTIC_MODE true // CSV diagnostics; TCP stays 28 bytes.
#endif
static_assert(IMU_PITCH_AXIS >= 0 && IMU_PITCH_AXIS <= 2, "Invalid pitch axis");
static_assert(IMU_ROLL_AXIS >= 0 && IMU_ROLL_AXIS <= 2, "Invalid roll axis");
static_assert(IMU_PITCH_AXIS != IMU_ROLL_AXIS, "Tilt axes must differ");

// Camera lives on the S3-CAM daughterboard (flat cable), not on the carrier netlist.
// 0=unverified map; 1=common Freenove/S3-EYE map (verify board revision).
// Profile 1 shares GPIO4/5 with UWB: isolate the tag for testing, rewire for concurrent use.
#ifndef RGB_BOARD_PROFILE
#define RGB_BOARD_PROFILE 0
#endif
#if RGB_BOARD_PROFILE == 1
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
#else
#define PWDN_GPIO_NUM       -1
#define RESET_GPIO_NUM      -1
#define XCLK_GPIO_NUM       -1
#define SIOD_GPIO_NUM       -1
#define SIOC_GPIO_NUM       -1
#define Y9_GPIO_NUM         -1
#define Y8_GPIO_NUM         -1
#define Y7_GPIO_NUM         -1
#define Y6_GPIO_NUM         -1
#define Y5_GPIO_NUM         -1
#define Y4_GPIO_NUM         -1
#define Y3_GPIO_NUM         -1
#define Y2_GPIO_NUM         -1
#define VSYNC_GPIO_NUM      -1
#define HREF_GPIO_NUM       -1
#define PCLK_GPIO_NUM       -1
#endif

// Geometria das Câmeras no Suporte Frontal
// Câmera Superior: OV2640 RGB | Câmera Inferior: MLX90640 Térmica
#define CAMERA_LENS_OFFSET_Y_MM   20.0f // Deslocamento vertical medido no .blend (Z_rgb 147mm - Z_term 127mm)
// O MLX90640ESF-BAA tem 110 x 75 graus em sua matriz nativa (32 x 24).
// A montagem requer girar a visualizacao 90 graus para a direita.
// Na cabeca, o FOV efetivo torna-se 75 graus na horizontal e 110 na
// vertical (imagem corrigida de 24 x 32 pixels).
#define THERMAL_SENSOR_FOV_X_DEG  110.0f
#define THERMAL_SENSOR_FOV_Y_DEG   75.0f
// Centro da lente termica em coordenadas da cabeca, relativo ao LiDAR.
// Perfil de orientacao termica salvo no ESP: 0=-X normal (hipotese anterior),
// 1=+X normal, 2=-X espelhado na horizontal, 3=+X espelhado. Alterar no app
// apos teste com alvo quente; o valor salvo prevalece sobre este padrao.
// O relato de imagem no lado oposto motiva testar +X primeiro, ainda sem prova
// do sinal fisico do LiDAR/lente em bancada.
#define THERMAL_ORIENTATION_PROFILE_DEFAULT 1
// Alvo unico: 8 quadros completos/s (16 subpaginas/s). Preferencias antigas
// de taxa sao ignoradas. Overruns descartam a cópia incoerente sem reduzir o
// alvo; o fallback de transporte pode reduzir clock/cadência. Alvo não é taxa medida.
#define THERMAL_DEFAULT_FULL_FPS 8
static_assert(THERMAL_DEFAULT_FULL_FPS == 8, "Thermal acquisition target must be 8 complete frames/s");
// Offset da lente em coordenadas fisicas da cabeca, independente do perfil.
#define THERMAL_OFFSET_X_M       0.0f
#define THERMAL_OFFSET_Y_M      -0.025f
#define THERMAL_OFFSET_Z_M       0.050f
#define THERMAL_INVERT_LIDAR_Y   true // Deve acompanhar o espelhamento vertical padrão do app.
#define THERMAL_FUSION_MIN_RANGE_M 0.40f // Paralaxe excessiva perto da lente.

// Configurações de Rede TCP / Wi-Fi SoftAP
#define WIFI_SSID           "ArScanner_Net"
#define WIFI_PASS           "scanner123"
#define TCP_PORT            8888
#define POINTS_PER_BATCH    15   // Agrupamento de 15 pontos por pacote TCP anti-latência

// ==========================================
// COMANDOS DE CONTROLE TCP (CELULAR -> ESP32-S3)
// ==========================================
#define CMD_START_SCAN        0x01
#define CMD_STOP_SCAN         0x02
#define CMD_HEARTBEAT         0x03
#define CMD_SET_SPEED         0x04  // Seguido de float (4 bytes): velocidade RPM do motor de passo
#define CMD_SET_MODE          0x05  // Seguido de uint8_t: 0=Contínuo 360°, 1=Ping-Pong 180°
#define CMD_SET_LIDAR_SPEED   0x06  // Seguido de uint8_t: velocidade PWM do motor do LiDAR (0-255)
#define CMD_SET_PAN_ENABLED   0x07  // byte 0=hold external axis, 1=rotate; LiDAR keeps measuring
#define CMD_PARK_PAN          0x08  // Return to the confirmed physical zero; requires heartbeat.
#define CMD_CONFIRM_PAN_ZERO  0x09  // No payload: head aligned to physical zero, scan/pan stopped.
// Imagens usam HTTP :8889 (/rgb e /thermal). O TCP contém apenas registros
// ScanPointPacket consecutivos, sem cabeçalho de lote nem quadros de imagem.
#define SURFACE_FLAG_HOTSPOT           0x02
#define SURFACE_FLAG_THERMAL_MISSING   0x04
#define SURFACE_FLAG_LIDAR_WEAK        0x08 // Sensor warning; not an invalid-distance flag.

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
    uint8_t surfaceFlags;    // Bits: 1=planar (reservado), 2=hotspot, 4=sem medição térmica
    int16_t pitchCentiDeg;   // Pitch do drone em centigraus (MPU6050 com inversão de montagem)
    int16_t rollCentiDeg;    // Roll do drone em centigraus (MPU6050)
    uint32_t timestampMs;    // millis() local do scanner; não sincronizado com a base
};

// ==========================================
// PROTOCOLO UWB TWO-WAY RANGING (TWR)
// ==========================================
#pragma pack(pop)

static_assert(sizeof(ScanPointPacket) == 28, "O protocolo TCP exige registros de 28 bytes");

#endif // CONFIG_H
