#if UNITY_EDITOR
using System;
using System.Linq;
using ArScanner.Network;
using ArScanner.Rendering;
using ArScanner.Spatial;
using ArScanner.UI;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArScanner.EditorTools
{
    // The viewer must own its controls; no dependency on the removed UDP receiver.
    public class ViewerSceneValidation : IProcessSceneWithReport
    {
        private const string ViewerPath = "Assets/Scenes/CenaViewer.unity";
        private const string RuntimeCheckKey = "ArScanner.ViewerRuntimeCheck";
        private static int runtimeFrames;
        private static double deadline;

        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (scene.path == ViewerPath) Validate(scene);
        }

        private static ArScannerHUD Validate(Scene scene)
        {
            var controllers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<ArScannerController>(true)).ToArray();
            Require(controllers.Length == 1, "CenaViewer must contain exactly one ArScannerController.");
            var controller = controllers[0];
            Require(controller.isActiveAndEnabled, "Viewer controller must be active.");
            var hud = controller.GetComponent<ArScannerHUD>();
            Require(hud != null && hud.isActiveAndEnabled && hud.showImGuiHUD, "Main HUD is missing or hidden.");
            Require(hud.tcpReceiver != null && hud.tcpReceiver == controller.GetComponent<PointCloudTcpReceiver>() &&
                hud.tcpReceiver.isActiveAndEnabled, "Start/stop and speed controls have no active TCP receiver.");
            Require(hud.pointRenderer != null && hud.pointRenderer == controller.GetComponent<ThermalPointCloudRenderer>(),
                "Clear/export and orientation controls have no point renderer.");
            Require(Mathf.Abs(hud.pointRenderer.pointSize - .0125f) < .00001f,
                "Viewer quads must start at 12.5 mm, twice the previous point diameter.");
            Require(!hud.pointRenderer.enableSurfaceLod && !hud.pointRenderer.hideBackFacingPoints,
                "Viewer must start in point-only mode with both sides visible.");
            Require(hud.anchorManager != null && hud.anchorManager == controller.GetComponent<UwbAnchorManager>(),
                "Scanner placement controls have no spatial manager.");
            Require(hud.simulator != null && hud.simulator == controller.GetComponent<PointCloudSimulator>(),
                "Simulator control has no simulator.");
            Require(hud.uwbReceiver != null && hud.uwbReceiver == controller.GetComponent<UwbDataReceiver>(),
                "UWB diagnostics have no receiver.");
            Require(hud.anchorManager.autoUwbPositioning || hud.anchorManager.localPreviewWithoutUwb,
                "Hardware viewer must enable automatic UWB positioning or local preview positioning.");
            if (!Application.isPlaying)
                Require(hud.uwbReceiver.transportMode == UwbTransportMode.USB,
                    "Hardware viewer must start with USB UWB transport.");
            return hud;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new BuildFailedException(message);
        }

        // Batch integration check: load the actual scene and exercise Awake/Start/Update.
        // Run without -quit; the checker exits after Play Mode verification.
        public static void CheckViewerInPlayMode()
        {
            Validate(EditorSceneManager.OpenScene(ViewerPath));
            SessionState.SetString(RuntimeCheckKey + ".Exception", "");
            SessionState.SetBool(RuntimeCheckKey, true);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        private static void ResumeRuntimeCheck()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Application.logMessageReceived -= CaptureRuntimeException;
            Application.logMessageReceived += CaptureRuntimeException;
        }

        private static void CaptureRuntimeException(string message, string stackTrace, LogType type)
        {
            if ((type == LogType.Exception || type == LogType.Error) && SessionState.GetBool(RuntimeCheckKey, false))
                SessionState.SetString(RuntimeCheckKey + ".Exception", message);
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(RuntimeCheckKey, false)) return;
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            runtimeFrames = 0;
            deadline = EditorApplication.timeSinceStartup + 30;
            EditorApplication.update += CheckRuntimeFrame;
        }

        private static void CheckIntegratedControls(ArScannerHUD hud)
        {
            SurfaceLodAsyncValidation.Run();
            UwbInstantPoseEstimatorValidation.Run();
            float[] calibratedScales = new float[3], calibratedOffsets = new float[3];
            Require(UwbRangeCalibrationProfile.TryFitTwoPoint(.8f,
                    new[] {1.386f,1.470f,1.351f},1.5f,
                    new[] {2.233f,2.264f,2.250f},
                    calibratedScales,calibratedOffsets,out _) &&
                calibratedScales.All(value => value > .5f && value < 1.5f),
                "Two measured positions must produce a bounded reusable UWB profile.");
            var flat = new SurfaceLodBuilder.Sample[16];
            for (int i=0;i<flat.Length;i++) flat[i] = new SurfaceLodBuilder.Sample {
                position=new Vector3((i%4)*.035f+.005f,0f,(i/4)*.035f+.005f),
                viewDirection=Vector3.up,
                color=new Color32(235,235,235,255),hasThermal=false};
            var lod = SurfaceLodBuilder.Build(flat,new Vector3(0,0,-2),.8f,2.5f,.16f,.018f,
                2.5f,out bool[] merged,out int polygons);
            Require(polygons==1 && lod.triangles.Length==6 && merged.All(value=>value),
                "Flat neighboring squares must become one LOD rectangle with two triangles.");
            var triangle = lod.triangles;
            var vertex = lod.vertices;
            Require(Vector3.Cross(vertex[triangle[1]]-vertex[triangle[0]],
                    vertex[triangle[2]]-vertex[triangle[0]]).y > 0f,
                "Surface front must follow its acquisition direction.");
            UnityEngine.Object.Destroy(lod);
            flat[15].position += Vector3.up*.1f;
            lod = SurfaceLodBuilder.Build(flat,new Vector3(0,0,-2),.8f,2.5f,.16f,.018f,
                2.5f,out merged,out polygons);
            Require(polygons==0 && !merged.Any(value=>value),
                "LOD must leave a depth discontinuity as separate points.");
            UnityEngine.Object.Destroy(lod);
            var uwbAnchors = new[] {new Vector3(0f,.035801f,0f),
                new Vector3(-.077f,0f,0f),new Vector3(.077f,0f,0f)};
            Vector3 trueUwbPosition = new Vector3(.1f,.25f,1.5f);
            var ranges = uwbAnchors.Select(a => Vector3.Distance(a,trueUwbPosition)).ToArray();
            Require(UwbMotionEstimator.TryEstimate(uwbAnchors,ranges,new Vector3(.1f,.1f,1f),
                    .08f,out Vector3 uwbEstimate,out float uwbResidual) &&
                uwbEstimate.z > 1.2f && uwbResidual < .08f,
                "Calibrated UWB motion must use three new ranges, including high-sigma solutions.");
            Require(UwbMotionEstimator.GeometrySigma(uwbAnchors,uwbEstimate,.05f) > .5f,
                "The compact UWB board must report its amplified geometric uncertainty.");
            ranges[1] = -1f;
            Require(!UwbMotionEstimator.TryEstimate(uwbAnchors,ranges,Vector3.forward,.08f,
                    out _,out _),"Missing UWB range must suspend the motion estimate.");
            var stableBiases = Enumerable.Repeat(.62f,20).ToArray();
            stableBiases[0] = 1.5f;
            Require(UwbMotionEstimator.TryRobustBias(stableBiases,out float robustBias) &&
                Mathf.Abs(robustBias-.62f) < .01f,
                "One multipath outlier must not shift initial UWB calibration.");
            for (int i=0; i<stableBiases.Length; i++) stableBiases[i] = i%2==0 ? .3f : .8f;
            Require(!UwbMotionEstimator.TryRobustBias(stableBiases,out _),
                "Unstable initial UWB ranges must require recalibration.");
            var usbPacket = new byte[28];
            Array.Copy(BitConverter.GetBytes(1.25f),0,usbPacket,0,4);
            Array.Copy(BitConverter.GetBytes(2.5f),0,usbPacket,4,4);
            Array.Copy(BitConverter.GetBytes(3.75f),0,usbPacket,8,4);
            for (int i=12; i<24; i+=4) Array.Copy(BitConverter.GetBytes(2f),0,usbPacket,i,4);
            Array.Copy(BitConverter.GetBytes((uint)1234),0,usbPacket,24,4);
            string usbLine = "@UWB28:" + BitConverter.ToString(usbPacket).Replace("-", "");
            Require(UwbDataReceiver.TryParseSerialPacket(usbLine,out var usbPosition) &&
                Mathf.Abs(usbPosition.tagY-2.5f)<.001f && usbPosition.timestampMs==1234 &&
                !UwbDataReceiver.TryParseSerialPacket(usbLine.Substring(0,usbLine.Length-2),out _),
                "USB serial framing must accept one complete base packet and reject truncation.");
            Require(UwbAnchorManager.TryCalculateScannerYaw(Vector3.zero, Vector3.forward, 90, out float yaw)
                && Mathf.Abs(Mathf.DeltaAngle(yaw, 270)) < .01f, "Heading must subtract measured pan.");
            Require(UwbAnchorManager.TryCalculateScannerYaw(Vector3.zero, Vector3.right, -90, out yaw)
                && Mathf.Abs(Mathf.DeltaAngle(yaw, 180)) < .01f, "CCW pan must align with AR heading.");
            Require(Mathf.Abs(Mathf.DeltaAngle(
                    UwbAnchorManager.RootYawForAlignedHeading(270f,200f)+200f,270f)) < .01f,
                "AR heading must account for the viewer's saved 200-degree local correction.");
            Require(!UwbAnchorManager.TryCalculateScannerYaw(Vector3.zero, Vector3.zero, 0, out yaw),
                "Coincident placement cannot define heading.");
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var renderer = hud.pointRenderer;
            var preparePreview = typeof(UwbAnchorManager).GetMethod("SetPreviewSupportPosition", flags);
            preparePreview.Invoke(hud.anchorManager, new object[] {new Vector3(0f,-.1f,0f)});
            // This integration test feeds synthetic points with no live AR session.
            // Keep its simulator tracking explicit; hardware now requires heading.
            hud.anchorManager.isTracking = true;
            renderer.ClearPointCloud();
            renderer.isPaused = false;
            hud.tcpReceiver.incomingPoints.Enqueue(new ScanPointData { posZ_mm = 2000, surfaceFlags = ScanPointData.ThermalUnavailableFlag });
            var update = typeof(ThermalPointCloudRenderer).GetMethod("Update", flags);
            update.Invoke(renderer, null);
            Require(renderer.activePointsCount == 1, "Point packet did not reach renderer.");
            Require(renderer.invertVerticalLidar, "Vertical LiDAR calibration must be enabled in the actual scene.");
            Require(Mathf.Abs(Mathf.DeltaAngle(renderer.pointYawOffset, 200f)) < .01f,
                "Observed 200-degree Y alignment must be the viewer default.");
            var initialParticles = new ParticleSystem.Particle[4];
            Require(renderer.targetParticleSystem.GetParticles(initialParticles) == 1 &&
                Vector3.Distance(initialParticles[0].position,
                    renderer.pointCloudRoot.TransformPoint(Quaternion.Euler(0f, 200f, 0f) *
                        new Vector3(0f, .1f, 2f))) < .001f,
                "Vertical inversion must reflect about the 50 mm optical center.");
            renderer.isPaused = true;
            renderer.pointOpacity = .25f;
            update.Invoke(renderer, null);
            var particles = new ParticleSystem.Particle[4];
            Require(renderer.targetParticleSystem.GetParticles(particles) == 1 && particles[0].startColor.a == 63,
                "Opacity must update existing particles while paused.");
            Vector3 beforeRebase = particles[0].position;
            Require(renderer.RebaseWorldPoints(new Pose(Vector3.zero, Quaternion.identity),
                new Pose(Vector3.right, Quaternion.identity)), "AR anchor correction was ignored.");
            update.Invoke(renderer, null);
            Require(renderer.targetParticleSystem.GetParticles(particles) == 1 &&
                Vector3.Distance(particles[0].position, beforeRebase + Vector3.right) < .001f,
                "Already collected points must follow an AR anchor correction.");
            var particleRenderer = renderer.targetParticleSystem.GetComponent<ParticleSystemRenderer>();
            var material = particleRenderer.sharedMaterial;
            Require(material.shader.name == "ArScanner/PointCloud" &&
                material.GetFloat("_ZWrite") > .5f && material.renderQueue >= 2000,
                "Depth-writing point shader must be packaged in the player.");
            Require(particleRenderer.renderMode == ParticleSystemRenderMode.Billboard &&
                particleRenderer.alignment == ParticleSystemRenderSpace.View &&
                material.GetFloat("_Cull") < .5f,
                "Point quads must face the camera and remain visible from both sides.");
            var surfaceMaterial = (Material)typeof(ThermalPointCloudRenderer)
                .GetField("surfaceMaterial", flags).GetValue(renderer);
            Require(surfaceMaterial != material && surfaceMaterial.GetFloat("_Cull") > 1.5f &&
                surfaceMaterial.GetFloat("_ZWrite") > .5f,
                "Measured LOD surfaces must retain their own depth-writing, backface-culled material.");
            var frame = new byte[776];
            Array.Copy(BitConverter.GetBytes(20f),0,frame,0,4);
            Array.Copy(BitConverter.GetBytes(40f),0,frame,4,4);
            frame[8] = 255;
            var apply = typeof(PointCloudTcpReceiver).GetMethod("ApplyCameraPreview", flags);
            apply.Invoke(hud.tcpReceiver, new object[] { 2, frame });
            var preview = hud.tcpReceiver.cameraPreviewTexture;
            Require(preview != null && preview.width == 24 && preview.height == 32 &&
                preview.GetPixel(23,31).r > .99f && hud.tcpReceiver.cameraMinTemperature == 20f,
                "Thermal HTTP payload must decode scale, dimensions and pixel order.");
            apply.Invoke(hud.tcpReceiver, new object[] { 2, new byte[20] });
            Require(hud.tcpReceiver.cameraPreviewTexture == null, "Malformed thermal data must clear stale image.");
            renderer.ClearPointCloud();
            renderer.isPaused = false;
            renderer.pointOpacity = 1f;
            hud.tcpReceiver.incomingPoints.Enqueue(new ScanPointData {posX_mm = 1000, posZ_mm = 2000,
                temperatureC = 30f, surfaceFlags = 0});
            update.Invoke(renderer, null);
            Require(renderer.targetParticleSystem.GetParticles(particles) == 1 &&
                particles[0].startColor.r != 235 && particles[0].startColor.b != 235 &&
                Mathf.Abs(particles[0].startSize - .0125f) < .00001f,
                "An isolated thermal match must retain its heat color and the 12.5 mm quad size.");
            var hot = ThermalPointCloudRenderer.AbsoluteThermalPalette(35f);
            Require(renderer.activeThermalPointsCount == 1 && hot.r == 255 && hot.g == 0 && hot.b == 0,
                "Absolute thermal colors must show 35 C in red and count fused points.");
            renderer.showThermalColors = false;
            update.Invoke(renderer, null);
            Require(renderer.targetParticleSystem.GetParticles(particles) == 1 &&
                particles[0].startColor.r == 235 && particles[0].startColor.g == 235,
                "Existing points must return to geometry color when thermal view is disabled.");
            Debug.Log("[Viewer validation] INTEGRATION PASSED: yaw, AR anchor, thermal colors and preview.");
        }

        private static void CheckRuntimeFrame()
        {
            if (runtimeFrames == 0)
            {
                var hud = UnityEngine.Object.FindFirstObjectByType<ArScannerHUD>();
                if (hud != null)
                {
                    // Exercise every expandable section in the shared right scroll during real GUI frames.
                    foreach (string field in new[] { "showDiagnostics", "showSensorDetails", "showMountingControls", "showExperimentControls" })
                        typeof(ArScannerHUD).GetField(field, System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.NonPublic).SetValue(hud, true);
                }
            }
            if (++runtimeFrames < 60 && EditorApplication.timeSinceStartup < deadline) return;
            EditorApplication.update -= CheckRuntimeFrame;
            SessionState.SetBool(RuntimeCheckKey, false);
            try
            {
                Require(string.IsNullOrEmpty(SessionState.GetString(RuntimeCheckKey + ".Exception", "")),
                    "Runtime exception: " + SessionState.GetString(RuntimeCheckKey + ".Exception", ""));
                var hud = Validate(SceneManager.GetSceneByPath(ViewerPath));
                Require(hud.pointRenderer.targetParticleSystem != null, "Point renderer did not initialize.");
                Require(hud.anchorManager.pointCloudRootContainer != null, "Scanner origin did not initialize.");
                Require(hud.simulator.isSimulationActive, "Editor smoke check must run without physical hardware.");
                CheckIntegratedControls(hud);
                Debug.Log("[Viewer validation] PLAY MODE PASSED: active HUD, scan/speed/2D receiver, placement, renderer, simulator and UWB references.");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }
    }
}
#endif
