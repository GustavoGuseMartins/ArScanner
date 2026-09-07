#ifndef THERMAL_MLX90640_H
#define THERMAL_MLX90640_H

#include "config.h"
#include <Wire.h>
#include <Adafruit_MLX90640.h>

class ThermalSensor {
private:
    Adafruit_MLX90640 mlx;
    float frame[768]; // Matriz de calor 32x24 pixels

public:
    ThermalSensor();
    bool begin();
    bool updateFrame();
    float getPointTemperature(float angleHorizDeg, float angleVertDeg = 0.0f);
};

#endif // THERMAL_MLX90640_H
