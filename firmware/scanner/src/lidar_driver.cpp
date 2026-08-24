#include "lidar_driver.h"

LidarDriver::LidarDriver() : lidarSerial(1) {}

void LidarDriver::begin() {
    lidarSerial.begin(LIDAR_BAUD, SERIAL_8N1, LIDAR_RX_PIN, LIDAR_TX_PIN);
    pinMode(LIDAR_PWM_PIN, OUTPUT);
    setMotorSpeed(200); // Valor de PWM inicial para estabilizar giro do LiDAR (~5-7Hz)
}

void LidarDriver::setMotorSpeed(uint8_t pwmVal) {
    analogWrite(LIDAR_PWM_PIN, pwmVal);
}

bool LidarDriver::readPacket(LidarMeasurement &measurement) {
    // Parser genérico para pacotes LiDAR Roborock/Neato/XV11 (4 bytes por amostra ou pacote de 22 bytes)
    if (lidarSerial.available() >= 4) {
        uint8_t header = lidarSerial.read();
        if (header == 0xFA) { // Byte de início padrão de pacotes de LiDAR de varredura
            uint8_t index = lidarSerial.read();
            uint8_t speedL = lidarSerial.read();
            uint8_t speedH = lidarSerial.read();

            // Calcular ângulo a partir do índice de pacote
            float baseAngle = (index - 0xA0) * 4.0f;

            // Ler dados da primeira amostra
            if (lidarSerial.available() >= 4) {
                uint8_t byte0 = lidarSerial.read();
                uint8_t byte1 = lidarSerial.read();
                uint8_t byte2 = lidarSerial.read();
                uint8_t byte3 = lidarSerial.read();

                uint16_t dist = (byte1 << 8) | byte0;
                bool invalidBit = (byte1 & 0x80) != 0;

                measurement.angleDeg = baseAngle;
                measurement.distanceMm = (float)(dist & 0x3FFF);
                measurement.isValid = !invalidBit && (measurement.distanceMm > 100.0f);
                return measurement.isValid;
            }
        }
    }
    return false;
}
