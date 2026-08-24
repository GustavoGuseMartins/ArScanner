#ifndef LIDAR_DRIVER_H
#define LIDAR_DRIVER_H

#include "config.h"

struct LidarMeasurement {
    float angleDeg;
    float distanceMm;
    bool isValid;
};

class LidarDriver {
private:
    HardwareSerial lidarSerial;

public:
    LidarDriver();
    void begin();
    void setMotorSpeed(uint8_t pwmVal);
    bool readPacket(LidarMeasurement &measurement);
};

#endif // LIDAR_DRIVER_H
