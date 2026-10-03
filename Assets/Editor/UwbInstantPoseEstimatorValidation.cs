#if UNITY_EDITOR
using System;
using ArScanner.Spatial;
using UnityEditor;
using UnityEngine;

namespace ArScanner.EditorTools
{
    public static class UwbInstantPoseEstimatorValidation
    {
        [MenuItem("Tools/AR Scanner/Validate Instant UWB Pose")]
        public static void Run()
        {
            var estimator = new UwbInstantPoseEstimator();
            var camera = new Vector3(0f,1.18f,0f);
            var scanner = new Vector3(.32f,1.55f,2.10f); // Includes a vertical offset.
            var anchors = new[]
            {
                camera + new Vector3(0f,.0484123f,0f),
                camera + new Vector3(-.0875f,0f,0f),
                camera + new Vector3(.0875f,0f,0f)
            };
            var biases = new[] {.59f,.66f,.62f};
            var raw = new float[3];
            var scales = new[] {1f,1f,1f};
            var offsets = new[] {-biases[0],-biases[1],-biases[2]};
            for (int i = 0; i < 3; i++)
                raw[i] = Vector3.Distance(scanner,anchors[i]) + biases[i];

            Require(estimator.TryUpdate(anchors,raw,scales,offsets,camera,Vector3.forward,
                .1f,out UwbInstantPoseEstimate first),"Corrected ranges must produce a point.");
            Require(Vector3.Distance(first.scannerWorld,scanner) < .015f,
                "Instant fix must recover the true XYZ including height.");
            Require(first.mirrorSeparationMeters > 1f && first.rmsResidualMeters < .01f,
                "Mirror separation and residual must be exposed.");
            Require(first.geometrySigmaMeters > .5f && first.quality == UwbInstantQuality.LowConfidence,
                "The compact 175 x 48.4 mm board must not advertise high 3D precision.");
            Require(!UwbAnchorManager.IsReliableAutomaticPose(first),
                "A low-confidence range solution must never place the scanner in AR.");
            var ambiguousEstimate = first;
            ambiguousEstimate.quality = UwbInstantQuality.Ambiguous;
            ambiguousEstimate.geometrySigmaMeters = .1f;
            Require(!UwbAnchorManager.IsReliableAutomaticPose(ambiguousEstimate),
                "A mirror-ambiguous solution must not place the scanner in AR.");
            Require(!UwbAnchorManager.IsApproximateAutomaticPose(ambiguousEstimate,3f,.35f),
                "Approximate fallback must not bypass unresolved mirror ambiguity.");
            var preciseEstimate = first;
            preciseEstimate.quality = UwbInstantQuality.Experimental;
            preciseEstimate.geometrySigmaMeters = .4f;
            Require(UwbAnchorManager.IsReliableAutomaticPose(preciseEstimate),
                "An experimental estimate below the geometry limit must remain usable.");

            estimator.Clear();
            Require(!estimator.HasPosition,"Clear must drop the temporal prior.");
            var grosslyInconsistent = (float[])raw.Clone();
            grosslyInconsistent[0] += .6f;
            Require(!estimator.TryUpdate(anchors,grosslyInconsistent,scales,offsets,camera,
                    Vector3.forward,.1f,out UwbInstantPoseEstimate rejected) &&
                rejected.quality == UwbInstantQuality.IncompatibleRanges,
                "Large pairwise inconsistency must not be converted into a pose.");

            // The three radios remain coplanar. Gaze parallel to their plane gives
            // no independent evidence for choosing in front/behind the PCB plane.
            estimator.Clear();
            Require(estimator.TryUpdate(anchors,raw,scales,offsets,camera,Vector3.right,
                .1f,out UwbInstantPoseEstimate mirrored) &&
                mirrored.quality == UwbInstantQuality.Ambiguous,
                "A symmetric mirror solution must be reported as ambiguous.");

            estimator.Clear();
            Require(estimator.TryUpdate(anchors,raw,scales,offsets,camera,Vector3.forward,
                .1f,out _),"Initial pose must be available for temporal test.");
            Vector3 moved = scanner + new Vector3(.2f,0f,0f);
            for (int i = 0; i < 3; i++)
                raw[i] = Vector3.Distance(moved,anchors[i]) + biases[i];
            Require(estimator.TryUpdate(anchors,raw,scales,offsets,camera,Vector3.forward,
                    .1f,out UwbInstantPoseEstimate filtered) && filtered.usedTemporalPrior &&
                filtered.scannerWorld.x > scanner.x && filtered.scannerWorld.x < moved.x,
                "Temporal prior must smooth a moving scanner without freezing it.");

            var tagOffset = new Vector3(0f,.12f,.02f);
            Require(Vector3.Distance(UwbAnchorManager.PanOriginFromRotatingTag(
                    new Vector3(0f,1.12f,.02f),0f,0f,tagOffset),
                new Vector3(0f,1f,0f)) < .0001f,
                "The UWB tag height must not become the displayed pan-axis height.");
            Require(Vector3.Distance(UwbAnchorManager.PanOriginFromRotatingTag(
                    new Vector3(.02f,1.12f,0f),0f,90f,tagOffset),
                new Vector3(0f,1f,0f)) < .0001f,
                "The tag's 20 mm forward offset must rotate with the pan.");

            Debug.Log("[UWB instant validation] PASSED: affine correction, height, mirror, rejection, temporal prior, tag offset.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception("[UWB instant validation] " + message);
        }
    }
}
#endif
