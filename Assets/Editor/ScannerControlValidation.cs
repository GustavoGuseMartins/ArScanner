#if UNITY_EDITOR
using System;
using System.Reflection;
using ArScanner.Network;
using ArScanner.Rendering;
using ArScanner.Spatial;
using UnityEngine;

namespace ArScanner.EditorTools
{
    public static class ScannerControlValidation
    {
        private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            ValidateReferenceAndCommandAcknowledgements();
            ValidateUnknownHeadingIndicator();
            ValidateThermalStatus();
            ValidateImuStatus();
            Debug.Log("[ScannerControlValidation] PASS: pan reference, delayed/stale command acknowledgements, timeout, neutral heading indicator, IMU v11/legacy parsing and invalid-sample gate.");
        }

        private static void ValidateImuStatus()
        {
            const string rawJson = "\"imuRaw\":{\"accelX\":0,\"accelY\":1,\"accelZ\":0," +
                "\"gyroX\":0.2,\"gyroY\":-0.1,\"gyroZ\":0.3," +
                "\"angleX\":90,\"angleY\":0,\"angleZ\":12}";
            var go = new GameObject("ImuStatusValidation");
            go.SetActive(false);
            try
            {
                var receiver = go.AddComponent<PointCloudTcpReceiver>();
                receiver.autoConnect = false;
                receiver.isConnected = true;
                SetField(receiver, "statusReceivedTime", Time.unscaledTime);
                receiver.status = JsonUtility.FromJson<PointCloudTcpReceiver.ScannerStatus>(
                    "{\"diagnosticVersion\":11,\"imuReady\":true,\"imuAgeMs\":20," +
                    "\"imuState\":\"ready\",\"imuIdentity\":104,\"imuInitAttempts\":3," +
                    "\"imuReadErrors\":7,\"imuBiasCalibrated\":true,\"imuTiltApplied\":false," +
                    "\"imuCalibrated\":false,\"imuSampleIntervalUs\":10000," +
                    "\"imuIntegrationGaps\":2," + rawJson + "}");
                Require(receiver.status.imuState == "ready" && receiver.status.imuIdentity == 0x68 &&
                    receiver.status.imuInitAttempts == 3 && receiver.status.imuReadErrors == 7 &&
                    receiver.status.imuBiasCalibrated && !receiver.status.imuTiltApplied &&
                    !receiver.status.imuCalibrated && receiver.status.imuSampleIntervalUs == 10000 &&
                    receiver.status.imuIntegrationGaps == 2 && receiver.HasUsableImuSnapshot,
                    "IMU v11 health and a recent complete sample must survive JSON parsing without confusing bias and applied tilt.");
                Require(Mathf.Approximately(receiver.status.imuRaw.angleX, 90f) &&
                    Mathf.Approximately(receiver.status.imuRaw.angleZ, 12f),
                    "Sensor-frame angles must remain unchanged diagnostics rather than being interpreted as AR attitude.");
                receiver.status.imuAgeMs = 101;
                Require(!receiver.HasUsableImuSnapshot && receiver.HasReadableImuSnapshot,
                    "A delayed IMU reading can be inspected with its age but must not be marked usable for timely orientation.");
                receiver.status.imuAgeMs = 174; // Observed during USB thermal acquisition.
                Require(receiver.HasReadableImuSnapshot && !receiver.HasUsableImuSnapshot,
                    "The observed thermal delay must retain sensor diagnostics without claiming fresh orientation.");
                receiver.status.imuAgeMs = 1001;
                Require(!receiver.HasReadableImuSnapshot,
                    "An IMU sample older than one second must not remain in the numeric diagnostic display.");
                receiver.status.imuAgeMs = uint.MaxValue;
                Require(!receiver.HasUsableImuSnapshot && !receiver.HasReadableImuSnapshot,
                    "The no-sample sentinel must not be interpreted as a fresh zero attitude.");
                receiver.status.imuAgeMs = 20;
                receiver.status.imuReady = false;
                receiver.status.imuState = "sample_read_failed";
                Require(!receiver.HasUsableImuSnapshot && !receiver.HasReadableImuSnapshot,
                    "A failed read with cached numeric fields must not expose an initialized IMU sample.");
                receiver.status.imuReady = true;
                receiver.status.imuRaw.accelY = float.NaN;
                Require(!receiver.HasUsableImuSnapshot && !receiver.HasReadableImuSnapshot, "Nonfinite acceleration must not reach the IMU diagnostic display.");
                receiver.status.imuRaw.accelY = 1f;
                receiver.status.imuRaw.gyroZ = float.PositiveInfinity;
                Require(!receiver.HasUsableImuSnapshot && !receiver.HasReadableImuSnapshot, "Nonfinite gyro data must not reach the IMU diagnostic display.");
                receiver.status.imuRaw.gyroZ = .3f;
                receiver.status.imuRaw.angleZ = float.NaN;
                Require(!receiver.HasUsableImuSnapshot && !receiver.HasReadableImuSnapshot, "Nonfinite integrated angles must not reach the IMU diagnostic display.");
                receiver.status.imuRaw = null;
                Require(!receiver.HasUsableImuSnapshot && !receiver.HasReadableImuSnapshot, "A missing IMU payload must not be replaced by apparent zero readings.");

                receiver.status = JsonUtility.FromJson<PointCloudTcpReceiver.ScannerStatus>(
                    "{\"diagnosticVersion\":10,\"imuReady\":true,\"imuAgeMs\":20," +
                    "\"imuCalibrated\":true," + rawJson + "}");
                Require(receiver.status.imuCalibrated && !receiver.status.imuBiasCalibrated &&
                    !receiver.status.imuTiltApplied && receiver.HasUsableImuSnapshot,
                    "Firmware v10 must retain valid diagnostic samples without assigning its legacy tilt flag to v11 bias fields.");
                SetField(receiver, "statusReceivedTime", Time.unscaledTime - 3f);
                Require(!receiver.HasUsableImuSnapshot && !receiver.HasReadableImuSnapshot, "A stale HTTP status cannot keep IMU numeric diagnostics available.");
                SetField(receiver, "statusReceivedTime", Time.unscaledTime);
                receiver.isConnected = false;
                Require(!receiver.HasUsableImuSnapshot && !receiver.HasReadableImuSnapshot, "A disconnected scanner cannot claim a usable IMU snapshot.");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        private static void ValidateThermalStatus()
        {
            var go = new GameObject("ThermalStatusValidation");
            go.SetActive(false);
            try
            {
                var receiver = go.AddComponent<PointCloudTcpReceiver>();
                receiver.autoConnect = false;
                receiver.isConnected = true;
                SetField(receiver, "statusReceivedTime", Time.unscaledTime);
                receiver.status = JsonUtility.FromJson<PointCloudTcpReceiver.ScannerStatus>(
                    "{\"diagnosticVersion\":9,\"thermalReady\":true,\"thermalFrameReady\":false," +
                    "\"thermalFrames\":1,\"thermalAgeMs\":50,\"thermalState\":\"assembling\"," +
                    "\"thermalSubpageMask\":1,\"thermalDuplicateSubpages\":3,\"thermalFrameTimeouts\":2," +
                    "\"thermalReadErrors\":4,\"thermalOverruns\":5,\"thermalInvalidFrames\":6," +
                    "\"thermalLastSubpage\":0,\"thermalRefreshHz\":8,\"thermalI2cHz\":400000," +
                    "\"thermalReadDurationMs\":12,\"thermalFrameSpanMs\":130,\"thermalRawError\":-104}");
                Require(receiver.status.thermalDuplicateSubpages == 3 &&
                    receiver.status.thermalI2cHz == 400000 && receiver.status.thermalRawError == -104 &&
                    receiver.status.thermalState == "assembling" && !receiver.HasFreshThermalFrame,
                    "Initialized v9 sensor with a partial pair must not be presented as a fresh thermal frame.");
                receiver.status.thermalFrameReady = true;
                Require(receiver.HasFreshThermalFrame, "A published fresh pair must be available.");
                var frame = new byte[776];
                Array.Copy(BitConverter.GetBytes(20f), 0, frame, 0, 4);
                Array.Copy(BitConverter.GetBytes(40f), 0, frame, 4, 4);
                receiver.cameraPreviewMode = 2;
                Invoke(receiver, "ApplyCameraPreview", 2, frame);
                Require(receiver.HasFreshThermalPreview, "Fresh sensor and HTTP image must allow the thermal preview.");
                SetField(receiver, "cameraPreviewReceivedTime", Time.unscaledTime - 2f);
                Require(!receiver.HasFreshThermalPreview && receiver.HasFreshThermalFrame,
                    "A delayed HTTP image must not appear live merely because the sensor keeps measuring.");
                receiver.status.thermalAgeMs = 1001;
                Require(!receiver.HasFreshThermalFrame, "Expired thermal frames must not remain available.");
                receiver.status.thermalAgeMs = 50;
                receiver.status.diagnosticVersion = 8;
                receiver.status.thermalFrameReady = false;
                Require(receiver.HasFreshThermalFrame, "v8 firmware must retain legacy preview compatibility.");
                receiver.status.thermalFrames = 0;
                Require(!receiver.HasFreshThermalFrame, "Initialization without any frame is not an image.");
                receiver.status.thermalFrames = 1;
                receiver.isConnected = false;
                Require(!receiver.HasFreshThermalFrame, "Disconnected scanner snapshots cannot claim a live image.");
            }
            finally
            {
                var receiver = go.GetComponent<PointCloudTcpReceiver>();
                if (receiver != null && receiver.cameraPreviewTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(receiver.cameraPreviewTexture);
                    SetProperty(receiver, "cameraPreviewTexture", null);
                }
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static void ValidateReferenceAndCommandAcknowledgements()
        {
            var go = new GameObject("ScannerControlValidation");
            go.SetActive(false);
            try
            {
                var receiver = go.AddComponent<PointCloudTcpReceiver>();
                receiver.autoConnect = false;
                receiver.isConnected = true;
                SetField(receiver, "statusReceivedTime", Time.unscaledTime);
                receiver.status = JsonUtility.FromJson<PointCloudTcpReceiver.ScannerStatus>(
                    "{\"diagnosticVersion\":8,\"stepsPerRevolution\":12800,\"panReferenceValid\":true," +
                    "\"panReferenceRestored\":true,\"panReferenceDirty\":false," +
                    "\"panReferenceState\":\"restored\",\"panSteps\":3200,\"panReferenceCalibrationVersion\":1}");
                Require(receiver.status.panSteps == 3200 && receiver.status.panReferenceRestored &&
                    receiver.status.panReferenceCalibrationVersion == 1 && receiver.HasKnownPanReference,
                    "Firmware v8 reference telemetry must survive JSON parsing.");
                receiver.status.panReferenceDirty = true;
                receiver.status.panMoving = true;
                receiver.status.panReferenceState = "moving";
                Require(receiver.HasUsablePanReference && !receiver.CanConfirmPanReference,
                    "A dirty motion checkpoint preserves the live reference, but cannot confirm a stationary zero.");
                receiver.status.panReferenceValid = false;
                Require(!receiver.HasUsablePanReference && !receiver.HasKnownPanReference,
                    "An interrupted or unreferenced v8 pan must block acquisition.");
                receiver.status.diagnosticVersion = 7;
                Require(receiver.HasUsablePanReference && !receiver.HasKnownPanReference,
                    "Legacy firmware remains compatible without claiming a physical zero.");
                receiver.status.diagnosticVersion = 8;
                receiver.status.panReferenceValid = true;
                receiver.status.panMoving = false;
                receiver.status.panReferenceDirty = false;
                Require(receiver.CanConfirmPanReference,
                    "A stationary connected v8 scanner should expose physical zero confirmation.");

                SetProperty(receiver, "ScanStartPending", true);
                SetField(receiver, "commandedStateAt", 10f);
                receiver.status.isScanning = true;
                ApplyStatus(receiver, 9f, 10.1f);
                Require(receiver.ScanStartPending && !receiver.isScanning,
                    "A pre-command HTTP request must not acknowledge a new scan.");
                SetField(receiver, "controlRevision", 1);
                ApplyStatus(receiver, 10f, 10.1f, 0);
                Require(receiver.ScanStartPending && !receiver.isScanning,
                    "An earlier HTTP request in the same Unity frame must not acknowledge the later GUI command.");
                receiver.status.isScanning = false;
                ApplyStatus(receiver, 10.1f, 10.2f);
                Require(receiver.ScanStartPending && !receiver.isScanning,
                    "A post-command standby snapshot must leave start pending.");
                receiver.status.isScanning = true;
                ApplyStatus(receiver, 10.2f, 10.3f);
                Require(!receiver.ScanStartPending && receiver.isScanning,
                    "Scanning starts in the app only after the scanner confirms it.");

                receiver.isScanning = receiver.status.isScanning = false;
                SetProperty(receiver, "PanZeroConfirmationPending", true);
                SetField(receiver, "commandedStateAt", 20f);
                receiver.status.panSteps = 0;
                receiver.status.panDegrees = 0f;
                receiver.status.panReferenceState = "confirmed";
                receiver.status.panReferenceRestored = true;
                int confirmed = 0;
                receiver.PanZeroReferenceConfirmed += () => confirmed++;
                ApplyStatus(receiver, 20.1f, 20.2f);
                Require(receiver.PanZeroConfirmationPending && confirmed == 0,
                    "A restored old zero must not acknowledge a new confirmation.");
                receiver.status.panReferenceRestored = false;
                receiver.status.panSteps = receiver.status.stepsPerRevolution;
                ApplyStatus(receiver, 20.2f, 20.3f);
                Require(receiver.PanZeroConfirmationPending && confirmed == 0,
                    "Wrapped angle zero with nonzero steps must not confirm the physical zero.");
                receiver.status.panSteps = 0;
                receiver.status.panMoving = true;
                ApplyStatus(receiver, 20.3f, 20.4f);
                Require(receiver.PanZeroConfirmationPending && confirmed == 0,
                    "A moving pan must not acknowledge physical zero confirmation.");
                receiver.status.panMoving = false;
                ApplyStatus(receiver, 20.4f, 20.5f);
                Require(!receiver.PanZeroConfirmationPending && confirmed == 1,
                    "A fresh stopped zero-step confirmation must notify pose/heading invalidation once.");
                ApplyStatus(receiver, 20.5f, 20.6f);
                Require(confirmed == 1, "Repeated status must not repeat zero confirmation callbacks.");

                SetProperty(receiver, "ScanStartPending", true);
                SetField(receiver, "controlRequestAt", 30f);
                Invoke(receiver, "UpdateControlConfirmation", 35.1f);
                Require(!receiver.ScanStartPending && !receiver.isScanning &&
                    receiver.scannerCommandStatus.Contains("não confirmou"),
                    "A start timeout must clear pending state and report an unconfirmed acquisition.");
                SetProperty(receiver, "PanZeroConfirmationPending", true);
                receiver.isConnected = false;
                Invoke(receiver, "UpdateControlConfirmation", 36f);
                Require(!receiver.PanZeroConfirmationPending,
                    "Disconnect must release a pending zero confirmation.");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        private static void ValidateUnknownHeadingIndicator()
        {
            // Keep the object inactive to exercise the indicator without renderer,
            // networking, or AR lifecycle setup. Inject only its actual dependencies.
            var go = new GameObject("ScannerAxesValidation");
            go.SetActive(false);
            try
            {
                go.transform.rotation = Quaternion.Euler(20f, 72f, 10f);
                var spatial = go.AddComponent<UwbAnchorManager>();
                var renderer = go.AddComponent<ThermalPointCloudRenderer>();
                var receiver = go.GetComponent<PointCloudTcpReceiver>();
                receiver.autoConnect = false;
                receiver.isConnected = true;
                receiver.status = new PointCloudTcpReceiver.ScannerStatus {
                    diagnosticVersion = 8, panReferenceValid = true, panDegrees = 30f
                };
                SetField(receiver, "statusReceivedTime", Time.unscaledTime);
                var axesRoot = new GameObject("Axes").transform;
                axesRoot.SetParent(go.transform, false);
                var axes = (LineRenderer[])GetField(renderer, "scannerAxes");
                for (int i = 0; i < axes.Length; i++)
                {
                    axes[i] = new GameObject("XYZ"[i].ToString()).AddComponent<LineRenderer>();
                    axes[i].transform.SetParent(axesRoot, false);
                }
                SetField(renderer, "spatial", spatial);
                SetField(renderer, "receiver", receiver);
                SetField(renderer, "axesRoot", axesRoot);
                Invoke(renderer, "UpdateScannerAxes");
                Require(!axesRoot.gameObject.activeSelf,
                    "An arbitrary scene origin must not display a scanner indicator.");
                SetProperty(spatial, "HasPoseEstimate", true);
                Invoke(renderer, "UpdateScannerAxes");
                Require(axesRoot.gameObject.activeSelf && !axes[0].enabled && axes[1].enabled &&
                    !axes[2].enabled && Quaternion.Angle(axesRoot.rotation, Quaternion.identity) < .001f,
                    "Before heading alignment, show only the true AR vertical at the known position.");
                SetProperty(spatial, "PreviewHeadingAligned", true);
                Invoke(renderer, "UpdateScannerAxes");
                Require(axes[0].enabled && axes[2].enabled &&
                    Mathf.Approximately(renderer.pointYawOffset, 200f),
                    "Known heading restores horizontal axes while preserving the 200 degree mounting extrinsic.");
                receiver.status.panReferenceValid = false;
                Invoke(renderer, "UpdateScannerAxes");
                Require(!axes[0].enabled && !axes[2].enabled,
                    "A lost physical pan reference must hide directional arrows even with a cached AR yaw.");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        private static void ApplyStatus(PointCloudTcpReceiver receiver, float requestedAt, float now,
            int? revision = null)
            => Invoke(receiver, "ApplyControlStatus", receiver.status, requestedAt, now,
                revision ?? (int)GetField(receiver, "controlRevision"));
        private static void Invoke(object target, string name, params object[] args)
            => target.GetType().GetMethod(name, InstancePrivate).Invoke(target, args);
        private static object GetField(object target, string name)
            => target.GetType().GetField(name, InstancePrivate).GetValue(target);
        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, InstancePrivate).SetValue(target, value);
        private static void SetProperty(object target, string name, object value)
            => target.GetType().GetProperty(name).SetValue(target, value);
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
#endif
