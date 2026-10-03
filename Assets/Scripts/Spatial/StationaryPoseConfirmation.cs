using System.Collections.Generic;
using UnityEngine;

namespace ArScanner.Spatial
{
    // Agreement between repeated estimates is a stability check, not a physical
    // accuracy claim. Range bias and mounting errors still need independent truth.
    public sealed class StationaryPoseConfirmation
    {
        public const float MaximumSpreadMeters = .10f;
        public const float MinimumViewSeparationMeters = .10f;
        private const float MaximumAgeSeconds = 20f;
        private const float MinimumSpanSeconds = .4f;
        private const int MinimumObservations = 3;
        private const int Capacity = 8;

        private struct Vote
        {
            public long id;
            public Vector3 position, camera;
            public float time;
        }

        private readonly List<Vote> votes = new List<Vote>(Capacity);
        private long lastId = long.MinValue;
        public int Count => votes.Count;
        public int ViewCount { get; private set; }
        public float SpreadMeters { get; private set; }
        public Vector3 Mean { get; private set; }
        public string Reason { get; private set; } = "observations";

        public void Clear()
        {
            votes.Clear();
            lastId = long.MinValue;
            ViewCount = 0;
            SpreadMeters = 0f;
            Mean = Vector3.zero;
            Reason = "observations";
        }

        public bool TryAdd(long id, Vector3 position, Vector3 camera, float time,
            out Vector3 confirmedPosition)
            => TryAddCore(id, position, camera, time, true, out confirmedPosition);

        // The caller must already have an accepted height-constrained multiview
        // solve. Each vote below is checked against a NEW complete range cycle;
        // it does not add observability by repeatedly voting for a cached fit.
        public bool TryAddValidatedMultiviewCycle(long id, Vector3 position, Vector3 camera,
            float time, Vector3[] anchors, Vector3 ranges, out Vector3 confirmedPosition)
        {
            confirmedPosition = Mean;
            if (!RangeCycleMatchesCandidate(position, anchors, ranges))
            {
                Reason = "incompatible_cycle";
                return false;
            }
            return TryAddCore(id, position, camera, time, false, out confirmedPosition);
        }

        public static bool RangeCycleMatchesCandidate(Vector3 position, Vector3[] anchors, Vector3 ranges)
        {
            if (!Finite(position) || anchors == null || anchors.Length != 3 || !Finite(ranges)) return false;
            float squared = 0f;
            for (int i = 0; i < 3; i++)
            {
                if (!Finite(anchors[i]) || ranges[i] < .08f || ranges[i] > 35f) return false;
                float error = Vector3.Distance(position, anchors[i]) - ranges[i];
                if (Mathf.Abs(error) > .20f) return false;
                squared += error * error;
            }
            return Mathf.Sqrt(squared / 3f) <= .12f;
        }

        private bool TryAddCore(long id, Vector3 position, Vector3 camera, float time,
            bool requireAdditionalViews, out Vector3 confirmedPosition)
        {
            confirmedPosition = Mean;
            if (!Finite(position) || !Finite(camera) || !Finite(time))
            {
                Reason = "invalid";
                return false;
            }
            if (id == lastId || votes.Exists(vote => vote.id == id))
            { Reason = "duplicate"; return false; }
            votes.RemoveAll(vote => time < vote.time || time - vote.time > MaximumAgeSeconds);
            // A new cluster starts a new confirmation; never average a jump into
            // the last accepted scanner position merely because its sigma is low.
            foreach (Vote vote in votes)
                if (Vector3.Distance(position, vote.position) > MaximumSpreadMeters)
                {
                    votes.Clear();
                    break;
                }
            lastId = id;
            votes.Add(new Vote { id = id, position = position, camera = camera, time = time });
            if (votes.Count > Capacity) votes.RemoveAt(0);

            Mean = Vector3.zero;
            SpreadMeters = 0f;
            var views = new List<Vector3>();
            foreach (Vote vote in votes)
            {
                Mean += vote.position;
                bool distinct = true;
                foreach (Vector3 view in views)
                    if (Vector3.Distance(view, vote.camera) < MinimumViewSeparationMeters)
                    { distinct = false; break; }
                if (distinct) views.Add(vote.camera);
                foreach (Vote other in votes)
                    SpreadMeters = Mathf.Max(SpreadMeters,
                        Vector3.Distance(vote.position, other.position));
            }
            Mean /= votes.Count;
            ViewCount = views.Count;
            confirmedPosition = Mean;
            Reason = votes.Count < MinimumObservations ? "observations"
                : requireAdditionalViews && ViewCount < 2 ? "viewpoints"
                : time - votes[0].time < MinimumSpanSeconds ? "span"
                : "confirmed";
            return Reason == "confirmed";
        }

        public static bool RequiresHeadingRevalidation(Vector3 previousOrigin, Vector3 nextOrigin)
            => Vector3.Distance(previousOrigin, nextOrigin) > MaximumSpreadMeters;

        public static bool MatchesSupportHeight(float tagY, float supportY,
            float originHeight, float tagOffsetY)
            => Finite(tagY) && Finite(supportY) && Finite(originHeight) && Finite(tagOffsetY) &&
                Mathf.Abs(tagY - (supportY + originHeight + tagOffsetY)) <= .03f;

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
