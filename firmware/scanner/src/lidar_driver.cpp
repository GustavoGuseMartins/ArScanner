#include "lidar_driver.h"

LidarDriver::LidarDriver() : lidarSerial(1) {}

void LidarDriver::begin() {
    // Garante que o MOSFET IRLZ44N (Q1) comece desligado para evitar picos de corrente no boot
    pinMode(LIDAR_MOSFET_GATE_PIN, OUTPUT);
    digitalWrite(LIDAR_MOSFET_GATE_PIN, LOW);
    ledcSetup(LIDAR_PWM_CHANNEL, 1000, 8);
    ledcAttachPin(LIDAR_MOSFET_GATE_PIN, LIDAR_PWM_CHANNEL);

    // ~400 ms de margem a 115200 baud para aquisições I2C/câmera mais lentas.
    // O consumidor ainda precisa drenar a UART continuamente durante o scan.
    lidarSerial.setRxBufferSize(4096);
    // Inicializa UART1 (RX = LIDAR_RX_PIN [GPIO1], TX = LIDAR_TX_PIN [GPIO3])
    lidarSerial.begin(LIDAR_BAUD, SERIAL_8N1, LIDAR_RX_PIN, LIDAR_TX_PIN);
    resetInput();
    Serial.printf("[LiDAR] UART inicializada: RX=GPIO%d, TX=GPIO%d, Baud=%d\n", LIDAR_RX_PIN, LIDAR_TX_PIN, LIDAR_BAUD);

    // Motor permanece DESLIGADO no boot até comando explícito do celular (START_SCAN)
    stopMotor();
}

void LidarDriver::setMotorSpeed(uint8_t pwmVal) {
    if (pwmVal > 0) {
        ledcWrite(LIDAR_PWM_CHANNEL, pwmVal);
    } else {
        stopMotor();
    }
}

void LidarDriver::stopMotor() {
    ledcWrite(LIDAR_PWM_CHANNEL, 0);
}

void LidarDriver::resetInput() {
    nextSample = 4;
    parser.reset();
    hasPacket = false;
    // Descarta apenas o que já estava na fila, sem aguardar novos bytes.
    int buffered = lidarSerial.available();
    while (buffered-- > 0) lidarSerial.read();
}

bool LidarDriver::readPacket(LidarMeasurement &measurement) {
    measurement = {};
    // Cada pacote completo entrega quatro amostras em chamadas consecutivas.
    // Não bloqueia esperando bytes e preserva pacotes fragmentados entre chamadas.
    int bytesBudget = 4096;
    for (;;) {
        while (nextSample < 4) {
            measurement = pendingPacket.samples[nextSample++];
            if (measurement.isValid) return true;
        }

        if (bytesBudget-- <= 0 || lidarSerial.available() <= 0) return false;
        const int value = lidarSerial.read();
        if (value < 0) return false;
        if (parser.feed(static_cast<uint8_t>(value), pendingPacket)) {
            lastPacketMs = millis();
            hasPacket = true;
            // No hardware timestamp: estimate acquisition from UART backlog,
            // serialization time and measured rotor speed. Calibrate fixed latency.
            const uint32_t now = micros();
            const float rpm = parser.diagnostics().lastRpm;
            if (rpm < 60.0f || rpm > 1000.0f) continue;
            const uint32_t queuedUs = (uint32_t)((uint64_t)lidarSerial.available()*10000000ULL/LIDAR_BAUD);
            const uint32_t frameUs = 22U*10000000U/LIDAR_BAUD;
            for (int i = 0; i < 4; ++i) {
                uint32_t age = queuedUs + frameUs + LIDAR_LATENCY_US +
                    (uint32_t)((3-i)*60000000.0f/(rpm*360.0f));
                pendingPacket.samples[i].sampleTimeUs = now-age;
                if (age > LIDAR_MAX_AGE_US) pendingPacket.samples[i].isValid = false;
            }
            nextSample = 0;
        }
    }
}
