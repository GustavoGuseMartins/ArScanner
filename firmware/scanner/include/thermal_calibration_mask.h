#pragma once
#include <stdint.h>
#include <string.h>

namespace ThermalCalibration {
constexpr unsigned PixelCount = 768;
constexpr unsigned MaskBytes = PixelCount / 8;
constexpr unsigned MaxMaskedPixels = 38; // floor(5% of 768), at least 730 valid.
inline bool valid(const uint8_t *mask, unsigned pixel) {
    return !mask || (mask[pixel / 8] & (1U << (pixel % 8))) != 0;
}
inline uint16_t buildValidityMask(const uint16_t *eeprom, uint8_t *mask) {
    memset(mask, 0xFF, MaskBytes);
    uint16_t masked = 0;
    for (unsigned pixel = 0; pixel < PixelCount; ++pixel) {
        uint16_t value = eeprom[pixel + 64];
        if (value == 0 || (value & 1U)) {
            mask[pixel / 8] &= uint8_t(~(1U << (pixel % 8)));
            ++masked;
        }
    }
    return masked;
}
inline bool withinPartialLimit(unsigned masked) {
    return masked > 0 && masked <= MaxMaskedPixels && PixelCount - masked >= 730;
}
}
