#pragma once
#include <Arduino.h>
#include <DW1000.h>
#include "uwb_twr.h"

inline bool uwbWaitSent() {
    uint32_t start = millis();
    do {
        DW1000.readSystemEventStatusRegister();
        if (DW1000.isTransmitDone()) return true;
        delayMicroseconds(20);
    } while ((uint32_t)(millis()-start) < 20);
    DW1000.idle();
    return false;
}
template<class Packet> bool uwbReceive(Packet &packet, DW1000Time &rxTime) {
    DW1000.newReceive();
    DW1000.setDefaults();
    DW1000.receivePermanently(false);
    DW1000.clearAllStatus();
    DW1000.startReceive();
    uint32_t start = millis();
    do {
        DW1000.readSystemEventStatusRegister();
        if (DW1000.isReceiveDone()) {
            if (DW1000.getDataLength() != sizeof(Packet)) return false;
            DW1000.getData((byte*)&packet, sizeof(Packet));
            DW1000.getReceiveTimestamp(rxTime);
            DW1000.clearReceiveStatus();
            return true;
        }
        if (DW1000.isReceiveFailed() || DW1000.isReceiveTimeout()) return false;
        delayMicroseconds(20);
    } while ((uint32_t)(millis()-start) < 35);
    return false;
}
template<class Packet> bool uwbSend(const Packet &packet, DW1000Time &txTime) {
    DW1000.newTransmit();
    DW1000.setDefaults();
    DW1000.setData((byte*)&packet, sizeof(Packet));
    DW1000.startTransmit();
    if (!uwbWaitSent()) return false;
    DW1000.getTransmitTimestamp(txTime);
    DW1000.clearTransmitStatus();
    return true;
}
template<class Packet> bool uwbMatches(const Packet &p, const UwbTwrPollPacket &poll, uint8_t type) {
    return p.msgType == type && p.anchorAddr == poll.anchorAddr &&
           p.tagAddr == poll.tagAddr && p.seq == poll.seq;
}
