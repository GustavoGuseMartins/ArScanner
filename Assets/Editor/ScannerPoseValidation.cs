#if UNITY_EDITOR
using System;
using System.Reflection;
using ArScanner.Network;
using ArScanner.Spatial;
using UnityEngine;

namespace ArScanner.EditorTools
{
    public static class ScannerPoseValidation
    {
        public static void Run()
        {
            SurfaceLodValidation.Run();
            TemporalPoseValidation.Run();
            UwbInstantPoseEstimatorValidation.Run();
            UwbMultiviewValidation.Run();
            StationaryPoseValidation.Run();
            ScannerControlValidation.Run();
            ThermalMaskedPreviewValidation.Run();
            LidarQualityValidation.Run();
            ScannerVisualPoseValidation.Run();
            Vector3 origin = new Vector3(.3f,1f,2f);
            Vector3 offset = new Vector3(.03f,.12f,.02f);
            Quaternion head = Quaternion.Euler(0f,120f,0f);
            Require(UwbAnchorManager.TryCalculateScannerYawFromTag(origin+head*offset,
                origin+head*Vector3.forward,offset,30f,out float heading) &&
                Mathf.Abs(Mathf.DeltaAngle(heading,90f)) < .001f,
                "Yaw alignment must remove pan and the tag lever arm without depending on old yaw.");
            Require(!UwbAnchorManager.AutomaticCaptureReady(true,true,false,true,true),
                "An approximate three-radio position must not enable scanning.");
            Require(!UwbAnchorManager.AutomaticCaptureReady(true,true,true,false,true),
                "A multiview position without observed support must not enable scanning.");
            Require(!UwbAnchorManager.AutomaticCaptureReady(true,true,true,true,false),
                "Capture must wait for a confirmed heading.");
            Require(UwbAnchorManager.AutomaticCaptureReady(true,true,true,true,true),
                "A validated stationary pose and heading should enable scanning.");
            var visual = new UwbVisualPositionRefiner();
            Vector3 biasedTag = new Vector3(1f, 1f, 2.38f);
            visual.Add(new Vector3(1f,.9f,2f),new Vector3(0f,1f,0f),1f);
            Require(!visual.TryRefine(biasedTag,1.1f,out _,out _,out _,out _,out _),
                "A single depth hit must not move the UWB position.");
            visual.Add(new Vector3(1.03f,.92f,2.02f),new Vector3(.3f,1f,0f),1.5f);
            visual.Add(new Vector3(.98f,.91f,2.01f),new Vector3(.45f,1f,.12f),2f);
            Require(visual.TryRefine(biasedTag,2.1f,out Vector3 refinedTag,
                out _,out float correction,out float visualBaseline,out int visualCount) &&
                refinedTag.z < biasedTag.z && refinedTag.z > biasedTag.z-.251f &&
                Mathf.Approximately(refinedTag.y,biasedTag.y) && correction > .01f &&
                visualBaseline >= .25f && visualCount >= 3,
                "Stable depth observations must bound the horizontal UWB bias.");
            Require(!visual.TryRefine(biasedTag,63f,out _,out _,out _,out _,out _),
                "An expired depth point must not move a later scan origin.");
            var d = new UwbDataReceiver.BaseDiagnostics {
                version = 2, state = 3, cycleValid = true,
                t1Ms = 100, t2Ms = 130, t3Ms = 160, timestampMs = 170
            };
            Require(UwbDataReceiver.IsCoherentRangeCycle(d), "Complete sequential cycle rejected.");
            d.t3Ms = 250;
            Require(!UwbDataReceiver.IsCoherentRangeCycle(d), "Wide acquisition span accepted.");
            d.t1Ms = uint.MaxValue - 30; d.t2Ms = uint.MaxValue - 10; d.t3Ms = 20; d.timestampMs = 25;
            Require(UwbDataReceiver.IsCoherentRangeCycle(d), "Millis rollover broke range coherence.");
            d.cycleValid = false;
            Require(!UwbDataReceiver.IsCoherentRangeCycle(d), "Incomplete cycle accepted.");
            d.version = 1; d.state = 1;
            Require(!UwbDataReceiver.IsCoherentRangeCycle(d), "Legacy incomplete cycle accepted.");
            d.state = 3;
            Require(UwbDataReceiver.IsCoherentRangeCycle(d), "Legacy complete ranges rejected.");
            d.s2 = 7;
            Require(!UwbDataReceiver.IsCoherentRangeCycle(d), "Failed exchange accepted.");

            var go = new GameObject("PoseValidation");
            try
            {
                var spatial = go.AddComponent<UwbAnchorManager>();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var history = (UwbArMultiviewEstimator)typeof(UwbAnchorManager)
                    .GetField("multiviewEstimator",flags).GetValue(spatial);
                var a = new[] { new Vector3(0,.05f,0), new Vector3(-.0875f,0,0), new Vector3(.0875f,0,0) };
                Require(history.AddSample(a,new Vector3(2f,2f,2f)), "Could not seed old history.");
                typeof(UwbAnchorManager).GetField("hasMultiviewPose",flags).SetValue(spatial,true);
                spatial.ResumeAutomaticUwb();
                Require(history.SampleCount == 0 && !(bool)typeof(UwbAnchorManager)
                    .GetField("hasMultiviewPose",flags).GetValue(spatial),
                    "Mode transition retained old position/history.");
                spatial.SetScannerStationary(false);
                spatial.isTracking = true;
                Require(!spatial.CanAcceptPoints, "Unsynchronized motion authorized point capture.");
                spatial.SetScannerStationary(true);
                Require(!spatial.CanAcceptPoints && !spatial.PreviewHeadingAligned,
                    "Relocation must require a new position and heading.");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
            Debug.Log("[ScannerPoseValidation] PASS: mirror gate, timestamps, rollover, resets, acquisition gate.");
        }

        public static void BuildValidated()
        {
            Run();
            AndroidBuildScript.BuildAndroidApk();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
#endif
