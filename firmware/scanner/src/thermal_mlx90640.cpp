#include "thermal_mlx90640.h"

ThermalSensor::ThermalSensor() {}

bool ThermalSensor::begin() {
    Wire.begin(I2C_SDA_PIN, I2C_SCL_PIN, I2C_FREQ);
    if (!mlx.begin(MLX90640_I2CADDR_DEFAULT, &Wire)) {
        return false;
    }
    mlx.setMode(MLX90640_CHESS);
    mlx.setResolution(MLX90640_ADC_18BIT);
    mlx.setRefreshRate(MLX90640_16_HZ);
    return true;
}

bool ThermalSensor::updateFrame() {
    if (mlx.getFrame(frame) == 0) {
        return true;
    }
    return false;
}

float ThermalSensor::getPointTemperature(float angleHorizDeg, float angleVertDeg) {
    // FOV do MLX90640ESF-BAA (Wide Angle): 110° Horizontal x 75° Vertical (32x24)
    float fovHMin = -THERMAL_FOV_H_DEG * 0.5f; // -55°
    float fovHMax = THERMAL_FOV_H_DEG * 0.5f;  // +55°
    float fovVMin = -THERMAL_FOV_V_DEG * 0.5f; // -37.5°
    float fovVMax = THERMAL_FOV_V_DEG * 0.5f;  // +37.5°

    if (angleHorizDeg < fovHMin || angleHorizDeg > fovHMax ||
        angleVertDeg < fovVMin || angleVertDeg > fovVMax) {
        return 21.0f; // Temperatura ambiente de referência fora do cone térmico
    }

    // Mapeamento angular bilinear nos 32x24 pixels
    int col = (int)((angleHorizDeg - fovHMin) / THERMAL_FOV_H_DEG * 31.0f);
    int row = (int)((angleVertDeg - fovVMin) / THERMAL_FOV_V_DEG * 23.0f);

    col = constrain(col, 0, 31);
    row = constrain(row, 0, 23);

    int index = row * 32 + col;
    return frame[index];
}
