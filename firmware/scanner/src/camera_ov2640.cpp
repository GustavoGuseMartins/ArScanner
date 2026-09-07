#include "camera_ov2640.h"
#include "config.h"

CameraOv2640::CameraOv2640() : currentFb(nullptr), fovHorizontalDeg(66.0f), fovVerticalDeg(50.0f) {}

bool CameraOv2640::begin() {
    camera_config_t config;
    config.ledc_channel = LEDC_CHANNEL_0;
    config.ledc_timer = LEDC_TIMER_0;
    config.pin_d0 = Y2_GPIO_NUM;
    config.pin_d1 = Y3_GPIO_NUM;
    config.pin_d2 = Y4_GPIO_NUM;
    config.pin_d3 = Y5_GPIO_NUM;
    config.pin_d4 = Y6_GPIO_NUM;
    config.pin_d5 = Y7_GPIO_NUM;
    config.pin_d6 = Y8_GPIO_NUM;
    config.pin_d7 = Y9_GPIO_NUM;
    config.pin_xclk = XCLK_GPIO_NUM;
    config.pin_pclk = PCLK_GPIO_NUM;
    config.pin_vsync = VSYNC_GPIO_NUM;
    config.pin_href = HREF_GPIO_NUM;
    config.pin_sccb_sda = SIOD_GPIO_NUM;
    config.pin_sccb_scl = SIOC_GPIO_NUM;
    config.pin_pwdn = PWDN_GPIO_NUM;
    config.pin_reset = RESET_GPIO_NUM;
    config.xclk_freq_hz = 20000000;
    config.pixel_format = PIXFORMAT_RGB565; // Formato direto sem custo de descompressão JPEG
    config.frame_size = FRAMESIZE_QVGA;     // 320x240 pixels em PSRAM
    config.jpeg_quality = 12;
    config.fb_count = 2;                    // Double buffering DMA em PSRAM
    config.grab_mode = CAMERA_GRAB_LATEST;
    config.fb_location = CAMERA_FB_IN_PSRAM;

    esp_err_t err = esp_camera_init(&config);
    if (err != ESP_OK) {
        Serial.printf("[ERRO] Falha ao inicializar camera OV2640: 0x%x\n", err);
        return false;
    }

    Serial.println("[OK] Camera OV2640 inicializada em QVGA RGB565 via DMA PSRAM.");
    return true;
}

void CameraOv2640::updateFrame() {
    if (currentFb != nullptr) {
        esp_camera_fb_return(currentFb);
        currentFb = nullptr;
    }
    currentFb = esp_camera_fb_get();
}

bool CameraOv2640::getPixelColor(float angleHorizDeg, float angleVertDeg, uint8_t &r, uint8_t &g, uint8_t &b) {
    if (currentFb == nullptr || currentFb->buf == nullptr) {
        r = 200; g = 200; b = 200;
        return false;
    }

    // Projeção angular para coordenadas de pixel (320x240)
    float uNorm = (angleHorizDeg + (fovHorizontalDeg * 0.5f)) / fovHorizontalDeg;
    float vNorm = (angleVertDeg + (fovVerticalDeg * 0.5f)) / fovVerticalDeg;

    if (uNorm < 0.0f || uNorm >= 1.0f || vNorm < 0.0f || vNorm >= 1.0f) {
        r = 180; g = 180; b = 180;
        return false;
    }

    int px = (int)(uNorm * 320.0f);
    int py = (int)(vNorm * 240.0f);
    px = constrain(px, 0, 319);
    py = constrain(py, 0, 239);

    int index = (py * 320 + px) * 2; // 2 bytes por pixel RGB565
    uint16_t pixel = (currentFb->buf[index] << 8) | currentFb->buf[index + 1];

    // Extração RGB565 (5 bits R, 6 bits G, 5 bits B)
    r = (uint8_t)(((pixel >> 11) & 0x1F) * 255 / 31);
    g = (uint8_t)(((pixel >> 5) & 0x3F) * 255 / 63);
    b = (uint8_t)((pixel & 0x1F) * 255 / 31);

    return true;
}
