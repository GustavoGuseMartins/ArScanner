using System;
using System.Collections.Generic;
using ArScanner.Network;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ArScanner.Spatial
{
    [DefaultExecutionOrder(-90)]
    public sealed class ScannerVisualPoseObserver : MonoBehaviour
    {
        public bool enableNaturalCoverRecognition = false;
        public string Status { get; private set; } = "Reconhecimento da tampa: experimental, desligado.";
        public int DetectedCoverCount { get; private set; }
        public int ObservationCount => estimator.ObservationCount;
        private UwbAnchorManager spatial;
        private PointCloudTcpReceiver scanner;
        private ARCameraManager cameraManager;
        private Camera arCamera;
        private readonly ScannerOrangeCoverDetector detector = new ScannerOrangeCoverDetector();
        private readonly ScannerDiskPoseEstimator estimator = new ScannerDiskPoseEstimator();
        private readonly List<ScannerDiskBlob> blobs = new List<ScannerDiskBlob>();
        private readonly UwbPhonePauseGate phonePauseGate = new UwbPhonePauseGate();
        private ScannerVisualPoseObservation latest;
        private bool hasObservation, conversionPending;
        private float nextFrameTime;
        private long epoch = -1;
        private byte[] rgb;
        private int generation;
        private XRCpuImage.AsyncConversion conversion;
        private ScannerDiskView pendingView;
        private Quaternion pendingRotation;
        private float pendingAxisY;
        private long pendingEpoch;
        private int pendingGeneration;
        private bool recognitionWasEnabled;

        private void Update()
        {
            if (recognitionWasEnabled != enableNaturalCoverRecognition)
                SetNaturalCoverRecognition(enableNaturalCoverRecognition);
            if (spatial == null) spatial = GetComponent<UwbAnchorManager>();
            if (scanner == null) scanner = GetComponent<PointCloudTcpReceiver>();
            if (spatial != null && epoch != spatial.AutomaticPoseEpoch)
            { epoch = spatial.AutomaticPoseEpoch; ResetObservations(); }
            PollConversion();
            if (!enableNaturalCoverRecognition) return;
            if (cameraManager != null) return;
            arCamera = spatial != null && spatial.arCameraTransform != null
                ? spatial.arCameraTransform.GetComponent<Camera>() : Camera.main;
            if (arCamera == null) return;
            cameraManager = arCamera.GetComponent<ARCameraManager>();
            if (cameraManager != null) cameraManager.frameReceived += OnCameraFrame;
        }

        public void ResetObservations()
        { estimator.Clear(); phonePauseGate.Clear(); hasObservation = false; generation++; }

        public void SetNaturalCoverRecognition(bool enabled)
        {
            enableNaturalCoverRecognition = recognitionWasEnabled = enabled;
            if (conversionPending) { conversion.Dispose(); conversionPending = false; }
            ResetObservations();
            DetectedCoverCount = 0;
            Status = enabled
                ? "Experimental: observe a face laranja do LiDAR, parado, a 0,3–3 m; depois desloque o celular pelo menos 25 cm e pause."
                : "Reconhecimento da tampa: experimental, desligado.";
        }

        public bool TryGetObservation(out ScannerVisualPoseObservation observation)
        {
            observation = latest;
            return enableNaturalCoverRecognition && hasObservation && spatial != null && latest.poseEpoch == spatial.AutomaticPoseEpoch &&
                Time.unscaledTime - latest.timeSeconds <= 2f && scanner != null &&
                scanner.HasUsablePanReference && !scanner.ScannerControlBusy && !scanner.ScanStartPending &&
                !scanner.isScanning && !scanner.status.isScanning && !scanner.status.panMoving &&
                !scanner.status.panParking &&
                Mathf.Abs(Mathf.DeltaAngle(latest.observedPanDegrees, scanner.status.panDegrees)) <= .5f;
        }

        private void OnCameraFrame(ARCameraFrameEventArgs args)
        {
            if (!enableNaturalCoverRecognition || conversionPending || Time.unscaledTime < nextFrameTime ||
                spatial == null || scanner == null || cameraManager == null || arCamera == null) return;
            nextFrameTime = Time.unscaledTime + .3f;
            if (ARSession.state != ARSessionState.SessionTracking || !spatial.scannerStationary)
            { ResetObservations(); Status = "Visão: aguardando AR e scanner parado."; return; }
            if (!spatial.HasAutomaticSupportHeight || !scanner.HasUsablePanReference)
            { Status = "Visão: confirme o zero físico do pan e observe o apoio do scanner."; return; }
            if (scanner.ScannerControlBusy || scanner.ScanStartPending || scanner.isScanning ||
                scanner.status.isScanning || scanner.status.panParking || scanner.status.panMoving)
            { Status = "Visão: origem preservada durante o giro."; return; }
            if (!phonePauseGate.Update(arCamera.transform.position, arCamera.transform.rotation, Time.unscaledTime))
            { Status = "Visão: pause o celular para observar a tampa do LiDAR."; return; }
            if (!args.displayMatrix.HasValue || !args.projectionMatrix.HasValue ||
                !cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
            { Status = "Visão: imagem da câmera ainda indisponível."; return; }
            if (!FrameTimestampMatches(image.timestamp, args.timestampNs))
            { image.Dispose(); Status = "Visão: aguardando imagem e pose do mesmo quadro."; return; }
            int width = Math.Min(640, image.width);
            int height = Mathf.RoundToInt(image.height * width / (float)image.width);
            var view = new ScannerDiskView {
                cameraWorld = arCamera.transform.position,
                worldToClip = args.projectionMatrix.Value * arCamera.worldToCameraMatrix,
                displayMatrix = args.displayMatrix.Value,
                width = width, height = height, timeSeconds = Time.unscaledTime,
                panDegrees = scanner.status.panDegrees
            };
            float axisY = spatial.AutomaticSupportHeight + spatial.previewOriginHeight;
            long frameEpoch = spatial.AutomaticPoseEpoch;
            try
            {
                conversion = image.ConvertAsync(new XRCpuImage.ConversionParams {
                    inputRect = new RectInt(0, 0, image.width, image.height),
                    outputDimensions = new Vector2Int(width, height), outputFormat = TextureFormat.RGB24,
                    transformation = XRCpuImage.Transformation.None
                });
                pendingView = view; pendingAxisY = axisY; pendingEpoch = frameEpoch;
                pendingGeneration = generation; pendingRotation = arCamera.transform.rotation;
                conversionPending = true;
            }
            catch (InvalidOperationException) { conversionPending = false; Status = "Visão: conversão da câmera indisponível."; }
            finally { image.Dispose(); }
        }

        private void PollConversion()
        {
            if (!conversionPending || !conversion.status.IsDone()) return;
            try
            {
                // Poll on Unity's main thread. Native image callbacks may run
                // on a worker and must never inspect GameObjects or AR state.
                if (!enableNaturalCoverRecognition || conversion.status != XRCpuImage.AsyncConversionStatus.Ready ||
                    pendingGeneration != generation || spatial == null || pendingEpoch != spatial.AutomaticPoseEpoch ||
                    Time.unscaledTime - pendingView.timeSeconds > .3f ||
                    ARSession.state != ARSessionState.SessionTracking || !spatial.scannerStationary ||
                    !spatial.HasAutomaticSupportHeight || scanner == null || !scanner.HasUsablePanReference ||
                    scanner.ScannerControlBusy || scanner.ScanStartPending || scanner.isScanning ||
                    scanner.status.isScanning || scanner.status.panMoving || scanner.status.panParking ||
                    Mathf.Abs(Mathf.DeltaAngle(pendingView.panDegrees, scanner.status.panDegrees)) > .5f ||
                    Mathf.Abs(pendingAxisY - spatial.AutomaticSupportHeight - spatial.previewOriginHeight) > .005f ||
                    arCamera == null || Vector3.Distance(pendingView.cameraWorld, arCamera.transform.position) > .01f ||
                    Quaternion.Angle(pendingRotation, arCamera.transform.rotation) > 1f) return;
                var data = conversion.GetData<byte>();
                if (rgb == null || rgb.Length != data.Length) rgb = new byte[data.Length];
                data.CopyTo(rgb);
                Analyze(pendingView, pendingAxisY, pendingEpoch);
            }
            finally { conversion.Dispose(); conversionPending = false; }
        }

        private void Analyze(ScannerDiskView view, float axisY, long frameEpoch)
        {
            detector.Find(rgb, view.width, view.height, blobs);
            DetectedCoverCount = blobs.Count;
            float best = float.PositiveInfinity;
            ScannerDiskView selected = default;
            bool found = false;
            foreach (var blob in blobs)
            {
                Vector2 raw = new Vector2(blob.centerPixels.x / view.width, blob.centerPixels.y / view.height);
                if (!ScannerDiskPoseEstimator.ImageToViewport(raw, view.displayMatrix, out Vector2 uv)) continue;
                // Use the camera/projection from the SAME CPU frame, never a
                // later phone position after asynchronous image conversion.
                Matrix4x4 inverse = view.worldToClip.inverse;
                Vector4 far = inverse * new Vector4(uv.x * 2 - 1, uv.y * 2 - 1, .5f, 1);
                if (Mathf.Abs(far.w) < .00001f) continue;
                Vector3 ray = (new Vector3(far.x, far.y, far.z) / far.w - view.cameraWorld).normalized;
                if (Mathf.Abs(ray.y) < .12f) continue;
                float distance = (axisY + ScannerDiskPoseEstimator.CoverAboveAxis - view.cameraWorld.y) / ray.y;
                if (distance < .3f || distance > 3f) continue;
                Vector3 center = view.cameraWorld + ray * distance;
                if (spatial.HasPoseEstimate &&
                    Vector2.Distance(new Vector2(center.x, center.z),
                        new Vector2(spatial.currentSmoothedTagPosition.x, spatial.currentSmoothedTagPosition.z)) > .8f) continue;
                ScannerDiskView candidate = view; candidate.blob = blob; candidate.centerWorld = center;
                float bestShape = float.PositiveInfinity;
                for (int yaw = 0; yaw < 360; yaw += 5)
                {
                    Vector3 forward = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                    if (Vector3.Dot(forward, (view.cameraWorld - center).normalized) < .15f) continue;
                    bestShape = Mathf.Min(bestShape, ScannerDiskPoseEstimator.ShapeCost(candidate, center, yaw));
                }
                if (bestShape < .35f && bestShape < best)
                { selected = candidate; best = bestShape; found = true; }
            }
            if (!found) { Status = "Visão: aproxime e enquadre a face laranja do LiDAR, sem cobri-la."; return; }
            if (!estimator.Add(selected)) return;
            hasObservation = false;
            if (estimator.TryEstimate(axisY, spatial.EffectiveScannerTagOffset, frameEpoch, out latest))
            {
                hasObservation = true;
                Status = $"Tampa do LiDAR reconhecida em {latest.supportingViews} vistas; direção visual confirmada.";
            }
            else Status = "Visão: tampa candidata; pause em outra vista para confirmar posição e direção (" + estimator.Reason + ").";
        }

        public static bool FrameTimestampMatches(double cpuSeconds, long? frameNanoseconds)
        {
            return frameNanoseconds.HasValue && !double.IsNaN(cpuSeconds) && !double.IsInfinity(cpuSeconds) &&
                Math.Abs(cpuSeconds - frameNanoseconds.Value / 1e9) <= .05;
        }

        private void OnDisable()
        {
            if (cameraManager != null) cameraManager.frameReceived -= OnCameraFrame;
            if (conversionPending) { conversion.Dispose(); conversionPending = false; }
            cameraManager = null; ResetObservations();
        }
    }
}
