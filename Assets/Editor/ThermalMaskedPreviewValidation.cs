#if UNITY_EDITOR
using System;
using System.Reflection;
using ArScanner.Network;
using ArScanner.Rendering;
using UnityEngine;

namespace ArScanner.EditorTools
{
    public static class ThermalMaskedPreviewValidation
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            var go = new GameObject("ThermalMaskedPreviewValidation");
            go.SetActive(false);
            try
            {
                var receiver = go.AddComponent<PointCloudTcpReceiver>();
                receiver.autoConnect = false;
                receiver.isConnected = true;
                SetField(receiver, "statusReceivedTime", Time.unscaledTime);
                receiver.cameraPreviewMode = 2;
                receiver.status = JsonUtility.FromJson<PointCloudTcpReceiver.ScannerStatus>(
                    "{\"diagnosticVersion\":10,\"thermalFrameReady\":true,\"thermalReady\":true," +
                    "\"thermalFrames\":1,\"thermalAgeMs\":50,\"thermalState\":\"ready_partial\"," +
                    "\"thermalPartialCalibration\":true,\"thermalMaskedPixels\":18,\"thermalCalibrationWarning\":-4}");
                Require(receiver.status.thermalPartialCalibration && receiver.status.thermalMaskedPixels == 18 &&
                    receiver.status.thermalCalibrationWarning == -4,
                    "v10 partial calibration diagnostics must survive JSON parsing.");
                string recorded = JsonUtility.ToJson(receiver.status);
                Require(recorded.Contains("\"thermalPartialCalibration\":true") &&
                    recorded.Contains("\"thermalMaskedPixels\":18") && recorded.Contains("\"thermalCalibrationWarning\":-4"),
                    "Scanner JSONL must retain the partial calibration fields through its nested status serialization.");
                Require((string)Invoke(receiver, "CameraPreviewRoute", 2) == "thermal/masked" &&
                    (string)Invoke(receiver, "CameraPreviewRoute", 1) == "rgb",
                    "v10 thermal requests must use the mask endpoint without changing RGB.");

                int[] excluded = { 0, 7, 8, 31, 32, 63, 64, 95, 96, 127, 128, 255, 256, 511, 512, 735, 736, 767 };
                byte[] payload = Payload(true, excluded);
                for (int orientation = 0; orientation < 4; ++orientation)
                {
                    receiver.status.thermalOrientationProfile = orientation;
                    Invoke(receiver, "ApplyCameraPreview", 2, payload);
                    Require(receiver.HasFreshThermalPreview && receiver.CameraThermalPreviewHasMask &&
                        receiver.CameraThermalMaskedPixels == 18 && receiver.cameraPreviewTexture.format == TextureFormat.RGBA32 &&
                        receiver.cameraPreviewTexture.filterMode == FilterMode.Point,
                        "A partial pair must be available with explicit holes and no filtering across them.");
                    Require(receiver.ThermalPreviewNotice == "Imagem parcial: 18 pixels sem calibração foram excluídos",
                        "The fresh HUD must expose the excluded pixels as a partial image.");
                    Color32[] colors = receiver.cameraPreviewTexture.GetPixels32();
                    for (int pixel = 0; pixel < 768; ++pixel)
                    {
                        int row = pixel / 32, col = pixel % 32;
                        int displayX = (orientation & 2) != 0 ? row : 23-row;
                        Color32 actual = colors[(31-col)*24+displayX];
                        bool valid = Array.IndexOf(excluded, pixel) < 0;
                        Require(actual.a == (valid ? 255 : 0),
                            "The LSB native-pixel mask must rotate and mirror exactly with the thermal image.");
                        if (valid)
                        {
                            Color32 expected = ThermalPointCloudRenderer.AbsoluteThermalPalette(20f + 30f*(payload[8+pixel]/255f));
                            Require(actual.r == expected.r && actual.g == expected.g && actual.b == expected.b,
                                "Masking must not change temperatures or colors of valid pixels.");
                        }
                    }
                    ClearTexture(receiver);
                }

                Invoke(receiver, "ApplyCameraPreview", 2, Payload(false, null));
                Require(receiver.cameraPreviewTexture == null && receiver.cameraStatus.Contains("872"),
                    "A v10 mask request must reject a legacy-sized payload.");
                byte[] truncated = new byte[871];
                Invoke(receiver, "ApplyCameraPreview", 2, truncated);
                Require(receiver.cameraPreviewTexture == null, "A truncated mask cannot publish a preview.");
                Invoke(receiver, "ApplyCameraPreview", 2, new byte[873]);
                Require(receiver.cameraPreviewTexture == null, "Extra trailing bytes cannot be silently accepted as the v10 format.");
                byte[] emptyMask = Payload(true, null);
                Array.Clear(emptyMask, 776, 96);
                Invoke(receiver, "ApplyCameraPreview", 2, emptyMask);
                Require(receiver.cameraPreviewTexture == null && receiver.CameraThermalMaskedPixels == 0,
                    "A frame with no calibrated pixels cannot publish an image.");
                byte[] invalidScale = Payload(true, excluded);
                Array.Copy(BitConverter.GetBytes(float.NaN), invalidScale, 4);
                Invoke(receiver, "ApplyCameraPreview", 2, invalidScale);
                Require(receiver.cameraPreviewTexture == null, "Nonfinite thermal scale must still be rejected.");
                Invoke(receiver, "ApplyCameraPreview", 2, Payload(true, null));
                Require(receiver.HasFreshThermalPreview && receiver.CameraThermalMaskedPixels == 0 &&
                    receiver.ThermalPreviewNotice == "",
                    "The actual frame mask must control the notice even when an older status reported excluded pixels.");
                ClearTexture(receiver);

                receiver.status.diagnosticVersion = 9;
                receiver.status.thermalPartialCalibration = false;
                receiver.status.thermalMaskedPixels = 0;
                Require((string)Invoke(receiver, "CameraPreviewRoute", 2) == "thermal",
                    "Legacy firmware must keep its original route.");
                Invoke(receiver, "ApplyCameraPreview", 2, payload);
                Require(receiver.cameraPreviewTexture == null && receiver.cameraStatus.Contains("776"),
                    "A legacy route must reject a masked-sized payload.");
                Invoke(receiver, "ApplyCameraPreview", 2, Payload(false, null));
                Require(receiver.HasFreshThermalPreview && !receiver.CameraThermalPreviewHasMask &&
                    receiver.CameraThermalMaskedPixels == 0 && receiver.ThermalPreviewNotice == "" &&
                    receiver.cameraPreviewTexture.filterMode == FilterMode.Bilinear,
                    "A healthy 776-byte legacy frame must retain its existing preview behavior.");
            }
            finally
            {
                ClearTexture(go.GetComponent<PointCloudTcpReceiver>());
                UnityEngine.Object.DestroyImmediate(go);
            }
            Debug.Log("[ThermalMaskedPreviewValidation] PASS: exact endpoint payload sizes, transparent native mask in all orientations, partial diagnostics and healthy legacy preview.");
        }

        private static byte[] Payload(bool masked, int[] excluded)
        {
            var result = new byte[masked ? 872 : 776];
            Array.Copy(BitConverter.GetBytes(20f), 0, result, 0, 4);
            Array.Copy(BitConverter.GetBytes(50f), 0, result, 4, 4);
            for (int i = 0; i < 768; ++i) result[8+i] = (byte)i;
            if (masked)
            {
                for (int i = 776; i < result.Length; ++i) result[i] = 255;
                if (excluded != null)
                    foreach (int pixel in excluded)
                    {
                        result[776+(pixel>>3)] &= (byte)~(1<<(pixel&7));
                        result[8+pixel] = 255; // An excluded hot-looking byte must remain transparent.
                    }
            }
            return result;
        }

        private static void ClearTexture(PointCloudTcpReceiver receiver)
        {
            if (receiver == null || receiver.cameraPreviewTexture == null) return;
            UnityEngine.Object.DestroyImmediate(receiver.cameraPreviewTexture);
            typeof(PointCloudTcpReceiver).GetProperty("cameraPreviewTexture").SetValue(receiver, null);
        }
        private static object Invoke(object instance, string name, params object[] args)
            => instance.GetType().GetMethod(name, Private).Invoke(instance, args);
        private static void SetField(object instance, string name, object value)
            => instance.GetType().GetField(name, Private).SetValue(instance, value);
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
#endif
