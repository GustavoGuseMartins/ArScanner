#if UNITY_EDITOR
using System;
using System.Reflection;
using ArScanner.Network;
using ArScanner.Rendering;
using ArScanner.UI;
using UnityEngine;

namespace ArScanner.EditorTools
{
    public static class LidarQualityValidation
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            var go = new GameObject("LidarQualityValidation");
            go.SetActive(false);
            ThermalPointCloudRenderer renderer = null;
            try
            {
                var receiver = go.AddComponent<PointCloudTcpReceiver>();
                receiver.autoConnect = false;
                renderer = go.AddComponent<ThermalPointCloudRenderer>();
                renderer.maxPoints = 1000;
                renderer.enableSurfaceLod = false;
                renderer.invertVerticalLidar = false;
                renderer.pointYawOffset = 0;
                Invoke(renderer, "Awake");
                var hud = go.AddComponent<ArScannerHUD>();
                hud.tcpReceiver = receiver;
                hud.pointRenderer = renderer;

                Require(!renderer.rejectWeakLidarReturns,
                    "The quality experiment must be disabled by default.");
                byte[] payload = Packet(1000, 0, 2000, 35,
                    (byte)(ScanPointData.WeakSignalFlag | 2));
                Require(PointCloudTcpReceiver.TryParsePointPacket(payload, out ScanPointData weak) &&
                    weak.HasWeakLidarSignal && weak.surfaceFlags == 10 && weak.temperatureC == 35 &&
                    weak.timestampMs == 1234 && weak.pitchCentiDeg == -125 && weak.rollCentiDeg == 250,
                    "The 28-byte decoder must preserve weak warning, thermal flag, pose and timestamp.");
                Require(!PointCloudTcpReceiver.TryParsePointPacket(new byte[27], out _) &&
                    !PointCloudTcpReceiver.TryParsePointPacket(new byte[29], out _),
                    "Truncated or oversized point packets cannot be decoded as complete returns.");
                receiver.EnqueuePoint(weak);
                Invoke(renderer, "Update");
                Require(renderer.activePointsCount == 1 && renderer.activeThermalPointsCount == 1 &&
                    renderer.observedMaxTemp == 35 && renderer.weakLidarDiscardedPoints == 0,
                    "With the experiment disabled, a weak return must retain its geometry and real temperature.");

                receiver.EnqueuePoint(weak);
                renderer.SetRejectWeakLidarReturns(true);
                Require(renderer.activePointsCount == 0 && renderer.activeThermalPointsCount == 0 &&
                    receiver.incomingPoints.IsEmpty && renderer.observedMaxTemp == -999,
                    "Changing the rule must clear the cloud, thermal extrema and queued packets together.");
                receiver.EnqueuePoint(weak);
                // A warning is independent of the thermal availability flag.
                receiver.EnqueuePoint(new ScanPointData { posZ_mm = 3000, temperatureC = float.NaN,
                    surfaceFlags = (byte)(ScanPointData.WeakSignalFlag | ScanPointData.ThermalUnavailableFlag) });
                receiver.EnqueuePoint(new ScanPointData { posX_mm = 2000, posZ_mm = 2000,
                    temperatureC = 22, surfaceFlags = 2 });
                receiver.EnqueuePoint(new ScanPointData { posX_mm = 3000, posZ_mm = 2000,
                    temperatureC = float.NaN, surfaceFlags = ScanPointData.ThermalUnavailableFlag });
                receiver.EnqueuePoint(new ScanPointData { posZ_mm = 16000,
                    temperatureC = float.NaN, surfaceFlags = ScanPointData.ThermalUnavailableFlag });
                receiver.EnqueuePoint(new ScanPointData { posX_mm = float.NaN, posZ_mm = 2000,
                    surfaceFlags = ScanPointData.ThermalUnavailableFlag });
                receiver.EnqueuePoint(new ScanPointData { posZ_mm = 4000, temperatureC = float.NaN,
                    surfaceFlags = 2 });
                Invoke(renderer, "Update");
                Require(renderer.activePointsCount == 3 && renderer.activeThermalPointsCount == 1 &&
                    renderer.weakLidarDiscardedPoints == 2 && renderer.observedMinTemp == 22 &&
                    renderer.observedMaxTemp == 22 && receiver.incomingPoints.IsEmpty,
                    "The opt-in must discard only warned returns; missing thermal coverage and a 16 m return still permit geometry while nonfinite packets stay invalid.");
                // Same-voxel rejection must happen before averaging or replacing a real thermal sample.
                receiver.EnqueuePoint(new ScanPointData { posX_mm = 2000, posZ_mm = 2000,
                    temperatureC = 90, surfaceFlags = (byte)(2 | ScanPointData.WeakSignalFlag) });
                Invoke(renderer, "Update");
                Require(renderer.activePointsCount == 3 && renderer.activeThermalPointsCount == 1 &&
                    renderer.observedMaxTemp == 22 && renderer.weakLidarDiscardedPoints == 3,
                    "A rejected hot return must not alter an existing voxel or thermal scale.");
                renderer.SetRejectWeakLidarReturns(false);
                receiver.EnqueuePoint(weak);
                Invoke(renderer, "Update");
                Require(renderer.activePointsCount == 1 && renderer.activeThermalPointsCount == 1 &&
                    renderer.weakLidarDiscardedPoints == 0,
                    "Turning the experiment off must restore weak-return capture and reset its counter.");
                renderer.ClearPointCloud();
                Require(renderer.weakLidarDiscardedPoints == 0 && receiver.incomingPoints.IsEmpty,
                    "Clear cloud must reset quality statistics and pending returns.");

                receiver.isConnected = true;
                Set(receiver, "statusReceivedTime", Time.unscaledTime);
                receiver.status = JsonUtility.FromJson<PointCloudTcpReceiver.ScannerStatus>(
                    "{\"diagnosticVersion\":12,\"lidarWeakSamples\":7,\"lidarValidSamples\":100," +
                    "\"lidarInvalidSamples\":3,\"lidarChecksumErrors\":2}");
                Require(receiver.HasLidarQualityStatus && CanChange(hud) &&
                    receiver.status.lidarWeakSamples == 7 && receiver.status.lidarValidSamples == 100 &&
                    receiver.status.lidarInvalidSamples == 3 && receiver.status.lidarChecksumErrors == 2,
                    "Fresh v12 status must expose actual sensor counters and enable the stopped-capture experiment.");
                string recorded = JsonUtility.ToJson(receiver.status);
                Require(recorded.Contains("\"lidarWeakSamples\":7") &&
                    recorded.Contains("\"lidarChecksumErrors\":2"),
                    "Scanner diagnostics must retain quality counters in serialized session logs.");
                receiver.status.isScanning = true;
                Require(!CanChange(hud), "The rule cannot change during scanner capture.");
                receiver.status.isScanning = false;
                receiver.status.panMoving = true;
                Require(!CanChange(hud), "The rule cannot change while the head is moving.");
                receiver.status.panMoving = false;
                Set(receiver, "statusReceivedTime", Time.unscaledTime - 3);
                Require(!receiver.HasLidarQualityStatus && !CanChange(hud),
                    "Stale diagnostics cannot authorize the experiment.");
                Set(receiver, "statusReceivedTime", Time.unscaledTime);
                receiver.status = JsonUtility.FromJson<PointCloudTcpReceiver.ScannerStatus>("{\"diagnosticVersion\":11}");
                Require(!receiver.HasLidarQualityStatus && !CanChange(hud) &&
                    receiver.status.lidarWeakSamples == 0 && receiver.status.lidarChecksumErrors == 0,
                    "Legacy status must parse normally without offering an unsupported quality toggle.");
            }
            finally
            {
                if (renderer != null)
                {
                    // Edit-mode validation owns the runtime resources it initialized above.
                    foreach (string field in new[] { "axesRoot", "lodMesh", "lodObject", "pointMaterial", "surfaceMaterial" })
                    {
                        FieldInfo info = renderer.GetType().GetField(field, Private);
                        var resource = info.GetValue(renderer) as UnityEngine.Object;
                        if (resource is Transform transform) UnityEngine.Object.DestroyImmediate(transform.gameObject);
                        else if (resource != null) UnityEngine.Object.DestroyImmediate(resource);
                        info.SetValue(renderer, null);
                    }
                }
                UnityEngine.Object.DestroyImmediate(go);
            }
            Debug.Log("[LidarQualityValidation] PASS: 28-byte warning preservation, opt-in point pipeline, independent thermal validity, cloud/queue reset and fresh v12/legacy UI gates.");
        }

        private static byte[] Packet(float x, float y, float z, float temperature, byte flags)
        {
            var bytes = new byte[28];
            Array.Copy(BitConverter.GetBytes(x), 0, bytes, 0, 4);
            Array.Copy(BitConverter.GetBytes(y), 0, bytes, 4, 4);
            Array.Copy(BitConverter.GetBytes(z), 0, bytes, 8, 4);
            Array.Copy(BitConverter.GetBytes(temperature), 0, bytes, 12, 4);
            bytes[19] = flags;
            Array.Copy(BitConverter.GetBytes((short)-125), 0, bytes, 20, 2);
            Array.Copy(BitConverter.GetBytes((short)250), 0, bytes, 22, 2);
            Array.Copy(BitConverter.GetBytes((uint)1234), 0, bytes, 24, 4);
            return bytes;
        }
        private static bool CanChange(ArScannerHUD hud)
            => (bool)hud.GetType().GetProperty("CanChangeLidarQualityFilter", Private).GetValue(hud);
        private static void Invoke(object instance, string method)
            => instance.GetType().GetMethod(method, Private).Invoke(instance, null);
        private static void Set(object instance, string field, object value)
            => instance.GetType().GetField(field, Private).SetValue(instance, value);
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
#endif
