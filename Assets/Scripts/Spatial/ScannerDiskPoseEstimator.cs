using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArScanner.Spatial
{
    public struct ScannerVisualPoseObservation
    {
        public Vector3 originWorld, tagWorld, cameraWorld;
        public float headYawDegrees, observedPanDegrees, timeSeconds;
        public float positionSpreadMeters, yawSpreadDegrees;
        public int supportingViews, supportingObservations;
        public long poseEpoch;
    }

    public struct ScannerDiskBlob
    {
        public Vector2 centerPixels;
        public float xx, xy, yy;
        public int area;
        public RectInt bounds;
    }

    public struct ScannerDiskView
    {
        public Vector3 cameraWorld, centerWorld;
        public Matrix4x4 worldToClip, displayMatrix;
        public int width, height;
        public ScannerDiskBlob blob;
        public float timeSeconds, panDegrees;
    }

    // Model of the ORIGINAL orange circular LiDAR cover, not a printed marker.
    // Dimensions come from scanner_v2_montado: diameter 68 mm, front at +88 mm.
    // A colour region alone is insufficient: its projected shape must agree
    // with the same upright disk from distinct camera positions.
    public sealed class ScannerDiskPoseEstimator
    {
        public const float CoverRadius = .034f;
        public const float CoverForward = .088f;
        public const float CoverAboveAxis = .05f;
        private readonly List<ScannerDiskView> views = new List<ScannerDiskView>();
        public int ObservationCount => views.Count;
        public string Reason { get; private set; } = "views";
        public void Clear() { views.Clear(); Reason = "views"; }

        public bool Add(ScannerDiskView view)
        {
            if (!Finite(view.centerWorld) || !Finite(view.cameraWorld) || !Finite(view.timeSeconds) ||
                !Finite(view.panDegrees) || view.blob.area < 40)
                return false;
            views.RemoveAll(v => view.timeSeconds < v.timeSeconds || view.timeSeconds - v.timeSeconds > 20f);
            if (views.Count > 0)
            {
                ScannerDiskView previous = views[views.Count - 1];
                if (Mathf.Abs(Mathf.DeltaAngle(previous.panDegrees, view.panDegrees)) > .5f)
                    Clear();
                else if (view.timeSeconds - previous.timeSeconds < .35f) return false;
            }
            // Keep enough samples to test another view, without letting a long
            // stationary pause drown out the independent camera positions.
            int nearby = 0;
            foreach (var v in views)
                if (Vector3.Distance(v.cameraWorld, view.cameraWorld) < .08f) nearby++;
            if (nearby >= 3) return false;
            views.Add(view);
            if (views.Count > 12) views.RemoveAt(0);
            return true;
        }

        public bool TryEstimate(float axisHeight, Vector3 tagOffset, long epoch,
            out ScannerVisualPoseObservation observation)
        {
            observation = default;
            Reason = "views";
            if (!Finite(axisHeight) || !Finite(tagOffset)) { Reason = "invalid_geometry"; return false; }
            if (views.Count < 3) return false;
            Vector3 center = Vector3.zero;
            float baseline = 0, horizontalBaseline = 0;
            int independent = 0;
            var distinct = new List<Vector3>();
            foreach (var v in views)
            {
                center += v.centerWorld;
                bool newView = true;
                foreach (Vector3 p in distinct)
                    if (Vector3.Distance(p, v.cameraWorld) < .12f) newView = false;
                if (newView) { distinct.Add(v.cameraWorld); independent++; }
                foreach (var other in views)
                {
                    baseline = Mathf.Max(baseline, Vector3.Distance(v.cameraWorld, other.cameraWorld));
                    Vector3 separation = v.cameraWorld - other.cameraWorld;
                    horizontalBaseline = Mathf.Max(horizontalBaseline, new Vector2(separation.x, separation.z).magnitude);
                }
            }
            center /= views.Count;
            float spread = 0;
            foreach (var v in views) spread = Mathf.Max(spread, Vector3.Distance(v.centerWorld, center));
            Reason = "position_spread";
            if (spread > .04f || Mathf.Abs(center.y - axisHeight - CoverAboveAxis) > .025f) return false;
            Reason = "baseline";
            // Height-only motion can have weak yaw sensitivity near the front.
            // Require an actual sideways view before accepting the object model.
            if (independent < 2 || baseline < .25f || horizontalBaseline < .20f) return false;

            float bestCost = float.PositiveInfinity;
            int bestYaw = 0;
            var costs = new float[360];
            for (int yaw = 0; yaw < 360; yaw++)
            {
                float cost = 0;
                bool visible = true;
                Vector3 forward = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                foreach (var v in views)
                {
                    // The orange front, rather than the back of the disk, has
                    // to face every observing camera. Reject edge-on views.
                    if (Vector3.Dot(forward, (v.cameraWorld - center).normalized) < .15f)
                    { visible = false; break; }
                    cost += ShapeCost(v, center, yaw);
                }
                cost = visible ? cost / views.Count : float.PositiveInfinity;
                costs[yaw] = cost;
                if (cost < bestCost) { bestCost = cost; bestYaw = yaw; }
            }
            Reason = "disk_geometry";
            if (!Finite(bestCost) || bestCost > .35f) return false;
            float uncertainty = 0;
            for (int yaw = 0; yaw < 360; yaw++)
                if (costs[yaw] <= bestCost + .018f)
                    uncertainty = Mathf.Max(uncertainty, Mathf.Abs(Mathf.DeltaAngle(yaw, bestYaw)));
            Reason = "yaw_ambiguous";
            if (uncertainty > 8f) return false;
            Quaternion rotation = Quaternion.Euler(0, bestYaw, 0);
            Vector3 origin = center - rotation * new Vector3(0, CoverAboveAxis, CoverForward);
            origin.y = axisHeight;
            var latest = views[views.Count - 1];
            observation = new ScannerVisualPoseObservation {
                originWorld = origin, tagWorld = origin + rotation * tagOffset,
                cameraWorld = latest.cameraWorld, headYawDegrees = bestYaw,
                observedPanDegrees = latest.panDegrees, timeSeconds = latest.timeSeconds,
                positionSpreadMeters = spread, yawSpreadDegrees = uncertainty,
                supportingViews = independent, supportingObservations = views.Count,
                poseEpoch = epoch
            };
            Reason = "confirmed";
            return true;
        }

        public static float ShapeCost(ScannerDiskView view, Vector3 center, float yaw)
        {
            if (!TryPredictMoments(view, center, yaw, out Vector2 mean,
                out float xx, out float xy, out float yy)) return float.PositiveInfinity;
            float trace = Mathf.Max(xx + yy, 1f);
            float shape = Mathf.Sqrt(Mathf.Pow(xx - view.blob.xx, 2) +
                2 * Mathf.Pow(xy - view.blob.xy, 2) + Mathf.Pow(yy - view.blob.yy, 2)) / trace;
            float location = Vector2.Distance(mean, view.blob.centerPixels) / Mathf.Sqrt(trace);
            return shape + .25f * location;
        }

        public static bool TryPredictMoments(ScannerDiskView view, Vector3 center, float yaw,
            out Vector2 mean, out float xx, out float xy, out float yy)
        {
            mean = Vector2.zero; xx = xy = yy = 0;
            Vector3 right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
            const int count = 32;
            double sx = 0, sy = 0, sxx = 0, sxy = 0, syy = 0;
            for (int i = 0; i < count; i++)
            {
                float a = i * 2f * Mathf.PI / count;
                Vector3 p = center + CoverRadius * (right * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a));
                if (!WorldToPixels(view, p, out Vector2 point)) return false;
                sx += point.x; sy += point.y;
                sxx += (double)point.x * point.x; sxy += (double)point.x * point.y;
                syy += (double)point.y * point.y;
            }
            double mx = sx / count, my = sy / count;
            mean = new Vector2((float)mx, (float)my);
            // Covariance of a filled disk is half its circular boundary's.
            xx = (float)((sxx / count - mx * mx) * .5);
            xy = (float)((sxy / count - mx * my) * .5);
            yy = (float)((syy / count - my * my) * .5);
            return xx + yy >= 8f;
        }

        public static bool WorldToPixels(ScannerDiskView view, Vector3 world, out Vector2 pixels)
        {
            Vector4 clip = view.worldToClip * new Vector4(world.x, world.y, world.z, 1);
            pixels = default;
            if (clip.w <= .01f) return false;
            Vector2 viewport = new Vector2(clip.x / clip.w * .5f + .5f, clip.y / clip.w * .5f + .5f);
            Vector2 raw = ViewportToImage(viewport, view.displayMatrix);
            pixels = new Vector2(raw.x * view.width, raw.y * view.height);
            return Finite(pixels.x) && Finite(pixels.y);
        }

        // Matches ARCoreBackground's row-major mul(float4(uv,1,0), display).
        // Conversion uses Transformation.None: retain the sensor's original UVs.
        public static Vector2 ViewportToImage(Vector2 viewport, Matrix4x4 matrix) =>
            new Vector2(viewport.x * matrix.m00 + viewport.y * matrix.m10 + matrix.m20,
                viewport.x * matrix.m01 + viewport.y * matrix.m11 + matrix.m21);

        public static bool ImageToViewport(Vector2 raw, Matrix4x4 matrix, out Vector2 viewport)
        {
            float determinant = matrix.m00 * matrix.m11 - matrix.m10 * matrix.m01;
            viewport = default;
            if (!Finite(determinant) || Mathf.Abs(determinant) < .00001f) return false;
            float x = raw.x - matrix.m20, y = raw.y - matrix.m21;
            viewport = new Vector2((matrix.m11 * x - matrix.m10 * y) / determinant,
                (-matrix.m01 * x + matrix.m00 * y) / determinant);
            return viewport.x >= 0 && viewport.x <= 1 && viewport.y >= 0 && viewport.y <= 1;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }

    public sealed class ScannerOrangeCoverDetector
    {
        private byte[] mask;
        private int[] queue;
        public void Find(byte[] rgb, int width, int height, List<ScannerDiskBlob> blobs)
        {
            blobs.Clear();
            int total = width * height;
            if (rgb == null || rgb.Length < total * 3) return;
            if (mask == null || mask.Length != total) { mask = new byte[total]; queue = new int[total]; }
            for (int i = 0; i < total; i++)
            {
                int r = rgb[i * 3], g = rgb[i * 3 + 1], b = rgb[i * 3 + 2];
                // Original cover ranges from red-orange in shadow to orange.
                // Colour supplies candidates, not object identity. Some brown
                // surfaces share this colour; the physical disk model, support,
                // UWB proximity and distinct views must validate the candidate.
                mask[i] = (byte)(r >= 65 && g >= 18 && r > g * 1.65f &&
                    g > b * 1.35f && r - b >= 60 ? 1 : 0);
            }
            for (int seed = 0; seed < total; seed++)
            {
                if (mask[seed] != 1) continue;
                int read = 0, write = 1;
                queue[0] = seed; mask[seed] = 2;
                double sx = 0, sy = 0, sxx = 0, sxy = 0, syy = 0;
                int left = width, right = 0, bottom = height, top = 0;
                while (read < write)
                {
                    int index = queue[read++], x = index % width, y = index / width;
                    sx += x; sy += y; sxx += x * x; sxy += x * y; syy += y * y;
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    bottom = Math.Min(bottom, y); top = Math.Max(top, y);
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                            int adjacent = ny * width + nx;
                            if (mask[adjacent] != 1) continue;
                            mask[adjacent] = 2; queue[write++] = adjacent;
                        }
                }
                int w = right - left + 1, h = top - bottom + 1;
                float fill = write / (float)(w * h);
                if (write < 40 || write > total * .08f || left < 2 || bottom < 2 ||
                    right >= width - 2 || top >= height - 2 || w < 5 || h < 8 || fill < .35f) continue;
                float cx = (float)(sx / write), cy = (float)(sy / write);
                float xx = (float)(sxx / write - cx * cx), xy = (float)(sxy / write - cx * cy),
                    yy = (float)(syy / write - cy * cy);
                float determinant = xx * yy - xy * xy;
                if (determinant <= 0 || determinant / Mathf.Pow(xx + yy, 2) < .025f) continue;
                blobs.Add(new ScannerDiskBlob { centerPixels = new Vector2(cx + .5f, cy + .5f),
                    xx = xx, xy = xy, yy = yy, area = write, bounds = new RectInt(left, bottom, w, h) });
            }
        }
    }
}
