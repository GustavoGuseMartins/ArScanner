#pragma once
#include <stdint.h>
#include <math.h>

namespace ThermalAcquisition {
constexpr int NotReady = 1;
constexpr int InvalidFrame = -101;
constexpr int FrameTimeout = -104;
constexpr int InvalidCalibration = -105;
constexpr int InvalidConfiguration = -106;
constexpr int ReadDeadline = -107;
constexpr uint32_t MaxFrameAgeMs = 1000;

// This tracks actual subpage IDs, not the number of getFrame calls. Repeated
// subpages replace their own half; they can never complete the other half.
class FrameAssembler {
public:
    void reset() { seen = 0; startedMs = firstMs = lastMs = 0; }
    uint8_t mask() const { return seen; }
    void setDeadline(uint32_t value) { deadlineMs = value; reset(); }
    bool expired(uint32_t now) const { return seen && uint32_t(now - startedMs) > deadlineMs; }
    bool accept(uint8_t page, uint32_t timeMs, bool &duplicate) {
        duplicate = false;
        if (page > 1) return false;
        if (!seen) startedMs = timeMs;
        uint8_t flag = uint8_t(1U << page);
        duplicate = (seen & flag) != 0;
        pageMs[page] = timeMs;
        seen |= flag;
        if (seen != 3) return false;
        // Unsigned elapsed time remains valid across millis wraparound.
        uint32_t age0 = uint32_t(timeMs - pageMs[0]);
        uint32_t age1 = uint32_t(timeMs - pageMs[1]);
        firstMs = timeMs - (age0 > age1 ? age0 : age1);
        lastMs = timeMs;
        return uint32_t(timeMs - startedMs) <= deadlineMs;
    }
    uint32_t spanMs() const { return uint32_t(lastMs - firstMs); }
    uint32_t timestampMs() const { return firstMs + spanMs() / 2U; }
    static bool finiteFrame(const float *frame, const uint8_t *validity = nullptr) {
        if (!frame) return false;
        unsigned valid = 0;
        for (unsigned i = 0; i < 768; ++i) {
            bool usable = !validity || (validity[i >> 3] & (1U << (i & 7U)));
            if (usable) {
                if (!isfinite(frame[i])) return false;
                ++valid;
            } else if (!isnan(frame[i])) return false;
        }
        return valid != 0;
    }
private:
    uint8_t seen = 0;
    uint32_t pageMs[2] = {}, startedMs = 0, firstMs = 0, lastMs = 0;
    uint32_t deadlineMs = 500;
};
}
