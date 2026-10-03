#include "lidar_packet_parser.h"
#include <array>
#include <cassert>
#include <cstdio>
#include <vector>

using Bytes = std::array<uint8_t, 22>;

// Fixed protocol fixture: angles 0..3, 300 RPM, distances 1000..1003 mm.
static const Bytes golden = {{
    0xFA, 0xA0, 0x00, 0x4B,
    0xE8, 0x03, 0x1C, 0x02, 0xE9, 0x03, 0x1D, 0x02,
    0xEA, 0x03, 0x1E, 0x02, 0xEB, 0x03, 0x1F, 0x02,
    0xCD, 0x42
}};

// Independent weighted sum used only to build varied test inputs.
static void seal(Bytes &bytes) {
    uint32_t sum = 0;
    for (unsigned word = 0; word < 10; ++word) {
        sum += (bytes[2 * word] + 256u * bytes[2 * word + 1]) * (1u << (9 - word));
    }
    const uint16_t check = ((sum % 32768) + (sum / 32768)) % 32768;
    bytes[20] = check & 0xFF;
    bytes[21] = check >> 8;
}

template<class Container>
static unsigned feed(LidarPacketParser &parser, const Container &bytes, LidarPacket &result) {
    unsigned count = 0;
    for (uint8_t value : bytes) if (parser.feed(value, result)) ++count;
    return count;
}

static void fragmentedAndConsecutivePackets() {
    for (unsigned split = 0; split <= golden.size(); ++split) {
        LidarPacketParser parser;
        LidarPacket result = {};
        unsigned packets = 0;
        for (unsigned i = 0; i < split; ++i) packets += parser.feed(golden[i], result);
        assert(packets == (split == golden.size() ? 1u : 0u));
        // Represents a later UART read, with the existing parser state retained.
        for (unsigned i = split; i < golden.size(); ++i) packets += parser.feed(golden[i], result);
        assert(packets == 1);
        for (unsigned i = 0; i < 4; ++i) {
            assert(result.samples[i].isValid);
            assert(result.samples[i].angleDeg == i);
            assert(result.samples[i].distanceMm == 1000 + i);
            assert(result.samples[i].signalStrength == 540 + i);
            assert(!result.samples[i].strengthWarning);
        }
        assert(parser.diagnostics().lastRpm == 300.0f);
        assert(feed(parser, golden, result) == 1);
        assert(parser.diagnostics().validPackets == 2);
        assert(parser.diagnostics().validSamples == 8);
        assert(parser.diagnostics().weakSamples == 0);
        assert(parser.diagnostics().bytesReceived == 44);
    }
}

static void invalidFlagsAndBoundaryAngles() {
    LidarPacketParser parser;
    LidarPacket result = {};
    Bytes bytes = golden;
    bytes[1] = 0xF9;
    bytes[5] |= 0x80;   // Invalid data must not become a point.
    bytes[5] |= 0x40;   // An invalid distance's warning must not count as valid weak data.
    bytes[9] |= 0x40;   // Strength warning must not inflate distance by 16384 mm.
    bytes[12] = 100;    // Existing minimum-range filter.
    bytes[13] = 0;
    bytes[16] = 0xFA;   // Start-like byte inside payload is valid data.
    bytes[17] = 0x03;
    seal(bytes);
    assert(feed(parser, bytes, result) == 1);
    assert(!result.samples[0].isValid);
    assert(result.samples[0].strengthWarning);
    assert(result.samples[1].isValid && result.samples[1].distanceMm == 1001);
    assert(result.samples[1].strengthWarning);
    assert(!result.samples[2].isValid);
    assert(result.samples[3].isValid && result.samples[3].distanceMm == 1018);
    for (unsigned i = 0; i < 4; ++i) assert(result.samples[i].angleDeg == 356 + i);
    assert(parser.diagnostics().invalidSamples == 2);
    assert(parser.diagnostics().validSamples == 2);
    assert(parser.diagnostics().weakSamples == 1);
}

