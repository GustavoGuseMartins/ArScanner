#ifndef LIDAR_DRIVER_H
#define LIDAR_DRIVER_H

#include "config.h"
#include "lidar_packet_parser.h"

class LidarDriver {
private:
    HardwareSerial lidarSerial;
    LidarPacketParser parser;
    LidarPacket pendingPacket = {};
    uint8_t nextSample = 4;
    uint32_t lastPacketMs = 0;
    bool hasPacket = false;

public:
    LidarDriver();
    void begin();
    void setMotorSpeed(uint8_t pwmVal);
    void stopMotor();
    // Call from the acquisition task before restarting a scan.
    void resetInput();
    const LidarDiagnostics &diagnostics() const { return parser.diagnostics(); }
    bool hasRecentPacket() const { return hasPacket && (uint32_t)(millis()-lastPacketMs) < 500; }
    bool readPacket(LidarMeasurement &measurement);
};

#endif // LIDAR_DRIVER_H
