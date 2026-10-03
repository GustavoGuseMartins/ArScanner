using UnityEngine;

namespace ArScanner.Spatial
{
    // The radio geometry measures position relative to the phone, not scanner heading.
    // A forward-facing camera prior chooses one of the two mirror solutions; it does
    // not turn a poorly conditioned three-range estimate into an accurate 3D fix.
    public enum UwbInstantQuality
    {
        Unavailable,
        IncompatibleRanges,
        Ambiguous,
        LowConfidence,
        Experimental
    }

    public struct UwbInstantPoseEstimate
    {
        public Vector3 scannerWorld;
        public Vector3 correctedRanges;
        public float rmsResidualMeters;
        public float geometrySigmaMeters;
        public float mirrorSeparationMeters;
        public UwbInstantQuality quality;
        public bool usedTemporalPrior;
    }

    /// <summary>
    /// Single-epoch position estimate from the three DWM1000 radios on the phone.
    /// The caller supplies saved affine range calibration: corrected = scale*raw+offset.
    /// This class intentionally does not infer calibration from one epoch.
    /// </summary>
    public sealed class UwbInstantPoseEstimator
    {
        private const float MinimumRange = .08f;
        private const float MaximumRange = 35f;
        private const float NominalRangeSigma = .05f;
        // The phone board has 175 mm between the two lower radios.  A 15 cm
        // allowance was rejecting useful room-scale fixes before the AR prior
        // and temporal filter could help.  Keep the check conservative, but
        // match the multiview estimator's 25 cm gross inconsistency limit.
        private const float GrossPairwiseExcess = .25f;
        private bool hasPosition;
        private Vector3 previousWorld;

        public bool HasPosition => hasPosition;
        public Vector3 PreviousWorld => previousWorld;

        public void Clear()
        {
            hasPosition = false;
            previousWorld = Vector3.zero;
        }

