#pragma once
#include <stdint.h>

namespace ThermalEepromDiagnostics {
constexpr unsigned WordCount = 832;
struct Summary { uint32_t crc32; uint16_t broken, outlier; };

// Host-comparison CRC, not a factory checksum. IEEE CRC32 over the sensor's
// canonical byte order: high byte followed by low byte for every 16-bit word.
inline uint32_t crc32(const uint16_t *words) {
    uint32_t crc = 0xFFFFFFFFU;
    for (unsigned i = 0; i < WordCount; ++i) {
        for (unsigned byte = 0; byte < 2; ++byte) {
            crc ^= uint8_t(byte == 0 ? words[i] >> 8 : words[i]);
            for (unsigned bit = 0; bit < 8; ++bit)
                crc = (crc >> 1) ^ ((crc & 1U) ? 0xEDB88320U : 0U);
        }
    }
    return crc ^ 0xFFFFFFFFU;
}
inline Summary summarize(const uint16_t *words) {
    Summary result = {crc32(words), 0, 0};
    // Count ALL pixels. Melexis extraction stops at the fifth deviating pixel
    // because its fixed-size correction arrays cannot represent more safely.
    for (unsigned pixel = 0; pixel < 768; ++pixel) {
        uint16_t value = words[pixel + 64];
        if (value == 0) ++result.broken;
        else if (value & 1U) ++result.outlier;
    }
    return result;
}
inline unsigned differences(const uint16_t *first, const uint16_t *second) {
    unsigned result = 0;
    for (unsigned i = 0; i < WordCount; ++i) if (first[i] != second[i]) ++result;
    return result;
}
}
