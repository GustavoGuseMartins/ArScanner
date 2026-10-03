using System;

namespace ArScanner.Network
{
    // A one-way serial stream cannot establish absolute transport latency.
    // The lowest observed arrival offset is a conservative relative clock map;
    // high-latency samples are rejected rather than paired with a later AR pose.
    internal sealed class UwbDeviceClockMapper
    {
        private const int WindowSize = 32;
        private const int MinimumSamples = 8;
        private const double MinimumObservationSpanSeconds = 1.5;
        private const double MaximumExcessDelaySeconds = 0.10;
        private readonly double[] arrivalOffsets = new double[WindowSize];
        private int count, next;
        private uint previousDeviceMs;
        private double deviceSeconds, previousArrivalSeconds, firstArrivalSeconds;

        public void Clear()
        {
            count = next = 0;
            previousDeviceMs = 0;
            deviceSeconds = previousArrivalSeconds = firstArrivalSeconds = 0;
        }

        public bool TryMap(uint deviceMs, double arrivalSeconds,
            out double mappedSeconds, out double excessDelaySeconds)
        {
            mappedSeconds = excessDelaySeconds = double.NaN;
            if (double.IsNaN(arrivalSeconds) || double.IsInfinity(arrivalSeconds))
            {
                Clear();
                return false;
            }

            if (count == 0)
            {
                previousDeviceMs = deviceMs;
                firstArrivalSeconds = arrivalSeconds;
                previousArrivalSeconds = arrivalSeconds;
                arrivalOffsets[0] = arrivalSeconds;
                count = next = 1;
                return false;
            }

            int deltaMs = unchecked((int)(deviceMs - previousDeviceMs));
            double hostGap = arrivalSeconds - previousArrivalSeconds;
            if (deltaMs == 0 || hostGap < 0) return false;
            if (deltaMs < 0 || deltaMs > 5000 || hostGap > 5.0)
            {
                Clear();
                return TryMap(deviceMs, arrivalSeconds, out mappedSeconds,
                    out excessDelaySeconds);
            }

            previousDeviceMs = deviceMs;
            previousArrivalSeconds = arrivalSeconds;
            deviceSeconds += deltaMs / 1000.0;
            arrivalOffsets[next] = arrivalSeconds - deviceSeconds;
            next = (next + 1) % WindowSize;
            if (count < WindowSize) count++;

            double minimumOffset = double.PositiveInfinity;
            for (int i = 0; i < count; i++)
                minimumOffset = Math.Min(minimumOffset, arrivalOffsets[i]);
            mappedSeconds = deviceSeconds + minimumOffset;
            excessDelaySeconds = arrivalSeconds - mappedSeconds;
            return count >= MinimumSamples &&
                arrivalSeconds - firstArrivalSeconds >= MinimumObservationSpanSeconds &&
                excessDelaySeconds >= -0.001 &&
                excessDelaySeconds <= MaximumExcessDelaySeconds;
        }
    }
}
