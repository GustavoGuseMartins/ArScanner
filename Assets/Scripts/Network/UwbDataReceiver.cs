using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;
using System.Globalization;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Debug = UnityEngine.Debug;
using ArScanner.Spatial;

namespace ArScanner.Network
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct UwbPositionData
    {
        public float tagX;
        public float tagY;
        public float tagZ;
        public float distAnchor1;
        public float distAnchor2;
        public float distAnchor3;
        public uint timestampMs;
    }

    // USB is the normal path from the three radios mounted on the phone.
    // Wi-Fi remains an explicit diagnostic option, not an invisible fallback.
    public enum UwbTransportMode { USB = 0, UDP = 1, TCP = 2, Simulated = 3 }

    [DefaultExecutionOrder(-125)]
    public class UwbDataReceiver : MonoBehaviour
    {
        [Header("Modo de Transporte UWB")]
        [Tooltip("Modo de recepção dos dados ToF da base UWB")]
        public UwbTransportMode transportMode = UwbTransportMode.USB;

        [Header("Configuração UDP (Padrão Android / Wi-Fi)")]
        [Tooltip("Porta UDP na qual a base UWB ou bridge transmite os pacotes UwbPositionPacket")]
        public int udpPort = 9999;

        [Header("Configuração TCP (Opcional)")]
        public string tcpHost = "192.168.4.2";
        public int tcpPort = 9999;
        [Tooltip("Tempo sem posição válida até indicar perda de rastreamento")]
        [Min(0.1f)] public float dataTimeoutSeconds = 0.25f;

        [Header("Posição e Diagnóstico")]
        public Vector3 LatestPosition = Vector3.zero;
        public float DistanceAnchor1;
        public float DistanceAnchor2;
        public float DistanceAnchor3;
        public bool IsConnected;
        public long PacketsReceived;
        public uint LatestTimestampMs { get; private set; }
        public long AppliedSampleId { get; private set; }
        [Serializable] public class BaseDiagnostics
        {
            public string kind;
            public int version, radioMask, state, s1, s2, s3;
            public uint timestampMs;
            public float d1, d2, d3, sigma;
            public bool cycleValid;
            public uint t1Ms, t2Ms, t3Ms;
        }
        public BaseDiagnostics Diagnostics { get; private set; }
        public bool HasFreshDiagnostics { get; private set; }
        public string UwbDiagnosticCsvPath { get; private set; }
        public long AppliedDiagnosticsId { get; private set; }
        public bool HasLatestRangeTimes { get; private set; }
        public float LatestRangeTimingExcessDelaySeconds { get; private set; } = float.PositiveInfinity;
        private double latestRangeTime1Seconds, latestRangeTime2Seconds, latestRangeTime3Seconds;
        public bool TryGetLatestRangeTimes(out double time1Seconds,
            out double time2Seconds, out double time3Seconds)
        {
            time1Seconds = latestRangeTime1Seconds;
            time2Seconds = latestRangeTime2Seconds;
            time3Seconds = latestRangeTime3Seconds;
            return HasLatestRangeTimes && HasFreshThreeRanges;
        }
        public float DiagnosticsAgeSeconds => Diagnostics == null ? float.PositiveInfinity :
            (float)((Stopwatch.GetTimestamp()-lastDiagnosticsTicks)/(double)Stopwatch.Frequency);
        public bool IsBaseOnline => IsConnected || HasFreshDiagnostics;
        public bool HasFreshThreeRanges => HasFreshDiagnostics && DiagnosticsAgeSeconds < 1f &&
            Diagnostics != null && Diagnostics.radioMask == 7 &&
            IsCoherentRangeCycle(Diagnostics) &&
            IsFinite(Diagnostics.d1) && IsFinite(Diagnostics.d2) && IsFinite(Diagnostics.d3) &&
            Diagnostics.d1 > .08f && Diagnostics.d2 > .08f && Diagnostics.d3 > .08f &&
            Diagnostics.d1 <= 35f && Diagnostics.d2 <= 35f && Diagnostics.d3 <= 35f;
        public static bool IsCoherentRangeCycle(BaseDiagnostics d)
        {
            if (d == null || d.s1 != 0 || d.s2 != 0 || d.s3 != 0) return false;
            // Legacy firmware exposes completion through state; v2 also validates
            // sequential acquisition span, including uint32 millis() wraparound.
            if (d.version == 1) return d.state >= 2 && d.state <= 4;
            return d.version == 2 && d.cycleValid &&
                unchecked(d.t2Ms - d.t1Ms) <= 100u &&
                unchecked(d.t3Ms - d.t2Ms) <= 100u &&
                unchecked(d.t3Ms - d.t1Ms) <= 100u &&
                unchecked(d.timestampMs - d.t3Ms) <= 250u;
        }
        public string BaseStatus
        {
            get
            {
                string transport = transportMode == UwbTransportMode.USB ? "USB" : "Wi-Fi";
                if (!IsBaseOnline) return usbDevicePresent ? usbStatus : "Sem dados da base (USB/Wi-Fi)";
                if (!HasFreshDiagnostics) return $"{transport}: posição recebida; base sem diagnóstico";
                if (Diagnostics.state == 0) return $"{transport}: rádios SPI {Diagnostics.radioMask}/7 (máscara)";
                if (Diagnostics.state == 1) return $"{transport}: TWR incompleto [{Diagnostics.s1},{Diagnostics.s2},{Diagnostics.s3}]";
                if (Diagnostics.state == 2) return HasFreshThreeRanges
                    ? $"{transport}: três alcances recebidos; solução instantânea inconsistente"
                    : $"{transport}: alcances parciais/inconsistentes";
                if (Diagnostics.state == 3) return $"{transport}: três alcances recebidos; posição instantânea incerta ({Diagnostics.sigma:F2} m)";
                return IsConnected ? $"{transport}: posição experimental" : $"{transport}: posição expirou";
            }
        }
        public bool UsbHasRecentData =>
            (Stopwatch.GetTimestamp() - Interlocked.Read(ref lastUsbDataTicks)) /
                (double)Stopwatch.Frequency < 2.0;
        public string UsbStatus => usbStatus;
        public static string StageName(int stage)
        {
            switch (stage)
            {
                case 0: return "distância OK";
                case 1: return "rádio ausente";
                case 2: return "falha TX poll";
                case 3: return "sem resposta da tag";
                case 4: return "resposta incompatível";
                case 5: return "falha TX final";
                case 6: return "tempo TX divergente";
                case 7: return "sem relatório válido";
                default: return "etapa desconhecida";
            }
        }
        private string pendingDiagnostics;
        private long pendingDiagnosticsTicks;
        private long lastDiagnosticsTicks;
        private readonly UwbDeviceClockMapper diagnosticsClock = new UwbDeviceClockMapper();
        private StreamWriter uwbDiagnosticCsv;
        private int unflushedDiagnosticRows;

        private const int PacketSize = 28;
        private readonly object receiverLock = new object();
        private UdpClient udpClient;
        private TcpClient tcpClient;
        private CancellationTokenSource receiverCancellation;
        private UwbPositionData pendingPosition;
        private bool hasPendingPosition;
        private bool hasReceivedPacket;
        private long lastPacketTicks;
        private uint lastDeviceTimestamp;
        private bool started;
        private AndroidJavaObject usbSerial;
        private long lastUsbDataTicks;
        private float nextUsbCheckTime;
        private bool usbDevicePresent;
        private string usbStatus = "USB: base não conectada ao celular";
        private BaseDiagnostics diagnosticForLateUpdate;
        private UwbAnchorManager diagnosticPlacement;
        private PointCloudTcpReceiver diagnosticScanner;
        private ScannerVisualPoseObserver diagnosticVision;
        private long loggedDecisionRevision = -1, loggedPoseRevision = -1, loggedPoseEpoch = -1;
        private readonly Vector3[] diagnosticAnchors = new Vector3[3];

        private void Start()
        {
            started = true;
            StartReceiver();
        }

        private void OnEnable()
        {
            if (started) StartReceiver();
        }

        private void Update()
        {
            if (transportMode == UwbTransportMode.USB) PollUsb();
            // A thread de rede só entrega snapshots; Unity/HUD leem uma posição completa no main thread.
            BaseDiagnostics newDiagnostic = null;
            lock (receiverLock)
            {
                if (pendingDiagnostics != null)
                {
                    long receivedTicks = pendingDiagnosticsTicks;
                    try
                    {
                        var d = JsonUtility.FromJson<BaseDiagnostics>(pendingDiagnostics);
                        if (d != null && d.kind == "uwb_status" && (d.version == 1 || d.version == 2) &&
                            d.state >= 0 && d.state <= 4 && d.radioMask >= 0 && d.radioMask <= 7 &&
                            IsFinite(d.d1) && IsFinite(d.d2) && IsFinite(d.d3) && IsFinite(d.sigma))
                        {
                            Diagnostics = d;
                            lastDiagnosticsTicks = Stopwatch.GetTimestamp();
                            ++AppliedDiagnosticsId;
                            newDiagnostic = d;
                            UpdateRangeTimes(d, receivedTicks);
                        }
                    }
                    catch (ArgumentException) { }
                    pendingDiagnostics = null;
                    pendingDiagnosticsTicks = 0;
                }
                HasFreshDiagnostics = Diagnostics != null &&
                    (Stopwatch.GetTimestamp()-lastDiagnosticsTicks)/(double)Stopwatch.Frequency < 3;
                if (hasPendingPosition)
                {
                    ApplyPosition(pendingPosition);
                    hasPendingPosition = false;
                }
                float timeout = IsFinite(dataTimeoutSeconds) ? Math.Max(0.1f, dataTimeoutSeconds) : 2f;
                IsConnected = hasReceivedPacket &&
                    (Stopwatch.GetTimestamp() - lastPacketTicks) / (double)Stopwatch.Frequency < timeout;
            }
            if (newDiagnostic != null && transportMode == UwbTransportMode.USB)
                diagnosticForLateUpdate = newDiagnostic;
        }

        private void UpdateRangeTimes(BaseDiagnostics diagnostic, long receivedTicks)
        {
            HasLatestRangeTimes = false;
            LatestRangeTimingExcessDelaySeconds = float.PositiveInfinity;
            if (diagnostic.version != 2 || !IsCoherentRangeCycle(diagnostic) ||
                receivedTicks <= 0) return;

            long nowTicks = Stopwatch.GetTimestamp();
            double arrivalSeconds = Time.realtimeSinceStartupAsDouble -
                (nowTicks - receivedTicks) / (double)Stopwatch.Frequency;
            if (!diagnosticsClock.TryMap(diagnostic.timestampMs, arrivalSeconds,
                out double statusSeconds, out double excessDelaySeconds)) return;

            // Each radio is sampled sequentially. Retain all three acquisition
            // instants so the corresponding antenna pose can be interpolated.
            double t1 = statusSeconds - unchecked(diagnostic.timestampMs - diagnostic.t1Ms) / 1000.0;
            double t2 = statusSeconds - unchecked(diagnostic.timestampMs - diagnostic.t2Ms) / 1000.0;
            double t3 = statusSeconds - unchecked(diagnostic.timestampMs - diagnostic.t3Ms) / 1000.0;
            if (t1 > t2 || t2 > t3 || t3 > arrivalSeconds ||
                arrivalSeconds - t1 > 0.50) return;

            latestRangeTime1Seconds = t1;
            latestRangeTime2Seconds = t2;
            latestRangeTime3Seconds = t3;
            LatestRangeTimingExcessDelaySeconds = (float)excessDelaySeconds;
            HasLatestRangeTimes = true;
        }

        private void LateUpdate()
        {
            // A cycle can wait for the next AR frame and succeed without another
            // radio packet. Log that decision separately from packet arrival.
            if (transportMode != UwbTransportMode.USB) return;
            if (diagnosticPlacement == null) diagnosticPlacement = GetComponent<UwbAnchorManager>();
            bool arrival = diagnosticForLateUpdate != null;
            bool changed = diagnosticPlacement != null &&
                (loggedDecisionRevision != diagnosticPlacement.RangeDecisionRevision ||
                 loggedPoseRevision != diagnosticPlacement.PoseRevision ||
                 loggedPoseEpoch != diagnosticPlacement.AutomaticPoseEpoch);
            if ((!arrival && !changed) || Diagnostics == null || !HasFreshDiagnostics) return;
            AppendUwbDiagnostic(diagnosticForLateUpdate ?? Diagnostics,
                arrival ? changed ? "arrival_and_decision" : "arrival" : "decision");
            diagnosticForLateUpdate = null;
            if (diagnosticPlacement != null)
            {
                loggedDecisionRevision = diagnosticPlacement.RangeDecisionRevision;
                loggedPoseRevision = diagnosticPlacement.PoseRevision;
                loggedPoseEpoch = diagnosticPlacement.AutomaticPoseEpoch;
            }
        }

        private static string CsvText(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

        private void AppendUwbDiagnostic(BaseDiagnostics d, string eventKind)
        {
            try
            {
                if (uwbDiagnosticCsv == null)
                {
                    string directory = Path.Combine(Application.persistentDataPath, "UwbDiagnostics");
                    Directory.CreateDirectory(directory);
                    UwbDiagnosticCsvPath = Path.Combine(directory,
                        "Uwb_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + ".csv");
                    uwbDiagnosticCsv = new StreamWriter(UwbDiagnosticCsvPath, false, new UTF8Encoding(false));
                    uwbDiagnosticCsv.WriteLine("phoneUtc,baseMs,radioMask,state,d1Raw,d2Raw,d3Raw,d1Corrected,d2Corrected,d3Corrected,profileSaved,scale1,scale2,scale3,offset1,offset2,offset3,baseSigma,stage1,stage2,stage3,phoneFrameTime,arTracking,camX,camY,camZ,camQx,camQy,camQz,camQw,scanActive,scannerStatusFresh,panDegrees,poseAccepted,multiviewSamples,multiviewReason,sampleResult,phoneBaselineM,geometryCondition,autoUwbStatus,anchor1X,anchor1Y,anchor1Z,anchor2X,anchor2Y,anchor2Z,anchor3X,anchor3Y,anchor3Z,panParking,protocolVersion,cycleCoherent,t1Ms,t2Ms,t3Ms,scannerStationary,headingAligned,canAcceptPoints,poseX,poseY,poseZ,tagX,tagY,tagZ,headingYaw,headingPan,headingSource,headingTargetX,headingTargetY,headingTargetZ,multiviewAccepted,supportObserved,supportY,solverResidual,solverSigma,headingStatus,foregroundX,foregroundY,foregroundZ,foregroundGapM,foregroundAgeS,rawTagX,rawTagY,rawTagZ,visualX,visualY,visualZ,visualCorrectionM,visualObservations,visualBaselineM,rangeTime1Phone,rangeTime2Phone,rangeTime3Phone,rangeTimingExtraDelayS,rangePoseStatus,appliedDiagnosticsId,rangeDecisionCycleId,rangeProcessedCycleId,rangeDecisionRevision,rangeDecision,rangeRetryCount,rangeSupersededCycleCount,diagnosticEventKind,poseRevision,poseEpoch,poseCandidateCount,poseCandidateViews,poseCandidateSpreadM,poseCandidateTagX,poseCandidateTagY,poseCandidateTagZ,heightConsistentPose,poseRevalidationRequired,poseCommitReason,frozenPoseReason,panReferenceValid,panReferenceRestored,panReferenceDirty,panReferenceState,panMoving,panSteps,visualObserverStatus,visualObserverObservations,scanStartPending,scannerCommandStatus,arHistoryEpochRevision,arHistoryResetReason,scannerDiagnosticVersion,thermalInitialized,thermalFrameReady,thermalState,thermalError,thermalFrames,thermalAgeMs,thermalSubpageMask,thermalDuplicateSubpages,thermalFrameTimeouts,thermalReadErrors,thermalOverruns,thermalInvalidFrames,thermalLastSubpage,thermalRefreshHz,thermalI2cHz,thermalReadDurationMs,thermalFrameSpanMs,thermalRawError");
                    Debug.Log("[UWB] Diagnóstico salvo em " + UwbDiagnosticCsvPath);
                }
                var scales = new float[3];
                var offsets = new float[3];
                UwbRangeCalibrationProfile.Load(scales, offsets, out bool saved);
                float[] raw = { d.d1, d.d2, d.d3 };
                var columns = new string[146];
                columns[0] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                columns[1] = d.timestampMs.ToString(CultureInfo.InvariantCulture);
                columns[2] = d.radioMask.ToString(CultureInfo.InvariantCulture);
                columns[3] = d.state.ToString(CultureInfo.InvariantCulture);
                for (int i = 0; i < 3; i++)
                {
                    columns[4+i] = raw[i].ToString("F4", CultureInfo.InvariantCulture);
                    columns[7+i] = raw[i] > 0f
                        ? (scales[i]*raw[i]+offsets[i]).ToString("F4", CultureInfo.InvariantCulture)
                        : "";
                    columns[11+i] = scales[i].ToString("F6", CultureInfo.InvariantCulture);
                    columns[14+i] = offsets[i].ToString("F6", CultureInfo.InvariantCulture);
                }
                columns[10] = saved ? "1" : "0";
                columns[17] = d.sigma.ToString("F4", CultureInfo.InvariantCulture);
                columns[18] = d.s1.ToString(CultureInfo.InvariantCulture);
                columns[19] = d.s2.ToString(CultureInfo.InvariantCulture);
                columns[20] = d.s3.ToString(CultureInfo.InvariantCulture);
                columns[21] = Time.unscaledTime.ToString("F6", CultureInfo.InvariantCulture);
                Camera arCamera = Camera.main;
                bool arTracking = arCamera != null && ARSession.state == ARSessionState.SessionTracking;
                columns[22] = arTracking ? "1" : "0";
                if (arTracking)
                {
                    Vector3 position = arCamera.transform.position;
                    Quaternion rotation = arCamera.transform.rotation;
                    columns[23] = position.x.ToString("F6", CultureInfo.InvariantCulture);
                    columns[24] = position.y.ToString("F6", CultureInfo.InvariantCulture);
                    columns[25] = position.z.ToString("F6", CultureInfo.InvariantCulture);
                    columns[26] = rotation.x.ToString("F6", CultureInfo.InvariantCulture);
                    columns[27] = rotation.y.ToString("F6", CultureInfo.InvariantCulture);
                    columns[28] = rotation.z.ToString("F6", CultureInfo.InvariantCulture);
                    columns[29] = rotation.w.ToString("F6", CultureInfo.InvariantCulture);
                }
                if (diagnosticPlacement == null) diagnosticPlacement = GetComponent<UwbAnchorManager>();
                if (diagnosticScanner == null) diagnosticScanner = FindFirstObjectByType<PointCloudTcpReceiver>();
                columns[30] = diagnosticScanner != null && diagnosticScanner.isScanning ? "1" : "0";
                bool freshScanner = diagnosticScanner != null && diagnosticScanner.HasFreshStatus;
                columns[31] = freshScanner ? "1" : "0";
                columns[32] = freshScanner && diagnosticScanner.status != null
                    ? diagnosticScanner.status.panDegrees.ToString("F3", CultureInfo.InvariantCulture) : "";
                if (diagnosticPlacement != null)
                {
                    columns[33] = diagnosticPlacement.HasPoseEstimate ? "1" : "0";
                    columns[34] = diagnosticPlacement.MultiviewSampleCount.ToString(CultureInfo.InvariantCulture);
                    columns[35] = diagnosticPlacement.MultiviewReason;
                    columns[36] = diagnosticPlacement.MultiviewSampleResult;
                    columns[37] = diagnosticPlacement.MultiviewBaseline.ToString("F4", CultureInfo.InvariantCulture);
                    columns[38] = diagnosticPlacement.MultiviewCondition.ToString("F6", CultureInfo.InvariantCulture);
                    columns[39] = "\"" + diagnosticPlacement.AutoUwbStatus.Replace("\"", "\"\"") + "\"";
                    if (arTracking && diagnosticPlacement.TryGetDiagnosticAnchors(diagnosticAnchors))
                        for (int anchor = 0; anchor < 3; anchor++)
                            for (int axis = 0; axis < 3; axis++)
                                columns[40 + anchor * 3 + axis] = diagnosticAnchors[anchor][axis].ToString("F6", CultureInfo.InvariantCulture);
                }
                columns[49] = freshScanner && diagnosticScanner.status != null && diagnosticScanner.status.panParking ? "1" : "0";
                columns[50] = d.version.ToString(CultureInfo.InvariantCulture);
                columns[51] = IsCoherentRangeCycle(d) ? "1" : "0";
                columns[52] = d.version >= 2 ? d.t1Ms.ToString(CultureInfo.InvariantCulture) : "";
                columns[53] = d.version >= 2 ? d.t2Ms.ToString(CultureInfo.InvariantCulture) : "";
                columns[54] = d.version >= 2 ? d.t3Ms.ToString(CultureInfo.InvariantCulture) : "";
                columns[55] = diagnosticPlacement != null && diagnosticPlacement.scannerStationary ? "1" : "0";
                columns[56] = diagnosticPlacement != null && diagnosticPlacement.PreviewHeadingAligned ? "1" : "0";
                columns[57] = diagnosticPlacement != null && diagnosticPlacement.CanAcceptPoints ? "1" : "0";
                if (diagnosticPlacement != null)
                {
                    Vector3 pose = diagnosticPlacement.localPreviewPosition;
                    Vector3 tag = diagnosticPlacement.currentSmoothedTagPosition;
                    Vector3 target = diagnosticPlacement.LastHeadingTargetWorld;
                    columns[58] = pose.x.ToString("F4", CultureInfo.InvariantCulture);
                    columns[59] = pose.y.ToString("F4", CultureInfo.InvariantCulture);
                    columns[60] = pose.z.ToString("F4", CultureInfo.InvariantCulture);
                    columns[61] = tag.x.ToString("F4", CultureInfo.InvariantCulture);
                    columns[62] = tag.y.ToString("F4", CultureInfo.InvariantCulture);
                    columns[63] = tag.z.ToString("F4", CultureInfo.InvariantCulture);
                    columns[64] = diagnosticPlacement.PreviewHeadingAligned
                        ? diagnosticPlacement.LastHeadingYawDegrees.ToString("F2", CultureInfo.InvariantCulture) : "";
                    columns[65] = diagnosticPlacement.PreviewHeadingAligned
                        ? diagnosticPlacement.LastHeadingPanDegrees.ToString("F2", CultureInfo.InvariantCulture) : "";
                    columns[66] = diagnosticPlacement.HeadingSource;
                    columns[67] = target.x.ToString("F4", CultureInfo.InvariantCulture);
                    columns[68] = target.y.ToString("F4", CultureInfo.InvariantCulture);
                    columns[69] = target.z.ToString("F4", CultureInfo.InvariantCulture);
                    columns[70] = diagnosticPlacement.HasValidatedMultiviewPose ? "1" : "0";
                    columns[71] = diagnosticPlacement.HasAutomaticSupportHeight ? "1" : "0";
                    columns[72] = diagnosticPlacement.HasAutomaticSupportHeight
                        ? diagnosticPlacement.AutomaticSupportHeight.ToString("F4", CultureInfo.InvariantCulture) : "";
                    columns[73] = diagnosticPlacement.motionResidualMeters.ToString("F4", CultureInfo.InvariantCulture);
                    columns[74] = diagnosticPlacement.motionGeometrySigmaMeters.ToString("F4", CultureInfo.InvariantCulture);
                    columns[75] = "\"" + diagnosticPlacement.HeadingStatus.Replace("\"", "\"\"") + "\"";
                    if (diagnosticPlacement.HasSupportForeground)
                    {
                        Vector3 foreground = diagnosticPlacement.LastSupportForegroundWorld;
                        columns[76] = foreground.x.ToString("F4", CultureInfo.InvariantCulture);
                        columns[77] = foreground.y.ToString("F4", CultureInfo.InvariantCulture);
                        columns[78] = foreground.z.ToString("F4", CultureInfo.InvariantCulture);
                        columns[80] = diagnosticPlacement.SupportForegroundAgeSeconds
                            .ToString("F3", CultureInfo.InvariantCulture);
                        if (diagnosticPlacement.HasPoseEstimate)
                            columns[79] = diagnosticPlacement.ForegroundToTagHorizontalDistance
                                .ToString("F4", CultureInfo.InvariantCulture);
                    }
                    if (diagnosticPlacement.HasValidatedMultiviewPose)
                    {
                        Vector3 rawTag = diagnosticPlacement.RawUwbTagPosition;
                        Vector3 visual = diagnosticPlacement.VisualSurfaceWorld;
                        columns[81] = rawTag.x.ToString("F4", CultureInfo.InvariantCulture);
                        columns[82] = rawTag.y.ToString("F4", CultureInfo.InvariantCulture);
                        columns[83] = rawTag.z.ToString("F4", CultureInfo.InvariantCulture);
                        columns[87] = diagnosticPlacement.VisualCorrectionMeters
                            .ToString("F4", CultureInfo.InvariantCulture);
                        columns[88] = diagnosticPlacement.VisualSupportingObservations
                            .ToString(CultureInfo.InvariantCulture);
                        columns[89] = diagnosticPlacement.VisualCameraBaselineMeters
                            .ToString("F4", CultureInfo.InvariantCulture);
                        if (diagnosticPlacement.VisualCorrectionMeters > 0f)
                        {
                            columns[84] = visual.x.ToString("F4", CultureInfo.InvariantCulture);
                            columns[85] = visual.y.ToString("F4", CultureInfo.InvariantCulture);
                            columns[86] = visual.z.ToString("F4", CultureInfo.InvariantCulture);
                        }
                    }
                }
                if (TryGetLatestRangeTimes(out double t1, out double t2, out double t3))
                {
                    columns[90] = t1.ToString("F6", CultureInfo.InvariantCulture);
                    columns[91] = t2.ToString("F6", CultureInfo.InvariantCulture);
                    columns[92] = t3.ToString("F6", CultureInfo.InvariantCulture);
                    columns[93] = LatestRangeTimingExcessDelaySeconds.ToString("F4",
                        CultureInfo.InvariantCulture);
                }
                if (diagnosticPlacement != null)
                    columns[94] = "\"" + diagnosticPlacement.RangePoseStatus.Replace("\"", "\"\"") + "\"";
                columns[95] = AppliedDiagnosticsId.ToString(CultureInfo.InvariantCulture);
                columns[102] = eventKind;
                if (diagnosticPlacement != null)
                {
                    columns[96] = diagnosticPlacement.RangeDecisionCycleId.ToString(CultureInfo.InvariantCulture);
                    columns[97] = diagnosticPlacement.RangeProcessedCycleId.ToString(CultureInfo.InvariantCulture);
                    columns[98] = diagnosticPlacement.RangeDecisionRevision.ToString(CultureInfo.InvariantCulture);
                    columns[99] = CsvText(diagnosticPlacement.RangeDecision);
                    columns[100] = diagnosticPlacement.RangeRetryCount.ToString(CultureInfo.InvariantCulture);
                    columns[101] = diagnosticPlacement.RangeSupersededCycleCount.ToString(CultureInfo.InvariantCulture);
                    columns[103] = diagnosticPlacement.PoseRevision.ToString(CultureInfo.InvariantCulture);
                    columns[104] = diagnosticPlacement.AutomaticPoseEpoch.ToString(CultureInfo.InvariantCulture);
                    columns[105] = diagnosticPlacement.PoseCandidateCount.ToString(CultureInfo.InvariantCulture);
                    columns[106] = diagnosticPlacement.PoseCandidateViews.ToString(CultureInfo.InvariantCulture);
                    columns[107] = diagnosticPlacement.PoseCandidateSpreadMeters.ToString("F4", CultureInfo.InvariantCulture);
                    Vector3 candidate = diagnosticPlacement.PoseCandidateTag;
                    for (int axis = 0; axis < 3; axis++)
                        columns[108 + axis] = candidate[axis].ToString("F6", CultureInfo.InvariantCulture);
                    columns[111] = diagnosticPlacement.HasHeightConsistentPose ? "1" : "0";
                    columns[112] = diagnosticPlacement.PoseRevalidationRequired ? "1" : "0";
                    columns[113] = CsvText(diagnosticPlacement.PoseCommitReason);
                    columns[114] = CsvText(diagnosticPlacement.FrozenPoseReason);
                    columns[125] = diagnosticPlacement.ArHistoryEpochRevision.ToString(CultureInfo.InvariantCulture);
                    columns[126] = CsvText(diagnosticPlacement.ArHistoryResetReason);
                }
                if (freshScanner && diagnosticScanner.status != null)
                {
                    var scannerStatus = diagnosticScanner.status;
                    columns[115] = scannerStatus.panReferenceValid ? "1" : "0";
                    columns[116] = scannerStatus.panReferenceRestored ? "1" : "0";
                    columns[117] = scannerStatus.panReferenceDirty ? "1" : "0";
                    columns[118] = CsvText(scannerStatus.panReferenceState);
                    columns[119] = scannerStatus.panMoving ? "1" : "0";
                    columns[120] = scannerStatus.panSteps.ToString(CultureInfo.InvariantCulture);
                    columns[127] = scannerStatus.diagnosticVersion.ToString(CultureInfo.InvariantCulture);
                    columns[128] = scannerStatus.thermalReady ? "1" : "0";
                    columns[129] = diagnosticScanner.HasFreshThermalFrame ? "1" : "0";
                    columns[130] = CsvText(scannerStatus.thermalState);
                    columns[131] = scannerStatus.thermalError.ToString(CultureInfo.InvariantCulture);
                    columns[132] = scannerStatus.thermalFrames.ToString(CultureInfo.InvariantCulture);
                    columns[133] = scannerStatus.thermalAgeMs.ToString(CultureInfo.InvariantCulture);
                    columns[134] = scannerStatus.thermalSubpageMask.ToString(CultureInfo.InvariantCulture);
                    columns[135] = scannerStatus.thermalDuplicateSubpages.ToString(CultureInfo.InvariantCulture);
                    columns[136] = scannerStatus.thermalFrameTimeouts.ToString(CultureInfo.InvariantCulture);
                    columns[137] = scannerStatus.thermalReadErrors.ToString(CultureInfo.InvariantCulture);
                    columns[138] = scannerStatus.thermalOverruns.ToString(CultureInfo.InvariantCulture);
                    columns[139] = scannerStatus.thermalInvalidFrames.ToString(CultureInfo.InvariantCulture);
                    columns[140] = scannerStatus.thermalLastSubpage.ToString(CultureInfo.InvariantCulture);
                    columns[141] = scannerStatus.thermalRefreshHz.ToString(CultureInfo.InvariantCulture);
                    columns[142] = scannerStatus.thermalI2cHz.ToString(CultureInfo.InvariantCulture);
                    columns[143] = scannerStatus.thermalReadDurationMs.ToString(CultureInfo.InvariantCulture);
                    columns[144] = scannerStatus.thermalFrameSpanMs.ToString(CultureInfo.InvariantCulture);
                    columns[145] = scannerStatus.thermalRawError.ToString(CultureInfo.InvariantCulture);
                }
                if (diagnosticVision == null) diagnosticVision = GetComponent<ScannerVisualPoseObserver>();
                if (diagnosticVision != null)
                {
                    columns[121] = CsvText(diagnosticVision.Status);
                    columns[122] = diagnosticVision.ObservationCount.ToString(CultureInfo.InvariantCulture);
                }
                if (diagnosticScanner != null)
                {
                    columns[123] = diagnosticScanner.ScanStartPending ? "1" : "0";
                    columns[124] = CsvText(diagnosticScanner.scannerCommandStatus);
                }
                uwbDiagnosticCsv.WriteLine(string.Join(",", columns));
                if (++unflushedDiagnosticRows >= 10)
                {
                    uwbDiagnosticCsv.Flush();
                    unflushedDiagnosticRows = 0;
                }
            }
            catch (IOException ex)
            {
                Debug.LogWarning("[UWB] Não foi possível salvar o diagnóstico: " + ex.Message);
                CloseUwbDiagnostic();
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.LogWarning("[UWB] Sem acesso ao arquivo de diagnóstico: " + ex.Message);
                CloseUwbDiagnostic();
            }
        }

        private void CloseUwbDiagnostic()
        {
            uwbDiagnosticCsv?.Dispose();
            uwbDiagnosticCsv = null;
            unflushedDiagnosticRows = 0;
            diagnosticForLateUpdate = null;
            loggedDecisionRevision = loggedPoseRevision = loggedPoseEpoch = -1;
        }

        public void StartReceiver()
        {
            StopReceiver();
            if (transportMode == UwbTransportMode.Simulated ||
                transportMode == UwbTransportMode.USB) return;
            lock (receiverLock)
            {
                var cancellation = new CancellationTokenSource();
                receiverCancellation = cancellation;
                UwbTransportMode mode = transportMode;
                int port = mode == UwbTransportMode.UDP ? udpPort : tcpPort;
                string host = tcpHost;
                new Thread(() => ReceiveLoop(cancellation, mode, host, port))
                {
                    Name = "UwbReceiverThread",
                    IsBackground = true
                }.Start();
            }
        }

        public void StopReceiver()
        {
            CloseUwbDiagnostic();
            CloseUsb();
            lock (receiverLock)
            {
                receiverCancellation?.Cancel();
                receiverCancellation = null;
                udpClient?.Close();
                tcpClient?.Close();
                udpClient = null;
                tcpClient = null;
                hasPendingPosition = false;
                hasReceivedPacket = false;
                IsConnected = false;
                pendingDiagnostics = null;
                pendingDiagnosticsTicks = 0;
                Diagnostics = null;
                HasFreshDiagnostics = false;
                HasLatestRangeTimes = false;
                LatestRangeTimingExcessDelaySeconds = float.PositiveInfinity;
                diagnosticsClock.Clear();
            }
        }

        private void ReceiveLoop(CancellationTokenSource cancellation, UwbTransportMode mode, string host, int port)
        {
            CancellationToken token = cancellation.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (mode == UwbTransportMode.UDP) ReceiveUdp(cancellation, port);
                        else ReceiveTcp(cancellation, host, port);
                    }
                    catch (Exception ex)
                    {
                        if (!token.IsCancellationRequested)
                            Debug.LogWarning($"[UWB {mode}] Conexão interrompida: {ex.Message}");
                    }
                    finally
                    {
                        lock (receiverLock)
                        {
                            if (receiverCancellation == cancellation)
                            {
                                hasPendingPosition = false;
                                hasReceivedPacket = false;
                            }
                        }
                    }
                    if (token.WaitHandle.WaitOne(2000)) break;
                }
            }
            finally
            {
                lock (receiverLock)
                {
                    if (receiverCancellation == cancellation) receiverCancellation = null;
                    cancellation.Dispose();
                }
            }
        }

        private void ReceiveUdp(CancellationTokenSource cancellation, int port)
        {
            CancellationToken token = cancellation.Token;
            using (var client = new UdpClient(port))
            {
                lock (receiverLock)
                {
                    if (receiverCancellation != cancellation) return;
                    udpClient = client;
                }
                client.Client.ReceiveTimeout = 500;
                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                while (!token.IsCancellationRequested)
                {
                    byte[] data;
                    try { data = client.Receive(ref remote); }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut ||
                                                     ex.SocketErrorCode == SocketError.WouldBlock)
                    {
                        continue;
                    }
                    // Cada datagrama contém exatamente um UwbPositionPacket.
                    if (UsbHasRecentData) continue;
                    if (data.Length == PacketSize) ProcessPacket(cancellation, ParsePacket(data));
                    else if (data.Length > 0 && data.Length <= 1024 && data[0] == (byte)'{')
                    {
                        lock (receiverLock)
                            if (receiverCancellation == cancellation)
                            {
                                pendingDiagnostics = Encoding.UTF8.GetString(data);
                                pendingDiagnosticsTicks = Stopwatch.GetTimestamp();
                            }
                    }
                }
            }
        }

        private void ReceiveTcp(CancellationTokenSource cancellation, string host, int port)
        {
            CancellationToken token = cancellation.Token;
            using (var client = new TcpClient())
            {
                lock (receiverLock)
                {
                    if (receiverCancellation != cancellation) return;
                    tcpClient = client;
                }
                var result = client.BeginConnect(host, port, null, null);
                using (WaitHandle connected = result.AsyncWaitHandle)
                {
                    int signaled = WaitHandle.WaitAny(new[] { connected, token.WaitHandle }, 3000);
                    if (token.IsCancellationRequested) return;
                    if (signaled != 0) throw new SocketException((int)SocketError.TimedOut);
                    client.EndConnect(result);
                }

                NetworkStream stream = client.GetStream();
                stream.ReadTimeout = 2000;
                byte[] buffer = new byte[PacketSize];
                int bytesRead = 0;
                while (!token.IsCancellationRequested)
                {
                    int count = stream.Read(buffer, bytesRead, PacketSize - bytesRead);
                    // EOF é desconexão, inclusive no meio de um registro.
                    if (count == 0) throw new IOException("A base UWB encerrou o fluxo TCP.");
                    bytesRead += count;
                    if (bytesRead != PacketSize) continue;
                    ProcessPacket(cancellation, ParsePacket(buffer));
                    bytesRead = 0;
                }
            }
        }

        private void ProcessPacket(CancellationTokenSource cancellation, UwbPositionData data)
        {
            QueuePosition(data, cancellation);
        }

        private void QueuePosition(UwbPositionData data, CancellationTokenSource cancellation = null)
        {
            if (!IsFinite(data.tagX) || !IsFinite(data.tagY) || !IsFinite(data.tagZ) ||
                !IsFinite(data.distAnchor1) || !IsFinite(data.distAnchor2) || !IsFinite(data.distAnchor3) ||
                data.distAnchor1 < 0f || data.distAnchor2 < 0f || data.distAnchor3 < 0f) return;

            lock (receiverLock)
            {
                if (cancellation != null && receiverCancellation != cancellation) return;
                long nowTicks = Stopwatch.GetTimestamp();
                // Reject duplicate/reordered UDP within a live device clock epoch.
                // After a timeout a restarted base may legitimately have a lower counter.
                if (hasReceivedPacket && unchecked((int)(data.timestampMs-lastDeviceTimestamp)) <= 0 &&
                    (nowTicks-lastPacketTicks)/(double)Stopwatch.Frequency < Math.Max(.1f,dataTimeoutSeconds)) return;
                lastDeviceTimestamp = data.timestampMs;
                pendingPosition = data;
                hasPendingPosition = true;
                hasReceivedPacket = true;
                lastPacketTicks = nowTicks;
                Interlocked.Increment(ref PacketsReceived);
            }
        }

        private void ApplyPosition(UwbPositionData data)
        {
            LatestTimestampMs = data.timestampMs;
            ++AppliedSampleId;
            LatestPosition = new Vector3(data.tagX, data.tagY, data.tagZ);
            DistanceAnchor1 = data.distAnchor1;
            DistanceAnchor2 = data.distAnchor2;
            DistanceAnchor3 = data.distAnchor3;
        }

        public void SetSimulatedPosition(Vector3 simPos)
        {
            if (!IsFinite(simPos.x) || !IsFinite(simPos.y) || !IsFinite(simPos.z)) return;
            lock (receiverLock)
            {
                LatestPosition = simPos;
                DistanceAnchor1 = DistanceAnchor2 = DistanceAnchor3 = 0f;
                hasPendingPosition = false;
                hasReceivedPacket = true;
                lastPacketTicks = Stopwatch.GetTimestamp();
                IsConnected = true;
                ++AppliedSampleId;
                Interlocked.Increment(ref PacketsReceived);
            }
        }

        private static UwbPositionData ParsePacket(ReadOnlySpan<byte> bytes)
        {
            return new UwbPositionData
            {
                tagX = ReadFloat(bytes),
                tagY = ReadFloat(bytes.Slice(4)),
                tagZ = ReadFloat(bytes.Slice(8)),
                distAnchor1 = ReadFloat(bytes.Slice(12)),
                distAnchor2 = ReadFloat(bytes.Slice(16)),
                distAnchor3 = ReadFloat(bytes.Slice(20)),
                timestampMs = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(24))
            };
        }

        private static float ReadFloat(ReadOnlySpan<byte> bytes)
        {
            return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes));
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public void RequestUsbPermissionAgain()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            usbSerial?.Call("requestPermissionAgain");
            nextUsbCheckTime = 0f;
