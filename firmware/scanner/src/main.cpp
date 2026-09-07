#include <WiFi.h>
#include <math.h>
#include "config.h"
#include "stepper_tmc2209.h"
#include "lidar_driver.h"
#include "thermal_mlx90640.h"
#include "camera_ov2640.h"
#include "imu_mpu6050.h"
#include "uwb_tag.h"

// Instâncias dos Sensores e Periféricos
StepperController stepper;
LidarDriver lidar;
ThermalSensor thermal;
CameraOv2640 camera;
ImuSensor imu;
UwbTag uwbTag;

// Servidor TCP Socket
WiFiServer tcpServer(TCP_PORT);
WiFiClient tcpClient;

// Fila do FreeRTOS para transporte inter-tarefas entre Core 1 e Core 0
QueueHandle_t scanQueue;

// Task Core 1: Coleta, Fusão Multissensorial e Projeção Matemática 3D
void TaskSensorCore1(void *pvParameters) {
    stepper.begin();
    stepper.setSpeedRpm(15.0f); // 15 RPM para varredura contínua

    thermal.begin();
    camera.begin();
    imu.begin();

    unsigned long lastThermalReadMs = 0;
    unsigned long lastCameraReadMs = 0;

    for (;;) {
        // Atualiza passo do motor TMC2209
        stepper.update();
        float currentBaseAngle = stepper.getCurrentAngle();

        // Atualização inercial contínua do MPU6050
        imu.update();
        float dronePitch = imu.getPitch();
        float droneRoll = imu.getRoll();

        // Leitura da câmera térmica a 16Hz (~62ms)
        if (millis() - lastThermalReadMs > 60) {
            thermal.updateFrame();
            lastThermalReadMs = millis();
        }

        // Leitura da câmera óptica OV2640 a ~15Hz (~66ms)
        if (millis() - lastCameraReadMs > 65) {
            camera.updateFrame();
            lastCameraReadMs = millis();
        }

        // Leitura do feixe do LiDAR
        LidarMeasurement measurement;
        if (lidar.readPacket(measurement)) {
            // Conversão de Coordenadas Esféricas para Cartesianas Locais no ESP32-S3 (FPU)
            float distMeters = measurement.distanceMm / 1000.0f;
            float baseRad = currentBaseAngle * DEG_TO_RAD;
            float lidarRad = measurement.angleDeg * DEG_TO_RAD;

            // Coordenadas antes da compensação de atitude
            float lx = distMeters * sinf(lidarRad) * cosf(baseRad);
            float ly = distMeters * cosf(lidarRad);
            float lz = distMeters * sinf(lidarRad) * sinf(baseRad);

            // Aplicação da matriz de rotação inercial do MPU6050 (Pitch e Roll do drone em voo)
            float pitchRad = dronePitch * DEG_TO_RAD;
            float rollRad = droneRoll * DEG_TO_RAD;

            // Rotação Pitch (eixo X) e Roll (eixo Z)
            float cx = lx * cosf(rollRad) - ly * sinf(rollRad);
            float cy = lx * sinf(rollRad) + ly * cosf(rollRad);
            float cz = lz * cosf(pitchRad) - cy * sinf(pitchRad);
            cy = lz * sinf(pitchRad) + cy * cosf(pitchRad);

            // Ângulos relativos ao eixo óptico frontal das câmeras para lookup
            float relAngleH = measurement.angleDeg - 90.0f;
            float relAngleV = currentBaseAngle;
            if (relAngleH > 180.0f) relAngleH -= 360.0f;

            // Amostragem da Câmera Superior (OV2640 RGB)
            uint8_t r = 255, g = 255, b = 255;
            camera.getPixelColor(relAngleH, relAngleV, r, g, b);

            // Amostragem da Câmera Inferior (MLX90640 Térmica 110°x75°)
            float tempC = thermal.getPointTemperature(relAngleH, relAngleV);

            // Classificação de flags: 2=Hotspot (>28°C), 1=Superfície planar/fria, 0=Ponto livre
            uint8_t flags = (tempC >= 28.0f) ? 2 : 1;

            ScanPointPacket pkt;
            pkt.posX_mm = cx * 1000.0f;
            pkt.posY_mm = cy * 1000.0f;
            pkt.posZ_mm = cz * 1000.0f;
            pkt.temperatureC = tempC;
            pkt.r = r;
            pkt.g = g;
            pkt.b = b;
            pkt.surfaceFlags = flags;
            pkt.pitchCentiDeg = (int16_t)(dronePitch * 100.0f);
            pkt.rollCentiDeg = (int16_t)(droneRoll * 100.0f);
            pkt.timestampMs = millis();

            // Envia para a fila inter-tarefas
            xQueueSend(scanQueue, &pkt, 0);
        }

        vTaskDelay(pdMS_TO_TICKS(1)); // Cede tempo para watchdog
    }
}

// Task Core 0: Wi-Fi, Servidor TCP com Batching Anti-Latência e UWB
void TaskNetworkCore0(void *pvParameters) {
    WiFi.softAP(WIFI_SSID, WIFI_PASS);
    IPAddress apIP = WiFi.softAPIP();
    Serial.print("Servidor TCP ArScanner iniciado no IP: ");
    Serial.println(apIP);

    tcpServer.begin();
    uwbTag.begin();

    ScanPointPacket batchBuffer[POINTS_PER_BATCH];
    int batchCount = 0;

    for (;;) {
        // Verifica conexão do cliente Unity
        if (!tcpClient || !tcpClient.connected()) {
            tcpClient = tcpServer.available();
            if (tcpClient) {
                Serial.println("[TCP Server] Novo cliente Unity conectado!");
                tcpClient.setNoDelay(true); // Desativa algoritmo de Nagle para latência zero
            }
        }

        if (tcpClient && tcpClient.connected()) {
            ScanPointPacket pkt;
            // Coleta pontos e envia em lotes de 15 pontos para otimizar pacotes TCP
            while (xQueueReceive(scanQueue, &pkt, 0) == pdTRUE) {
                batchBuffer[batchCount++] = pkt;

                if (batchCount >= POINTS_PER_BATCH) {
                    tcpClient.write((const uint8_t*)batchBuffer, sizeof(ScanPointPacket) * POINTS_PER_BATCH);
                    batchCount = 0;
                }
            }

            // Se sobrar pontos e o fluxo pausar, transmite o restante
            if (batchCount > 0) {
                tcpClient.write((const uint8_t*)batchBuffer, sizeof(ScanPointPacket) * batchCount);
                batchCount = 0;
            }
        } else {
            // Drena a fila para evitar estouro de memória quando desconectado
            ScanPointPacket discardPkt;
            while (xQueueReceive(scanQueue, &discardPkt, 0) == pdTRUE);
            batchCount = 0;
        }

        vTaskDelay(pdMS_TO_TICKS(2));
    }
}

void setup() {
    Serial.begin(115200);
    delay(1000);

    Serial.println("==============================================");
    Serial.println("Iniciando Scanner 3D Aereo (ESP32-S3)...");
    Serial.println("==============================================");

    lidar.begin();

    // Capacidade da fila FreeRTOS ampliada para 250 medições
    scanQueue = xQueueCreate(250, sizeof(ScanPointPacket));

    xTaskCreatePinnedToCore(TaskSensorCore1, "SensorCore1", 10240, NULL, 2, NULL, 1);
    xTaskCreatePinnedToCore(TaskNetworkCore0, "NetworkCore0", 8192, NULL, 1, NULL, 0);
}

void loop() {
    vTaskDelay(pdMS_TO_TICKS(1000));
}
