#pragma once
#include <stdint.h>
#include <stddef.h>
#include <stdio.h>
#include "scan_geometry.h"

struct ScanTraceSample {
    uint32_t sequence, sampleUs;
    float lidarAngleDeg, distanceMm, panInputDeg;
    float calibratedAngleDeg, pitchDeg, rollDeg;
    ScanGeometry::Vec3 projectedM;
    bool queued;
    uint16_t signalStrength;
    bool strengthWarning;
};

constexpr const char *scanTraceCsvHeader =
    "sequence,sample_us,lidar_deg,distance_mm,pan_input_deg,lidar_calibrated_deg,pitch_deg,roll_deg,x_mm,y_mm,z_mm,queued,signal_strength,strength_warning\n";
// CSV stores the exact projection used by TCP, BEFORE Unity/AR transforms.
inline int formatScanTrace(char *line, size_t size, const ScanTraceSample &s) {
    return snprintf(line,size,"%lu,%lu,%.3f,%.1f,%.4f,%.3f,%.3f,%.3f,%.3f,%.3f,%.3f,%d,%u,%d\n",
        (unsigned long)s.sequence,(unsigned long)s.sampleUs,s.lidarAngleDeg,
        s.distanceMm,s.panInputDeg,s.calibratedAngleDeg,s.pitchDeg,s.rollDeg,
        s.projectedM.x*1000,s.projectedM.y*1000,s.projectedM.z*1000,s.queued ? 1 : 0,
        (unsigned)s.signalStrength,s.strengthWarning ? 1 : 0);
}

// External lock required. Accepted raw measurements plus the firmware's 3D
// projection; coordinates precede Unity/AR transforms and use the pan-axis frame.
template<size_t Capacity> class ScanTrace {
    ScanTraceSample samples[Capacity] = {};
    size_t next = 0, count = 0;
    uint32_t sequence = 0;
public:
    void push(uint32_t us, float angle, float mm, float pan, float calibratedAngle,
              float pitch, float roll, ScanGeometry::Vec3 projected, bool queued,
              uint16_t signalStrength = 0, bool strengthWarning = false) {
        samples[next] = {++sequence,us,angle,mm,pan,calibratedAngle,pitch,roll,projected,queued,
            signalStrength,strengthWarning};
        next = (next+1)%Capacity;
        if (count < Capacity) ++count;
    }
    size_t copy(ScanTraceSample *dest) const {
        for (size_t i=0;i<count;++i) dest[i] = samples[(next+Capacity-count+i)%Capacity];
        return count;
    }
};
