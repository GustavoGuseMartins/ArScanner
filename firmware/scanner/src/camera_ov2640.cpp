#include "camera_ov2640.h"
#include "config.h"
#include "img_converters.h"

CameraOv2640::CameraOv2640() : initialized(false), currentFb(nullptr), fovHorizontalDeg(66.0f), fovVerticalDeg(50.0f) {}

bool CameraOv2640::begin() {
    const int pins[] = {XCLK_GPIO_NUM,SIOD_GPIO_NUM,SIOC_GPIO_NUM,Y9_GPIO_NUM,Y8_GPIO_NUM,
        Y7_GPIO_NUM,Y6_GPIO_NUM,Y5_GPIO_NUM,Y4_GPIO_NUM,Y3_GPIO_NUM,Y2_GPIO_NUM,
        VSYNC_GPIO_NUM,HREF_GPIO_NUM,PCLK_GPIO_NUM};
    const int reserved[] = {
#if UWB_TAG_ENABLED
        UWB_MOSI_PIN,UWB_MISO_PIN,UWB_SCK_PIN,UWB_IRQ_PIN,
#endif
        UWB_CS_PIN, // Always held high in the isolated camera build.
        I2C_SDA_PIN,I2C_SCL_PIN,LIDAR_RX_PIN,LIDAR_TX_PIN,LIDAR_MOSFET_GATE_PIN,
        STEPPER_STEP_PIN,STEPPER_DIR_PIN};
    for (int pin : pins) {
        if (pin < 0) { Serial.println(message); return false; }
        for (int other : reserved) if (pin == other) {
            message = "RGB: GPIO da camera conflita com periferico da PCB (perfil comum: SCCB4/5 x UWB4/5). Requer conferir/religar hardware.";
            Serial.println(message);
            return false;
        }
    }
    if (!psramFound()) { message = "RGB: PSRAM nao detectada; confira perfil N16R8 qio_opi."; return false; }
    if (!frameMutex) frameMutex = xSemaphoreCreateMutex();
    if (!frameMutex) { message = "RGB: memoria insuficiente para mutex."; return false; }
    camera_config_t c = {};
    c.ledc_channel = LEDC_CHANNEL_7;
    c.ledc_timer = LEDC_TIMER_3;
    c.pin_d0=Y2_GPIO_NUM; c.pin_d1=Y3_GPIO_NUM; c.pin_d2=Y4_GPIO_NUM; c.pin_d3=Y5_GPIO_NUM;
    c.pin_d4=Y6_GPIO_NUM; c.pin_d5=Y7_GPIO_NUM; c.pin_d6=Y8_GPIO_NUM; c.pin_d7=Y9_GPIO_NUM;
    c.pin_xclk=XCLK_GPIO_NUM; c.pin_pclk=PCLK_GPIO_NUM;
    c.pin_vsync=VSYNC_GPIO_NUM; c.pin_href=HREF_GPIO_NUM;
    c.pin_sccb_sda=SIOD_GPIO_NUM; c.pin_sccb_scl=SIOC_GPIO_NUM;
    c.pin_pwdn=PWDN_GPIO_NUM; c.pin_reset=RESET_GPIO_NUM;
    c.xclk_freq_hz=20000000;
    c.pixel_format=PIXFORMAT_JPEG; c.frame_size=FRAMESIZE_QVGA;
    c.jpeg_quality=12; c.fb_count=2; c.fb_location=CAMERA_FB_IN_PSRAM;
    c.grab_mode=CAMERA_GRAB_LATEST;
    esp_err_t error = esp_camera_init(&c);
    if (error != ESP_OK) {
        Serial.printf("[RGB] esp_camera_init: 0x%x\n", error);
        message = "RGB: falha ao inicializar; confira pinagem, cabo flat e log esp_camera_init.";
        return false;
    }
    initialized = true;
    message = "RGB: aguardando quadro JPEG recente.";
    return true;
}

void CameraOv2640::updateFrame() {
    if (!initialized) return;
    camera_fb_t *next = esp_camera_fb_get();
    if (!next) return;
    xSemaphoreTake(frameMutex, portMAX_DELAY);
    if (currentFb) esp_camera_fb_return(currentFb);
    currentFb = next;
    frameTimeMs = millis();
    xSemaphoreGive(frameMutex);
}

bool CameraOv2640::getPixelColor(float angleHorizDeg, float angleVertDeg, uint8_t &r, uint8_t &g, uint8_t &b) {
    // JPEG preview is not a calibrated per-ray colour measurement.
    r = g = b = 200;
    return false;
}

bool CameraOv2640::getJpegFrame(uint8_t **out_jpg, size_t *out_len) {
    if (!out_jpg || !out_len || !initialized || !frameMutex) return false;
    *out_jpg = nullptr; *out_len = 0;
    if (xSemaphoreTake(frameMutex, pdMS_TO_TICKS(100)) != pdTRUE) return false;
    if (currentFb && currentFb->format == PIXFORMAT_JPEG && millis()-frameTimeMs <= 1000) {
        *out_jpg = (uint8_t*)malloc(currentFb->len);
        if (*out_jpg) {
            memcpy(*out_jpg,currentFb->buf,currentFb->len);
            *out_len = currentFb->len;
        }
    }
    xSemaphoreGive(frameMutex);
    return *out_jpg != nullptr;
}

void CameraOv2640::freeJpegFrame(uint8_t *jpg_buf) {
    if (jpg_buf != nullptr) {
        free(jpg_buf);
    }
}
