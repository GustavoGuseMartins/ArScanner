#pragma once
#include <stdint.h>
#include <math.h>

// Version 2: asymmetric double-sided TWR. Both firmwares must be updated.
// APS013: https://forum.qorvo.com/uploads/short-url/x34DrF7EW5fQP9wY3aNESqPKz8z.pdf
#define UWB_TWR_MSG_POLL     0x51
#define UWB_TWR_MSG_RESPONSE 0x52
#define UWB_TWR_MSG_FINAL    0x53
#define UWB_TWR_MSG_REPORT   0x54
#pragma pack(push, 1)
struct UwbTwrPollPacket { uint8_t msgType, anchorAddr, tagAddr, seq; };
using UwbTwrResponsePacket = UwbTwrPollPacket;
struct UwbTwrFinalPacket {
    uint8_t msgType, anchorAddr, tagAddr, seq;
    int64_t round1, reply2;
};
struct UwbTwrReportPacket {
    uint8_t msgType, anchorAddr, tagAddr, seq;
    float distanceMeters;
};
#pragma pack(pop)
inline bool uwbDistance(int64_t round1, int64_t reply1, int64_t round2,
                        int64_t reply2, float &meters) {
    // All four intervals are local modulo-40-bit differences, below 100 ms.
    const int64_t maxInterval = 6389760000LL;
    if (round1 <= 0 || reply1 <= 0 || round2 <= 0 || reply2 <= 0 ||
        round1 > maxInterval || reply1 > maxInterval ||
        round2 > maxInterval || reply2 > maxInterval) return false;
    // Convert BEFORE multiplication (signed 64-bit products can overflow).
    double tof = (double(round1)*round2-double(reply1)*reply2) /
                 (double(round1)+round2+reply1+reply2);
    meters = float(tof * (299702547.0 / 63897600000.0));
    return isfinite(meters) && meters >= 0.08f && meters <= 35.0f;
}
