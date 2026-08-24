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

float ThermalSensor::getPointTemperature(float relativeAngleDeg) {
    // FOV horizontal do MLX90640 é aproximadamente 55° (32 colunas)
    // Mapeia o ângulo relativo ao centro da lente (-27.5° a +27.5°) para uma coluna (0 a 31)
    float fovMin = -27.5f;
    float fovMax = 27.5f;
    if (relativeAngleDeg < fovMin || relativeAngleDeg > fovMax) {
        return 20.0f; // Temperatura ambiente padrão fora do FOV
    }

    int col = (int)((relativeAngleDeg - fovMin) / (fovMax - fovMin) * 31.0f);
    if (col < 0) col = 0;
    if (col > 31) col = 31;

    // Retorna a temperatura média da coluna central (linha 12)
    int index = 12 * 32 + col;
    return frame[index];
}
