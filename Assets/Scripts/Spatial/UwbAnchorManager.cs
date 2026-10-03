using ArScanner.Network;
using System;
using UnityEngine;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ArScanner.Spatial
{
    [RequireComponent(typeof(UwbDataReceiver))]
    [DefaultExecutionOrder(-100)]
    public class UwbAnchorManager : MonoBehaviour
    {
        [Header("Configuração de Montagem UWB")]
        [Tooltip("Verdadeiro se a placa de 3 âncoras UWB estiver fisicamente conectada ao celular via USB-C")]
        public bool anchorMountedOnPhone = true;

        [Tooltip("Offset físico entre a antena central das âncoras UWB e a lente da câmera do celular")]
        public Vector3 uwbToCameraOffset = new Vector3(0f, -0.05f, 0.02f); // 5cm abaixo da câmera

        [Tooltip("Centro da tag UWB em relação ao eixo do pan, nas coordenadas da cabeça giratória")]
        public Vector3 scannerTagOffsetFromPanAxis = new Vector3(0f, 0.12f, 0.02f);

        [Header("Referências Espaciais")]
        [Tooltip("Pose de aquisição do scanner. Pontos acumulados são armazenados em coordenadas de mundo.")]
        public Transform pointCloudRootContainer;

        [Tooltip("Transform de referência do visor AR (Câmera do Celular / AR Camera rastreada pelo ARCore)")]
        public Transform arCameraTransform;

        [Header("Atitude do Drone (MPU6050 no Scanner)")]
        [Tooltip("Ângulos de inclinação do Scanner medidos pelo MPU6050 em voo")]
        public float dronePitchDeg = 0f;
        public float droneRollDeg = 0f;
        public float droneYawDeg = 0f;

        [Header("Filtro e Suavização de Movimento (UWB)")]
        [Tooltip("Velocidade de interpolação Lerp para amortecer o jitter de rádio do UWB")]
        [Range(1.0f, 30.0f)]
        public float positionLerpSpeed = 8.0f;

        [Tooltip("Distância mínima em metros para registrar movimento da tag (Deadband de ruído ToF)")]
        public float positionDeadband = 0.008f; // 8 milímetros

        [Header("Calibração de Orientação e Offset")]
        [Tooltip("Offset de rotação em graus (Yaw) da base física das âncoras")]
        public float yawOffsetDegrees = 0f;

        [Tooltip("Offset de translação física da base")]
        public Vector3 baseOriginOffset = Vector3.zero;
        [Tooltip("Rotação completa da placa UWB em relação à câmera/referência fixa, medida na montagem")]
        public Vector3 baseRotationEuler = Vector3.zero;
        [Tooltip("Prévia de bancada com scanner imóvel; não é ancoragem UWB nem movimento livre")]
        public bool localPreviewWithoutUwb = true;
        [Tooltip("Ao entrar no AR, usa automaticamente as três distâncias da base USB e a pose AR do celular")]
        public bool autoUwbPositioning = false;
        [Tooltip("Alinha a direção pela tampa original do LiDAR observada de pontos de vista distintos")]
        public bool autoAlignHeading = false;
        [Tooltip("Ensaio guiado opcional: o usuário deve apontar um ponto livre fisicamente à frente do LiDAR. Não reconhece o scanner.")]
        public bool autoSupportHeadingFallback = false;
        [Tooltip("Multivista só é válida com o scanner imóvel. Desative para acompanhar o eixo experimentalmente, sem gravar pontos.")]
        public bool scannerStationary = true;
        [Tooltip("Permite exibir uma pose aproximada quando AR + UWB têm uma solução plausível, mas a geometria é fraca")]
        public bool allowApproximateUwbPose = true;
        [Tooltip("Maior incerteza geométrica aceita para a pose aproximada")]
        [Range(.5f, 5f)]
        public float approximateGeometryLimit = 3f;
        [Tooltip("Maior erro residual aceito para a pose aproximada")]
        [Range(.1f, 1f)]
        public float approximateResidualLimit = .35f;
        [Tooltip("Posição manual da prévia no mundo AR, em metros")]
        public Vector3 localPreviewPosition = new Vector3(0f, 0f, 1f);
        [Tooltip("Altura da origem do firmware acima do plano de apoio, em metros; medir na montagem")]
        public float previewOriginHeight = 0.1f; // Measured pan-axis height above support: 100 mm.
        public string PreviewPlacementSource { get; private set; } = "ainda não marcada";
        public bool PreviewPlaced { get; private set; }
        public bool HasPoseEstimate { get; private set; }
        public bool HasSavedUwbCalibration { get; private set; }
        public UwbInstantQuality CurrentUwbQuality { get; private set; } = UwbInstantQuality.Unavailable;
        public Vector3 LastCorrectedUwbRanges { get; private set; }
        public string AutoUwbStatus { get; private set; } = "UWB: aguardando base USB e três alcances.";
        public bool PreviewHeadingAligned { get; private set; }
        public bool HasValidatedMultiviewPose => hasMultiviewPose;
        public Vector3 EffectiveScannerTagOffset => ScannerTagOffset();
        public bool HasHeightConsistentPose => hasMultiviewPose && hasSupportHeight &&
            StationaryPoseConfirmation.MatchesSupportHeight(currentSmoothedTagPosition.y,
                supportHeightY, previewOriginHeight, ScannerTagOffset().y) &&
            Mathf.Abs(localPreviewPosition.y - (supportHeightY + previewOriginHeight)) <= .03f;
        public string HeadingSource { get; private set; } = "";
        public string HeadingStatus { get; private set; } = "Aguardando posição multivista.";
        public Vector3 LastHeadingTargetWorld { get; private set; }
        public float LastHeadingYawDegrees { get; private set; }
        public float LastHeadingPanDegrees { get; private set; }
        public bool HasUwbRangeReference { get; private set; }
        public float UwbRangeBiasMeters { get; private set; }
        public bool HasUwbMotionReference { get; private set; }
        public bool uwbMotionTestMode;
        public int motionSamplesApplied, motionSamplesRejected;
        public int multiviewSamplesAccepted;
        public float motionResidualMeters = float.NaN;
        public float motionGeometrySigmaMeters = float.PositiveInfinity;
        public Vector3 motionEstimatedWorld;
        private string motionCalibrationNotice = "";
        public string MotionStatus => motionCalibrationActive
            ? $"Calibração UWB: {motionCalibrationCount}/20 leituras. Mantenha o scanner parado."
            : !HasUwbMotionReference
            ? string.IsNullOrEmpty(motionCalibrationNotice)
                ? "UWB 3D: marque a origem e calibre as três distâncias."
                : motionCalibrationNotice
            : !uwbMotionTestMode ? "UWB 3D calibrado; ative o teste para acompanhar o scanner."
            : $"UWB 3D teste: X {motionEstimatedWorld.x:F2}, Y {motionEstimatedWorld.y:F2}, Z {motionEstimatedWorld.z:F2} m | resíduo {motionResidualMeters:F2} m | incerteza geométrica ≈{motionGeometrySigmaMeters:F1} m | aceitas {motionSamplesApplied}, rejeitadas {motionSamplesRejected}";
        public string MotionBiasStatus => HasUwbMotionReference
            ? $"Correções UWB: {motionRangeBias[0]:+0.00;-0.00;0.00} / {motionRangeBias[1]:+0.00;-0.00;0.00} / {motionRangeBias[2]:+0.00;-0.00;0.00} m"
            : "Correções UWB ainda não calibradas.";
        private readonly float[] motionRangeBias = new float[3];
        private readonly float[] motionCorrectedRanges = new float[3];
        private readonly Vector3[] motionAnchors = new Vector3[3];
        private readonly float[] autoRangeScales = new float[3];
        private readonly float[] autoRangeOffsets = new float[3];
        private readonly float[] autoRawRanges = new float[3];
        private readonly UwbInstantPoseEstimator instantEstimator = new UwbInstantPoseEstimator();
        private readonly UwbArMultiviewEstimator multiviewEstimator = new UwbArMultiviewEstimator();
        private readonly UwbVisualPositionRefiner visualPositionRefiner = new UwbVisualPositionRefiner();
        private readonly StationaryPoseConfirmation poseConfirmation = new StationaryPoseConfirmation();
        private readonly StationaryPoseConfirmation visualConfirmation = new StationaryPoseConfirmation();
        public int PoseCandidateCount => poseConfirmation.Count;
        public int PoseCandidateViews => poseConfirmation.ViewCount;
        public float PoseCandidateSpreadMeters => poseConfirmation.SpreadMeters;
        public Vector3 PoseCandidateTag { get; private set; }
        public bool StationaryPosePinned { get; private set; }
        public bool enableExperimentalVisualPose
        {
            get => visualPoseObserver != null && visualPoseObserver.enableNaturalCoverRecognition;
            set
            {
                if (visualPoseObserver == null) visualPoseObserver = GetComponent<ScannerVisualPoseObserver>();
                if (visualPoseObserver != null) visualPoseObserver.SetNaturalCoverRecognition(value);
            }
        }
        public bool CanConfirmPoseCandidate => scannerStationary && HasHeightConsistentPose &&
            !PoseRevalidationRequired && ARSession.state == ARSessionState.SessionTracking &&
            scannerReceiver != null && scannerReceiver.HasUsablePanReference && !scannerReceiver.ScannerControlBusy &&
            !scannerReceiver.ScanStartPending && !scannerReceiver.isScanning && !scannerReceiver.status.isScanning;
        public string PoseLocalizationStatus => localPreviewWithoutUwb
            ? PreviewPlaced ? "Eixo marcado no apoio; alinhe a direção manualmente antes de escanear."
                : "Aponte o centro para a projeção do eixo no apoio e marque sua posição."
            : !scannerStationary ? "Pare a base do scanner para localizar e capturar."
            : ARSession.state != ARSessionState.SessionTracking ? "Aguarde o rastreamento AR antes de localizar o eixo."
            : scannerReceiver == null || !scannerReceiver.HasFreshStatus
            ? "Aguardando status recente do scanner. A localização fica pausada até a conexão responder."
            : !HasSavedUwbCalibration ? "Calibre as duas distâncias UWB no menu para localizar o eixo automaticamente."
            : !scannerReceiver.HasUsablePanReference ? "Alinhe a cabeça à frente da base e confirme o zero físico do pan."
            : StationaryPosePinned ? "Eixo UWB fixado; alinhe a direção manualmente e confira a origem no scanner."
            : !hasSupportHeight ? SupportObservationStatus
            : PoseRevalidationRequired ? "Uma nova posição diverge do eixo anterior. Pause para validar novas distâncias antes de realinhar."
            : hasMultiviewPose ? "Eixo UWB estável; você pode fixá-lo. Depois alinhe a direção manualmente."
            : hasObservableStationaryCandidate ? $"Geometria multivista validada. Pare nesta posição para confirmar {PoseCandidateCount}/3 novos ciclos de distâncias."
            : MultiviewProgressMessage();
        public bool PoseRevalidationRequired { get; private set; }
        public string PoseCommitReason { get; private set; } = "none";
        public string FrozenPoseReason { get; private set; } = "";
        public long PoseRevision { get; private set; }
        public long AutomaticPoseEpoch { get; private set; }
        public long RangeDecisionRevision { get; private set; }
        public long RangeDecisionCycleId { get; private set; } = -1;
        public long RangeProcessedCycleId { get; private set; } = -1;
        public int RangeRetryCount { get; private set; }
        public int RangeSupersededCycleCount { get; private set; }
        public string RangeDecision { get; private set; } = "waiting";
        public string ArHistoryResetReason => arPoseHistory.LastResetReason;
        public long ArHistoryEpochRevision => arPoseHistory.EpochRevision;
        private int rangeRevisionRetryCount;
        private long rangeQueryCycleId = -1;
        private long visualObservationRevision;
        private long lastVisualRefinementRevision = -1;
        private Vector3 confirmedVisualCorrection;
        private bool hasConfirmedVisualCorrection;
        private bool hasConfirmedScannerVisualPose;
        private ScannerVisualPoseObserver visualPoseObserver;
        private float lastVisualGeometryObservationTime = -100f;
        private bool hasObservableStationaryCandidate;
        private Vector3 observableStationaryCandidate;
        private long observableCandidateCycleId = -1;
        private float observableCandidateTime = -100f;
        private bool observableCandidateCommitted;
        private float lastSolvedSupportHeight = float.NaN;
        private bool hasObservedPanStatus;
        private bool panStatusWasUnavailable;
        private float lastObservedPanDegrees;
        private bool hasMultiviewPose;
        private bool multiviewPausedForPan;
        private readonly UwbPhonePauseGate phonePauseGate = new UwbPhonePauseGate();
        private readonly UwbPhonePauseGate untimedRangePauseGate = new UwbPhonePauseGate();
        private bool untimedPhoneStable;
        private readonly UwbPhonePauseGate headingPhonePauseGate = new UwbPhonePauseGate();
        private readonly ArCameraPoseHistory arPoseHistory = new ArCameraPoseHistory();
        public string RangePoseStatus { get; private set; } = "Aguardando histórico AR e relógio UWB.";
        private bool phonePausedForRanges;
        private float supportCandidateY, supportCandidateSince = -1f;
        private float supportCandidateLastSeen = -100f;
        private int supportCandidateObservations;
        private float supportLastSeen = -100f, supportHeightY;
        private float foregroundLastSeen = -100f;
        private float nextSupportObservationTime;
        private bool hasSupportHeight;
        public string SupportObservationStatus { get; private set; } =
            "Enquadre o corpo do scanner sobre seu apoio e mantenha o celular parado por um segundo.";
        private bool hasSupportForeground;
        public bool HasSupportForeground => hasSupportForeground &&
            Time.unscaledTime - foregroundLastSeen <= 2.5f;
        public Vector3 LastSupportForegroundWorld { get; private set; }
        public float SupportForegroundAgeSeconds => HasSupportForeground
            ? Time.unscaledTime - foregroundLastSeen : float.PositiveInfinity;
        public Vector3 RawUwbTagPosition { get; private set; }
        public Vector3 VisualSurfaceWorld { get; private set; }
        public float VisualCorrectionMeters { get; private set; }
        public float VisualCameraBaselineMeters { get; private set; }
        public int VisualSupportingObservations { get; private set; }
        public float ForegroundToTagHorizontalDistance
        {
            get
            {
                if (!HasSupportForeground || !HasPoseEstimate) return float.NaN;
                Vector3 delta = currentSmoothedTagPosition - LastSupportForegroundWorld;
                return new Vector2(delta.x, delta.z).magnitude;
            }
        }
        private float headingCandidateSince = -1f, headingCandidateYaw;
        private Vector3 headingCandidateOrigin;
        public bool HasAutomaticSupportHeight => hasSupportHeight;
        public float AutomaticSupportHeight => supportHeightY;
        public int MultiviewSampleCount => multiviewEstimator.SampleCount;
        public string MultiviewReason => multiviewEstimator.RejectionReason;
        public string MultiviewSampleResult => phonePausedForRanges ? multiviewEstimator.LastSampleResult : "phone_moving";
        public float MultiviewBaseline => multiviewEstimator.PhoneBaselineMeters;
        public float MultiviewCondition => multiviewEstimator.GeometryCondition;
        public bool TryGetDiagnosticAnchors(Vector3[] anchors) => GetMotionAnchors(anchors);
        private long lastAutoDiagnosticsId = -1;
        private long lastComparisonDiagnosticsId = -1;
        private const int MotionCalibrationSamples = 20;
        private readonly float[,] motionCalibrationBiases = new float[MotionCalibrationSamples,3];
        private bool motionCalibrationActive;
        private int motionCalibrationCount;
        private long lastCalibrationDiagnosticsId = -1;
        private long lastMotionDiagnosticsId = -1;
        private float lastMotionSampleTime;
        private ARRaycastManager raycasts;
        private PointCloudTcpReceiver scannerReceiver;
        private ArScanner.Rendering.ThermalPointCloudRenderer pointRenderer;
        private ARPlaneManager planes;
        private ARAnchorManager anchorManager;
        private ARAnchor previewAnchor;
        private int previewPlacementVersion;
        private Pose lastPreviewAnchorPose;
        private readonly List<ARRaycastHit> placementHits = new List<ARRaycastHit>();
        public string PoseStatus => uwbReceiver != null && uwbReceiver.transportMode == UwbTransportMode.Simulated
            ? "SIMULAÇÃO — pose sintética" : autoUwbPositioning && !localPreviewWithoutUwb
            ? isTracking ? "UWB USB — pose experimental ativa" : AutoUwbStatus
            : localPreviewWithoutUwb
            ? PreviewPlaced ? "PRÉVIA LOCAL — origem marcada; yaw manual" : "PRÉVIA LOCAL — origem arbitrária; marque o scanner"
            : uwbMotionTestMode ? isTracking ? "UWB 3D experimental — trajetória estimada" : "UWB 3D: aguardando distância consistente"
            : isTracking ? "UWB aproximado — orientação/tempo não calibrados" : "SEM POSE UWB — pontos suspensos";
        public static bool AutomaticCaptureReady(bool stationary, bool tracking,
            bool multiviewAccepted, bool supportObserved, bool headingAligned)
            => stationary && tracking && multiviewAccepted && supportObserved && headingAligned;
        public bool CanAcceptPoints => uwbReceiver != null && uwbReceiver.transportMode == UwbTransportMode.Simulated
            ? isTracking : ARSession.state == ARSessionState.SessionTracking &&
                !uwbMotionTestMode &&
                (autoUwbPositioning && !localPreviewWithoutUwb
                    ? AutomaticCaptureReady(scannerStationary, isTracking,
                        hasMultiviewPose, hasSupportHeight, PreviewHeadingAligned) &&
                        HasHeightConsistentPose && StationaryPosePinned && !PoseRevalidationRequired &&
                        scannerReceiver != null && scannerReceiver.HasUsablePanReference
                    : scannerStationary && PreviewHeadingAligned &&
                        scannerReceiver != null && scannerReceiver.HasUsablePanReference &&
                        ((localPreviewWithoutUwb && PreviewPlaced) || isTracking));
        public string AcquisitionStatus => !scannerStationary
            ? "Eixo em movimento: acompanhamento experimental. Captura exige scanner parado e tempos sincronizados para movimento livre."
            : !HasPoseEstimate ? AutoUwbStatus
            : PoseRevalidationRequired ? "A posição mudou; confirme a origem e realinhe a direção antes de capturar."
            : autoUwbPositioning && !localPreviewWithoutUwb && !hasMultiviewPose
            ? "Posição UWB ainda aproximada: caminhe e pause em pontos separados. Scan bloqueado até uma solução multivista."
            : autoUwbPositioning && !localPreviewWithoutUwb && !hasSupportHeight
            ? "Apoio do scanner não confirmado pela profundidade. Scan bloqueado."
            : autoUwbPositioning && !localPreviewWithoutUwb && !HasHeightConsistentPose
            ? "Posição incompatível com a altura do eixo sobre o apoio. Refaça a localização."
            : autoUwbPositioning && !localPreviewWithoutUwb && !StationaryPosePinned
            ? "Posição estável. Confira o eixo e use Fixar eixo UWB antes de alinhar a direção e capturar."
            : !PreviewHeadingAligned ? "Posição disponível. Aponte um ponto livre do apoio na direção física eixo → LiDAR e use o alinhamento manual."
            : "Scanner declarado parado; posição e direção disponíveis para captura.";

        [Header("Status de Rastreamento")]
        public Vector3 currentSmoothedTagPosition = Vector3.zero;
        public Vector3 droneWorldPosition = Vector3.zero;
        public bool isTracking = false;

        private UwbDataReceiver uwbReceiver;
        private Vector3 rawPreviousPosition = Vector3.zero;
        private bool hasPosition;
        private bool ownsRoot;
        private long lastSampleId = -1;

        private void Awake()
        {
            InitializeReferences();
        }

        public void InitializeReferences()
        {
            uwbReceiver = GetComponent<UwbDataReceiver>();
            if (arCameraTransform == null && Camera.main != null)
            {
                arCameraTransform = Camera.main.transform;
            }

            if (pointCloudRootContainer == null)
            {
                GameObject rootGo = new GameObject("UwbPointCloudRootContainer");
                pointCloudRootContainer = rootGo.transform;
                pointCloudRootContainer.position = Vector3.zero;
                pointCloudRootContainer.rotation = Quaternion.identity;
                ownsRoot = true;
            }
        }

        private void Start()
        {
            scannerReceiver = FindFirstObjectByType<PointCloudTcpReceiver>();
            if (scannerReceiver != null) scannerReceiver.PanZeroReferenceConfirmed += OnPanZeroReferenceConfirmed;
            pointRenderer = GetComponent<ArScanner.Rendering.ThermalPointCloudRenderer>();
            visualPoseObserver = GetComponent<ScannerVisualPoseObserver>();
            if (visualPoseObserver == null) visualPoseObserver = gameObject.AddComponent<ScannerVisualPoseObserver>();
            UwbRangeCalibrationProfile.Load(autoRangeScales, autoRangeOffsets,
                out bool savedCalibration);
            HasSavedUwbCalibration = savedCalibration;
            multiviewEstimator.Clear();
            multiviewSamplesAccepted = 0;
            multiviewPausedForPan = false;
            baseRotationEuler.z = PlayerPrefs.GetFloat("arscanner.uwb.boardRotationZ", baseRotationEuler.z);
            if (autoUwbPositioning && HasSavedUwbCalibration && uwbReceiver != null &&
                uwbReceiver.transportMode == UwbTransportMode.USB)
            {
                localPreviewWithoutUwb = false;
                uwbMotionTestMode = false;
            }
            else
            {
                localPreviewWithoutUwb = true;
                uwbMotionTestMode = false;
            }
            var xrOrigin = FindAnyObjectByType<XROrigin>();
            if (xrOrigin != null)
            {
                planes = xrOrigin.GetComponent<ARPlaneManager>();
                if (planes == null) planes = xrOrigin.gameObject.AddComponent<ARPlaneManager>();
                planes.requestedDetectionMode |= PlaneDetectionMode.Horizontal;
                raycasts = xrOrigin.GetComponent<ARRaycastManager>();
                if (raycasts == null) raycasts = xrOrigin.gameObject.AddComponent<ARRaycastManager>();
                anchorManager = xrOrigin.GetComponent<ARAnchorManager>();
                if (anchorManager == null) anchorManager = xrOrigin.gameObject.AddComponent<ARAnchorManager>();
            }
            // ARCore needs an occlusion manager to produce depth raycasts. The
            // support-plane path below remains available on devices without Depth.
            if (arCameraTransform != null)
            {
                var occlusion = arCameraTransform.GetComponent<AROcclusionManager>();
                if (occlusion == null) occlusion = arCameraTransform.gameObject.AddComponent<AROcclusionManager>();
                occlusion.requestedEnvironmentDepthMode = EnvironmentDepthMode.Fastest;
            }
            Debug.Log("[UWB Spatial] UwbAnchorManager inicializado com sucesso.");
        }

        // Align translation independently of pan/beam geometry. Nothing follows the phone after this.
        public bool PlacePreviewAtScreenCenter(out string message)
        {
            if (ARSession.state != ARSessionState.SessionTracking || raycasts == null)
            {
                message = "Aguarde o rastreamento AR e aponte para o apoio sob o eixo do scanner.";
                return false;
            }
            var screenCenter = new Vector2(Screen.width*.5f, Screen.height*.5f);
            bool hasPlane = raycasts.Raycast(screenCenter, placementHits, TrackableType.PlaneWithinPolygon) &&
                placementHits.Count > 0 && Vector3.Dot(placementHits[0].pose.up, Vector3.up) >= .95f;
            ARRaycastHit hit = hasPlane ? placementHits[0] : default;
            bool knownSupport = hasSupportHeight || localPreviewWithoutUwb && PreviewPlaced;
            float knownSupportY = hasSupportHeight ? supportHeightY : localPreviewPosition.y - previewOriginHeight;
            // A foreground depth point may lie on the casing or top of the
            // scanner. It can only substitute for an already known support.
            if (!hasPlane && knownSupport && raycasts.Raycast(screenCenter, placementHits, TrackableType.Depth) &&
                placementHits.Count > 0 && placementHits[0].distance >= .15f && placementHits[0].distance <= 10f &&
                IsDepthCompatibleWithPreviewSupport(placementHits[0].pose, knownSupportY))
            {
                Pose depthPose = placementHits[0].pose;
                ++previewPlacementVersion;
                if (previewAnchor != null) anchorManager.TryRemoveAnchor(previewAnchor);
                previewAnchor = null;
                PreviewPlacementSource = "profundidade compatível com o apoio já observado";
                SetPreviewSupportPosition(depthPose.position);
                AttachDepthPreviewAnchorAsync(new Pose(depthPose.position, Quaternion.identity),
                    previewPlacementVersion);
                message = $"Origem colocada no apoio apontado, com {previewOriginHeight*100f:F1} cm até o eixo. Confira o eixo verde antes do scan.";
                return true;
            }
            if (!hasPlane)
            {
                message = "Observe uma área livre da mesa ou chão até o AR reconhecer o apoio horizontal; depois marque a projeção do eixo nesse plano.";
                return false;
            }
            if (hit.distance < .15f || hit.distance > 10f)
            {
                message = "Aproxime o celular do apoio horizontal sob o eixo do scanner.";
                return false;
            }
            ARPlane plane = planes != null ? planes.GetPlane(hit.trackableId) : null;
            if (plane == null || anchorManager == null)
            {
                message = "Plano detectado, mas a âncora AR ainda não está pronta. Tente marcar novamente.";
                return false;
            }
            ARAnchor newAnchor;
            try { newAnchor = anchorManager.AttachAnchor(plane, hit.pose); }
            catch (System.InvalidOperationException)
            {
                message = "ARCore ainda não aceitou a âncora neste plano. Aguarde e tente novamente.";
                return false;
            }
            if (newAnchor == null)
            {
                message = "Não foi possível fixar o scanner no plano AR. Aponte novamente para o apoio.";
                return false;
            }
            ++previewPlacementVersion;
            if (previewAnchor != null) anchorManager.TryRemoveAnchor(previewAnchor);
            previewAnchor = newAnchor;
            lastPreviewAnchorPose = new Pose(previewAnchor.transform.position, previewAnchor.transform.rotation);
            PreviewPlacementSource = "plano AR (confira se é o apoio, não o chão atrás)";
            SetPreviewSupportPosition(hit.pose.position);
            message = "Origem marcada no plano AR. Confira se ele corresponde ao apoio do scanner.";
            return true;
        }

        public static bool IsDepthCompatibleWithPreviewSupport(Pose depthPose, float knownSupportY)
            => Finite(depthPose.position.sqrMagnitude) && Finite(knownSupportY) &&
                Vector3.Dot(depthPose.rotation * Vector3.up, Vector3.up) >= .95f &&
                Mathf.Abs(depthPose.position.y - knownSupportY) <= .03f;

        private void SetPreviewSupportPosition(Vector3 supportPosition)
        {
            ResetAutomaticHistory(true);
            scannerStationary = true;
            localPreviewPosition = supportPosition + Vector3.up*previewOriginHeight;
            localPreviewWithoutUwb = true;
            PreviewPlaced = true;
            HasPoseEstimate = true;
            PreviewHeadingAligned = false;
            HasUwbRangeReference = false;
            HasUwbMotionReference = false;
            motionCalibrationActive = false;
            motionCalibrationNotice = "";
            uwbMotionTestMode = false;
            AutoUwbStatus = "Scanner fixado pela câmera no apoio. UWB continua disponível para diagnóstico de distâncias.";
            pointCloudRootContainer.SetPositionAndRotation(localPreviewPosition, Quaternion.Euler(0,droneYawDeg,0));
        }

        private async void AttachDepthPreviewAnchorAsync(Pose supportPose, int placementVersion)
        {
            if (anchorManager == null) return;
            try
            {
                var result = await anchorManager.TryAddAnchorAsync(supportPose);
                if (!result.status.IsSuccess() || result.value == null) return;
                if (placementVersion != previewPlacementVersion || !this)
                {
                    anchorManager.TryRemoveAnchor(result.value);
                    return;
                }
                previewAnchor = result.value;
                lastPreviewAnchorPose = new Pose(previewAnchor.transform.position, previewAnchor.transform.rotation);
                localPreviewPosition = previewAnchor.transform.position + Vector3.up*previewOriginHeight;
            }
            catch (InvalidOperationException) { /* Depth placement remains valid without an AR anchor. */ }
        }

        private bool TryGetRangeComparison(out float measured, out float arDistance)
        {
            measured = arDistance = 0f;
            if (!PreviewPlaced || !anchorMountedOnPhone || arCameraTransform == null ||
                uwbReceiver == null || !uwbReceiver.HasFreshDiagnostics) return false;
            var d = uwbReceiver.Diagnostics;
            if (d == null || d.radioMask != 7 || d.d1 <= 0f || d.d2 <= 0f || d.d3 <= 0f ||
                float.IsNaN(d.d1) || float.IsNaN(d.d2) || float.IsNaN(d.d3)) return false;
            measured = (d.d1 + d.d2 + d.d3) / 3f;
            Vector3 baseWorld = arCameraTransform.TransformPoint(uwbToCameraOffset);
            arDistance = Vector3.Distance(baseWorld, localPreviewPosition);
            return arDistance > .1f;
        }

        public string UwbRangeStatus
        {
            get
            {
                if (!TryGetRangeComparison(out float measured, out float arDistance))
                    return "Conferência UWB: marque o scanner e aguarde três distâncias recentes.";
                float corrected = measured - (HasUwbRangeReference ? UwbRangeBiasMeters : 0f);
                return HasUwbRangeReference
                    ? $"UWB {corrected:F2} m (corrigido) | AR {arDistance:F2} m | diferença {corrected-arDistance:+0.00;-0.00;0.00} m"
                    : $"UWB bruto {measured:F2} m | AR {arDistance:F2} m; calibre o desvio nesta posição.";
            }
        }

        private bool GetMotionAnchors(Vector3[] anchors)
        {
            if (!anchorMountedOnPhone || arCameraTransform == null || anchors == null || anchors.Length != 3)
                return false;
            return GetMotionAnchors(new Pose(arCameraTransform.position,
                arCameraTransform.rotation), anchors);
        }

        private bool GetMotionAnchors(Pose cameraPose, Vector3[] anchors)
        {
            if (!anchorMountedOnPhone || anchors == null || anchors.Length != 3) return false;
            // Measured antenna centres: 175 mm base, 100 mm equal sides.
            anchors[0] = AnchorAtCameraPose(cameraPose, 0);
            anchors[1] = AnchorAtCameraPose(cameraPose, 1);
            anchors[2] = AnchorAtCameraPose(cameraPose, 2);
            return true;
        }

        private Vector3 AnchorAtCameraPose(Pose cameraPose, int index)
        {
            Vector3 center = cameraPose.position + cameraPose.rotation * uwbToCameraOffset;
            Quaternion board = cameraPose.rotation * Quaternion.Euler(baseRotationEuler);
            Vector3 offset = index == 0 ? new Vector3(0f, .0484123f, 0f) :
                index == 1 ? new Vector3(-.0875f, 0f, 0f) :
                new Vector3(.0875f, 0f, 0f);
            return center + board * offset;
        }

        private bool TryGetRangeAnchors(Vector3[] anchors, out Pose middleCameraPose,
            out bool stationaryDuringCycle)
        {
            middleCameraPose = default;
            stationaryDuringCycle = false;
            if (uwbReceiver == null || !uwbReceiver.HasFreshThreeRanges ||
                ARSession.state != ARSessionState.SessionTracking ||
                arCameraTransform == null || !anchorMountedOnPhone ||
                anchors == null || anchors.Length != 3) return false;

            if (uwbReceiver.TryGetLatestRangeTimes(out double t1, out double t2, out double t3))
            {
                if (!arPoseHistory.TryGet(t1, out Pose pose1) ||
                    !arPoseHistory.TryGet(t2, out Pose pose2) ||
                    !arPoseHistory.TryGet(t3, out Pose pose3))
                {
                    bool futurePose = arPoseHistory.TryGetBounds(out double oldest, out double latest) &&
                        t1 >= oldest && t3 > latest;
                    SetRangeDecision(futurePose ? "pending_future_pose" : "history_missing");
                    RangePoseStatus = futurePose
                        ? "Leitura aguardando a próxima pose AR para interpolar; será tentada novamente."
                        : "UWB temporizado, mas sem histórico AR contínuo no instante da leitura.";
                    return false;
                }
                anchors[0] = AnchorAtCameraPose(pose1, 0);
                anchors[1] = AnchorAtCameraPose(pose2, 1);
                anchors[2] = AnchorAtCameraPose(pose3, 2);
                middleCameraPose = pose2;
                stationaryDuringCycle = Vector3.Distance(pose1.position, pose3.position) <= .02f &&
                    Quaternion.Angle(pose1.rotation, pose3.rotation) <= 3f;
                RangePoseStatus = "Cada alcance associado à pose AR interpolada da respectiva antena; " +
                    "atraso USB mínimo observado, latência absoluta não calibrada.";
                return true;
            }

            // V2 timestamps must be aligned before use. An abnormal USB delay
            // or missing AR history must never silently fall back to arrival.
            if (uwbReceiver.Diagnostics != null && uwbReceiver.Diagnostics.version >= 2)
            {
                SetRangeDecision("clock_pending_or_delayed");
                RangePoseStatus = "Aguardando alinhamento do relógio UWB e histórico AR para esta leitura.";
                return false;
            }

            // Legacy V1 has no individual range timestamps. Use receipt pose
            // only after a continuous phone pause and a very recent packet.
            Pose current = new Pose(arCameraTransform.position, arCameraTransform.rotation);
            if (uwbReceiver.DiagnosticsAgeSeconds > .35f ||
                !untimedPhoneStable)
            {
                SetRangeDecision("legacy_waiting_pause");
                RangePoseStatus = "Aguardando sincronismo UWB ou celular parado para associar alcance e pose AR.";
                return false;
            }
            middleCameraPose = current;
            stationaryDuringCycle = true;
            RangePoseStatus = "Pose AR no recebimento, com celular parado; tempo UWB absoluto indisponível.";
            return GetMotionAnchors(current, anchors);
        }

        private void BeginRangeQuery()
        {
            long id = uwbReceiver != null ? uwbReceiver.AppliedDiagnosticsId : -1;
            if (rangeQueryCycleId != id)
            {
                if (rangeQueryCycleId >= 0 && rangeQueryCycleId != RangeProcessedCycleId)
                    RangeSupersededCycleCount++;
                rangeQueryCycleId = id;
                RangeRetryCount = 0;
            }
            else RangeRetryCount++;
        }

        private void SetRangeDecision(string decision)
        {
            long id = uwbReceiver != null ? uwbReceiver.AppliedDiagnosticsId : -1;
            if (RangeDecisionCycleId == id && RangeDecision == decision &&
                rangeRevisionRetryCount == RangeRetryCount) return;
            RangeDecisionCycleId = id;
            RangeDecision = decision;
            rangeRevisionRetryCount = RangeRetryCount;
            RangeDecisionRevision++;
        }

        private void MarkRangeProcessed()
        {
            long id = uwbReceiver != null ? uwbReceiver.AppliedDiagnosticsId : -1;
            if (RangeProcessedCycleId != id)
            {
                RangeProcessedCycleId = id;
                RangeDecisionRevision++;
            }
            SetRangeDecision("processed");
        }

        private void SetFrozenPose(string reason, string message)
        {
            FrozenPoseReason = reason;
            RangePoseStatus = "Consulta temporal pausada: " + message;
            SetRangeDecision(reason);
        }

        private void UpdateMotionCalibration()
        {
            if (!motionCalibrationActive || uwbReceiver == null ||
                !uwbReceiver.HasFreshThreeRanges ||
                ARSession.state != ARSessionState.SessionTracking) return;
            if (lastCalibrationDiagnosticsId == uwbReceiver.AppliedDiagnosticsId) return;
            if (!TryGetRangeAnchors(motionAnchors, out _, out bool stationaryDuringCycle) ||
                !stationaryDuringCycle) return;
            lastCalibrationDiagnosticsId = uwbReceiver.AppliedDiagnosticsId;
            var d = uwbReceiver.Diagnostics;
            if (d == null || d.radioMask != 7 || d.d1 <= 0f || d.d2 <= 0f || d.d3 <= 0f) return;
            float[] raw = {d.d1,d.d2,d.d3};
            for (int i = 0; i < 3; i++)
                motionCalibrationBiases[motionCalibrationCount,i] = raw[i] -
                    Vector3.Distance(motionAnchors[i],localPreviewPosition);
            if (++motionCalibrationCount < MotionCalibrationSamples) return;

            float total = 0f;
            for (int i = 0; i < 3; i++)
            {
                var sorted = new float[MotionCalibrationSamples];
                for (int sample = 0; sample < MotionCalibrationSamples; sample++)
                    sorted[sample] = motionCalibrationBiases[sample,i];
                if (!UwbMotionEstimator.TryRobustBias(sorted,out float bias))
                {
                    motionCalibrationActive = false;
                    motionCalibrationCount = 0;
                    motionCalibrationNotice = "Calibração instável (>20 cm de variação). Repita com scanner parado e visão direta.";
                    return; // Multipath/motion made this calibration unusable.
                }
                motionRangeBias[i] = bias;
                total += motionRangeBias[i];
            }
            UwbRangeBiasMeters = total/3f;
            HasUwbRangeReference = HasUwbMotionReference = true;
            motionCalibrationActive = false;
            motionCalibrationNotice = "";
            motionEstimatedWorld = localPreviewPosition;
            motionSamplesApplied = motionSamplesRejected = 0;
            motionResidualMeters = 0f;
            motionGeometrySigmaMeters = UwbMotionEstimator.GeometrySigma(motionAnchors,
                motionEstimatedWorld,.05f);
            lastMotionDiagnosticsId = -1;
            lastMotionSampleTime = Time.unscaledTime;
        }

        private void UpdateContinuousUwbComparison()
        {
            if (!PreviewPlaced || uwbReceiver == null || !uwbReceiver.HasFreshThreeRanges ||
                ARSession.state != ARSessionState.SessionTracking ||
                lastComparisonDiagnosticsId == uwbReceiver.AppliedDiagnosticsId ||
                !TryGetRangeAnchors(motionAnchors, out _, out bool stationaryDuringCycle) ||
                !stationaryDuringCycle) return;
            lastComparisonDiagnosticsId = uwbReceiver.AppliedDiagnosticsId;

            var d = uwbReceiver.Diagnostics;
            if (d == null || d.radioMask != 7 || d.d1 <= 0.05f || d.d2 <= 0.05f || d.d3 <= 0.05f) return;

            float expD1 = Vector3.Distance(motionAnchors[0], localPreviewPosition);
            float expD2 = Vector3.Distance(motionAnchors[1], localPreviewPosition);
            float expD3 = Vector3.Distance(motionAnchors[2], localPreviewPosition);

            float err1 = d.d1 - expD1;
            float err2 = d.d2 - expD2;
            float err3 = d.d3 - expD3;

            if (scannerStationary && !motionCalibrationActive)
            {
                if (!HasUwbMotionReference)
                {
                    motionRangeBias[0] = err1;
                    motionRangeBias[1] = err2;
                    motionRangeBias[2] = err3;
                    HasUwbMotionReference = HasUwbRangeReference = true;
                }
                else
                {
                    motionRangeBias[0] = Mathf.Lerp(motionRangeBias[0], err1, 0.03f);
                    motionRangeBias[1] = Mathf.Lerp(motionRangeBias[1], err2, 0.03f);
                    motionRangeBias[2] = Mathf.Lerp(motionRangeBias[2], err3, 0.03f);
                }
                UwbRangeBiasMeters = (motionRangeBias[0] + motionRangeBias[1] + motionRangeBias[2]) / 3f;
                motionEstimatedWorld = localPreviewPosition;
            }

            AutoUwbStatus = $"FUSÃO AR+UWB: d1={d.d1:F2}m (AR: {expD1:F2}m, erro {err1 * 100f:+0;-0;0}cm) " +
                           $"d2={d.d2:F2}m ({err2 * 100f:+0;-0;0}cm) d3={d.d3:F2}m ({err3 * 100f:+0;-0;0}cm)";
        }

        public void RotateBoardInPhone90()
        {
            baseRotationEuler.z = Mathf.Repeat(baseRotationEuler.z+90f,360f);
            PlayerPrefs.SetFloat("arscanner.uwb.boardRotationZ", baseRotationEuler.z);
            PlayerPrefs.Save();
            ResetAutomaticHistory();
            HasPoseEstimate = false;
            CurrentUwbQuality = UwbInstantQuality.Unavailable;
            HasUwbMotionReference = HasUwbRangeReference = false;
            motionCalibrationActive = false;
            motionCalibrationNotice = "";
            uwbMotionTestMode = false;
            localPreviewWithoutUwb = !autoUwbPositioning;
            isTracking = false;
            PreviewPlaced = false;
        }

        public void ResumeAutomaticUwb()
        {
            ResetAutomaticHistory(true);
            InvalidateHeading();
            localPreviewWithoutUwb = false;
            uwbMotionTestMode = false;
            instantEstimator.Clear();
            lastAutoDiagnosticsId = -1;
            HasPoseEstimate = PreviewPlaced = isTracking = false;
            CurrentUwbQuality = UwbInstantQuality.Unavailable;
            AutoUwbStatus = "UWB USB: aguardando três distâncias recentes do scanner.";
        }

        public void SetScannerStationary(bool stationary)
        {
            scannerStationary = stationary;
            PreviewHeadingAligned = false; // Moving the base may change its yaw.
            ResumeAutomaticUwb();
        }

        public void InvalidateHeading()
        {
            PreviewHeadingAligned = false;
            HeadingSource = "";
            HeadingStatus = "Aguardando observação da frente física do scanner ou alinhamento manual.";
            headingCandidateSince = -1f;
            headingPhonePauseGate.Clear();
        }

        private void ResetAutomaticHistory(bool forceRevision = false)
        {
            if (forceRevision || hasSupportHeight || poseConfirmation.Count > 0 ||
                hasConfirmedVisualCorrection || hasConfirmedScannerVisualPose)
            {
                PoseRevision++;
                AutomaticPoseEpoch++;
            }
            instantEstimator.Clear();
            multiviewEstimator.Clear();
            poseConfirmation.Clear();
            visualConfirmation.Clear();
            PoseCandidateTag = Vector3.zero;
            StationaryPosePinned = false;
            hasObservableStationaryCandidate = false;
            observableCandidateCycleId = -1;
            observableCandidateTime = -100f;
            observableCandidateCommitted = false;
            lastSolvedSupportHeight = float.NaN;
            hasObservedPanStatus = panStatusWasUnavailable = false;
            PoseRevalidationRequired = false;
            PoseCommitReason = "reset";
            FrozenPoseReason = "";
            confirmedVisualCorrection = Vector3.zero;
            hasConfirmedVisualCorrection = hasConfirmedScannerVisualPose = false;
            visualObservationRevision = 0;
            lastVisualRefinementRevision = -1;
            lastVisualGeometryObservationTime = -100f;
            phonePauseGate.Clear();
            untimedRangePauseGate.Clear();
            untimedPhoneStable = false;
            headingPhonePauseGate.Clear();
            arPoseHistory.Clear();
            RangePoseStatus = "Aguardando histórico AR e relógio UWB.";
            headingCandidateSince = supportCandidateSince = -1f;
            supportCandidateLastSeen = -100f;
            supportCandidateObservations = 0;
            hasSupportHeight = false;
            SupportObservationStatus = "Enquadre o corpo do scanner sobre seu apoio e mantenha o celular parado por um segundo.";
            hasSupportForeground = false;
            foregroundLastSeen = -100f;
            nextSupportObservationTime = 0f;
            visualPositionRefiner.Clear();
            VisualCorrectionMeters = VisualCameraBaselineMeters = 0f;
            VisualSupportingObservations = 0;
            VisualSurfaceWorld = RawUwbTagPosition = Vector3.zero;
            hasMultiviewPose = multiviewPausedForPan = phonePausedForRanges = false;
            multiviewSamplesAccepted = 0;
            lastAutoDiagnosticsId = -1;
            lastComparisonDiagnosticsId = -1;
            lastMotionSampleTime = 0f;
        }

        private void OnPanZeroReferenceConfirmed()
        {
            if (autoUwbPositioning && !localPreviewWithoutUwb) ResumeAutomaticUwb();
            else { AutomaticPoseEpoch++; PoseRevision++; InvalidateHeading(); }
        }

        private void UpdateUwbMotionTest()
        {
            if (!HasUwbMotionReference || uwbReceiver == null || !uwbReceiver.HasFreshThreeRanges ||
                ARSession.state != ARSessionState.SessionTracking)
            {
                isTracking = false;
                return;
            }
            var d = uwbReceiver.Diagnostics;
            if (d == null || d.radioMask != 7 || d.d1 <= 0f || d.d2 <= 0f || d.d3 <= 0f ||
                uwbReceiver.DiagnosticsAgeSeconds > 1.5f)
            {
                isTracking = false;
                return;
            }
            if (lastMotionDiagnosticsId == uwbReceiver.AppliedDiagnosticsId) return;
            if (!TryGetRangeAnchors(motionAnchors, out _, out _))
            {
                isTracking = false;
                return;
            }
            lastMotionDiagnosticsId = uwbReceiver.AppliedDiagnosticsId;
            float[] raw = {d.d1,d.d2,d.d3};
            for (int i = 0; i < 3; i++) motionCorrectedRanges[i] = raw[i]-motionRangeBias[i];
            if (!UwbMotionEstimator.TryEstimate(motionAnchors,motionCorrectedRanges,
                motionEstimatedWorld,.08f,out Vector3 estimate,out float residual))
            {
                motionSamplesRejected++;
                isTracking = false;
                return;
            }
            motionResidualMeters = residual;
            motionGeometrySigmaMeters = UwbMotionEstimator.GeometrySigma(motionAnchors,estimate,.05f);
            float elapsed = Mathf.Max(.1f,Time.unscaledTime-lastMotionSampleTime);
            lastMotionSampleTime = Time.unscaledTime;
            if (residual > .35f || Vector3.Distance(estimate,motionEstimatedWorld) > 3f*elapsed+.15f)
            {
                motionSamplesRejected++;
                isTracking = false;
                return;
            }
            motionEstimatedWorld = Vector3.Lerp(motionEstimatedWorld,estimate,.65f);
            droneWorldPosition = motionEstimatedWorld;
            motionSamplesApplied++;
            isTracking = true;
            pointCloudRootContainer.SetPositionAndRotation(motionEstimatedWorld,
                Quaternion.Euler(0f,droneYawDeg,0f));
        }

        // The observed direction is the current head's +Z (pan axis -> LiDAR).
        // Subtract motor pan to recover the stationary scanner-base heading in AR.
        public static bool TryCalculateScannerYaw(Vector3 origin, Vector3 target, float pan, out float yaw)
        {
            Vector3 forward = target - origin;
            forward.y = 0;
            yaw = 0;
            if (float.IsNaN(forward.sqrMagnitude) || float.IsInfinity(forward.sqrMagnitude) || forward.sqrMagnitude < .04f || float.IsNaN(pan) || float.IsInfinity(pan)) return false;
            yaw = Mathf.Repeat(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg - pan, 360f);
            return true;
        }

        public static float RootYawForAlignedHeading(float physicalHeading, float localPointYaw)
        {
            return Mathf.Repeat(physicalHeading - localPointYaw, 360f);
        }

        public static bool TryCalculateScannerYawFromTag(Vector3 tag, Vector3 target,
            Vector3 tagOffset, float pan, out float yaw)
        {
            Vector3 delta = target - tag;
            delta.y = 0f;
            yaw = 0f;
            if (!Finite(delta.sqrMagnitude) || !Finite(tagOffset.sqrMagnitude) || !Finite(pan) ||
                delta.sqrMagnitude < .04f || delta.sqrMagnitude <= tagOffset.x*tagOffset.x) return false;
            // target - tag = R_head * (-offset.x, 0, distanceAlongHeadZ-offset.z).
            // Choose the target ahead of the tag. No previous yaw/origin is needed.
            float forward = Mathf.Sqrt(delta.sqrMagnitude-tagOffset.x*tagOffset.x);
            if (forward + tagOffset.z < .2f) return false;
            yaw = Mathf.Repeat((Mathf.Atan2(delta.x,delta.z) -
                Mathf.Atan2(-tagOffset.x,forward))*Mathf.Rad2Deg-pan,360f);
            return true;
        }

        private bool TryGetClearSupportTarget(out Vector3 target, out string message)
        {
            target = Vector3.zero;
            if (!scannerStationary || !PreviewPlaced ||
                ARSession.state != ARSessionState.SessionTracking || raycasts == null)
            {
                message = "Aguarde a posição estável do scanner.";
                return false;
            }
            if (autoUwbPositioning && !localPreviewWithoutUwb &&
                (!hasMultiviewPose || !hasSupportHeight || PoseRevalidationRequired || !StationaryPosePinned))
            {
                message = "Aguarde a posição multivista e o apoio; depois fixe o eixo UWB antes de alinhar a direção.";
                return false;
            }
            if (scannerReceiver != null && !scannerReceiver.HasUsablePanReference)
            {
                message = "Confirme o zero físico do pan antes de alinhar a direção.";
                return false;
            }
            Vector2 center = new Vector2(Screen.width*.5f, Screen.height*.5f);
            if (!raycasts.Raycast(center, placementHits, TrackableType.PlaneWithinPolygon) ||
                placementHits.Count == 0 ||
                Vector3.Dot(placementHits[0].pose.up, Vector3.up) < .95f)
            {
                message = "Aponte o centro para a mesa livre à frente do LiDAR.";
                return false;
            }
            target = placementHits[0].pose.position;
            float planeDistance = placementHits[0].distance;
            float supportY = localPreviewWithoutUwb
                ? localPreviewPosition.y - previewOriginHeight : supportHeightY;
            if (Mathf.Abs(target.y - supportY) > .05f)
            {
                message = "O centro da câmera atingiu outro plano; aponte para o apoio do scanner.";
                return false;
            }
            // A plane raycast can pass through the scanner and hit the table
            // behind it. Never turn that background point into a heading.
            if (raycasts.Raycast(center, placementHits, TrackableType.Depth) &&
                placementHits.Count > 0 &&
                placementHits[0].distance < planeDistance - .06f &&
                placementHits[0].pose.position.y > supportY + .04f)
            {
                message = "O centro está sobre o scanner ou outro objeto. Aponte para a mesa livre à frente do LiDAR.";
                return false;
            }
            Vector3 horizontal = target - localPreviewPosition;
            horizontal.y = 0f;
            if (horizontal.magnitude < .25f || horizontal.magnitude > 1.5f)
            {
                message = "Escolha na mesa um ponto entre 25 cm e 1,5 m à frente do eixo.";
                return false;
            }
            message = "";
            return true;
        }

        public bool AlignPreviewForwardAtScreenCenter(float panDegrees, out string message,
            bool automatic = false)
        {
            if (scannerReceiver != null && scannerReceiver.ScannerControlBusy)
            {
                message = "Aguarde o término do comando e o pan parado para alinhar a direção.";
                HeadingStatus = message;
                return false;
            }
            if (!TryGetClearSupportTarget(out Vector3 target, out message))
            {
                HeadingStatus = message;
                return false;
            }
            float yaw;
            bool aligned = localPreviewWithoutUwb
                ? TryCalculateScannerYaw(localPreviewPosition, target, panDegrees, out yaw)
                : TryCalculateScannerYawFromTag(currentSmoothedTagPosition, target,
                    ScannerTagOffset(), panDegrees, out yaw);
            if (!aligned)
            {
                message = "Escolha um ponto a pelo menos 20 cm do eixo, no sentido eixo → LiDAR.";
                HeadingStatus = message;
                return false;
            }
            // Point geometry and axes carry a saved local yaw correction. Remove
            // it from the root so the final visible heading matches the AR mark.
            float pointYaw = pointRenderer != null ? pointRenderer.pointYawOffset : 0f;
            droneYawDeg = RootYawForAlignedHeading(yaw, pointYaw);
            // In automatic mode the observed point is the tag, not the pan axis.
            // A new heading changes the rotated lever arm and therefore the origin.
            if (!localPreviewWithoutUwb)
            {
                localPreviewPosition = PanOriginFromRotatingTag(currentSmoothedTagPosition,
                    yaw, panDegrees, ScannerTagOffset());
                droneWorldPosition = motionEstimatedWorld = localPreviewPosition;
            }
            PreviewHeadingAligned = true;
            HeadingSource = automatic ? "auto_support" : "manual_support";
            LastHeadingTargetWorld = target;
            LastHeadingYawDegrees = yaw;
            LastHeadingPanDegrees = panDegrees;
            HeadingStatus = automatic ? "Direção alinhada automaticamente." :
                "Direção alinhada pelo ponto escolhido na mesa.";
            pointCloudRootContainer.SetPositionAndRotation(localPreviewPosition,
                Quaternion.Euler(0, droneYawDeg, 0));
            message = $"Direção alinhada ao AR ({yaw:F1}°). O zero mecânico do pan foi preservado.";
            Debug.Log($"[UWB Heading] {HeadingSource}: yaw={yaw:F1} pan={panDegrees:F1} " +
                $"tag={currentSmoothedTagPosition:F3} target={target:F3} origin={localPreviewPosition:F3}");
            return true;
        }

        private bool UpdateAutomaticSupportHeight()
        {
            if (raycasts == null || arCameraTransform == null ||
                ARSession.state != ARSessionState.SessionTracking) return false;
            float now = Time.unscaledTime;
            if (hasSupportForeground && now - foregroundLastSeen > 2.5f)
                hasSupportForeground = false;
            // A measured support remains the same throughout this stationary AR
            // epoch, including while collecting the first multiview solution.
            bool lockedSupport = hasSupportHeight && scannerStationary;
            if (now < nextSupportObservationTime)
                return lockedSupport || hasSupportHeight && now - supportLastSeen < 2f;
            nextSupportObservationTime = now + .2f;
            var center = new Vector2(Screen.width * .5f, Screen.height * .5f);
            if (!raycasts.Raycast(center, placementHits, TrackableType.PlaneWithinPolygon) ||
                placementHits.Count == 0)
            {
                SupportObservationStatus = "O AR ainda não encontrou o apoio. Enquadre o scanner e parte da mesa ou chão ao redor.";
                return lockedSupport || hasSupportHeight && now - supportLastSeen < 2f;
            }
            if (Vector3.Dot(placementHits[0].pose.up, Vector3.up) < .95f ||
                placementHits[0].distance < .15f || placementHits[0].distance > 8f)
            {
                SupportObservationStatus = "O apoio no centro precisa ser horizontal e estar entre 15 cm e 8 m do celular.";
                return lockedSupport || hasSupportHeight && now - supportLastSeen < 2f;
            }

            Vector3 support = placementHits[0].pose.position;
            float planeDistance = placementHits[0].distance;
            // A later plane may be the floor or another table. Keep the
            // established support, but do not call its old depth point current.
            if (lockedSupport && Mathf.Abs(support.y - supportHeightY) > .05f)
                return true;
            // A plane alone might be the floor behind the scanner. Require a
            // depth hit on the scanner above that plane before using its height.
            if (!raycasts.Raycast(center, placementHits, TrackableType.Depth) ||
                placementHits.Count == 0)
            {
                SupportObservationStatus = "Apoio encontrado; falta profundidade no corpo do scanner. Mova o celular um pouco e volte a enquadrá-lo.";
                return lockedSupport || hasSupportHeight && now - supportLastSeen < 2f;
            }
            if (placementHits[0].distance > planeDistance - .04f ||
                placementHits[0].pose.position.y < support.y + .04f ||
                placementHits[0].pose.position.y > support.y + .5f)
            {
                SupportObservationStatus = "A profundidade no centro ainda não corresponde ao corpo sobre o apoio. Mire no corpo, com a mesa ou chão visível ao redor.";
                return lockedSupport || hasSupportHeight && now - supportLastSeen < 2f;
            }
            Vector3 foreground = placementHits[0].pose.position;
            if (LastCorrectedUwbRanges.x > 0f)
            {
                float range = (LastCorrectedUwbRanges.x + LastCorrectedUwbRanges.y +
                    LastCorrectedUwbRanges.z) / 3f;
                Vector3 expectedTag = support + Vector3.up *
                    (previewOriginHeight + ScannerTagOffset().y);
                if (Mathf.Abs(Vector3.Distance(arCameraTransform.position, expectedTag) - range) > .7f)
                {
                    SupportObservationStatus = "O corpo observado diverge da distância UWB. Confira o enquadramento e o perfil de duas distâncias.";
                    return lockedSupport;
                }
            }
            if (!lockedSupport)
            {
                if (supportCandidateSince < 0f ||
                    Mathf.Abs(support.y - supportCandidateY) > .035f ||
                    now - supportCandidateLastSeen > .45f)
                {
                    supportCandidateY = support.y;
                    supportCandidateSince = now;
                    hasSupportHeight = false;
                    supportCandidateObservations = 0;
                    visualPositionRefiner.Clear();
                }
                supportCandidateLastSeen = now;
                supportCandidateObservations++;
                supportLastSeen = now;
                if (now - supportCandidateSince < .8f || supportCandidateObservations < 3)
                {
                    SupportObservationStatus = "Corpo e apoio encontrados. Mantenha esse enquadramento por um segundo para confirmar a altura.";
                    return false;
                }
                supportHeightY = supportCandidateY;
                hasSupportHeight = true;
            }
            else supportLastSeen = now;
            SupportObservationStatus = "Altura do apoio confirmada.";
            LastSupportForegroundWorld = foreground;
            foregroundLastSeen = now;
            hasSupportForeground = true;
            // A depth hit at the bottom of the device can help locate its
            // horizontal position, but only repeated hits from different AR
            // viewpoints can influence the UWB position.
            if (foreground.y >= supportHeightY + .07f &&
                foreground.y <= supportHeightY + .42f)
            {
                if (visualPositionRefiner.Add(foreground, arCameraTransform.position, now))
                    visualObservationRevision++;
            }
            return true;
        }

        private bool ConsiderStationaryCandidate(Vector3 rawTag, bool heightConstrained,
            Vector3 camera, long cycleId)
            => ConsiderStationaryCandidateCore(rawTag, heightConstrained, camera, cycleId, false, null, Vector3.zero);

        private void CacheObservableStationaryCandidate(Vector3 rawTag, long cycleId)
        {
            if (!hasObservableStationaryCandidate || Vector3.Distance(rawTag, observableStationaryCandidate) > .01f)
            {
                if (observableCandidateCommitted) poseConfirmation.Clear();
                observableCandidateCommitted = false;
            }
            observableStationaryCandidate = PoseCandidateTag = rawTag;
            observableCandidateCycleId = cycleId;
            observableCandidateTime = Time.unscaledTime;
            hasObservableStationaryCandidate = true;
            if (hasMultiviewPose && !hasConfirmedScannerVisualPose &&
                StationaryPoseConfirmation.RequiresHeadingRevalidation(localPreviewPosition,
                    OriginForTag(rawTag + (hasConfirmedVisualCorrection ? confirmedVisualCorrection : Vector3.zero))))
            {
                PoseRevalidationRequired = true;
                InvalidateHeading();
            }
        }

        private bool ConsiderValidatedRangeCandidate(Vector3 rawTag, Vector3 camera,
            long cycleId, Vector3[] anchors, Vector3 ranges)
        {
            if (!hasObservableStationaryCandidate || observableCandidateCommitted || cycleId <= observableCandidateCycleId)
                return false;
            return ConsiderStationaryCandidateCore(rawTag, true, camera, cycleId, true, anchors, ranges);
        }

        private bool ConsiderStationaryCandidateCore(Vector3 rawTag, bool heightConstrained,
            Vector3 camera, long cycleId, bool validateFreshCycle, Vector3[] anchors, Vector3 ranges)
        {
            PoseCandidateTag = rawTag;
            if (!heightConstrained || !hasSupportHeight ||
                !StationaryPoseConfirmation.MatchesSupportHeight(rawTag.y, supportHeightY,
                    previewOriginHeight, ScannerTagOffset().y))
            {
                poseConfirmation.Clear();
                PoseCommitReason = "support_height_required";
                return false;
            }
            Vector3 proposedTag = rawTag + (hasConfirmedVisualCorrection
                ? confirmedVisualCorrection : Vector3.zero);
            Vector3 proposedOrigin = OriginForTag(proposedTag);
            if (hasMultiviewPose && !hasConfirmedScannerVisualPose &&
                StationaryPoseConfirmation.RequiresHeadingRevalidation(localPreviewPosition, proposedOrigin))
            {
                PoseRevalidationRequired = true;
                InvalidateHeading();
            }
            Vector3 confirmedRawTag;
            bool confirmed = validateFreshCycle
                ? poseConfirmation.TryAddValidatedMultiviewCycle(cycleId, rawTag, camera, Time.unscaledTime,
                    anchors, ranges, out confirmedRawTag)
                : poseConfirmation.TryAdd(cycleId, rawTag, camera, Time.unscaledTime, out confirmedRawTag);
            if (!confirmed)
            {
                PoseCommitReason = "candidate_" + poseConfirmation.Reason;
                return false;
            }
            RawUwbTagPosition = confirmedRawTag;
            if (hasConfirmedScannerVisualPose)
            {
                // The scanner's known geometry supplies an independent origin.
                // A later UWB fit may update diagnostics, never pull it back to a
                // biased range-only position while the base is declared stationary.
                confirmedVisualCorrection = currentSmoothedTagPosition - confirmedRawTag;
                VisualCorrectionMeters = confirmedVisualCorrection.magnitude;
                PoseCommitReason = "uwb_checked_visual_origin_preserved";
                PoseRevalidationRequired = false;
            }
            else
            {
                Vector3 confirmedTag = confirmedRawTag + (hasConfirmedVisualCorrection
                    ? confirmedVisualCorrection : Vector3.zero);
                CommitStationaryTag(confirmedTag, "stationary_multiview_confirmed");
            }
            poseConfirmation.Clear();
            observableCandidateCommitted = true;
            return true;
        }

        public bool TryConfirmPoseCandidate(out string message)
        {
            if (!CanConfirmPoseCandidate)
            {
                message = PoseLocalizationStatus;
                return false;
            }
            StationaryPosePinned = true;
            PoseCommitReason = "operator_confirmed_multiview";
            SetFrozenPose("frozen_operator", "eixo validado e fixado pelo usuário.");
            message = "Eixo UWB fixado. Confira sua posição no scanner e alinhe a direção manualmente antes de capturar.";
            return true;
        }

        private Vector3 OriginForTag(Vector3 tag)
        {
            float pan = scannerReceiver != null && scannerReceiver.HasFreshStatus &&
                scannerReceiver.status != null ? scannerReceiver.status.panDegrees : 0f;
            float pointYaw = pointRenderer != null ? pointRenderer.pointYawOffset : 0f;
            return PanOriginFromRotatingTag(tag, droneYawDeg + pointYaw, pan, ScannerTagOffset());
        }

        private void CommitStationaryTag(Vector3 tag, string reason)
        {
            Vector3 origin = OriginForTag(tag);
            bool invalidate = !hasMultiviewPose ||
                StationaryPoseConfirmation.RequiresHeadingRevalidation(localPreviewPosition, origin);
            if (PreviewHeadingAligned && TryCalculateScannerYawFromTag(tag,
                LastHeadingTargetWorld, ScannerTagOffset(), LastHeadingPanDegrees, out float newYaw))
                invalidate |= Mathf.Abs(Mathf.DeltaAngle(newYaw, LastHeadingYawDegrees)) > 10f;
            if (invalidate) InvalidateHeading();
            bool changed = !hasMultiviewPose || Vector3.Distance(localPreviewPosition, origin) > .001f;
            currentSmoothedTagPosition = tag;
            localPreviewPosition = droneWorldPosition = motionEstimatedWorld = origin;
            PreviewPlaced = HasPoseEstimate = isTracking = hasMultiviewPose = true;
            PoseRevalidationRequired = false;
            PoseCommitReason = reason;
            PreviewPlacementSource = hasConfirmedVisualCorrection
                ? "AR + UWB confirmado + evidência visual preservada + altura do apoio"
                : "AR + UWB confirmado em pontos de vista distintos + altura do apoio";
            if (changed) PoseRevision++;
            motionSamplesApplied++;
            if (pointCloudRootContainer != null)
                pointCloudRootContainer.SetPositionAndRotation(origin, Quaternion.Euler(0f, droneYawDeg, 0f));
        }

        private void UpdateIndependentVisualRefinement()
        {
            if (!scannerStationary || !hasMultiviewPose || !hasSupportHeight ||
                hasConfirmedScannerVisualPose || visualObservationRevision == lastVisualRefinementRevision)
                return;
            lastVisualRefinementRevision = visualObservationRevision;
            if (!visualPositionRefiner.TryRefine(RawUwbTagPosition, Time.unscaledTime,
                out Vector3 visualTag, out Vector3 surface, out float correction,
                out float baseline, out int observations))
                return; // Expiration does not erase an already confirmed correction.
            if (!visualConfirmation.TryAdd(visualObservationRevision, visualTag,
                arCameraTransform.position, Time.unscaledTime, out Vector3 confirmedTag)) return;
            confirmedTag.y = RawUwbTagPosition.y;
            confirmedVisualCorrection = confirmedTag - RawUwbTagPosition;
            hasConfirmedVisualCorrection = true;
            VisualSurfaceWorld = surface;
            VisualCorrectionMeters = confirmedVisualCorrection.magnitude;
            VisualCameraBaselineMeters = baseline;
            VisualSupportingObservations = observations;
            CommitStationaryTag(confirmedTag, "depth_refinement_confirmed");
            visualConfirmation.Clear();
        }

        public bool TryUseVisualPoseObservation()
        {
            if (!enableExperimentalVisualPose || StationaryPosePinned ||
                !scannerStationary || !hasMultiviewPose || !hasSupportHeight ||
                scannerReceiver == null || !scannerReceiver.HasUsablePanReference ||
                scannerReceiver.ScannerControlBusy || scannerReceiver.ScanStartPending ||
                scannerReceiver.status == null || scannerReceiver.isScanning || scannerReceiver.status.isScanning ||
                scannerReceiver.status.panParking || scannerReceiver.status.panMoving || ARSession.state != ARSessionState.SessionTracking ||
                (pointRenderer != null && pointRenderer.activePointsCount > 0) ||
                (PreviewHeadingAligned && HeadingSource == "manual_support")) return false;
            if (visualPoseObserver == null) visualPoseObserver = GetComponent<ScannerVisualPoseObserver>();
            if (visualPoseObserver == null || !visualPoseObserver.TryGetObservation(out ScannerVisualPoseObservation observation))
                return false;
            float age = Time.unscaledTime - observation.timeSeconds;
            Vector3 offset = ScannerTagOffset();
            if (observation.poseEpoch != AutomaticPoseEpoch || !Finite(age) || age < 0f || age > 2f ||
                observation.timeSeconds <= lastVisualGeometryObservationTime ||
                observation.supportingViews < 2 || observation.supportingObservations < 3 ||
                !Finite(observation.positionSpreadMeters) || observation.positionSpreadMeters > .05f ||
                !Finite(observation.yawSpreadDegrees) || observation.yawSpreadDegrees > 8f ||
                !Finite(observation.headYawDegrees) || !Finite(observation.observedPanDegrees) ||
                !Finite(observation.originWorld.sqrMagnitude) || !Finite(observation.tagWorld.sqrMagnitude) ||
                Mathf.Abs(Mathf.DeltaAngle(observation.observedPanDegrees, scannerReceiver.status.panDegrees)) > 2f ||
                Mathf.Abs(observation.originWorld.y - (supportHeightY + previewOriginHeight)) > .03f ||
                !StationaryPoseConfirmation.MatchesSupportHeight(observation.tagWorld.y, supportHeightY,
                    previewOriginHeight, offset.y) ||
                Vector3.Distance(observation.tagWorld, RawUwbTagPosition) > .8f) return false;
            Vector3 expectedTag = observation.originWorld +
                Quaternion.Euler(0f, observation.headYawDegrees, 0f) * offset;
            if (Vector3.Distance(observation.tagWorld, expectedTag) > .03f) return false;

            float physicalBaseYaw = Mathf.Repeat(observation.headYawDegrees - observation.observedPanDegrees, 360f);
            float pointYaw = pointRenderer != null ? pointRenderer.pointYawOffset : 0f;
            bool changed = !hasConfirmedScannerVisualPose ||
                Vector3.Distance(localPreviewPosition, observation.originWorld) > .001f ||
                Mathf.Abs(Mathf.DeltaAngle(droneYawDeg, RootYawForAlignedHeading(physicalBaseYaw, pointYaw))) > .1f;
            // Origin and heading from the same visual observation are committed
            // together, never combining a new position with a stale yaw.
            droneYawDeg = RootYawForAlignedHeading(physicalBaseYaw, pointYaw);
            currentSmoothedTagPosition = observation.tagWorld;
            localPreviewPosition = droneWorldPosition = motionEstimatedWorld = observation.originWorld;
            confirmedVisualCorrection = observation.tagWorld - RawUwbTagPosition;
            hasConfirmedVisualCorrection = hasConfirmedScannerVisualPose = true;
            PreviewHeadingAligned = PreviewPlaced = HasPoseEstimate = isTracking = true;
            PoseRevalidationRequired = false;
            PoseCommitReason = "scanner_visual_geometry_confirmed";
            HeadingSource = "visual_lidar_geometry";
            LastHeadingYawDegrees = physicalBaseYaw;
            LastHeadingPanDegrees = observation.observedPanDegrees;
            LastHeadingTargetWorld = observation.originWorld +
                Quaternion.Euler(0f, observation.headYawDegrees, 0f) * Vector3.forward;
            HeadingStatus = "Origem e direção confirmadas pela geometria visual do scanner em vários pontos de vista.";
            PreviewPlacementSource = "Geometria visual do scanner + multivista UWB + altura do apoio";
            VisualCorrectionMeters = confirmedVisualCorrection.magnitude;
            VisualSupportingObservations = observation.supportingObservations;
            VisualSurfaceWorld = observation.originWorld + Quaternion.Euler(0f, observation.headYawDegrees, 0f) *
                new Vector3(0f, ScannerDiskPoseEstimator.CoverAboveAxis, ScannerDiskPoseEstimator.CoverForward);
            VisualCameraBaselineMeters = 0f; // This observation API reports spread and view count, not baseline.
            lastVisualGeometryObservationTime = observation.timeSeconds;
            if (changed) PoseRevision++;
            poseConfirmation.Clear();
            visualConfirmation.Clear();
            if (pointCloudRootContainer != null)
                pointCloudRootContainer.SetPositionAndRotation(localPreviewPosition, Quaternion.Euler(0f, droneYawDeg, 0f));
            return true;
        }

        private void UpdateAutomaticHeading()
        {
            if (enableExperimentalVisualPose && autoUwbPositioning && !localPreviewWithoutUwb)
            {
                if (!PreviewHeadingAligned && hasMultiviewPose && !PoseRevalidationRequired)
                {
                    if (!TryUseVisualPoseObservation()) HeadingStatus = visualPoseObserver != null
                        ? visualPoseObserver.Status : "Aguardando observação da frente física do scanner.";
                }
                // A free table point has no observable relationship to the
                // scanner's front. Keep that choice available only to the user.
                return;
            }
            if (!autoSupportHeadingFallback)
            {
                if (autoAlignHeading && !PreviewHeadingAligned)
                    HeadingStatus = "Aponte um ponto livre do apoio na direção física eixo → LiDAR e use o alinhamento manual.";
                return;
            }
            if (!autoAlignHeading || PreviewHeadingAligned || !scannerStationary ||
                !HasPoseEstimate || ARSession.state != ARSessionState.SessionTracking ||
                scannerReceiver == null || !scannerReceiver.HasFreshStatus ||
                !scannerReceiver.HasUsablePanReference || scannerReceiver.ScannerControlBusy ||
                scannerReceiver.status == null || scannerReceiver.isScanning ||
                scannerReceiver.status.isScanning || scannerReceiver.status.panParking || scannerReceiver.status.panMoving ||
                raycasts == null || arCameraTransform == null ||
                (!localPreviewWithoutUwb && (!hasMultiviewPose || !hasSupportHeight)))
            {
                headingCandidateSince = -1f;
                headingPhonePauseGate.Clear();
                return;
            }
            if (!TryGetClearSupportTarget(out Vector3 target, out string targetStatus))
            {
                headingCandidateSince = -1f;
                HeadingStatus = targetStatus;
                return;
            }
            Vector3 horizontal = target - localPreviewPosition;
            horizontal.y = 0f;
            float pan = scannerReceiver.status.panDegrees;
            float candidateYaw;
            bool candidateValid = localPreviewWithoutUwb
                ? TryCalculateScannerYaw(localPreviewPosition, target, pan, out candidateYaw)
                : TryCalculateScannerYawFromTag(currentSmoothedTagPosition, target,
                    ScannerTagOffset(), pan, out candidateYaw);
            if (!candidateValid) { headingCandidateSince = -1f; HeadingStatus =
                "A direção para esse ponto ainda é ambígua."; return; }

            float now = Time.unscaledTime;
            bool phoneStable = headingPhonePauseGate.Update(arCameraTransform.position,
                arCameraTransform.rotation, now);
            if (headingCandidateSince < 0f ||
                Mathf.Abs(Mathf.DeltaAngle(candidateYaw, headingCandidateYaw)) > 3f ||
                Vector3.Distance(localPreviewPosition, headingCandidateOrigin) > .05f)
            {
                headingCandidateYaw = candidateYaw;
                headingCandidateOrigin = localPreviewPosition;
                headingCandidateSince = now;
            }
            if (!phoneStable || now - headingCandidateSince < 1f)
            {
                HeadingStatus = "Ponto válido; mantenha o celular imóvel por um segundo.";
                return;
            }
            if (AlignPreviewForwardAtScreenCenter(pan, out string message, true))
                AutoUwbStatus = "Direção alinhada automaticamente. " + AutoUwbStatus;
            else Debug.Log("[UWB Spatial] Alinhamento automático adiado: " + message);
            headingCandidateSince = -1f;
        }

        private void Update()
        {
            if (autoUwbPositioning && !localPreviewWithoutUwb && uwbReceiver != null &&
                uwbReceiver.transportMode == UwbTransportMode.USB)
            {
                UpdateAutomaticUwb();
                UpdateAutomaticHeading();
                return;
            }
            if (uwbMotionTestMode)
            {
                UpdateUwbMotionTest();
                return;
            }
            if (localPreviewWithoutUwb && uwbReceiver != null && uwbReceiver.transportMode != UwbTransportMode.Simulated)
            {
                isTracking = false;
                hasPosition = false;
                if (previewAnchor != null)
                {
                    Pose current = new Pose(previewAnchor.transform.position, previewAnchor.transform.rotation);
                    var cloud = GetComponent<ArScanner.Rendering.ThermalPointCloudRenderer>();
                    if (cloud == null || cloud.RebaseWorldPoints(lastPreviewAnchorPose, current))
                    {
                        droneYawDeg = Mathf.Repeat(droneYawDeg +
                            Mathf.DeltaAngle(lastPreviewAnchorPose.rotation.eulerAngles.y,
                                current.rotation.eulerAngles.y), 360f);
                        lastPreviewAnchorPose = current;
                    }
                    localPreviewPosition = current.position + Vector3.up * previewOriginHeight;
                }
                pointCloudRootContainer.SetPositionAndRotation(localPreviewPosition, Quaternion.Euler(0f, droneYawDeg, 0f));
                UpdateMotionCalibration();
                UpdateContinuousUwbComparison();
                UpdateAutomaticHeading();
                return;
            }
            if (uwbReceiver == null || !uwbReceiver.IsConnected)
            {
                isTracking = false;
                hasPosition = false;
                return;
            }
            // A repeated range must never move with the current phone pose.
            // This still uses reception time; motion-grade acquisition needs clock synchronization.
            if (hasPosition && lastSampleId == uwbReceiver.AppliedSampleId) return;
            lastSampleId = uwbReceiver.AppliedSampleId;

            Vector3 rawPos = uwbReceiver.LatestPosition;

            // Filtro de deadband para ruído estático de rádio ToF
            rawPreviousPosition = rawPos;

            // Aplica rotação de calibração da base (Yaw)
            Quaternion baseRotation = Quaternion.Euler(0f, yawOffsetDegrees, 0f) * Quaternion.Euler(baseRotationEuler);
            Vector3 calibratedPos = baseRotation * rawPreviousPosition + baseOriginOffset;

            // Suavização via Lerp
            // Primeiro transforma para o mundo; suavizar no referencial móvel do
            // telefone faria o filtro atrasar a compensação do movimento da câmera.
            Vector3 measuredWorldPosition = calibratedPos;
            if (anchorMountedOnPhone)
            {
                if (arCameraTransform == null) { isTracking = false; return; }
                measuredWorldPosition = arCameraTransform.TransformPoint(calibratedPos + uwbToCameraOffset);
            }
            // Filter in world space only; no extra frame-rate-dependent lag atop the base filter.
            if (!hasPosition || Vector3.Distance(droneWorldPosition, measuredWorldPosition) > positionDeadband)
                droneWorldPosition = measuredWorldPosition;
            currentSmoothedTagPosition = calibratedPos;
            hasPosition = true;

            isTracking = true;

            if (pointCloudRootContainer != null)
            {
                pointCloudRootContainer.position = droneWorldPosition;
                // Pitch/roll já foram aplicados pelo firmware. A rotação do celular
                // não mede a orientação do scanner. Yaw permanece uma calibração
                // manual no mundo AR até existir uma fonte de heading no protocolo.
                pointCloudRootContainer.rotation = Quaternion.Euler(0f, droneYawDeg, 0f);
            }
        }

        private void LateUpdate()
        {
            if (ARSession.state != ARSessionState.SessionTracking || arCameraTransform == null)
            {
                if (arPoseHistory.TryGetBounds(out _, out _))
                {
                    AutomaticPoseEpoch++;
                    PoseRevision++;
                    InvalidateHeading();
                }
                arPoseHistory.Clear();
                untimedRangePauseGate.Clear();
                untimedPhoneStable = false;
                return;
            }
            double now = Time.realtimeSinceStartupAsDouble;
            Pose cameraPose = new Pose(arCameraTransform.position, arCameraTransform.rotation);
            long historyRevision = arPoseHistory.EpochRevision;
            arPoseHistory.Add(now, cameraPose);
            if (historyRevision != arPoseHistory.EpochRevision &&
                arPoseHistory.LastResetReason == "spatial_discontinuity")
            {
                ResetAutomaticHistory(true);
                InvalidateHeading();
                if (autoUwbPositioning && !localPreviewWithoutUwb) SuspendAutomaticPose();
                arPoseHistory.Add(now, cameraPose);
                RangePoseStatus = "Referencial AR mudou; confirme novamente posição e direção do scanner.";
                SetRangeDecision("ar_spatial_discontinuity");
            }
            untimedPhoneStable = untimedRangePauseGate.Update(arCameraTransform.position,
                arCameraTransform.rotation, Time.unscaledTime);
        }

        private void UpdateAutomaticUwb()
        {
            FrozenPoseReason = "";
            // Validate the AR frame before any frozen-pose early return. A pose
            // from a lost/relocalized session cannot authorize new points.
            if (ARSession.state != ARSessionState.SessionTracking || !GetMotionAnchors(motionAnchors))
            {
                SuspendAutomaticPose();
                ResetAutomaticHistory();
                PreviewHeadingAligned = false;
                CurrentUwbQuality = UwbInstantQuality.Unavailable;
                AutoUwbStatus = "Aguardando rastreamento AR; será necessário realinhar a direção.";
                SetRangeDecision("tracking_unavailable");
                return;
            }
            if (scannerReceiver == null) scannerReceiver = FindFirstObjectByType<PointCloudTcpReceiver>();
            if (scannerReceiver == null || !scannerReceiver.HasFreshStatus || scannerReceiver.status == null ||
                !Finite(scannerReceiver.status.panDegrees))
            {
                // Losing an HTTP response does not move the stationary scanner
                // or invalidate its AR frame. Keep the measurements and any
                // fixed origin, but process no range cycles until status returns.
                // Acquisition and fixation still require HasUsablePanReference,
                // which itself requires a fresh scanner status.
                if (scannerStationary && hasObservedPanStatus && scannerReceiver != null &&
                    scannerReceiver.status != null && Finite(scannerReceiver.status.panDegrees))
                {
                    panStatusWasUnavailable = true;
                    CurrentUwbQuality = UwbInstantQuality.Unavailable;
                    AutoUwbStatus = "Aguardando status recente do pan. Posição e leituras preservadas; captura pausada até a conexão responder.";
                    SetRangeDecision("pan_status_waiting_preserved");
                    return;
                }
                SuspendAutomaticPose();
                ResetAutomaticHistory();
                CurrentUwbQuality = UwbInstantQuality.Unavailable;
                PreviewHeadingAligned = false;
                AutoUwbStatus = "Aguardando ângulo recente do pan para converter a posição da tag em eixo.";
                SetRangeDecision("pan_status_unavailable");
                return;
            }
            float observedPan = scannerReceiver.status.panDegrees;
            bool panChangedDuringGap = panStatusWasUnavailable && hasObservedPanStatus &&
                Mathf.Abs(Mathf.DeltaAngle(lastObservedPanDegrees, observedPan)) > 2f;
            panStatusWasUnavailable = false;
            if (panChangedDuringGap && !StationaryPosePinned &&
                (pointRenderer == null || pointRenderer.activePointsCount == 0))
            {
                // The tag is attached to the moving head. Unfixed multiview
                // ranges from before an unobserved pan change describe another
                // tag position; they must not be combined with the new cycles.
                SuspendAutomaticPose();
                ResetAutomaticHistory(true);
                InvalidateHeading();
                hasObservedPanStatus = true;
                lastObservedPanDegrees = observedPan;
                AutoUwbStatus = "O pan mudou durante a ausência de status. Refaça as pausas para localizar a tag na posição atual.";
                SetRangeDecision("pan_changed_after_status_gap");
                return;
            }
            hasObservedPanStatus = true;
            lastObservedPanDegrees = observedPan;
            if (!scannerReceiver.HasUsablePanReference && PreviewHeadingAligned)
                InvalidateHeading();
            bool panMoving = scannerReceiver != null &&
                (scannerReceiver.ScannerControlBusy || scannerReceiver.ScanStartPending || scannerReceiver.isScanning ||
                    (scannerReceiver.HasFreshStatus && scannerReceiver.status != null &&
                        (scannerReceiver.status.isScanning || scannerReceiver.status.panParking || scannerReceiver.status.panMoving)));
            if (panMoving)
            {
                if (!multiviewPausedForPan)
                {
                    multiviewEstimator.Clear();
                    phonePauseGate.Clear();
                    multiviewPausedForPan = true;
                }
                if (scannerStationary && HasPoseEstimate)
                {
                    CurrentUwbQuality = UwbInstantQuality.LowConfidence;
                    AutoUwbStatus = "AR + UWB: eixo congelado durante o giro do scanner.";
                    SetFrozenPose("frozen_pan", "movimento ou comando do scanner em andamento.");
                    return;
                }
                SuspendAutomaticPose();
                CurrentUwbQuality = UwbInstantQuality.Unavailable;
                AutoUwbStatus = "UWB: pare o pan para localizar; seu ângulo ainda não está sincronizado com os alcances.";
                SetFrozenPose("frozen_pan_without_pose", "movimento ou comando do scanner em andamento.");
                return;
            }
            if (scannerStationary && pointRenderer != null &&
                pointRenderer.activePointsCount > 0)
            {
                CurrentUwbQuality = UwbInstantQuality.LowConfidence;
                AutoUwbStatus = "AR + UWB: eixo congelado para preservar os pontos capturados. " +
                    "Limpe a nuvem e refaça a localização para mudar a origem.";
                SetFrozenPose("frozen_cloud", "origem preservada enquanto há pontos capturados.");
                return;
            }
            if (scannerStationary && StationaryPosePinned && HasHeightConsistentPose)
            {
                CurrentUwbQuality = UwbInstantQuality.LowConfidence;
                AutoUwbStatus = "Eixo UWB fixado pelo usuário. Retome a localização para calcular outra origem.";
                SetFrozenPose("frozen_operator", "eixo validado e fixado pelo usuário.");
                return;
            }
            bool supportReady = UpdateAutomaticSupportHeight();
            TryUseVisualPoseObservation();
            UpdateIndependentVisualRefinement();
            if (multiviewPausedForPan)
            {
                multiviewPausedForPan = false;
                instantEstimator.Clear();
            }
            if (uwbReceiver == null || !uwbReceiver.HasFreshThreeRanges)
            {
                if (scannerStationary && hasMultiviewPose)
                {
                    CurrentUwbQuality = UwbInstantQuality.LowConfidence;
                    AutoUwbStatus = "AR + UWB: eixo congelado; aguardando novas distâncias.";
                    SetFrozenPose("frozen_waiting_ranges", "aguardando novas distâncias; pose confirmada preservada.");
                    return;
                }
                SuspendAutomaticPose();
                LastCorrectedUwbRanges = Vector3.zero;
                CurrentUwbQuality = UwbInstantQuality.Unavailable;
                AutoUwbStatus = "UWB USB: aguardando três distâncias recentes do scanner.";
                SetRangeDecision("ranges_unavailable");
                return;
            }
            if (!HasSavedUwbCalibration)
            {
                SuspendAutomaticPose();
                var raw = uwbReceiver.Diagnostics;
                LastCorrectedUwbRanges = new Vector3(raw.d1, raw.d2, raw.d3);
                CurrentUwbQuality = UwbInstantQuality.Unavailable;
                AutoUwbStatus = "UWB: três distâncias brutas recebidas, mas sem perfil de alcance validado. Marque o scanner no AR para escanear parado.";
                SetRangeDecision("calibration_required");
                return;
            }
            phonePausedForRanges = phonePauseGate.Update(arCameraTransform.position,
                arCameraTransform.rotation, Time.unscaledTime);
            if (lastAutoDiagnosticsId == uwbReceiver.AppliedDiagnosticsId) return;
            BeginRangeQuery();
            if (!TryGetRangeAnchors(motionAnchors, out Pose rangeCameraPose,
                out bool stationaryDuringCycle))
            {
                if (!hasMultiviewPose && RangeDecision != "pending_future_pose") SuspendAutomaticPose();
                CurrentUwbQuality = UwbInstantQuality.Unavailable;
                AutoUwbStatus = RangePoseStatus;
                return;
            }
            lastAutoDiagnosticsId = uwbReceiver.AppliedDiagnosticsId;
            MarkRangeProcessed();
            var ranges = uwbReceiver.Diagnostics;
            autoRawRanges[0] = ranges.d1;
            autoRawRanges[1] = ranges.d2;
            autoRawRanges[2] = ranges.d3;
            float elapsed = lastMotionSampleTime <= 0f ? .1f :
                Mathf.Clamp(Time.unscaledTime-lastMotionSampleTime,.01f,.5f);
            lastMotionSampleTime = Time.unscaledTime;
            bool solved = instantEstimator.TryUpdate(motionAnchors,autoRawRanges,autoRangeScales,
                autoRangeOffsets,rangeCameraPose.position,
                rangeCameraPose.rotation * Vector3.forward,
                elapsed,out UwbInstantPoseEstimate estimate);
            LastCorrectedUwbRanges = estimate.correctedRanges;

            // One trinca is poorly conditioned at room distances. When the
            // scanner is stationary, combine cycles taken from distinct AR
            // camera positions so the phone movement supplies the baseline.
            bool added = scannerStationary && phonePausedForRanges && stationaryDuringCycle &&
                multiviewEstimator.AddSample(motionAnchors, estimate.correctedRanges);
            if (added)
                multiviewSamplesAccepted++;
            // Fresh cycles validate an already observable multiview candidate
            // even when the four-sample-per-pause solver history is full.
            Vector3 multiviewTag = Vector3.zero;
            float multiviewResidual = float.PositiveInfinity, multiviewSigma = float.PositiveInfinity;
            float phoneBaseline = 0f;
            int multiviewInliers = 0;
            float tagHeight = supportHeightY + previewOriginHeight + ScannerTagOffset().y;
            bool candidateExpired = hasObservableStationaryCandidate &&
                Time.unscaledTime - observableCandidateTime > 20f;
            if (candidateExpired)
            {
                hasObservableStationaryCandidate = observableCandidateCommitted = false;
                poseConfirmation.Clear();
            }
            bool solveAtHeight = supportReady && (added || candidateExpired ||
                (!Finite(lastSolvedSupportHeight) && multiviewEstimator.SampleCount >= 8) ||
                (Finite(lastSolvedSupportHeight) && Mathf.Abs(tagHeight - lastSolvedSupportHeight) > .002f));
            if (solveAtHeight) lastSolvedSupportHeight = tagHeight;
            bool heightConstrained = solveAtHeight &&
                multiviewEstimator.TryEstimateAtHeight(
                    tagHeight,
                    out multiviewTag, out multiviewResidual, out multiviewSigma,
                    out phoneBaseline, out multiviewInliers);
            // Once a support height is available, a free 3D solve cannot replace
            // a height-constrained pose or authorize capture at another height.
            bool multiviewAccepted = heightConstrained || (added && !supportReady && !hasSupportHeight &&
                multiviewEstimator.TryEstimate(out multiviewTag, out multiviewResidual,
                    out multiviewSigma, out phoneBaseline, out multiviewInliers));
            if (multiviewAccepted)
            {
                CurrentUwbQuality = UwbInstantQuality.Experimental;
                motionResidualMeters = multiviewResidual;
                motionGeometrySigmaMeters = multiviewSigma;
                if (heightConstrained)
                    CacheObservableStationaryCandidate(multiviewTag, uwbReceiver.AppliedDiagnosticsId);
                else
                {
                    ConsiderStationaryCandidate(multiviewTag, false, rangeCameraPose.position, uwbReceiver.AppliedDiagnosticsId);
                    AutoUwbStatus = "Posição multivista candidata; enquadre o scanner para confirmar seu apoio antes de capturar.";
                    return;
                }
            }
            if (hasObservableStationaryCandidate && !observableCandidateCommitted)
            {
                bool confirmed = scannerStationary && supportReady && phonePausedForRanges && stationaryDuringCycle &&
                    uwbReceiver.AppliedDiagnosticsId > observableCandidateCycleId &&
                    ConsiderValidatedRangeCandidate(observableStationaryCandidate, rangeCameraPose.position,
                        uwbReceiver.AppliedDiagnosticsId, motionAnchors, estimate.correctedRanges);
                AutoUwbStatus = confirmed
                    ? "AR + UWB + apoio: eixo confirmado pela geometria multivista e novos ciclos de distâncias. " +
                      "Confira a posição, fixe o eixo e alinhe a direção manualmente."
                    : PoseLocalizationStatus;
                if (!confirmed && PoseCommitReason == "candidate_incompatible_cycle")
                    AutoUwbStatus += " A última trinca divergiu da candidata e não contou para a confirmação.";
                return;
            }

            if (scannerStationary && hasMultiviewPose)
            {
                CurrentUwbQuality = UwbInstantQuality.LowConfidence;
                AutoUwbStatus = $"AR + UWB: eixo congelado; reunindo novas leituras em posições AR " +
                    $"distintas ({multiviewEstimator.SampleCount} amostras).";
                return;
            }

            if (!solved)
            {
                SuspendAutomaticPose();
                instantEstimator.Clear();
                motionSamplesRejected++;
                CurrentUwbQuality = estimate.quality;
                AutoUwbStatus = MultiviewProgressMessage();
                if (phonePausedForRanges && multiviewEstimator.LastSampleResult == "pairwise")
                    AutoUwbStatus += " A última leitura foi descartada: distâncias incompatíveis.";
                return;
            }

            // A single coplanar trinca is often poorly conditioned even when
            // it is physically consistent.  Let AR choose the branch and use
            // the UWB ranges to constrain it, then expose the uncertainty to
            // the operator instead of hiding the axis altogether.
            if (allowApproximateUwbPose && !IsReliableAutomaticPose(estimate) &&
                IsApproximateAutomaticPose(estimate,
                approximateGeometryLimit, approximateResidualLimit))
            {
                if (scannerReceiver == null) scannerReceiver = FindFirstObjectByType<PointCloudTcpReceiver>();
                float approximatePan = scannerReceiver != null && scannerReceiver.HasFreshStatus &&
                    scannerReceiver.status != null ? scannerReceiver.status.panDegrees : 0f;
                float approximateYaw = pointRenderer != null ? pointRenderer.pointYawOffset : 0f;
                Vector3 approximateOrigin = PanOriginFromRotatingTag(estimate.scannerWorld,
                    droneYawDeg + approximateYaw, approximatePan, ScannerTagOffset());
                CurrentUwbQuality = UwbInstantQuality.LowConfidence;
                motionResidualMeters = estimate.rmsResidualMeters;
                motionGeometrySigmaMeters = estimate.geometrySigmaMeters;
                motionEstimatedWorld = approximateOrigin;
                motionSamplesApplied++;
                localPreviewPosition = approximateOrigin;
                droneWorldPosition = approximateOrigin;
                currentSmoothedTagPosition = estimate.scannerWorld;
                PreviewPlaced = HasPoseEstimate = true;
                PreviewPlacementSource = "AR + UWB aproximado + deslocamento da tag ao eixo";
                isTracking = true;
                // Provisional position must keep updating. Only a successful
                // stationary multiview solve may set hasMultiviewPose.
                AutoUwbStatus = $"AR + UWB aproximado: eixo visível; erro {motionResidualMeters:F2} m; " +
                    $"incerteza ≈{motionGeometrySigmaMeters:F2} m. " +
                    (scannerStationary ? "Mova o celular para refinar." : "Movimento experimental; captura suspensa.");
                pointCloudRootContainer.SetPositionAndRotation(approximateOrigin,
                    Quaternion.Euler(0f,droneYawDeg,0f));
                return;
            }
            CurrentUwbQuality = estimate.quality;
            motionResidualMeters = estimate.rmsResidualMeters;
            motionGeometrySigmaMeters = estimate.geometrySigmaMeters;
            if (!IsReliableAutomaticPose(estimate))
            {
                SuspendAutomaticPose();
                instantEstimator.Clear();
                motionSamplesRejected++;
                AutoUwbStatus = MultiviewProgressMessage();
                return;
            }
            if (scannerReceiver == null) scannerReceiver = FindFirstObjectByType<PointCloudTcpReceiver>();
            float panDegrees = scannerReceiver != null && scannerReceiver.HasFreshStatus &&
                scannerReceiver.status != null ? scannerReceiver.status.panDegrees : 0f;
            float pointYaw = pointRenderer != null ? pointRenderer.pointYawOffset : 0f;
            Vector3 panOrigin = PanOriginFromRotatingTag(estimate.scannerWorld,
                droneYawDeg + pointYaw, panDegrees, ScannerTagOffset());
            motionEstimatedWorld = panOrigin;
            motionSamplesApplied++;
            localPreviewPosition = panOrigin;
            droneWorldPosition = panOrigin;
            currentSmoothedTagPosition = estimate.scannerWorld;
            PreviewPlaced = HasPoseEstimate = true;
            PreviewPlacementSource = "três distâncias UWB + deslocamento da tag ao eixo";
            isTracking = true;
            string calibration = HasSavedUwbCalibration ? "perfil salvo" : "sem perfil";
            AutoUwbStatus = $"UWB USB: pose experimental; {calibration}; erro {motionResidualMeters:F2} m; " +
                $"incerteza geométrica ≈{motionGeometrySigmaMeters:F1} m.";
            pointCloudRootContainer.SetPositionAndRotation(panOrigin,
                Quaternion.Euler(0f,droneYawDeg,0f));
        }

        public static bool IsReliableAutomaticPose(UwbInstantPoseEstimate estimate)
        {
            return estimate.quality == UwbInstantQuality.Experimental &&
                !float.IsNaN(estimate.geometrySigmaMeters) &&
                !float.IsInfinity(estimate.geometrySigmaMeters) &&
                estimate.geometrySigmaMeters <= .5f;
        }

        public static bool IsApproximateAutomaticPose(UwbInstantPoseEstimate estimate,
            float geometryLimit, float residualLimit)
        {
            return estimate.quality != UwbInstantQuality.Unavailable &&
                estimate.quality != UwbInstantQuality.IncompatibleRanges &&
                estimate.quality != UwbInstantQuality.Ambiguous &&
                Finite(estimate.geometrySigmaMeters) && Finite(estimate.rmsResidualMeters) &&
                estimate.geometrySigmaMeters <= geometryLimit &&
                estimate.rmsResidualMeters <= residualLimit;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private string MultiviewProgressMessage() => phonePausedForRanges
            ? multiviewEstimator.ProgressMessage
            : $"Pare o celular por 2 s nesta posição para medir. {multiviewEstimator.SampleCount} leituras coletadas; varie também a altura e o lado.";

        private void SuspendAutomaticPose()
        {
            isTracking = false;
            HasPoseEstimate = false;
            PreviewPlaced = false;
            hasMultiviewPose = false;
        }

        public static Vector3 PanOriginFromRotatingTag(Vector3 tagWorld, float baseYawDegrees,
            float panDegrees, Vector3 tagOffsetFromPanAxis)
        {
            return tagWorld - Quaternion.Euler(0f,baseYawDegrees + panDegrees,0f) *
                tagOffsetFromPanAxis;
        }

        private Vector3 ScannerTagOffset()
        {
            float[] offset = scannerReceiver != null && scannerReceiver.geometry != null
                ? scannerReceiver.geometry.tagPhysicalOffset : null;
            if (offset != null && offset.Length == 3)
            {
                var measured = new Vector3(offset[0],offset[1],offset[2]);
                if (!float.IsNaN(measured.sqrMagnitude) &&
                    !float.IsInfinity(measured.sqrMagnitude) && measured.magnitude < .5f)
                    return measured;
            }
            return scannerTagOffsetFromPanAxis;
        }

        public void UpdateDroneAttitude(float pitch, float roll, float yaw)
        {
            dronePitchDeg = pitch;
            droneRollDeg = roll;
            droneYawDeg = yaw;
        }

        private void OnDestroy()
        {
            if (scannerReceiver != null) scannerReceiver.PanZeroReferenceConfirmed -= OnPanZeroReferenceConfirmed;
            if (previewAnchor != null && anchorManager != null) anchorManager.TryRemoveAnchor(previewAnchor);
            if (ownsRoot && pointCloudRootContainer != null) Destroy(pointCloudRootContainer.gameObject);
        }
    }
}
