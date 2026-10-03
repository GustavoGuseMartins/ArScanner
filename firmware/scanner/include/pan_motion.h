#pragma once

// Shared with the ISR and host regression test; no Arduino/heap/timer calls.
inline __attribute__((always_inline)) int panDirectionToZero(int step, int steps) {
    return step <= steps / 2 ? -1 : 1;
}
inline __attribute__((always_inline)) void advancePan(int &step, int &direction,
    bool &running, bool &parking, int resumeDirection, int mode, int steps) {
    if (!running) return;
    step += direction;
    if (step >= steps) step -= steps;
    if (step < 0) step += steps;
    if (parking && step == 0) {
        parking = false;
        running = false;
        direction = mode == 1 ? 1 : resumeDirection;
    } else if (!parking && mode == 1) {
        if (step >= steps / 2) direction = -1;
        else if (step == 0) direction = 1;
    }
}
