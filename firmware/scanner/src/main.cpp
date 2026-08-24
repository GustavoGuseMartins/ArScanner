#include <WiFi.h>
#include "config.h"
#include "stepper_tmc2209.h"
#include "lidar_driver.h"
#include "thermal_mlx90640.h"
#include "imu_mpu6050.h"
#include "uwb_tag.h"

// Instâncias dos Sensores e Periféricos
StepperController stepper;
LidarDriver lidar;
ThermalSensor thermal;
ImuSensor imu;
UwbTag uwbTag;

// Servidor TCP Socket
WiFiServer tcpServer(TCP_PORT);
WiFiClient tcpClient;

// Fila do FreeRTOS para transporte inter-tarefas entre Core 1 e Core 0
QueueHandle_t scanQueue;

// Task Core 1: Coleta de Dados e Passo do Motor
void TaskSensorCore1(void *pvParameters) {
    stepper.begin();
    stepper.setSpeedRpm(15.0f); // 15 RPM para varredura suave

    thermal.begin();
    imu.begin();

    unsigned long lastThermalReadMs = 0;

    for (;;) {
        // Atualiza passo do motor TMC2209
        stepper.update();
        float currentBaseAngle = stepper.getCurrentAngle();

        // Leitura da câmera térmica a 16Hz (~62ms)
        if (millis() - lastThermalReadMs > 60) {
            thermal.updateFrame();
            lastThermalReadMs = millis();
        }

        // Leitura do LiDAR
        LidarMeasurement measurement;
        if (lidar.readPacket(measurement)) {
            ScanPointPacket pkt;
            pkt.baseAngleDeg = currentBaseAngle;
            pkt.lidarAngleDeg = measurement.angleDeg;
            pkt.distanceMm = measurement.distanceMm;
            pkt.temperatureC = thermal.getPointTemperature(measurement.angleDeg);
            pkt.r = 255; // Reservado para camera OV2640
            pkt.g = 255;
            pkt.b = 255;
            pkt.timestampMs = millis();

            // Envia pacote formatado para a fila sem bloquear o motor
            xQueueSend(scanQueue, &pkt, 0);
        }

        vTaskDelay(pdMS_TO_TICKS(1)); // Cede tempo para o watchdog
    }
}

// Task Core 0: Wi-Fi, Servidor TCP e UWB
void TaskNetworkCore0(void *pvParameters) {
    WiFi.softAP(WIFI_SSID, WIFI_PASS);
    IPAddress apIP = WiFi.softAPIP();
    Serial.print("Servidor TCP iniciado no IP: ");
    Serial.println(apIP);

    tcpServer.begin();
    uwbTag.begin();

    ScanPointPacket pktToTransmit;

    for (;;) {
        // Verifica se há um cliente Unity conectado via TCP
        if (!tcpClient || !tcpClient.connected()) {
            tcpClient = tcpServer.available();
            if (tcpClient) {
                Serial.println("Novo cliente Unity conectado via TCP!");
            }
        }

        // Se houver cliente conectado, drena a fila do FreeRTOS e transmite via TCP
        if (tcpClient && tcpClient.connected()) {
            while (xQueueReceive(scanQueue, &pktToTransmit, 0) == pdTRUE) {
                tcpClient.write((const uint8_t*)&pktToTransmit, sizeof(ScanPointPacket));
            }
        } else {
            // Se não houver cliente, apenas descarta itens velhos da fila para não estourar RAM
            while (xQueueReceive(scanQueue, &pktToTransmit, 0) == pdTRUE);
        }

        vTaskDelay(pdMS_TO_TICKS(2));
    }
}

void setup() {
    Serial.begin(115200);
    delay(1000);

    lidar.begin();

    // Cria a fila do FreeRTOS com capacidade para 100 pacotes de amostragem
    scanQueue = xQueueCreate(100, sizeof(ScanPointPacket));

    // Criação das tarefas FreeRTOS fixadas nos núcleos 0 e 1 do ESP32-S3
    xTaskCreatePinnedToCore(TaskSensorCore1, "SensorCore1", 8192, NULL, 2, NULL, 1);
    xTaskCreatePinnedToCore(TaskNetworkCore0, "NetworkCore0", 8192, NULL, 1, NULL, 0);
}

void loop() {
    // Loop principal mantido vazio pois o processamento roda nas Tasks FreeRTOS
    vTaskDelay(pdMS_TO_TICKS(1000));
}
