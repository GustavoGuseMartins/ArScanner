using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArScanner.Spatial
{
    // The radios range sequentially. Wait for a pause before pairing a cycle
    // with a single camera pose, until individual radio timestamps are available.
    public sealed class UwbPhonePauseGate
    {
        private float since = -1f;
        private Vector3 referencePosition;
        private Quaternion referenceRotation;
        public void Clear() => since = -1f;
        public bool Update(Vector3 position, Quaternion rotation, float time)
        {
            if (since < 0f || time < since || Vector3.Distance(position, referencePosition) > .02f ||
                Quaternion.Angle(rotation, referenceRotation) > 3f)
            {
                since = time;
                referencePosition = position;
                referenceRotation = rotation;
            }
            return time - since >= .6f;
        }
    }

    /// <summary>
    /// Estimates a stationary UWB tag from several AR camera poses. The phone's
    /// movement supplies the baseline that the compact three-radio PCB lacks.
    /// A sample is one complete three-range cycle and the AR pose at receipt.
    /// </summary>
    public sealed class UwbArMultiviewEstimator
    {
        private const int MaxSamples = 80;
        private const float MinimumSampleBaseline = .10f;
        private const float MinimumTotalBaseline = .45f;
        private const float GrossPairwiseExcess = .25f;
        private const float InlierResidual = .20f;
        private const float NominalRangeSigma = .05f;

        private struct Sample
        {
            public Vector3[] anchors;
            public Vector3 ranges;
            public Vector3 center;
        }

        private readonly List<Sample> samples = new List<Sample>(MaxSamples);

        public int SampleCount => samples.Count;
        public string RejectionReason { get; private set; } = "samples";
        public string LastSampleResult { get; private set; } = "none";
        public float PhoneBaselineMeters { get; private set; }
        public float GeometryCondition { get; private set; }

        public string ProgressMessage => RejectionReason == "samples"
            ? $"Localizando scanner: {SampleCount} leituras. Com o pan parado, mova o celular para os lados e para cima; pause 2 s em cada posição."
            : RejectionReason == "baseline"
            ? $"Localizando scanner: deslocamento {PhoneBaselineMeters:F2} m. Meça em posições separadas por pelo menos 0,45 m."
            : RejectionReason == "geometry" || RejectionReason == "singular"
            ? "Posições ainda pouco variadas: mova o celular para o lado e depois para cima; pause 2 s em cada posição."
            : "As leituras ainda divergem entre si. Pause com as antenas desobstruídas em outra posição; confira as distâncias se persistir.";

        public void Clear()
        {
            samples.Clear();
            RejectionReason = "samples";
            LastSampleResult = "none";
            PhoneBaselineMeters = GeometryCondition = 0f;
        }

        public bool AddSample(Vector3[] anchors, Vector3 ranges)
        {
            LastSampleResult = "invalid_range";
            if (anchors == null || anchors.Length != 3 || !Finite(ranges)) return false;
            for (int i = 0; i < 3; i++)
                if (!Finite(anchors[i]) || !Finite(ranges[i]) ||
                    ranges[i] < .08f || ranges[i] > 35f) return false;

            float d01 = Vector3.Distance(anchors[0], anchors[1]);
            float d02 = Vector3.Distance(anchors[0], anchors[2]);
            float d12 = Vector3.Distance(anchors[1], anchors[2]);
            if (Mathf.Abs(ranges.x - ranges.y) > d01 + GrossPairwiseExcess ||
                Mathf.Abs(ranges.x - ranges.z) > d02 + GrossPairwiseExcess ||
                Mathf.Abs(ranges.y - ranges.z) > d12 + GrossPairwiseExcess)
            {
                LastSampleResult = "pairwise";
                return false;
            }

            Vector3 center = (anchors[0] + anchors[1] + anchors[2]) / 3f;
            int nearbySamples = 0;
            for (int i = 0; i < samples.Count; i++)
                if (Vector3.Distance(samples[i].center, center) < MinimumSampleBaseline)
                    nearbySamples++;
            // Keep a few repeated cycles at each pause for averaging, while
            // requiring the complete set to span distinct camera positions.
            if (nearbySamples >= 4)
            {
                LastSampleResult = "position_full";
                return false;
            }

            var copy = new Vector3[3];
            copy[0] = anchors[0]; copy[1] = anchors[1]; copy[2] = anchors[2];
            samples.Add(new Sample { anchors = copy, ranges = ranges, center = center });
            if (samples.Count > MaxSamples) samples.RemoveAt(0);
            LastSampleResult = "accepted";
            return true;
        }

        public bool TryEstimate(out Vector3 position, out float residualMeters,
            out float geometrySigmaMeters, out float phoneBaselineMeters,
            out int inlierCount)
        {
            position = Vector3.zero;
            residualMeters = float.PositiveInfinity;
            geometrySigmaMeters = float.PositiveInfinity;
            phoneBaselineMeters = 0f;
            inlierCount = 0;
            RejectionReason = "samples";
            GeometryCondition = 0f;
            PhoneBaselineMeters = MaximumCenterBaseline();
            if (samples.Count < 8) return false;

            phoneBaselineMeters = PhoneBaselineMeters;
            RejectionReason = "baseline";
            if (phoneBaselineMeters < MinimumTotalBaseline) return false;

            RejectionReason = "singular";
            if (!TryLinearSeed(out position)) return false;
            for (int iteration = 0; iteration < 20; iteration++)
            {
                float h00 = 0f, h01 = 0f, h02 = 0f;
                float h11 = 0f, h12 = 0f, h22 = 0f;
                Vector3 gradient = Vector3.zero;
                for (int s = 0; s < samples.Count; s++)
                {
                    Sample sample = samples[s];
                    for (int i = 0; i < 3; i++)
                    {
                        Vector3 delta = position - sample.anchors[i];
                        float length = Mathf.Max(.001f, delta.magnitude);
                        float error = length - sample.ranges[i];
                        float weight = Mathf.Min(1f, .15f / Mathf.Max(.001f, Mathf.Abs(error)));
                        Vector3 direction = delta / length;
                        gradient += weight * error * direction;
                        h00 += weight * direction.x * direction.x;
                        h01 += weight * direction.x * direction.y;
                        h02 += weight * direction.x * direction.z;
                        h11 += weight * direction.y * direction.y;
                        h12 += weight * direction.y * direction.z;
                        h22 += weight * direction.z * direction.z;
                    }
                }
                if (!SolveSymmetric(h00, h01, h02, h11, h12, h22,
                    -gradient, out Vector3 step)) return false;
                step = Vector3.ClampMagnitude(step, .5f);
                position += step;
                if (!Finite(position) || step.sqrMagnitude < .000001f) break;
            }

            var absoluteErrors = new List<float>(samples.Count * 3);
            float ih00 = 0f, ih01 = 0f, ih02 = 0f;
            float ih11 = 0f, ih12 = 0f, ih22 = 0f;
            for (int s = 0; s < samples.Count; s++)
            {
                Sample sample = samples[s];
                for (int i = 0; i < 3; i++)
                {
                    Vector3 delta = position - sample.anchors[i];
                    float length = Mathf.Max(.001f, delta.magnitude);
                    float error = length - sample.ranges[i];
                    float absolute = Mathf.Abs(error);
                    absoluteErrors.Add(absolute);
                    if (absolute <= InlierResidual)
                    {
                        inlierCount++;
                        Vector3 direction = delta / length;
                        ih00 += direction.x * direction.x;
                        ih01 += direction.x * direction.y;
                        ih02 += direction.x * direction.z;
                        ih11 += direction.y * direction.y;
                        ih12 += direction.y * direction.z;
                        ih22 += direction.z * direction.z;
                    }
                }
            }
            if (absoluteErrors.Count == 0) return false;
            absoluteErrors.Sort();
            residualMeters = absoluteErrors[absoluteErrors.Count / 2];
            RejectionReason = "residual";
            if (inlierCount < 18 || residualMeters > .12f) return false;

            float minEigen, maxEigen;
            SymmetricEigenExtrema(ih00, ih01, ih02, ih11, ih12, ih22,
                out minEigen, out maxEigen);
            float condition = maxEigen > 1e-6f ? minEigen / maxEigen : 0f;
            GeometryCondition = condition;
            geometrySigmaMeters = NominalRangeSigma / Mathf.Sqrt(Mathf.Max(minEigen, 1e-6f));
            bool accepted = condition >= .08f && geometrySigmaMeters <= .5f;
            RejectionReason = accepted ? "accepted" : "geometry";
            return accepted;
        }

        // A horizontal AR support plane and measured mechanical height remove
        // the weakest dimension of the compact coplanar radio array. The plane
        // must be observed independently; never derive this height from UWB.
        public bool TryEstimateAtHeight(float tagHeight, out Vector3 position,
            out float residualMeters, out float horizontalSigmaMeters,
            out float phoneBaselineMeters, out int inlierCount)
        {
            position = Vector3.zero;
            residualMeters = horizontalSigmaMeters = float.PositiveInfinity;
            inlierCount = 0;
            phoneBaselineMeters = 0f;
            RejectionReason = "samples";
            GeometryCondition = 0f;
            if (!Finite(tagHeight) || samples.Count < 8) return false;

            float baseline = 0f;
            for (int i = 0; i < samples.Count; i++)
                for (int j = i + 1; j < samples.Count; j++)
                {
                    Vector3 delta = samples[i].center - samples[j].center;
                    baseline = Mathf.Max(baseline, new Vector2(delta.x, delta.z).magnitude);
                }
            PhoneBaselineMeters = phoneBaselineMeters = baseline;
            RejectionReason = "baseline";
            if (baseline < .35f) return false;

            Sample reference = samples[0];
            Vector3 refAnchor = reference.anchors[0];
            float refRange = reference.ranges[0];
            double refNorm = refAnchor.x * refAnchor.x + refAnchor.z * refAnchor.z +
                (tagHeight - refAnchor.y) * (tagHeight - refAnchor.y);
            double hxx = 0d, hxz = 0d, hzz = 0d, bx = 0d, bz = 0d;
            foreach (Sample sample in samples)
                for (int i = 0; i < 3; i++)
                {
                    Vector3 a = sample.anchors[i];
                    double rowX = 2d * (a.x - refAnchor.x);
                    double rowZ = 2d * (a.z - refAnchor.z);
                    double norm = a.x * a.x + a.z * a.z +
                        (tagHeight - a.y) * (tagHeight - a.y);
                    double value = norm - refNorm + refRange * refRange -
                        sample.ranges[i] * sample.ranges[i];
                    hxx += rowX * rowX; hxz += rowX * rowZ; hzz += rowZ * rowZ;
                    bx += rowX * value; bz += rowZ * value;
                }
            RejectionReason = "singular";
            double determinant = hxx * hzz - hxz * hxz;
            if (determinant < 1e-8d) return false;
            position = new Vector3((float)((bx * hzz - bz * hxz) / determinant),
                tagHeight, (float)((bz * hxx - bx * hxz) / determinant));
            if (!Finite(position)) return false;

            for (int iteration = 0; iteration < 20; iteration++)
            {
                float xx = 0f, xz = 0f, zz = 0f, gx = 0f, gz = 0f;
                foreach (Sample sample in samples)
                    for (int i = 0; i < 3; i++)
                    {
                        Vector3 delta = position - sample.anchors[i];
                        float length = Mathf.Max(.001f, delta.magnitude);
                        float error = length - sample.ranges[i];
                        float weight = Mathf.Min(1f, .15f / Mathf.Max(.001f, Mathf.Abs(error)));
                        float dx = delta.x / length, dz = delta.z / length;
                        xx += weight * dx * dx; xz += weight * dx * dz;
                        zz += weight * dz * dz;
                        gx += weight * error * dx; gz += weight * error * dz;
                    }
                float det = xx * zz - xz * xz;
                if (det < 1e-8f) return false;
                Vector2 step = new Vector2((-gx * zz + gz * xz) / det,
                    (-gz * xx + gx * xz) / det);
                step = Vector2.ClampMagnitude(step, .5f);
                position.x += step.x; position.z += step.y;
                if (!Finite(position)) return false;
                if (step.sqrMagnitude < .000001f) break;
            }

            var errors = new List<float>(samples.Count * 3);
            float infoXx = 0f, infoXz = 0f, infoZz = 0f;
            foreach (Sample sample in samples)
                for (int i = 0; i < 3; i++)
                {
                    Vector3 delta = position - sample.anchors[i];
                    float length = Mathf.Max(.001f, delta.magnitude);
                    float error = Mathf.Abs(length - sample.ranges[i]);
                    errors.Add(error);
                    if (error > InlierResidual) continue;
                    inlierCount++;
                    float dx = delta.x / length, dz = delta.z / length;
                    infoXx += dx * dx; infoXz += dx * dz; infoZz += dz * dz;
                }
            errors.Sort();
            residualMeters = errors[errors.Count / 2];
            RejectionReason = "residual";
            if (inlierCount < 18 || residualMeters > .12f) return false;

            float trace = infoXx + infoZz;
            float spread = Mathf.Sqrt(Mathf.Max(0f,
                (infoXx - infoZz) * (infoXx - infoZz) + 4f * infoXz * infoXz));
            float minimum = .5f * (trace - spread);
            float maximum = .5f * (trace + spread);
            GeometryCondition = maximum > 1e-6f ? minimum / maximum : 0f;
            horizontalSigmaMeters = NominalRangeSigma /
                Mathf.Sqrt(Mathf.Max(minimum, 1e-6f));
            bool accepted = GeometryCondition >= .08f && horizontalSigmaMeters <= .5f;
            RejectionReason = accepted ? "accepted" : "geometry";
            return accepted;
        }

        private bool TryLinearSeed(out Vector3 seed)
        {
            seed = Vector3.zero;
            Sample reference = samples[0];
            float h00 = 0f, h01 = 0f, h02 = 0f;
            float h11 = 0f, h12 = 0f, h22 = 0f;
            Vector3 rhs = Vector3.zero;
            Vector3 referenceAnchor = reference.anchors[0];
            float referenceRange = reference.ranges[0];
            float referenceNorm = Vector3.Dot(referenceAnchor, referenceAnchor);
            float referenceRangeSquared = referenceRange * referenceRange;
            for (int s = 0; s < samples.Count; s++)
            {
                for (int i = 0; i < 3; i++)
                {
                    Vector3 difference = samples[s].anchors[i] - referenceAnchor;
                    Vector3 row = 2f * difference;
                    float value = referenceRangeSquared - samples[s].ranges[i] * samples[s].ranges[i] +
                        Vector3.Dot(samples[s].anchors[i], samples[s].anchors[i]) - referenceNorm;
                    h00 += row.x * row.x; h01 += row.x * row.y; h02 += row.x * row.z;
                    h11 += row.y * row.y; h12 += row.y * row.z; h22 += row.z * row.z;
                    rhs += row * value;
                }
            }
            return SolveSymmetric(h00, h01, h02, h11, h12, h22, rhs, out seed) && Finite(seed);
        }

        private float MaximumCenterBaseline()
        {
            float maximum = 0f;
            for (int i = 0; i < samples.Count; i++)
                for (int j = i + 1; j < samples.Count; j++)
                    maximum = Mathf.Max(maximum, Vector3.Distance(samples[i].center, samples[j].center));
            return maximum;
        }

        private static bool SolveSymmetric(float a, float b, float c, float d, float e, float f,
            Vector3 rhs, out Vector3 solution)
        {
            solution = Vector3.zero;
            float determinant = a * (d * f - e * e) - b * (b * f - c * e) + c * (b * e - c * d);
            if (!Finite(determinant) || Mathf.Abs(determinant) < 1e-8f) return false;
            solution.x = (rhs.x * (d * f - e * e) + rhs.y * (c * e - b * f) +
                rhs.z * (b * e - c * d)) / determinant;
            solution.y = (rhs.x * (c * e - b * f) + rhs.y * (a * f - c * c) +
                rhs.z * (b * c - a * e)) / determinant;
            solution.z = (rhs.x * (b * e - c * d) + rhs.y * (b * c - a * e) +
                rhs.z * (a * d - b * b)) / determinant;
            return Finite(solution);
        }

        private static void SymmetricEigenExtrema(float a, float b, float c, float d, float e, float f,
            out float minimum, out float maximum)
        {
            // Three Jacobi sweeps are enough for the acceptance test; the matrix
            // is a small positive semidefinite information matrix.
            float[,] m = { { a, b, c }, { b, d, e }, { c, e, f } };
            for (int sweep = 0; sweep < 8; sweep++)
            {
                Rotate(m, 0, 1); Rotate(m, 0, 2); Rotate(m, 1, 2);
            }
            minimum = Mathf.Min(m[0, 0], Mathf.Min(m[1, 1], m[2, 2]));
            maximum = Mathf.Max(m[0, 0], Mathf.Max(m[1, 1], m[2, 2]));
        }

        private static void Rotate(float[,] m, int p, int q)
        {
            if (Mathf.Abs(m[p, q]) < 1e-7f) return;
            float theta = .5f * Mathf.Atan2(2f * m[p, q], m[q, q] - m[p, p]);
            float cosine = Mathf.Cos(theta), sine = Mathf.Sin(theta);
            float app = m[p, p], aqq = m[q, q], apq = m[p, q];
            m[p, p] = cosine * cosine * app - 2f * sine * cosine * apq + sine * sine * aqq;
            m[q, q] = sine * sine * app + 2f * sine * cosine * apq + cosine * cosine * aqq;
            m[p, q] = m[q, p] = 0f;
            for (int k = 0; k < 3; k++)
            {
                if (k == p || k == q) continue;
                float mkp = m[k, p], mkq = m[k, q];
                m[k, p] = m[p, k] = cosine * mkp - sine * mkq;
                m[k, q] = m[q, k] = sine * mkp + cosine * mkq;
            }
        }

        private static bool Finite(Vector3 value) =>
            Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
