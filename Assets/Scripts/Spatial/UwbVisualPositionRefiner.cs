using System.Collections.Generic;
using UnityEngine;

namespace ArScanner.Spatial
{
    /// <summary>
    /// Uses repeated AR depth hits on the scanner body as a bounded horizontal
    /// check on a stationary UWB solution. A single hit is never enough: a
    /// nearby object or a stale AR hit must not relocate the scan origin.
    /// </summary>
    public sealed class UwbVisualPositionRefiner
    {
        private const int MaxObservations = 32;
        private const float MaxAgeSeconds = 60f;
        private const float ClusterRadiusMeters = .18f;
        private const float MinimumCameraBaselineMeters = .25f;
        private const float MaximumUwbVisualGapMeters = .8f;
        private const float SurfaceAllowanceMeters = .12f;
        private const float MaximumCorrectionMeters = .25f;

        private struct Observation
        {
            public Vector3 surface;
            public Vector3 camera;
            public float time;
        }

        private readonly List<Observation> observations = new List<Observation>();
        public int ObservationCount => observations.Count;

        public void Clear() => observations.Clear();

        public bool Add(Vector3 surface, Vector3 camera, float time)
        {
            if (!Finite(surface) || !Finite(camera) || !Finite(time)) return false;
            Prune(time);
            if (observations.Count > 0)
            {
                Observation last = observations[observations.Count - 1];
                if (time - last.time < .35f ||
                    (Vector3.Distance(camera, last.camera) < .04f && time - last.time < 1f))
                    return false;
            }
            observations.Add(new Observation { surface = surface, camera = camera, time = time });
            if (observations.Count > MaxObservations) observations.RemoveAt(0);
            return true;
        }

        public bool TryRefine(Vector3 uwbTag, float time, out Vector3 correctedTag,
            out Vector3 visualSurface, out float correctionMeters,
            out float cameraBaselineMeters, out int supportingObservations)
        {
            correctedTag = uwbTag;
            visualSurface = Vector3.zero;
            correctionMeters = cameraBaselineMeters = 0f;
            supportingObservations = 0;
            if (!Finite(uwbTag) || !Finite(time)) return false;
            Prune(time);

            int bestSeed = -1;
            float bestDistance = float.PositiveInfinity;
            for (int seed = 0; seed < observations.Count; seed++)
            {
                int count = 0;
                float minCameraX = float.PositiveInfinity, maxCameraX = float.NegativeInfinity;
                float minCameraZ = float.PositiveInfinity, maxCameraZ = float.NegativeInfinity;
                Vector3 sum = Vector3.zero;
                foreach (Observation observation in observations)
                {
                    Vector3 delta = observation.surface - observations[seed].surface;
                    if (new Vector2(delta.x, delta.z).magnitude > ClusterRadiusMeters) continue;
                    count++;
                    sum += observation.surface;
                    minCameraX = Mathf.Min(minCameraX, observation.camera.x);
                    maxCameraX = Mathf.Max(maxCameraX, observation.camera.x);
                    minCameraZ = Mathf.Min(minCameraZ, observation.camera.z);
                    maxCameraZ = Mathf.Max(maxCameraZ, observation.camera.z);
                }
                float baseline = new Vector2(maxCameraX - minCameraX,
                    maxCameraZ - minCameraZ).magnitude;
                if (count < 3 || baseline < MinimumCameraBaselineMeters) continue;
                Vector3 mean = sum / count;
                float distance = new Vector2(mean.x - uwbTag.x, mean.z - uwbTag.z).magnitude;
                if (distance > MaximumUwbVisualGapMeters) continue;
                if (bestSeed < 0 || count > supportingObservations ||
                    (count == supportingObservations && distance < bestDistance))
                {
                    bestSeed = seed;
                    bestDistance = distance;
                    supportingObservations = count;
                    cameraBaselineMeters = baseline;
                    visualSurface = mean;
                }
            }
            if (bestSeed < 0) return false;
            Vector2 error = new Vector2(visualSurface.x - uwbTag.x,
                visualSurface.z - uwbTag.z);
            if (error.magnitude <= SurfaceAllowanceMeters) return false;
            correctionMeters = Mathf.Min(MaximumCorrectionMeters,
                .65f * (error.magnitude - SurfaceAllowanceMeters));
            Vector2 correction = error.normalized * correctionMeters;
            correctedTag.x += correction.x;
            correctedTag.z += correction.y;
            return true;
        }

        private void Prune(float time)
        {
            observations.RemoveAll(sample => time < sample.time ||
                time - sample.time > MaxAgeSeconds);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) =>
            Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
