#ifndef LIDAR_PACKET_PARSER_H
#define LIDAR_PACKET_PARSER_H

#include <stdint.h>
#include <string.h>

struct LidarMeasurement {
    float angleDeg;
    float distanceMm;
    bool isValid;
    uint32_t sampleTimeUs = 0; // Estimated acquisition time, assigned by UART driver.
    uint16_t signalStrength = 0; // Raw return strength; no calibrated quality threshold.
    bool strengthWarning = false; // Sensor-provided weak-return warning, distinct from invalid.
};

struct LidarPacket {
    LidarMeasurement samples[4];
};

struct LidarDiagnostics {
    uint32_t bytesReceived = 0;
    uint32_t validPackets = 0;
    uint32_t checksumErrors = 0;
    uint32_t invalidSamples = 0;
    uint32_t validSamples = 0;
    uint32_t weakSamples = 0; // Valid distance samples with the sensor's strength warning.
    float lastRpm = 0.0f;
};

// Roborock LDS / Neato XV11: FA, index A0..F9, speed, 4 samples, checksum.
// Reference: https://github.com/Roborock-OpenSource/Cullinan#lds-serial-data
// The exact sensor model must support this 22-byte protocol.
class LidarPacketParser {
public:
    // Discard partial input while preserving diagnostic counters.
    void reset() { received = 0; }

    const LidarDiagnostics &diagnostics() const { return stats; }

    bool feed(uint8_t value, LidarPacket &result) {
        ++stats.bytesReceived;
        if (received == 0) {
            if (value == 0xFA) packet[received++] = value;
            return false;
        }
        if (received == 1 && !validIndex(value)) {
            // A repeated start byte may be the beginning of the next packet.
            received = value == 0xFA ? 1 : 0;
            return false;
        }
        packet[received++] = value;
        if (received != sizeof(packet)) return false;

        uint32_t checksum = 0;
        for (uint8_t i = 0; i < 20; i += 2) {
            checksum = (checksum << 1) + littleEndian(packet + i);
        }
        checksum = (checksum + (checksum >> 15)) & 0x7FFF;
        if (checksum != littleEndian(packet + 20)) {
            ++stats.checksumErrors;
            resynchronize();
            return false;
        }

        ++stats.validPackets;
        stats.lastRpm = littleEndian(packet + 2) / 64.0f;
        const uint16_t baseAngle = (packet[1] - 0xA0) * 4;
        for (uint8_t i = 0; i < 4; ++i) {
            const uint8_t *sample = packet + 4 + 4 * i;
            LidarMeasurement &measurement = result.samples[i];
            measurement.angleDeg = baseAngle + i;
            measurement.distanceMm = littleEndian(sample) & 0x3FFF;
            measurement.signalStrength = littleEndian(sample + 2);
            measurement.strengthWarning = (sample[1] & 0x40) != 0;
            // Bit 6 is only a strength warning, not an invalid-distance flag.
            measurement.isValid = !(sample[1] & 0x80) && measurement.distanceMm > 100.0f;
            if (measurement.isValid) {
                ++stats.validSamples;
                if (measurement.strengthWarning) ++stats.weakSamples;
            }
            else ++stats.invalidSamples;
        }
        received = 0;
        return true;
    }

private:
    uint8_t packet[22] = {};
    uint8_t received = 0;
    LidarDiagnostics stats;

    static bool validIndex(uint8_t value) { return value >= 0xA0 && value <= 0xF9; }

    static uint16_t littleEndian(const uint8_t *bytes) {
        return static_cast<uint16_t>(bytes[0]) | (static_cast<uint16_t>(bytes[1]) << 8);
    }

    void resynchronize() {
        // Retain a potential start already inside a corrupt/truncated packet.
        // Do not blindly discard all 22 bytes: UART byte loss shifts boundaries.
        for (uint8_t i = 1; i < sizeof(packet); ++i) {
            if (packet[i] == 0xFA &&
                (i == sizeof(packet) - 1 || validIndex(packet[i + 1]))) {
                received = sizeof(packet) - i;
                memmove(packet, packet + i, received);
                return;
            }
        }
        received = 0;
    }
};

#endif
