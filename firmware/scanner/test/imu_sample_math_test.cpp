#include "../include/imu_sample_math.h"
#include <cassert>
#include <cstdio>

int main() {
    const uint8_t negative[] = {0x80,0x00}, positive[] = {0x40,0x00};
    assert(ImuSampleMath::signedWord(negative,16384.f) == -2.f);
    assert(ImuSampleMath::signedWord(positive,16384.f) == 1.f);
    ImuSampleMath::CalibrationWindow stable, vibration;
    for (int i=0; i<300; ++i) {
        float values[] = {0.f,1.f,0.f,.2f,-.1f,.3f}; // Upright board.
        assert(stable.add(values));
        values[3] = i%2 ? 2.f : -2.f;
        assert(vibration.add(values));
    }
    assert(stable.stable() && !vibration.stable());
    const float rotating[] = {0,1,0,0,10,0};
    const float accelerating[] = {0,2,0,0,0,0};
    assert(!stable.add(rotating) && !stable.add(accelerating));
    puts("IMU sample validation PASS: signed decoding, upright calibration, motion rejection.");
}