static void fullStrengthAndDistanceBits() {
    LidarPacketParser parser;
    LidarPacket result = {};
    Bytes bytes = golden;
    bytes[4] = 0xFF;
    bytes[5] = 0x7F; // 14-bit maximum distance plus warning; bit 7 remains clear.
    bytes[6] = 0xFF;
    bytes[7] = 0xFF; // Return strength must retain both bytes without a sign bit.
    bytes[10] = 0;
    bytes[11] = 0; // Zero strength is still not the sensor's invalid-distance flag.
    seal(bytes);
    assert(feed(parser, bytes, result) == 1);
    assert(result.samples[0].isValid);
    assert(result.samples[0].distanceMm == 16383);
    assert(result.samples[0].signalStrength == 65535);
    assert(result.samples[0].strengthWarning);
    assert(result.samples[1].isValid);
    assert(result.samples[1].signalStrength == 0);
    assert(!result.samples[1].strengthWarning);
    assert(parser.diagnostics().validSamples == 4);
    assert(parser.diagnostics().weakSamples == 1);

    // New data must overwrite warning/intensity even if the packet object is reused.
    assert(feed(parser, golden, result) == 1);
    assert(result.samples[0].signalStrength == 540);
    assert(!result.samples[0].strengthWarning);
    assert(parser.diagnostics().weakSamples == 1);
}

static void corruptedDataAndResynchronization() {
    // Corrupt every byte individually, then recover at the next valid packet.
    for (unsigned changed = 0; changed < golden.size(); ++changed) {
        LidarPacketParser parser;
        LidarPacket result = {};
        Bytes corrupt = golden;
        corrupt[changed] ^= 0x01;
        assert(feed(parser, corrupt, result) == 0);
        assert(feed(parser, golden, result) == 1);
    }

    // Recover even when truncation means the next header is already buffered.
    for (unsigned length = 1; length < golden.size(); ++length) {
        LidarPacketParser parser;
        LidarPacket result = {};
        std::vector<uint8_t> stream(golden.begin(), golden.begin() + length);
        stream.insert(stream.end(), golden.begin(), golden.end());
        assert(feed(parser, stream, result) == 1);
    }

    // One lost byte at every position must not discard the following packet.
    for (unsigned missing = 0; missing < golden.size(); ++missing) {
        LidarPacketParser parser;
        LidarPacket result = {};
        std::vector<uint8_t> stream(golden.begin(), golden.end());
        stream.erase(stream.begin() + missing);
        stream.insert(stream.end(), golden.begin(), golden.end());
        assert(feed(parser, stream, result) == 1);
    }

    LidarPacketParser parser;
    LidarPacket result = {};
    const uint8_t noise[] = {0xAA, 0, 0xFA, 0x9F, 0xFA, 0xFF, 0xFA};
    assert(feed(parser, noise, result) == 0);
    assert(feed(parser, golden, result) == 1);
    Bytes badIndex = golden;
    badIndex[1] = 0xFF;
    seal(badIndex);
    assert(feed(parser, badIndex, result) == 0);
}

static void restartDropsPartialPacket() {
    LidarPacketParser parser;
    LidarPacket result = {};
    for (unsigned i = 0; i < 10; ++i) assert(!parser.feed(golden[i], result));
    parser.reset();
    for (unsigned i = 10; i < golden.size(); ++i) assert(!parser.feed(golden[i], result));
    assert(feed(parser, golden, result) == 1);
    assert(parser.diagnostics().bytesReceived == 44);
    assert(parser.diagnostics().validPackets == 1);
}

int main() {
    fragmentedAndConsecutivePackets();
    invalidFlagsAndBoundaryAngles();
    fullStrengthAndDistanceBits();
    corruptedDataAndResynchronization();
    restartDropsPartialPacket();
    std::puts("LiDAR parser: fragmentation, strength/warnings, all 4 samples, flags, checksum, noise, dropped bytes and restart passed.");
}
