using System;
using UnityEngine;

namespace ArScanner.Spatial
{
    // A calibrated, regularized least-squares estimate. With the phone-sized
    // anchor triangle, range data constrain depth much more than direction;
    // the prior prevents the poorly observed axes from jumping arbitrarily.
    public static class UwbMotionEstimator
    {
        public static bool TryRobustBias(float[] samples, out float bias)
        {
            bias = 0f;
            if (samples == null || samples.Length < 10) return false;
            var sorted = (float[])samples.Clone();
            foreach (float sample in sorted) if (!Finite(sample)) return false;
            Array.Sort(sorted);
            int trim = sorted.Length / 5;
            if (sorted[sorted.Length-trim-1]-sorted[trim] > .20f) return false;
            float sum = 0f;
            for (int i = trim; i < sorted.Length-trim; i++) sum += sorted[i];
            bias = sum/(sorted.Length-2*trim);
            return Finite(bias);
        }

        public static bool TryEstimate(Vector3[] anchors, float[] ranges, Vector3 prior,
            float priorWeight, out Vector3 estimate, out float rmsResidual)
        {
            estimate = prior;
            rmsResidual = float.PositiveInfinity;
            if (anchors == null || ranges == null || anchors.Length != 3 || ranges.Length != 3 ||
                !Finite(prior) || !Finite(priorWeight) || priorWeight <= 0f) return false;
            for (int i = 0; i < 3; i++)
                if (!Finite(anchors[i]) || !Finite(ranges[i]) || ranges[i] < .08f || ranges[i] > 35f)
                    return false;

            Vector3 position = prior;
            for (int iteration = 0; iteration < 12; iteration++)
            {
                float h00 = priorWeight, h01 = 0f, h02 = 0f;
                float h11 = priorWeight, h12 = 0f, h22 = priorWeight;
                Vector3 gradient = priorWeight * (position - prior);
                for (int i = 0; i < 3; i++)
                {
                    Vector3 delta = position - anchors[i];
                    float length = Mathf.Max(.001f, delta.magnitude);
                    float residual = length - ranges[i];
                    float weight = Mathf.Min(1f, .15f / Mathf.Max(.001f, Mathf.Abs(residual)));
                    Vector3 direction = delta / length;
                    gradient += weight * residual * direction;
                    h00 += weight * direction.x * direction.x;
                    h01 += weight * direction.x * direction.y;
                    h02 += weight * direction.x * direction.z;
                    h11 += weight * direction.y * direction.y;
                    h12 += weight * direction.y * direction.z;
                    h22 += weight * direction.z * direction.z;
                }
                if (!SolveSymmetric(h00,h01,h02,h11,h12,h22,-gradient,out Vector3 step))
                    return false;
                step = Vector3.ClampMagnitude(step, .4f);
                position += step;
                if (!Finite(position)) return false;
                if (step.sqrMagnitude < .000001f) break;
            }
            float sumSquared = 0f;
            for (int i = 0; i < 3; i++)
            {
                float residual = Vector3.Distance(position, anchors[i]) - ranges[i];
                sumSquared += residual * residual;
            }
            estimate = position;
            rmsResidual = Mathf.Sqrt(sumSquared / 3f);
            return Finite(rmsResidual);
        }

        public static float GeometrySigma(Vector3[] anchors, Vector3 position, float rangeSigma)
        {
            if (anchors == null || anchors.Length != 3 || !Finite(position) ||
                !Finite(rangeSigma) || rangeSigma <= 0f) return float.PositiveInfinity;
            Vector3[] direction = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                if (!Finite(anchors[i])) return float.PositiveInfinity;
                Vector3 delta = position - anchors[i];
                if (delta.sqrMagnitude < .0001f) return float.PositiveInfinity;
                direction[i] = delta.normalized;
            }
            Vector3 c0 = Vector3.Cross(direction[1],direction[2]);
            Vector3 c1 = Vector3.Cross(direction[2],direction[0]);
            Vector3 c2 = Vector3.Cross(direction[0],direction[1]);
            float determinant = Mathf.Abs(Vector3.Dot(direction[0],c0));
            if (determinant < 1e-8f) return float.PositiveInfinity;
            return rangeSigma * Mathf.Sqrt(c0.sqrMagnitude+c1.sqrMagnitude+c2.sqrMagnitude)/determinant;
        }

        private static bool SolveSymmetric(float a, float b, float c, float d, float e, float f,
            Vector3 rhs, out Vector3 solution)
        {
            solution = Vector3.zero;
            float determinant = a*(d*f-e*e)-b*(b*f-c*e)+c*(b*e-c*d);
            if (!Finite(determinant) || Mathf.Abs(determinant) < 1e-9f) return false;
            solution.x = (rhs.x*(d*f-e*e) + rhs.y*(c*e-b*f) + rhs.z*(b*e-c*d))/determinant;
            solution.y = (rhs.x*(c*e-b*f) + rhs.y*(a*f-c*c) + rhs.z*(b*c-a*e))/determinant;
            solution.z = (rhs.x*(b*e-c*d) + rhs.y*(b*c-a*e) + rhs.z*(a*d-b*b))/determinant;
            return Finite(solution);
        }

        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
