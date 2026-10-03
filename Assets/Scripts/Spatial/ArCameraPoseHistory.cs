using System;
using UnityEngine;

namespace ArScanner.Spatial
{
    // AR poses are captured on the main thread and queried only between real
    // tracking samples. A relocalization or a long tracking gap ends the epoch.
    internal sealed class ArCameraPoseHistory
    {
        private const int Capacity = 512;
        private const double MaximumSampleGapSeconds = 0.15;

        private struct Sample
        {
            public double timeSeconds;
            public Pose pose;
        }

        private readonly Sample[] samples = new Sample[Capacity];
        private int count, next;
        public long EpochRevision { get; private set; }
        public string LastResetReason { get; private set; } = "empty";

        public void Clear() => Clear("explicit_reset");

        private void Clear(string reason)
        {
            if (count > 0) EpochRevision++;
            count = next = 0;
            LastResetReason = reason;
        }

        public bool TryGetBounds(out double oldestSeconds, out double latestSeconds)
        {
            oldestSeconds = latestSeconds = double.NaN;
            if (count == 0) return false;
            oldestSeconds = samples[(next + Capacity - count) % Capacity].timeSeconds;
            latestSeconds = samples[(next + Capacity - 1) % Capacity].timeSeconds;
            return true;
        }

        public void Add(double timeSeconds, Pose pose)
        {
            if (!Finite(timeSeconds) || !Finite(pose.position) ||
                !Finite(pose.rotation)) return;
            if (count > 0)
            {
                Sample previous = samples[(next + Capacity - 1) % Capacity];
                double gap = timeSeconds - previous.timeSeconds;
                if (gap <= 0) return;
                // Do not interpolate through a tracking gap or an AR origin
                // jump while SessionTracking remains reported as active.
                if (Vector3.Distance(pose.position, previous.pose.position) > .5f ||
                    Quaternion.Angle(pose.rotation, previous.pose.rotation) > 45f)
                    Clear("spatial_discontinuity");
                else if (gap > MaximumSampleGapSeconds)
                    Clear("tracking_gap");
            }
            samples[next] = new Sample { timeSeconds = timeSeconds, pose = pose };
            next = (next + 1) % Capacity;
            if (count < Capacity) count++;
        }

        public bool TryGet(double timeSeconds, out Pose pose)
        {
            pose = default;
            if (count < 2 || !Finite(timeSeconds)) return false;
            int first = (next + Capacity - count) % Capacity;
            Sample before = samples[first];
            if (timeSeconds < before.timeSeconds) return false;
            for (int i = 1; i < count; i++)
            {
                Sample after = samples[(first + i) % Capacity];
                if (timeSeconds <= after.timeSeconds)
                {
                    double span = after.timeSeconds - before.timeSeconds;
                    if (span <= 0 || span > MaximumSampleGapSeconds) return false;
                    float fraction = (float)((timeSeconds - before.timeSeconds) / span);
                    pose = new Pose(Vector3.LerpUnclamped(before.pose.position,
                            after.pose.position, fraction),
                        Quaternion.SlerpUnclamped(before.pose.rotation,
                            after.pose.rotation, fraction));
                    return true;
                }
                before = after;
            }
            return false;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) &&
            Finite(value.z) && Finite(value.w);
    }
}