#endif
        }

        private void PollUsb()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (usbSerial == null)
                {
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                        usbSerial = new AndroidJavaObject("com.arscanner.usb.UwbUsbSerial", activity);
                }
                if (Time.unscaledTime >= nextUsbCheckTime)
                {
                    nextUsbCheckTime = Time.unscaledTime + 1f;
                    usbSerial.Call<bool>("ensureStarted");
                    usbStatus = usbSerial.Call<string>("getStatus");
                    usbDevicePresent = usbSerial.Call<bool>("isOpen") ||
                        !usbStatus.Contains("não conectada");
                }
                for (int i = 0; i < 40; i++)
                {
                    string line = usbSerial.Call<string>("pollLine");
                    if (string.IsNullOrEmpty(line)) break;
                    if (line.StartsWith("{\"kind\":\"uwb_status\"", StringComparison.Ordinal) &&
                        line.Length <= 1024)
                    {
                        Interlocked.Exchange(ref lastUsbDataTicks, Stopwatch.GetTimestamp());
                        lock (receiverLock)
                        {
                            pendingDiagnostics = line;
                            pendingDiagnosticsTicks = Stopwatch.GetTimestamp();
                        }
                    }
                    else if (line.StartsWith("@UWB28:", StringComparison.Ordinal) &&
                        TryParseSerialPacket(line, out UwbPositionData position))
                    {
                        Interlocked.Exchange(ref lastUsbDataTicks, Stopwatch.GetTimestamp());
                        QueuePosition(position);
                    }
                }
            }
            catch (AndroidJavaException ex)
            {
                usbStatus = "USB: falha de comunicação: " + ex.Message;
                CloseUsb();
            }
#endif
        }

        public static bool TryParseSerialPacket(string line, out UwbPositionData data)
        {
            data = default;
            if (line == null || line.Length != 63 || !line.StartsWith("@UWB28:", StringComparison.Ordinal))
                return false;
            byte[] bytes = new byte[PacketSize];
            for (int i = 0; i < PacketSize; i++)
                if (!byte.TryParse(line.Substring(7 + i*2, 2), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out bytes[i])) return false;
            data = ParsePacket(bytes);
            return IsFinite(data.tagX) && IsFinite(data.tagY) && IsFinite(data.tagZ) &&
                IsFinite(data.distAnchor1) && IsFinite(data.distAnchor2) && IsFinite(data.distAnchor3) &&
                data.distAnchor1 >= 0f && data.distAnchor2 >= 0f && data.distAnchor3 >= 0f;
        }

        private void CloseUsb()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (usbSerial != null)
            {
                try { usbSerial.Call("close"); } catch (AndroidJavaException) { }
                usbSerial.Dispose();
                usbSerial = null;
            }
#endif
            usbDevicePresent = false;
            Interlocked.Exchange(ref lastUsbDataTicks, 0);
        }

        private void OnDisable() => StopReceiver();
        private void OnDestroy() => StopReceiver();
    }
}
