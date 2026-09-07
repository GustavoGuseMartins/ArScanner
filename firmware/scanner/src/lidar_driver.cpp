#include "lidar_driver.h"

LidarDriver::LidarDriver() : lidarSerial(1) {}

void LidarDriver::begin() {
    // Garante que o MOSFET IRLZ44N (Q1) comece desligado para evitar picos de corrente no boot
    pinMode(LIDAR_MOSFET_GATE_PIN, OUTPUT);
    digitalWrite(LIDAR_MOSFET_GATE_PIN, LOW);

    // Inicializa UART1 com RX=GPIO3 e TX=GPIO1
    lidarSerial.begin(LIDAR_BAUD, SERIAL_8N1, LIDAR_RX_PIN, LIDAR_TX_PIN);

    // Aciona o motor do LiDAR via PWM no Gate do MOSFET
    setMotorSpeed(200); // PWM ~78% para estabilizar rotação (~5-7 Hz)
}

void LidarDriver::setMotorSpeed(uint8_t pwmVal) {
    analogWrite(LIDAR_MOSFET_GATE_PIN, pwmVal);
}

void LidarDriver::stopMotor() {
    analogWrite(LIDAR_MOSFET_GATE_PIN, 0);
    digitalWrite(LIDAR_MOSFET_GATE_PIN, LOW);
}

bool LidarDriver::readPacket(LidarMeasurement &measurement) {
    // Parser para pacotes LiDAR Roborock/Neato/XV11 (4 bytes por amostra ou pacote de 22 bytes)
    if (lidarSerial.available() >= 4) {
        uint8_t header = lidarSerial.read();
        if (header == 0xFA) { // Byte de início padrão de pacotes LiDAR
            uint8_t index = lidarSerial.read();
            uint8_t speedL = lidarSerial.read();
            uint8_t speedH = lidarSerial.read();
            (void)speedL;
            (void)speedH;

            // Calcular ângulo a partir do índice de pacote (0xA0 a 0xF9 -> 0° a 359°)
            float baseAngle = (index - 0xA0) * 4.0f;

            // Ler dados da amostra
            if (lidarSerial.available() >= 4) {
                uint8_t byte0 = lidarSerial.read();
                uint8_t byte1 = lidarSerial.read();
                uint8_t byte2 = lidarSerial.read();
                uint8_t byte3 = lidarSerial.read();
                (void)byte2;
                (void)byte3;

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
