#if UNITY_EDITOR
using System;
using System.Reflection;
using ArScanner.Network;
using ArScanner.Spatial;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ArScanner.EditorTools
{
    public static class StationaryPoseValidation
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            var confirmation = new StationaryPoseConfirmation();
            Vector3 target = new Vector3(.4f, .22f, 1.4f);
            Require(!confirmation.TryAdd(1, target, Vector3.zero, 1f, out _),
                "One accurate-looking frame must remain provisional.");
            Require(!confirmation.TryAdd(1, target, new Vector3(.3f, 0f, 0f), 1.6f, out _) && confirmation.Count == 1,
                "Repeating a packet must not confirm a stationary pose.");
            Require(!confirmation.TryAdd(2, target, Vector3.zero, 1.7f, out _),
                "Two observations must remain provisional.");
            Require(!confirmation.TryAdd(3, target, Vector3.zero, 2f, out _),
                "One viewpoint must not authorize a multiview confirmation.");
            Require(confirmation.TryAdd(4, target + new Vector3(.025f, 0f, .01f),
                new Vector3(.3f, 0f, 0f), 2.4f, out Vector3 confirmed) &&
                Vector3.Distance(confirmed, target) < .03f,
                "Repeated compatible estimates from distinct viewpoints should confirm.");
            Require(!confirmation.TryAdd(5, target + Vector3.right, new Vector3(.5f, 0f, 0f),
                2.8f, out _) && confirmation.Count == 1,
                "A sudden accepted UWB jump must start another confirmation instead of being averaged in.");
            Require(!StationaryPoseConfirmation.MatchesSupportHeight(.5f, 0f, .1f, .12f),
                "A free 3D solve at the wrong support height must not authorize capture.");
            var validated = new StationaryPoseConfirmation();
            Vector3[] anchors = { new Vector3(0f, 1.4f, 0f), new Vector3(-.0875f, 1.4f, 0f),
                new Vector3(.0875f, 1.4f, 0f) };
            Vector3 ranges = Ranges(anchors, target);
            Require(!validated.TryAddValidatedMultiviewCycle(21, target, Vector3.zero, 1f, anchors, ranges, out _) &&
                !validated.TryAddValidatedMultiviewCycle(21, target, Vector3.zero, 1.2f, anchors, ranges, out _) && validated.Count == 1,
                "A cached candidate cannot be confirmed by recounting the same radio cycle.");
            Require(!validated.TryAddValidatedMultiviewCycle(22, target, Vector3.zero, 1.3f, anchors,
                ranges + new Vector3(.4f, 0f, 0f), out _) && validated.Count == 1,
                "A new cycle that disagrees with the candidate must not count as confirmation.");
            Require(!validated.TryAddValidatedMultiviewCycle(23, target, Vector3.zero, 1.4f, anchors, ranges, out _) &&
                validated.TryAddValidatedMultiviewCycle(24, target, Vector3.zero, 1.8f, anchors, ranges, out _) && validated.ViewCount == 1,
                "After geometry is already observable, fresh compatible cycles must finish confirmation at a full pause.");
            Require(UwbAnchorManager.IsDepthCompatibleWithPreviewSupport(new Pose(Vector3.zero, Quaternion.identity), 0f) &&
                !UwbAnchorManager.IsDepthCompatibleWithPreviewSupport(new Pose(Vector3.up * .15f, Quaternion.identity), 0f) &&
                !UwbAnchorManager.IsDepthCompatibleWithPreviewSupport(new Pose(Vector3.zero, Quaternion.Euler(90f, 0f, 0f)), 0f),
                "A body, top or vertical depth hit must not become the manual scanner support.");

            var go = new GameObject("StationaryPoseValidation");
            try
            {
                var spatial = go.AddComponent<UwbAnchorManager>();
                Transform root = spatial.pointCloudRootContainer;
                if (root == null) root = new GameObject("StationaryPoseValidationRoot").transform;
                root.SetParent(go.transform);
                spatial.pointCloudRootContainer = root;
                Set(spatial, "ownsRoot", false);
                spatial.InitializeReferences();
                var receiver = go.GetComponent<UwbDataReceiver>();
                Set(spatial, "uwbReceiver", receiver);
                Set(spatial, "hasSupportHeight", true);
                Set(spatial, "supportHeightY", 0f);
                Require(!Consider(spatial, target + Vector3.up * .3f, false, Vector3.zero, 10),
                    "A free 3D candidate must remain separate from the committed stationary pose.");
                Require(!spatial.HasValidatedMultiviewPose,
                    "A candidate must not open the acquisition gate.");
                SeedConfirmation(spatial, target, 11);
                Require(Consider(spatial, target, true, new Vector3(.3f, 0f, 0f), 13) &&
                    spatial.HasValidatedMultiviewPose,
                    "A support-coherent stationary consensus must commit a pose.");
                Require(spatial.HasHeightConsistentPose,
                    "A confirmed height-constrained pose should agree with the physical support height.");
                Vector3 original = spatial.localPreviewPosition;
                spatial.localPreviewPosition += Vector3.up * .08f;
                Require(!spatial.HasHeightConsistentPose,
                    "Historical acceptance must not hide a later origin height inconsistency.");
                spatial.localPreviewPosition = original;
                spatial.droneYawDeg = 0f;
                typeof(UwbAnchorManager).GetProperty("PreviewHeadingAligned").SetValue(spatial, true);
                Require(!Consider(spatial, target + Vector3.right * .4f, true,
                    new Vector3(.5f, 0f, 0f), 14) && spatial.PoseRevalidationRequired &&
                    !spatial.PreviewHeadingAligned && Vector3.Distance(original, spatial.localPreviewPosition) < .001f,
                    "A new UWB jump must block capture and preserve the old origin until revalidated.");
                SeedConfirmation(spatial, target + Vector3.right * .4f, 15);
                Require(Consider(spatial, target + Vector3.right * .4f, true,
                    new Vector3(.3f, 0f, 0f), 17) && !spatial.PoseRevalidationRequired &&
                    !spatial.PreviewHeadingAligned,
                    "A confirmed relocation must still require a new heading.");

                Set(spatial, "hasConfirmedVisualCorrection", true);
                Set(spatial, "confirmedVisualCorrection", new Vector3(-.18f, 0f, 0f));
                Invoke(spatial, "CommitStationaryTag", spatial.RawUwbTagPosition + new Vector3(-.18f, 0f, 0f),
                    "depth_refinement_confirmed");
                original = spatial.localPreviewPosition;
                var visual = (UwbVisualPositionRefiner)Get(spatial, "visualPositionRefiner");
                float expired = Time.unscaledTime - 100f;
                visual.Add(target, Vector3.zero, expired);
                visual.Add(target, new Vector3(.3f, 0f, 0f), expired + .5f);
                visual.Add(target, new Vector3(.5f, 0f, 0f), expired + 1f);
                Set(spatial, "visualObservationRevision", 3L);
                Invoke(spatial, "UpdateIndependentVisualRefinement");
                Require((bool)Get(spatial, "hasConfirmedVisualCorrection") &&
                    Vector3.Distance(original, spatial.localPreviewPosition) < .001f,
                    "Expiration of depth evidence must not return a stationary origin to biased UWB.");

                Vector3 candidate = spatial.RawUwbTagPosition;
                Invoke(spatial, "CacheObservableStationaryCandidate", candidate, 60L);
                var votes = (StationaryPoseConfirmation)Get(spatial, "poseConfirmation");
                votes.Clear();
                Vector3 candidateRanges = Ranges(anchors, candidate);
                Require(!(bool)Invoke(spatial, "ConsiderValidatedRangeCandidate", candidate, Vector3.zero,
                    60L, anchors, candidateRanges) && votes.Count == 0,
                    "The cycle used to solve the candidate must not also count as a fresh validation.");
                float validationTime = Time.unscaledTime;
                votes.TryAddValidatedMultiviewCycle(61, candidate, Vector3.zero, validationTime - .7f, anchors, candidateRanges, out _);
                Require(!(bool)Invoke(spatial, "ConsiderValidatedRangeCandidate", candidate, Vector3.zero,
                    62L, anchors, candidateRanges + new Vector3(.4f, 0f, 0f)) && votes.Count == 1,
                    "Full-position verification must use the actual new corrected ranges.");
                votes.TryAddValidatedMultiviewCycle(63, candidate, Vector3.zero, validationTime - .3f, anchors, candidateRanges, out _);
                Require((bool)Invoke(spatial, "ConsiderValidatedRangeCandidate", candidate, Vector3.zero,
                    64L, anchors, candidateRanges) && spatial.HasHeightConsistentPose,
                    "A valid multiview candidate must commit without demanding another camera view after position_full.");
                Require(!spatial.StationaryPosePinned,
                    "Automatic stability confirmation must still leave explicit user fixation available.");

                typeof(UwbDataReceiver).GetProperty("AppliedDiagnosticsId").SetValue(receiver, 42L);
                Invoke(spatial, "BeginRangeQuery");
                Invoke(spatial, "SetRangeDecision", "pending_future_pose");
                long decision = spatial.RangeDecisionRevision;
                Invoke(spatial, "BeginRangeQuery");
                Invoke(spatial, "SetRangeDecision", "pending_future_pose");
                Require(spatial.RangeRetryCount == 1 && spatial.RangeDecisionRevision > decision,
                    "Waiting for the next AR frame must expose a retry for the same cycle.");
                Invoke(spatial, "MarkRangeProcessed");
                Require(spatial.RangeProcessedCycleId == 42 && spatial.RangeDecision == "processed",
                    "A retry success without a new radio packet must be observable in diagnostics.");
                Invoke(spatial, "SetFrozenPose", "frozen_cloud", "captured points");
                decision = spatial.RangeDecisionRevision;
                Invoke(spatial, "SetFrozenPose", "frozen_cloud", "captured points");
                Require(spatial.RangeDecisionRevision == decision,
                    "An unchanged freeze must not generate a diagnostic event on every frame.");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
            ValidateStationarySupportAndPinnedPose();
            Debug.Log("[StationaryPoseValidation] PASS: candidate consensus, retained support, pinned update, jump revalidation, visual retention and per-cycle retry diagnostics.");
        }

        private static void ValidateStationarySupportAndPinnedPose()
        {
            FieldInfo arState = typeof(ARSession).GetField("s_State", BindingFlags.Static | BindingFlags.NonPublic);
            object previousState = arState.GetValue(null);
            var go = new GameObject("StationarySupportAndPinValidation");
            go.SetActive(false);
            try
            {
                // Use the same runtime entry points while replacing only the
                // unavailable device tracking and network inputs in the editor.
                arState.SetValue(null, ARSessionState.SessionTracking);
                var spatial = go.AddComponent<UwbAnchorManager>();
                spatial.InitializeReferences();
                spatial.pointCloudRootContainer.SetParent(go.transform);
                Set(spatial, "ownsRoot", false);
                var camera = new GameObject("ValidationCamera").transform;
                camera.SetParent(go.transform);
                camera.position = new Vector3(0f, 1.4f, 0f);
                spatial.arCameraTransform = camera;
                Set(spatial, "raycasts", go.AddComponent<ARRaycastManager>());
                spatial.scannerStationary = true;
                spatial.autoUwbPositioning = true;
                spatial.localPreviewWithoutUwb = false;
                typeof(UwbAnchorManager).GetProperty("HasSavedUwbCalibration").SetValue(spatial, true);
                spatial.autoAlignHeading = spatial.autoSupportHeadingFallback = false;
                Set(spatial, "hasSupportHeight", true);
                Set(spatial, "supportHeightY", .35f);
                Set(spatial, "supportLastSeen", Time.unscaledTime - 100f);
                Set(spatial, "nextSupportObservationTime", Time.unscaledTime + 10f);
                long epoch = spatial.AutomaticPoseEpoch;
                Require(!spatial.HasValidatedMultiviewPose &&
                    (bool)Invoke(spatial, "UpdateAutomaticSupportHeight") &&
                    spatial.HasAutomaticSupportHeight && spatial.AutomaticSupportHeight == .35f &&
                    spatial.AutomaticPoseEpoch == epoch,
                    "Looking away before the first multiview solve must not expire an already observed stationary support.");

                var scanner = go.AddComponent<PointCloudTcpReceiver>();
                scanner.autoConnect = false;
                scanner.isConnected = true;
                scanner.status = new PointCloudTcpReceiver.ScannerStatus {
                    diagnosticVersion = 8, panReferenceValid = true, panDegrees = 0f };
                Set(scanner, "statusReceivedTime", Time.unscaledTime);
                Set(spatial, "scannerReceiver", scanner);
                spatial.droneYawDeg = 37f;
                Invoke(spatial, "CommitStationaryTag", new Vector3(.4f, .57f, 1.4f), "validation_multiview");
                Require(spatial.TryConfirmPoseCandidate(out _),
                    "An operator must be able to fix a support-coherent multiview pose with a fresh known pan reference.");
                typeof(UwbAnchorManager).GetProperty("PreviewHeadingAligned").SetValue(spatial, true);
                Vector3 origin = spatial.localPreviewPosition;
                Vector3 tag = spatial.currentSmoothedTagPosition;
                Vector3 root = spatial.pointCloudRootContainer.position;
                float yaw = spatial.droneYawDeg;
                long revision = spatial.PoseRevision;
                epoch = spatial.AutomaticPoseEpoch;
                var receiver = go.GetComponent<UwbDataReceiver>();
                typeof(UwbDataReceiver).GetProperty("Diagnostics").SetValue(receiver,
                    new UwbDataReceiver.BaseDiagnostics { version = 1, state = 2, radioMask = 7,
                        d1 = 3f, d2 = 3f, d3 = 3f });
                typeof(UwbDataReceiver).GetProperty("HasFreshDiagnostics").SetValue(receiver, true);
                Set(receiver, "lastDiagnosticsTicks", System.Diagnostics.Stopwatch.GetTimestamp());
                typeof(UwbDataReceiver).GetProperty("AppliedDiagnosticsId").SetValue(receiver, 100L);
                Require(receiver.HasFreshThreeRanges, "The pinned update regression needs an actual fresh radio input.");
                camera.position += new Vector3(.5f, 0f, .2f);
                scanner.status.panDegrees = 50f;
                Invoke(spatial, "Update");
                Require(spatial.StationaryPosePinned && spatial.HasHeightConsistentPose &&
                    Vector3.Distance(spatial.localPreviewPosition, origin) < .001f &&
                    Vector3.Distance(spatial.currentSmoothedTagPosition, tag) < .001f &&
                    Vector3.Distance(spatial.pointCloudRootContainer.position, root) < .001f &&
                    spatial.droneYawDeg == yaw && spatial.PreviewHeadingAligned &&
                    spatial.AutomaticPoseEpoch == epoch && spatial.PoseRevision == revision &&
                    spatial.RangeDecision == "frozen_operator",
                    "A fresh divergent range cycle and camera motion must not move or invalidate a user-fixed stationary origin during the normal Update path.");

                Set(scanner, "statusReceivedTime", Time.unscaledTime - 3f);
                Invoke(spatial, "Update");
                Require(spatial.StationaryPosePinned && spatial.HasHeightConsistentPose &&
                    spatial.HasAutomaticSupportHeight && spatial.PreviewHeadingAligned &&
                    Vector3.Distance(spatial.localPreviewPosition, origin) < .001f &&
                    Vector3.Distance(spatial.pointCloudRootContainer.position, root) < .001f &&
                    spatial.PoseRevision == revision && spatial.AutomaticPoseEpoch == epoch &&
                    spatial.RangeDecision == "pan_status_waiting_preserved" &&
                    !spatial.CanAcceptPoints && !spatial.CanConfirmPoseCandidate &&
                    spatial.PoseLocalizationStatus.Contains("status recente"),
                    "A stale HTTP status must preserve a fixed stationary calibration while blocking capture and fixation.");

                scanner.status.panDegrees = 70f;
                Set(scanner, "statusReceivedTime", Time.unscaledTime);
                Invoke(spatial, "Update");
                Require(spatial.StationaryPosePinned && spatial.HasHeightConsistentPose &&
                    spatial.PreviewHeadingAligned && spatial.CanAcceptPoints &&
                    Vector3.Distance(spatial.localPreviewPosition, origin) < .001f &&
                    spatial.PoseRevision == revision && spatial.AutomaticPoseEpoch == epoch,
                    "A fresh status must restore capture without moving the fixed pan axis when the head moved during the gap.");

                scanner.status.panReferenceValid = false;
                Invoke(spatial, "Update");
                Require(spatial.StationaryPosePinned && !spatial.PreviewHeadingAligned &&
                    !spatial.CanAcceptPoints && !spatial.CanConfirmPoseCandidate &&
                    spatial.PoseLocalizationStatus.Contains("zero físico"),
                    "An actual loss of the motor reference must still invalidate heading and block capture after status recovers.");
                scanner.status.panReferenceValid = true;
                Invoke(spatial, "Update");
                Require(!spatial.PreviewHeadingAligned && !spatial.CanAcceptPoints,
                    "Restoring the motor reference alone must not restore an invalidated AR heading.");

                scanner.status.panDegrees = float.NaN;
                Invoke(spatial, "Update");
                Require(!spatial.StationaryPosePinned && !spatial.HasValidatedMultiviewPose &&
                    !spatial.HasAutomaticSupportHeight && !spatial.CanAcceptPoints &&
                    spatial.RangeDecision == "pan_status_unavailable",
                    "An invalid pan value must not be treated as a recoverable stale HTTP response.");

                scanner.status.panDegrees = 10f;
                Set(scanner, "statusReceivedTime", Time.unscaledTime);
                typeof(UwbDataReceiver).GetProperty("HasFreshDiagnostics").SetValue(receiver, false);
                Set(spatial, "hasSupportHeight", true);
                Set(spatial, "supportHeightY", .35f);
                Invoke(spatial, "CommitStationaryTag", new Vector3(.4f, .57f, 1.4f), "validation_unfixed");
                Set(spatial, "nextSupportObservationTime", Time.unscaledTime + 10f);
                Invoke(spatial, "Update");
                var estimator = (UwbArMultiviewEstimator)Get(spatial, "multiviewEstimator");
                Vector3[] anchors = { new Vector3(0f, 1.4f, 0f), new Vector3(-.0875f, 1.4f, 0f),
                    new Vector3(.0875f, 1.4f, 0f) };
                Require(estimator.AddSample(anchors, Ranges(anchors, spatial.currentSmoothedTagPosition)),
                    "The status-gap regression must contain a real retained range sample.");
                int retainedSamples = estimator.SampleCount;
                epoch = spatial.AutomaticPoseEpoch;
                Set(scanner, "statusReceivedTime", Time.unscaledTime - 3f);
                Invoke(spatial, "Update");
                Require(estimator.SampleCount == retainedSamples && spatial.HasHeightConsistentPose &&
                    spatial.AutomaticPoseEpoch == epoch && !spatial.CanAcceptPoints,
                    "A stale status must also retain unfinished multiview work without authorizing points.");
                scanner.status.panDegrees = 12.5f;
                Set(scanner, "statusReceivedTime", Time.unscaledTime);
                Invoke(spatial, "Update");
                Require(estimator.SampleCount == 0 && !spatial.HasValidatedMultiviewPose &&
                    !spatial.HasAutomaticSupportHeight && spatial.AutomaticPoseEpoch > epoch &&
                    spatial.RangeDecision == "pan_changed_after_status_gap",
                    "If the tag moved with the head while status was absent, unfixed range history must be discarded before localization resumes.");

                Set(spatial, "hasSupportHeight", true);
                Set(spatial, "supportHeightY", .35f);
                Invoke(spatial, "CommitStationaryTag", new Vector3(.4f, .57f, 1.4f), "validation_ar_loss");
                Require(spatial.TryConfirmPoseCandidate(out _), "AR-loss regression requires a fixed stationary origin.");
                typeof(UwbAnchorManager).GetProperty("PreviewHeadingAligned").SetValue(spatial, true);
                arState.SetValue(null, ARSessionState.None);
                Invoke(spatial, "Update");
                Require(!spatial.StationaryPosePinned && !spatial.HasAutomaticSupportHeight &&
                    !spatial.HasValidatedMultiviewPose && !spatial.PreviewHeadingAligned &&
                    !spatial.CanAcceptPoints && spatial.RangeDecision == "tracking_unavailable",
                    "Losing the AR reference must still discard the fixed calibration and block acquisition.");
            }
            finally
            {
                arState.SetValue(null, previousState);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static void SeedConfirmation(UwbAnchorManager spatial, Vector3 target, long firstId)
        {
            var confirmation = (StationaryPoseConfirmation)Get(spatial, "poseConfirmation");
            confirmation.Clear();
            float now = Time.unscaledTime;
            confirmation.TryAdd(firstId, target, Vector3.zero, now - .7f, out _);
            confirmation.TryAdd(firstId + 1, target, Vector3.zero, now - .3f, out _);
        }

        private static bool Consider(UwbAnchorManager spatial, Vector3 tag, bool support, Vector3 camera, long id)
            => (bool)Invoke(spatial, "ConsiderStationaryCandidate", tag, support, camera, id);
        private static Vector3 Ranges(Vector3[] anchors, Vector3 target) => new Vector3(
            Vector3.Distance(anchors[0], target), Vector3.Distance(anchors[1], target), Vector3.Distance(anchors[2], target));
        private static object Invoke(object instance, string name, params object[] args)
            => instance.GetType().GetMethod(name, Private).Invoke(instance, args);
        private static object Get(object instance, string name) => instance.GetType().GetField(name, Private).GetValue(instance);
        private static void Set(object instance, string name, object value) => instance.GetType().GetField(name, Private).SetValue(instance, value);
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
#endif
