#pragma once
#include <stdint.h>

// Caller serializes writer/reader. Unsigned clock differences handle micros wrap.
template<unsigned Capacity> class StepHistory {
    struct Entry { uint32_t us; int32_t steps; } entries[Capacity] = {};
    unsigned next = 0, count = 0;
public:
    void clear() { next = count = 0; }
    __attribute__((always_inline)) inline void push(uint32_t us, int32_t steps) {
        entries[next] = {us, steps};
        next = (next + 1) % Capacity;
        if (count < Capacity) ++count;
    }
    bool at(uint32_t us, int32_t &steps) const {
        if (!count) return false;
        unsigned first = (next + Capacity - count) % Capacity;
        if (int32_t(us - entries[first].us) < 0) return false;
        unsigned lo = 0, hi = count;
        while (lo < hi) {
            unsigned mid = (lo + hi) / 2;
            if (int32_t(us - entries[(first+mid)%Capacity].us) >= 0) lo = mid+1;
            else hi = mid;
        }
        steps = entries[(first+lo-1)%Capacity].steps;
        return true;
    }
};
