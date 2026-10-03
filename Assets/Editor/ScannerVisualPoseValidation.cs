#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using ArScanner.Spatial;
using UnityEngine;

namespace ArScanner.EditorTools
{
    public static class ScannerVisualPoseValidation
    {
        private static readonly Vector3 Center = new Vector3(0f, .55f, 1.2f);
        private static readonly Vector3 TagOffset = new Vector3(.01f, .12f, .02f);

        public static void Run()
        {
            ValidateDisplayCoordinates();
            ValidateProjectedCover(0f);
            ValidateProjectedCover(57f);
            ValidateInsufficientAndContradictoryViews();
            ValidateColourCandidates();
            ValidateInvalidInputs();
            ValidateOptionalRecognition();
            Debug.Log("[ScannerVisualPoseValidation] PASS: projected disk, front/back assumption, independent views, display mapping, colour candidates and invalid inputs. Synthetic validation does not certify physical accuracy.");
        }

        private static void ValidateOptionalRecognition()
        {
            var go = new GameObject("OptionalCoverValidation");
            go.SetActive(false);
            try
            {
                var observer = go.AddComponent<ScannerVisualPoseObserver>();
                Require(!observer.enableNaturalCoverRecognition && !observer.TryGetObservation(out _),
                    "The experimental cover observer must be off by default and produce no mandatory observation.");
                observer.SetNaturalCoverRecognition(true);
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(ScannerVisualPoseObserver).GetField("hasObservation", flags).SetValue(observer, true);
                observer.SetNaturalCoverRecognition(false);
                Require(!(bool)typeof(ScannerVisualPoseObserver).GetField("hasObservation", flags).GetValue(observer) &&
                    !observer.TryGetObservation(out _) && observer.ObservationCount == 0,
                    "Disabling recognition must discard old observations before they can move the fixed origin.");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        private static void ValidateDisplayCoordinates()
        {
            Require(ScannerVisualPoseObserver.FrameTimestampMatches(12.03, 12000000000L) &&
                !ScannerVisualPoseObserver.FrameTimestampMatches(12.08, 12000000000L) &&
                !ScannerVisualPoseObserver.FrameTimestampMatches(double.NaN, 12000000000L) &&
                !ScannerVisualPoseObserver.FrameTimestampMatches(12, null),
                "A CPU image must match the supplied AR frame timestamp before its pose is used.");
            Vector2 viewport = new Vector2(.2f, .7f);
            Matrix4x4 rotated = Matrix4x4.identity;
            rotated.m00 = 0; rotated.m10 = -1; rotated.m20 = 1;
            rotated.m01 = 1; rotated.m11 = 0; rotated.m21 = 0;
            Vector2 sensor = ScannerDiskPoseEstimator.ViewportToImage(viewport, rotated);
            Require(Vector2.Distance(sensor, new Vector2(.3f, .2f)) < .00001f,
                "Portrait display mapping must use ARCore's row-vector convention.");
            Require(ScannerDiskPoseEstimator.ImageToViewport(sensor, rotated, out Vector2 recovered) &&
                Vector2.Distance(recovered, viewport) < .00001f,
                "The sensor-to-display inverse must preserve the same frame coordinates.");
            Matrix4x4 mirroredCrop = Matrix4x4.identity;
            mirroredCrop.m00 = -.8f; mirroredCrop.m20 = .9f;
            mirroredCrop.m11 = .7f; mirroredCrop.m21 = .15f;
            sensor = ScannerDiskPoseEstimator.ViewportToImage(viewport, mirroredCrop);
            Require(Vector2.Distance(sensor, new Vector2(.74f, .64f)) < .00001f &&
                ScannerDiskPoseEstimator.ImageToViewport(sensor, mirroredCrop, out recovered) &&
                Vector2.Distance(recovered, viewport) < .00001f,
                "A mirrored and cropped camera image must round-trip correctly.");
            Require(!ScannerDiskPoseEstimator.ImageToViewport(Vector2.one, Matrix4x4.zero, out _),
                "A singular display transform must not create a camera ray.");
        }

        private static void ValidateProjectedCover(float actualYaw)
        {
            var estimator = new ScannerDiskPoseEstimator();
            Quaternion rotation = Quaternion.Euler(0, actualYaw, 0);
            Vector3 cameraA = Center + rotation * new Vector3(0f, .4f, 1f);
            Vector3 cameraB = Center + rotation * new Vector3(.55f, .4f, .85f);
            ScannerDiskView viewA = SyntheticView(cameraA, actualYaw, 1f);
            ScannerDiskView viewB = SyntheticView(cameraB, actualYaw, 1.5f);
            Require(estimator.Add(viewA) && estimator.Add(viewB), "Synthetic independent views were rejected.");
            viewB.timeSeconds = 2f;
            Require(estimator.Add(viewB), "Third cover observation was rejected.");
            Require(estimator.TryEstimate(.5f, TagOffset, 7, out var pose),
                "Three filled-disk observations in two independent views should confirm an ideal original cover.");
            Vector3 expectedOrigin = Center - rotation * new Vector3(0f, .05f, .088f);
            Require(Mathf.Abs(Mathf.DeltaAngle(actualYaw, pose.headYawDegrees)) <= 2f &&
                Vector3.Distance(expectedOrigin, pose.originWorld) < .004f &&
                Vector3.Distance(expectedOrigin + rotation * TagOffset, pose.tagWorld) < .004f &&
                pose.supportingViews >= 2 && pose.supportingObservations == 3 &&
                pose.yawSpreadDegrees <= 8f && pose.poseEpoch == 7,
                "Known cover geometry must recover the head yaw, lever arm and observation epoch.");

            float frontCost = ScannerDiskPoseEstimator.ShapeCost(viewA, Center, actualYaw);
            float backCost = ScannerDiskPoseEstimator.ShapeCost(viewA, Center, actualYaw + 180f);
            Require(Mathf.Abs(frontCost - backCost) < .0001f,
                "Circle projection alone has a front/back ambiguity; facing direction relies on the original front-cover assumption.");
        }

        private static void ValidateInsufficientAndContradictoryViews()
        {
            var estimator = new ScannerDiskPoseEstimator();
            ScannerDiskView stationary = SyntheticView(Center + new Vector3(0, .4f, 1), 0, 1);
            for (int i = 0; i < 3; i++)
            {
                stationary.timeSeconds = 1 + .5f * i;
                Require(estimator.Add(stationary), "Repeated stationary fixture unexpectedly rejected.");
            }
            Require(!estimator.TryEstimate(.5f, TagOffset, 1, out _) && estimator.Reason == "baseline",
                "A single frontal view, however many repeated images, must not confirm the cover.");
            estimator.Clear();
            var frontalA = SyntheticView(Center + new Vector3(0, .25f, 1), 0, 1);
            var frontalB = SyntheticView(Center + new Vector3(0, .65f, 1), 0, 1.5f);
            estimator.Add(frontalA); estimator.Add(frontalB);
            frontalB.timeSeconds = 2; estimator.Add(frontalB);
            Require(!estimator.TryEstimate(.5f, TagOffset, 1, out _) && estimator.Reason == "baseline",
                "Vertical camera movement must not replace an independent sideways view of the scanner.");

            estimator.Clear();
            var front = SyntheticView(Center + new Vector3(0, .4f, 1), 0, 1);
            var back = SyntheticView(Center + new Vector3(0, .4f, -1), 0, 1.5f);
            estimator.Add(front); estimator.Add(back);
            back.timeSeconds = 2; estimator.Add(back);
            Require(!estimator.TryEstimate(.5f, TagOffset, 1, out _),
                "Contradictory front/back observations cannot represent the same one-sided orange cover.");

            estimator.Clear();
            estimator.Add(front);
            front.panDegrees = 5; front.timeSeconds = 2;
            Require(estimator.Add(front) && estimator.ObservationCount == 1,
                "Pan movement must discard cover observations from the previous head orientation.");
            front.timeSeconds = 25; estimator.Add(front);
            Require(estimator.ObservationCount == 1,
                "Expired views must not supply an old camera baseline to a new cover.");

            estimator.Clear();
            var a = SyntheticView(Center + new Vector3(0, .4f, 1), 0, 1);
            var b = SyntheticView(Center + new Vector3(.55f, .4f, .85f), 0, 1.5f);
            estimator.Add(a); estimator.Add(b);
            b.centerWorld += Vector3.right * .2f; b.timeSeconds = 2; estimator.Add(b);
            Require(!estimator.TryEstimate(.5f, TagOffset, 1, out _) && estimator.Reason == "position_spread",
                "Switching to another similarly coloured object must fail center consensus.");
        }

        private static ScannerDiskView SyntheticView(Vector3 camera, float yaw, float time)
        {
            Quaternion rotation = Quaternion.LookRotation(Center - camera, Vector3.up);
            Matrix4x4 viewMatrix = Matrix4x4.Scale(new Vector3(1, 1, -1)) *
                Matrix4x4.TRS(camera, rotation, Vector3.one).inverse;
            var view = new ScannerDiskView {
                cameraWorld = camera, centerWorld = Center,
                worldToClip = Matrix4x4.Perspective(60, 640f / 480f, .05f, 5f) * viewMatrix,
                displayMatrix = Matrix4x4.identity, width = 640, height = 480,
                timeSeconds = time, panDegrees = 0
            };
            // Sample the FILLED circle on a dense area grid. This is independent
            // of the estimator's 32-boundary-point/half-covariance approximation.
            Quaternion disk = Quaternion.Euler(0, yaw, 0);
            var pixels = new List<Vector2>();
            double sx = 0, sy = 0, sxx = 0, sxy = 0, syy = 0;
            const int radiusSteps = 40;
            for (int y = -radiusSteps; y <= radiusSteps; y++)
                for (int x = -radiusSteps; x <= radiusSteps; x++)
                {
                    float u = x / (float)radiusSteps, v = y / (float)radiusSteps;
                    if (u * u + v * v > 1) continue;
                    Vector3 point = Center + .034f * (disk * Vector3.right * u + Vector3.up * v);
                    Vector4 clip = view.worldToClip * new Vector4(point.x, point.y, point.z, 1);
                    Require(clip.w > 0, "Synthetic cover was projected behind its camera.");
                    Vector2 p = new Vector2((clip.x / clip.w * .5f + .5f) * view.width,
                        (clip.y / clip.w * .5f + .5f) * view.height);
                    pixels.Add(p); sx += p.x; sy += p.y;
                    sxx += (double)p.x * p.x; sxy += (double)p.x * p.y; syy += (double)p.y * p.y;
                }
            double n = pixels.Count, cx = sx / n, cy = sy / n;
            float xx = (float)(sxx / n - cx * cx), xy = (float)(sxy / n - cx * cy),
                yy = (float)(syy / n - cy * cy);
            view.blob = new ScannerDiskBlob { centerPixels = new Vector2((float)cx, (float)cy),
                xx = xx, xy = xy, yy = yy,
                area = Mathf.CeilToInt(4f * Mathf.PI * Mathf.Sqrt(xx * yy - xy * xy)) };
            return view;
        }

        private static void ValidateColourCandidates()
        {
            var detector = new ScannerOrangeCoverDetector();
            var blobs = new List<ScannerDiskBlob>();
            const int width = 160, height = 120;
            byte[] rgb = Canvas(width, height);
            PaintDisk(rgb, width, height, 30, 60, 10, 220, 95, 25);
            PaintDisk(rgb, width, height, 80, 60, 10, 130, 80, 35);
            PaintDisk(rgb, width, height, 130, 60, 10, 20, 50, 230);
            detector.Find(rgb, width, height, blobs);
            Require(blobs.Count == 1 && Vector2.Distance(blobs[0].centerPixels, new Vector2(30.5f, 60.5f)) < .1f,
                "The original orange candidate should survive while the chosen brown background and blue LED fixtures do not.");
            rgb = Canvas(width, height);
            PaintDisk(rgb, width, height, 4, 60, 10, 220, 95, 25);
            detector.Find(rgb, width, height, blobs);
            Require(blobs.Count == 0, "A disk cut by the image edge must not provide reliable shape moments.");
            rgb = Canvas(width, height);
            PaintDisk(rgb, width, height, 80, 60, 10, 140, 70, 30);
            detector.Find(rgb, width, height, blobs);
            Require(blobs.Count == 1,
                "An overlapping dark-brown colour remains only a candidate: RGB cannot certify scanner identity.");
            detector.Find(new byte[2], width, height, blobs);
            Require(blobs.Count == 0, "An incomplete camera buffer must not reuse previous detected blobs.");
        }

        private static byte[] Canvas(int width, int height)
        {
            byte[] data = new byte[width * height * 3];
            for (int i = 0; i < data.Length; i++) data[i] = 80;
            return data;
        }

        private static void PaintDisk(byte[] rgb, int width, int height, int cx, int cy, int radius,
            byte r, byte g, byte b)
        {
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius)
                    {
                        int i = (y * width + x) * 3;
                        rgb[i] = r; rgb[i + 1] = g; rgb[i + 2] = b;
                    }
        }

        private static void ValidateInvalidInputs()
        {
            var estimator = new ScannerDiskPoseEstimator();
            var a = SyntheticView(Center + new Vector3(0, .4f, 1), 0, 1);
            var b = SyntheticView(Center + new Vector3(.55f, .4f, .85f), 0, 1.5f);
            estimator.Add(a); estimator.Add(b); b.timeSeconds = 2; estimator.Add(b);
            Require(!estimator.TryEstimate(float.NaN, TagOffset, 1, out _) &&
                !estimator.TryEstimate(.5f, new Vector3(float.NaN, .12f, .02f), 1, out _),
                "Non-finite support height or tag extrinsics must not produce an accepted visual pose.");
            a.timeSeconds = float.NaN;
            Require(!estimator.Add(a), "A missing frame time must not contribute an independent observation.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
#endif
