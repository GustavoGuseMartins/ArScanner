#ifndef CAMERA_OV2640_H
#define CAMERA_OV2640_H

#include <Arduino.h>
#include "esp_camera.h"

class CameraOv2640 {
public:
    CameraOv2640();
    bool begin();
    void updateFrame();
    bool getPixelColor(float angleHorizDeg, float angleVertDeg, uint8_t &r, uint8_t &g, uint8_t &b);

private:
    camera_fb_t *currentFb;
    float fovHorizontalDeg;
    float fovVerticalDeg;
};

#endif // CAMERA_OV2640_H
