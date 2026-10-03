#ifndef CAMERA_OV2640_H
#define CAMERA_OV2640_H

#include <Arduino.h>
#include "esp_camera.h"
#include <freertos/FreeRTOS.h>
#include <freertos/semphr.h>

class CameraOv2640 {
public:
    CameraOv2640();
    bool begin();
    void updateFrame();
    bool getPixelColor(float angleHorizDeg, float angleVertDeg, uint8_t &r, uint8_t &g, uint8_t &b);
    bool getJpegFrame(uint8_t **out_jpg, size_t *out_len);
    void freeJpegFrame(uint8_t *jpg_buf);
    bool isInitialized() const { return initialized; }
    const char *statusMessage() const { return message; }

private:
    bool initialized;
    camera_fb_t *currentFb;
    float fovHorizontalDeg;
    float fovVerticalDeg;
    SemaphoreHandle_t frameMutex = nullptr;
    uint32_t frameTimeMs = 0;
    const char *message = "RGB desativada: prioridade UWB GPIO4/5 e termica. Requer perfil de camera e GPIOs sem conflito.";
};

#endif // CAMERA_OV2640_H
