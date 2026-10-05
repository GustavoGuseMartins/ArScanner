#pragma once
#include <stdint.h>
#include <math.h>

// Caller serializes access and clears on a reference/mode change or invalid
// orientation. The driver publishes continuous, unwrapped head yaw in degrees.
template<unsigned Capacity> class HeadYawHistory {
    static_assert(Capacity > 1, "Yaw interpolation needs at least two samples");
    struct Entry { uint32_t us; float yaw; } entries[Capacity] = {};
    unsigned next = 0, count = 0;
public:
    void clear() { next = count = 0; }
    void push(uint32_t us, float yaw) {
        if (!isfinite(yaw)) { clear(); return; }
        if (count) {
            const Entry &last = entries[(next + Capacity - 1) % Capacity];
            const int32_t elapsed = int32_t(us - last.us);
            if (elapsed == 0) return;
            if (elapsed < 0 || elapsed > 100000) clear();
        }
        entries[next] = {us, yaw};
        next = (next + 1) % Capacity;
        if (count < Capacity) ++count;
    }
    bool at(uint32_t us, float &yaw) const {
        if (!count) return false;
        const unsigned first = (next + Capacity - count) % Capacity;
        if (int32_t(us - entries[first].us) < 0) return false;
        unsigned lo = 0, hi = count;
        while (lo < hi) {
            const unsigned mid = (lo + hi) / 2;
            if (int32_t(us - entries[(first + mid) % Capacity].us) >= 0) lo = mid + 1;
            else hi = mid;
        }
        const Entry &before = entries[(first + lo - 1) % Capacity];
        const uint32_t elapsed = uint32_t(us - before.us);
        if (elapsed == 0) { yaw = before.yaw; return true; }
        if (lo == count) {
            // No future gyro is invented. A fresh sample may be held for at
            // most 20 ms, matching the existing LiDAR attitude tolerance.
            if (elapsed > 20000) return false;
            yaw = before.yaw;
            return true;
        }
        const Entry &after = entries[(first + lo) % Capacity];
        const uint32_t span = uint32_t(after.us - before.us);
        if (!span || span > 100000) return false;
        yaw = before.yaw + (after.yaw - before.yaw) * float(elapsed) / float(span);
        return isfinite(yaw);
    }
};