        /// <summary>
        /// Returns true when a bounded experimental position exists. Always inspect
        /// result.quality and result.geometrySigmaMeters before using it for mapping.
        /// Three coplanar radios alone cannot resolve the reflected solution.
        /// </summary>
        public bool TryUpdate(Vector3[] anchorsWorld, float[] rawRanges, float[] scales,
            float[] offsets, Vector3 cameraWorld, Vector3 cameraForward, float deltaSeconds,
            out UwbInstantPoseEstimate result)
        {
            result = new UwbInstantPoseEstimate
            {
                scannerWorld = hasPosition ? previousWorld : Vector3.zero,
                rmsResidualMeters = float.PositiveInfinity,
                geometrySigmaMeters = float.PositiveInfinity,
                quality = UwbInstantQuality.Unavailable
            };
            if (anchorsWorld == null || anchorsWorld.Length != 3 ||
                rawRanges == null || rawRanges.Length != 3 ||
                scales == null || scales.Length != 3 ||
                offsets == null || offsets.Length != 3 ||
                !Finite(cameraWorld) || !Finite(cameraForward) ||
                cameraForward.sqrMagnitude < .25f) return false;

            var corrected = new float[3];
            for (int k = 0; k < 3; k++)
            {
                if (!Finite(anchorsWorld[k]) || !Finite(rawRanges[k]) ||
                    !Finite(scales[k]) || !Finite(offsets[k]) ||
                    scales[k] <= 0f) return false;
                corrected[k] = scales[k] * rawRanges[k] + offsets[k];
                if (!Finite(corrected[k]) || corrected[k] < MinimumRange ||
                    corrected[k] > MaximumRange) return false;
            }
            result.correctedRanges = new Vector3(corrected[0],corrected[1],corrected[2]);
            result.usedTemporalPrior = hasPosition;
            for (int i = 0; i < 3; i++)
                for (int j = i + 1; j < 3; j++)
                    if (Mathf.Abs(corrected[i]-corrected[j]) >
                        Vector3.Distance(anchorsWorld[i],anchorsWorld[j]) + GrossPairwiseExcess)
                    {
                        result.quality = UwbInstantQuality.IncompatibleRanges;
                        return false;
                    }

            Vector3 forward = cameraForward.normalized;
            float approximateDistance = Mathf.Clamp((corrected[0]+corrected[1]+corrected[2])/3f,
                .15f, MaximumRange);
            Vector3 prior = hasPosition ? previousWorld : cameraWorld + forward*approximateDistance;
            bool exact = TryIntersectSpheres(anchorsWorld,corrected,out Vector3 first,
                out Vector3 second,out float mirrorSeparation);
            result.mirrorSeparationMeters = mirrorSeparation;

            Vector3 measurement;
            float scoreGap = float.PositiveInfinity;
            if (exact)
            {
                // The scanner is normally in front of the phone at acquisition.
                // Thereafter continuity takes precedence over the current gaze.
                float firstScore = Vector3.Distance(first,prior);
                float secondScore = Vector3.Distance(second,prior);
                scoreGap = Mathf.Abs(firstScore-secondScore);
                measurement = firstScore <= secondScore ? first : second;
            }
            else
            {
                // Small sphere inconsistencies are inevitable with real ranges.
                // A regularized fit gives a provisional point near the AR prior,
                // while the residual and quality make the inconsistency explicit.
                if (!UwbMotionEstimator.TryEstimate(anchorsWorld,corrected,prior,.08f,
                    out measurement,out float fitResidual) || fitResidual > .25f)
                {
                    result.quality = UwbInstantQuality.IncompatibleRanges;
                    return false;
                }
            }

            if (!Finite(measurement)) return false;
            Vector3 candidate = measurement;
            if (hasPosition)
            {
                float dt = Finite(deltaSeconds) ? Mathf.Clamp(deltaSeconds,.01f,.5f) : .1f;
                float maxStep = 3f*dt + .12f;
                candidate = Vector3.MoveTowards(previousWorld,candidate,maxStep);
                candidate = Vector3.Lerp(previousWorld,candidate,1f-Mathf.Exp(-7f*dt));
            }
            float residualSquared = 0f;
            for (int i = 0; i < 3; i++)
            {
                float e = Vector3.Distance(candidate,anchorsWorld[i])-corrected[i];
                residualSquared += e*e;
            }
            float residual = Mathf.Sqrt(residualSquared/3f);
            if (!Finite(residual) || residual > .35f)
            {
                result.quality = UwbInstantQuality.IncompatibleRanges;
                return false;
            }

            float sigma = UwbMotionEstimator.GeometrySigma(anchorsWorld,candidate,
                Mathf.Max(NominalRangeSigma,residual));
            result.scannerWorld = candidate;
            result.rmsResidualMeters = residual;
            result.geometrySigmaMeters = sigma;
            bool ambiguous = exact && mirrorSeparation > .20f && scoreGap < .12f;
            result.quality = ambiguous ? UwbInstantQuality.Ambiguous :
                !exact || !Finite(sigma) || sigma > .5f || residual > .10f
                    ? UwbInstantQuality.LowConfidence : UwbInstantQuality.Experimental;
            previousWorld = candidate;
            hasPosition = true;
            return true;
        }

        private static bool TryIntersectSpheres(Vector3[] a, float[] r, out Vector3 first,
            out Vector3 second, out float mirrorSeparation)
        {
            first = second = Vector3.zero;
            mirrorSeparation = 0f;
            Vector3 baseline = a[2]-a[1];
            float d = baseline.magnitude;
            if (d < .001f) return false;
            Vector3 ex = baseline/d;
            Vector3 top = a[0]-a[1];
            float i = Vector3.Dot(ex,top);
            Vector3 transverse = top-i*ex;
            float j = transverse.magnitude;
            if (j < .001f) return false;
            Vector3 ey = transverse/j;
            Vector3 ez = Vector3.Cross(ex,ey).normalized;
            double x = ((double)r[1]*r[1]-(double)r[2]*r[2]+(double)d*d)/(2.0*d);
            double y = ((double)r[1]*r[1]-(double)r[0]*r[0]+(double)i*i+
                (double)j*j-2*i*x)/(2*j);
            double zSquared = (double)r[1]*r[1]-x*x-y*y;
            if (double.IsNaN(zSquared) || zSquared < -1e-6) return false;
            float z = Mathf.Sqrt((float)System.Math.Max(0d,zSquared));
            Vector3 center = a[1]+ex*(float)x+ey*(float)y;
            first = center+ez*z;
            second = center-ez*z;
            mirrorSeparation = 2f*z;
            return Finite(first) && Finite(second);
        }

        private static bool Finite(Vector3 value) =>
            Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
