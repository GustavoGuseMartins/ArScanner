#ifndef IMU_SAMPLE_MATH_H
#define IMU_SAMPLE_MATH_H
#include <stdint.h>
#include <math.h>

namespace ImuSampleMath {
inline float signedWord(const uint8_t* bytes, float scale) {
    const uint16_t bits = (uint16_t(bytes[0]) << 8) | bytes[1];
    const int32_t value = bits >= 0x8000U ? int32_t(bits) - 65536 : bits;
    return float(value) / scale;
}

// Welford statistics; reject motion before calling its mean a gyro bias.
struct CalibrationWindow {
    unsigned count = 0;
    float mean[6] = {}, m2[6] = {};
    bool add(const float* values) {
        float gravity2 = 0;
        for (int i=0; i<6; ++i) if (!isfinite(values[i])) return false;
        for (int i=0; i<3; ++i) gravity2 += values[i]*values[i];
        if (gravity2 < .81f || gravity2 > 1.21f) return false;
        for (int i=3; i<6; ++i) if (fabsf(values[i]) > 5.f) return false;
        ++count;
        for (int i=0; i<6; ++i) {
            const float delta = values[i]-mean[i];
            mean[i] += delta/count;
            m2[i] += delta*(values[i]-mean[i]);
        }
        return true;
    }
    bool stable() const {
        if (count < 200) return false;
        for (int i=0; i<6; ++i) {
            const float limit = i<3 ? .02f : .35f;
            if (m2[i]/(count-1) > limit*limit) return false;
        }
        return true;
    }
};
}
#endif
