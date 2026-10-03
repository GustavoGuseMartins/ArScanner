using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace ArScanner.Network
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ScanPointData
    {
        // Bit 2 indica que a geometria não possui uma medição térmica válida.
        public const byte ThermalUnavailableFlag = 0x04;
        // Bit 3 preserva o aviso de retorno fraco fornecido pelo próprio LiDAR.
        public const byte WeakSignalFlag = 0x08;
        public bool HasWeakLidarSignal => (surfaceFlags & WeakSignalFlag) != 0;

        public float posX_mm;
        public float posY_mm;
        public float posZ_mm;
        public float temperatureC;
        public byte r;
        public byte g;
        public byte b;
        public byte surfaceFlags;
        public short pitchCentiDeg;
        public short rollCentiDeg;
        public uint timestampMs;
    }

    public class PointCloudTcpReceiver : MonoBehaviour
    {
        [Header("Configurações TCP")]
        [Tooltip("IP do Access Point criado pelo ESP32-S3 (ArScanner_Net)")]
        public string scannerIp = "192.168.4.1";
        public int scannerPort = 8888;
        public bool autoConnect = true;
        public float reconnectInterval = 2.0f;
        [Min(1)] public int maxBufferedPoints = 10000;

        [Header("Diagnóstico e Status")]
        public volatile bool isConnected;
        public int pointsInBuffer;
        public long totalPointsReceived;
        public long droppedPoints;
        public float packetsPerSecond;

        public ConcurrentQueue<ScanPointData> incomingPoints = new ConcurrentQueue<ScanPointData>();

        private const int PacketSize = 28;
        private readonly object connectionLock = new object();
        private readonly object queueLock = new object();
        private TcpClient tcpClient;
        private CancellationTokenSource receiverCancellation;
        private bool started;

        private const byte CMD_START_SCAN = 0x01;
        private const byte CMD_STOP_SCAN = 0x02;
        private const byte CMD_HEARTBEAT = 0x03;
        private const byte CMD_SET_SPEED = 0x04;
        private const byte CMD_SET_MODE = 0x05;
        private const byte CMD_SET_LIDAR_SPEED = 0x06;
        private const byte CMD_CONFIRM_PAN_ZERO = 0x09;
        private const float ControlConfirmationTimeoutSeconds = 5f;
        private static readonly byte[] HeartbeatCommand = { CMD_HEARTBEAT };

        [Header("Controle de Varredura Remoto")]
        public volatile bool isScanning;
        public float currentStepperSpeedRpm = 2.0f;
        public float currentLidarSpeedPercent = 0.65f; // ~165 PWM
        public int currentScanMode;
        public float currentHeadAngle;
        public bool panEnabled = true;
        [Serializable] public class ScannerStatus
        {
            public float lidarRpm;
            public uint lidarWeakSamples, lidarValidSamples, lidarInvalidSamples, lidarChecksumErrors;
            public float panDegrees;
            public int stepsPerRevolution;
            public uint timestampMs;
            public uint poseDrops;
            public bool imuCalibrated;
            public bool encoder;
            public int diagnosticVersion;
            public bool panEnabled;
            public bool rgbReady, thermalReady, imuReady, tagReady;
            public bool tagEnabled;
            public int rgbProfile;
            public int thermalOrientationProfile;
            public int thermalError;
            public bool thermalFrameReady;
            public bool thermalPartialCalibration;
            public int thermalMaskedPixels, thermalCalibrationWarning;
            public string thermalState;
            public int thermalSubpageMask, thermalLastSubpage, thermalRefreshHz, thermalRawError;
            public uint thermalDuplicateSubpages, thermalFrameTimeouts, thermalReadErrors,
                thermalOverruns, thermalInvalidFrames, thermalI2cHz,
                thermalReadDurationMs, thermalFrameSpanMs;
            public float thermalFrameRateHz;
            public uint thermalFrameRateWindowMs;
            public int thermalRequestedFrameRateHz, thermalTargetFrameRateHz;
            public string rgbMessage;
            public uint thermalFrames, thermalAgeMs, thermalFusedPoints, imuAgeMs;
            public uint imuSampleIntervalUs, imuIntegrationGaps;
            public string imuState;
            public int imuIdentity = -1;
            public uint imuInitAttempts, imuReadErrors;
            public bool imuBiasCalibrated, imuTiltApplied;
            public string imuOrientationState;
            public bool imuOrientationReferenceValid, imuOrientationEnabled, imuStationary, imuGravityValid;
            public uint imuOrientationGeneration, imuOrientationAgeMs, imuOrientationGaps, imuStationaryMs;
            public float imuRelativeHeadYawDeg, imuRelativeBaseYawDeg, imuPitchDeg, imuRollDeg, imuYawUncertaintyDeg;
            public float imuBaseQw, imuBaseQx, imuBaseQy, imuBaseQz;
            public ImuRawData imuRaw;
            public bool isScanning, panParking, imuI2cAck, thermalI2cAck;
            public bool panMoving, panReferenceValid, panReferenceRestored, panReferenceDirty;
            public string panReferenceState;
            public int panSteps;
            public uint panReferenceCalibrationVersion;
            public float stepperRpm;
            public int lidarPwm, scanMode, tagStage;
            public uint tagPolls, tagResponses, tagFinals, tagReports, tagRxErrors, tagLastPollAgeMs;
        }
        public ScannerStatus status;
        public bool HasFreshThermalFrame => HasFreshStatus && status.thermalFrames > 0 &&
            status.thermalAgeMs <= 1000 && (status.diagnosticVersion >= 9
                ? status.thermalFrameReady : status.thermalReady);
        public bool HasLidarQualityStatus => HasFreshStatus && status.diagnosticVersion >= 12;
        public string ScannerDiagnosticPath { get; private set; }
        private StreamWriter scannerDiagnosticLog;
        private bool scannerDiagnosticLogFailed;
        [Serializable] private class ScannerDiagnosticRecord
        {
            public string phoneUtc, httpMessage, commandStatus;
            public float phoneFrameTime;
            public bool statusFresh;
            public ScannerStatus scanner;
        }
        public string statusHttpMessage = "Aguardando HTTP do scanner.";
        public bool IsParking => HasFreshStatus && status.diagnosticVersion >= 5 && status.panParking;
        public bool HasUsablePanReference => HasFreshStatus && status != null &&
            (status.diagnosticVersion < 8 || status.panReferenceValid);
        public bool HasKnownPanReference => HasFreshStatus && status != null &&
            status.diagnosticVersion >= 8 && status.panReferenceValid;
        public bool ScanStartPending { get; private set; }
        public bool PanZeroConfirmationPending { get; private set; }
        public bool ScannerControlBusy => ScanStartPending || PanZeroConfirmationPending ||
            IsParking || Time.unscaledTime < parkRequestedUntil;
        public bool CanSetPanSpeed => HasFreshStatus && status.diagnosticVersion >= 5 &&
            !ScannerControlBusy && !DiagnosticsBusy && !isScanning && !status.isScanning &&
            !status.panMoving && !status.panParking;
        public bool CanConfirmPanReference => HasFreshStatus && status != null &&
            status.diagnosticVersion >= 8 && !ScannerControlBusy && !DiagnosticsBusy && !isScanning &&
            !status.isScanning && !status.panParking && !status.panMoving;
        public string scannerCommandStatus { get; private set; } = "";
        public event Action PanZeroReferenceConfirmed;
        private float parkRequestedUntil;
        private float commandedStateAt = -100f;
        private float controlRequestAt = -100f;
        private int controlRevision;
        public int cameraPreviewMode; // 0=off, 1=RGB, 2=thermal
        public Texture2D cameraPreviewTexture { get; private set; }
        public int CameraThermalMaskedPixels { get; private set; }
        public bool CameraThermalPreviewHasMask { get; private set; }
        public string ThermalPreviewNotice => CameraThermalMaskedPixels > 0
            ? $"Imagem parcial: {CameraThermalMaskedPixels} pixels sem calibração foram excluídos"
            : "";
        private float cameraPreviewReceivedTime = -100f;
        public bool HasFreshThermalPreview => cameraPreviewMode == 2 && cameraPreviewTexture != null &&
            HasFreshThermalFrame && Time.unscaledTime - cameraPreviewReceivedTime <= 1.5f;
        public string cameraStatus = "Prévia desligada.";
        public float cameraMinTemperature, cameraMaxTemperature;
        public float thermalFramesPerSecond { get; private set; }
        private uint previousThermalFrames;
        private uint previousThermalTimestampMs;
        private bool hasPreviousThermalStatus;
        private UnityWebRequest cameraRequest;
        private Coroutine cameraCoroutine;
        private float nextCameraTime;

        private float nextStatusTime, statusReceivedTime = -100f;
        private Coroutine statusCoroutine;
        private UnityWebRequest statusRequest;
        public bool HasFreshStatus => isConnected && status != null &&
            Time.unscaledTime-statusReceivedTime < 2.5f;
        public float StatusSnapshotAgeSeconds => Mathf.Max(0f, Time.unscaledTime - statusReceivedTime);
        // This validates the IMU sample at the scanner's status snapshot. The
        // HTTP polling cadence is suitable for diagnostics, not IMU integration.
        public bool HasUsableImuSnapshot => HasFreshStatus && IsUsableImuSnapshot(status);
        public bool HasReadableImuSnapshot => HasFreshStatus && IsReadableImuSnapshot(status);
        public bool HasUsableImuOrientation => HasFreshStatus && IsUsableImuOrientation(status);
        public bool ImuAllowsScanStart => HasFreshStatus && (status.diagnosticVersion < 14 ||
            (status.imuOrientationState != "reference_collecting" && (!status.imuOrientationEnabled ||
                (HasUsableImuOrientation && status.imuStationary))));
        public bool CanReferenceImuOrientation => CanSetPanSpeed && status.diagnosticVersion >= 14 &&
            status.panReferenceValid && HasUsableImuSnapshot && status.imuBiasCalibrated &&
            status.imuOrientationState != "reference_collecting";
        public static bool IsUsableImuOrientation(ScannerStatus value)
        {
            if (value == null || value.diagnosticVersion < 14 || !value.imuReady || !value.imuBiasCalibrated ||
                !value.imuOrientationReferenceValid || !value.imuGravityValid || value.imuOrientationAgeMs > 100 ||
                value.imuOrientationState != "ready") return false;
            if (!IsFinite(value.imuRelativeHeadYawDeg) || !IsFinite(value.imuRelativeBaseYawDeg) ||
                !IsFinite(value.imuPitchDeg) || !IsFinite(value.imuRollDeg) || !IsFinite(value.imuYawUncertaintyDeg)) return false;
            float norm = value.imuBaseQw*value.imuBaseQw + value.imuBaseQx*value.imuBaseQx +
                value.imuBaseQy*value.imuBaseQy + value.imuBaseQz*value.imuBaseQz;
            return value.imuYawUncertaintyDeg >= 0f && IsFinite(norm) && Mathf.Abs(norm-1f) <= .05f;
        }
        public static bool IsUsableImuSnapshot(ScannerStatus value)
            => IsValidImuSnapshot(value, 100);
        public static bool IsReadableImuSnapshot(ScannerStatus value)
            => IsValidImuSnapshot(value, 1000);
        private static bool IsValidImuSnapshot(ScannerStatus value, uint maximumAgeMs)
        {
            if (value == null || !value.imuReady || value.imuAgeMs > maximumAgeMs || value.imuRaw == null)
                return false;
            ImuRawData raw = value.imuRaw;
            return IsFinite(raw.accelX) && IsFinite(raw.accelY) && IsFinite(raw.accelZ) &&
                IsFinite(raw.gyroX) && IsFinite(raw.gyroY) && IsFinite(raw.gyroZ) &&
                IsFinite(raw.angleX) && IsFinite(raw.angleY) && IsFinite(raw.angleZ);
        }

        [Serializable] public class ImuRawData
        {
            public float accelX, accelY, accelZ; // g, sensor axes
            public float gyroX, gyroY, gyroZ; // deg/s, bias corrected by firmware
            public float angleX, angleY, angleZ;
        }
        [Serializable] public class ScannerGeometry
        {
            public float[] lidarOrigin, tagOffset, tagPhysicalOffset;
            public float lidarMountYaw, lidarAngleSign, lidarZeroDeg, panSign, panZeroDeg;
            public int stepsPerRev, gearRatio, microsteps, imuPitchAxis, imuRollAxis;
            public float imuPitchSign, imuRollSign;
            public bool imuApplyTilt, imuOnHead, tagOnHead, diagnosticMode;
            public int thermalOrientationProfile;
            public string coordinateFrame;
        }
        public ScannerGeometry geometry;
        [Serializable] private class ThermalOrientationResponse
        {
            public int thermalOrientationProfile = -1;
        }
        [Serializable] private class ImuOrientationResponse
        {
            public bool imuOrientationReferenceValid, imuOrientationEnabled;
            public uint imuOrientationGeneration;
            public string imuOrientationState;
        }
        [Serializable] private class ThermalFrameRateResponse
        {
            public int thermalRequestedFrameRateHz, thermalTargetFrameRateHz;
        }
        public string calibrationStatus = "Aguardando geometria do scanner.";
        public string lastScanCsvPath;
        public bool DiagnosticsBusy => diagnosticRequest != null;
        private Coroutine diagnosticCoroutine;
        private UnityWebRequest diagnosticRequest;
        private float nextGeometryTime;

        private float heartbeatTimer;
        private int ppsCounter;
        private float ppsTimer;

        private void Start()
        {
            started = true;
            if (autoConnect) ConnectToScanner();
        }

        private void OnEnable()
        {
            if (started && autoConnect) ConnectToScanner();
        }

        private void Update()
        {
            UpdateControlConfirmation(Time.unscaledTime);
            if (isConnected && statusCoroutine == null && cameraRequest == null && !DiagnosticsBusy && Time.unscaledTime >= nextStatusTime)
            {
                nextStatusTime = Time.unscaledTime + (ScanStartPending || PanZeroConfirmationPending ? .3f : 1f);
                statusCoroutine = StartCoroutine(FetchStatus());
            }
            if (!isConnected) geometry = null;
            else if (geometry == null && !ScanStartPending && !PanZeroConfirmationPending &&
                !DiagnosticsBusy && statusCoroutine == null && cameraRequest == null && Time.unscaledTime >= nextGeometryTime)
            {
                nextGeometryTime = Time.unscaledTime + 5f;
                RequestGeometry();
            }
            if (cameraPreviewMode != 0 && !ScanStartPending && !PanZeroConfirmationPending &&
                cameraRequest == null && statusCoroutine == null &&
                !DiagnosticsBusy && Time.unscaledTime >= nextCameraTime)
            {
                nextCameraTime = Time.unscaledTime + .2f;
                cameraCoroutine = StartCoroutine(FetchCameraPreview(cameraPreviewMode));
            }
            pointsInBuffer = incomingPoints.Count;
            ppsTimer += Time.unscaledDeltaTime;
            if (ppsTimer >= 1.0f)
            {
                packetsPerSecond = Interlocked.Exchange(ref ppsCounter, 0) / ppsTimer;
                ppsTimer = 0f;
            }

            if (isConnected && (isScanning || ScanStartPending || IsParking || Time.unscaledTime < parkRequestedUntil))
            {
                heartbeatTimer += Time.unscaledDeltaTime;
                if (heartbeatTimer >= 1.0f)
                {
                    heartbeatTimer = 0f;
                    SendCommand(HeartbeatCommand);
                }
            }
            else heartbeatTimer = 0f;
        }

        public void ConnectToScanner()
        {
            lock (connectionLock)
            {
                if (receiverCancellation != null) return;
                var cancellation = new CancellationTokenSource();
                receiverCancellation = cancellation;
                // Cada sessão mantém seu próprio token; uma sessão antiga não fecha a seguinte.
                new Thread(() => ReceiveLoop(cancellation, scannerIp, scannerPort))
                {
                    Name = "PointCloudTcpReceiverThread",
                    IsBackground = true
                }.Start();
            }
        }

        public void Disconnect()
        {
            CancelPendingControl("Scanner desconectado.");
            CancelStatusRequest();
            statusReceivedTime = -100f;
            hasPreviousThermalStatus = false;
            thermalFramesPerSecond = 0f;
            scannerDiagnosticLog?.Dispose();
            scannerDiagnosticLog = null;
            CancelDiagnosticRequest();
            CancelCameraRequest();
            geometry = null;
            lock (connectionLock)
            {
                receiverCancellation?.Cancel();
                receiverCancellation = null;
                tcpClient?.Close();
                tcpClient = null;
                isConnected = false;
                isScanning = false;
            }
        }

        private void ReceiveLoop(CancellationTokenSource cancellation, string host, int port)
        {
            CancellationToken token = cancellation.Token;
            byte[] buffer = new byte[PacketSize * 30];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    TcpClient client = null;
                    try
                    {
                        client = new TcpClient { NoDelay = true };
                        lock (connectionLock)
                        {
                            if (receiverCancellation != cancellation) return;
                            tcpClient = client;
                        }

                        // A timed-out BeginConnect may still complete later. Disposing
                        // its AsyncWaitHandle early crashes Mono's completion callback.
                        Task connection = client.ConnectAsync(host, port);
                        _ = connection.ContinueWith(t => { _ = t.Exception; },
                            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted |
                            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                        if (!connection.Wait(3000, token))
                            throw new SocketException((int)SocketError.TimedOut);

                        NetworkStream stream = client.GetStream();
                        stream.ReadTimeout = 500;
                        stream.WriteTimeout = 1000;
                        lock (connectionLock)
                        {
                            if (receiverCancellation != cancellation) return;
                            isConnected = true;
                            isScanning = false;
                        }
                        Debug.Log("[TCP Client] Conexão TCP estabelecida com sucesso!");
                        int leftover = 0;
                        while (!token.IsCancellationRequested)
                        {
                            int bytesRead;
                            try
                            {
                                bytesRead = stream.Read(buffer, leftover, buffer.Length - leftover);
                            }
                            catch (IOException ex) when (IsReadTimeout(ex))
                            {
                                // O scanner não envia pontos enquanto está parado.
                                continue;
                            }
                            if (bytesRead == 0) throw new IOException("Conexão remota encerrada pelo Scanner.");

                            int totalBytes = bytesRead + leftover;
                            int offset = 0;
                            while (totalBytes - offset >= PacketSize)
                            {
                                if (token.IsCancellationRequested) return;
                                if (TryParsePointPacket(buffer.AsSpan(offset, PacketSize), out ScanPointData point))
                                    EnqueuePoint(point);
                                offset += PacketSize;
                            }
                            leftover = totalBytes - offset;
                            if (leftover > 0) Buffer.BlockCopy(buffer, offset, buffer, 0, leftover);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!token.IsCancellationRequested)
                            Debug.LogWarning($"[TCP Client] Desconectado: {ex.Message}");
                    }
                    finally
                    {
                        client?.Close();
                        lock (connectionLock)
                        {
                            if (receiverCancellation == cancellation)
                            {
                                tcpClient = null;
                                isConnected = false;
                                isScanning = false;
                            }
                        }
                    }
                    float delay = IsFinite(reconnectInterval) ? Mathf.Clamp(reconnectInterval, 0.1f, 60f) : 2f;
                    if (token.WaitHandle.WaitOne((int)(delay * 1000))) break;
                }
            }
            finally
            {
                lock (connectionLock)
                {
                    if (receiverCancellation == cancellation) receiverCancellation = null;
                    cancellation.Dispose();
                }
            }
        }

        private static bool IsReadTimeout(IOException exception)
        {
            return exception.InnerException is SocketException socket &&
                (socket.SocketErrorCode == SocketError.TimedOut || socket.SocketErrorCode == SocketError.WouldBlock);
        }

        public static bool TryParsePointPacket(ReadOnlySpan<byte> bytes, out ScanPointData point)
        {
            point = default;
            if (bytes.Length != PacketSize) return false;
            point = new ScanPointData
            {
                posX_mm = ReadFloat(bytes),
                posY_mm = ReadFloat(bytes.Slice(4)),
                posZ_mm = ReadFloat(bytes.Slice(8)),
                temperatureC = ReadFloat(bytes.Slice(12)),
                r = bytes[16], g = bytes[17], b = bytes[18], surfaceFlags = bytes[19],
                pitchCentiDeg = BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(20)),
                rollCentiDeg = BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(22)),
                timestampMs = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(24))
            };
            return true;
        }

        private static float ReadFloat(ReadOnlySpan<byte> bytes)
        {
            return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes));
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public void EnqueuePoint(ScanPointData point)
        {
            if (!IsFinite(point.posX_mm) || !IsFinite(point.posY_mm) || !IsFinite(point.posZ_mm) ||
                ((point.surfaceFlags & ScanPointData.ThermalUnavailableFlag) == 0 && !IsFinite(point.temperatureC))) return;

            lock (queueLock)
            {
                int limit = Math.Max(1, maxBufferedPoints);
                while (incomingPoints.Count >= limit && incomingPoints.TryDequeue(out _))
                    Interlocked.Increment(ref droppedPoints);
                incomingPoints.Enqueue(point);
            }
            Interlocked.Increment(ref totalPointsReceived);
            Interlocked.Increment(ref ppsCounter);
        }

        public void SendStartScan()
        {
            lock (connectionLock)
            {
                if (ScannerControlBusy || isScanning) return;
                if (DiagnosticsBusy)
                {
                    scannerCommandStatus = "Aguarde a consulta ao scanner terminar antes de iniciar.";
                    return;
                }
                if (!HasFreshStatus || status.diagnosticVersion < 5)
                {
                    scannerCommandStatus = "Aguarde o status do scanner antes de iniciar.";
                    return;
                }
                if (!HasUsablePanReference)
                {
                    scannerCommandStatus = "Alinhe a cabeça à frente da base e confirme o zero do pan.";
                    return;
                }
                if (!ImuAllowsScanStart)
                {
                    scannerCommandStatus = "GY-25: mantenha a cabeça parada e refaça a referência antes de capturar.";
                    return;
                }
                if (status.isScanning || status.panParking || status.panMoving)
                {
                    scannerCommandStatus = "Aguarde a parada da cabeça antes de iniciar.";
                    return;
                }
                if (!TrySendCommand(new byte[] { CMD_START_SCAN }))
                {
                    scannerCommandStatus = "Não foi possível enviar o início ao scanner.";
                    return;
                }
                ScanStartPending = true;
                controlRevision++;
                controlRequestAt = Time.unscaledTime;
                commandedStateAt = Time.unscaledTime;
                parkRequestedUntil = 0;
                heartbeatTimer = 0f;
                scannerCommandStatus = "Início solicitado; aguardando confirmação do scanner.";
                RequestControlStatusSoon();
            }
        }

        public void SendStopScan()
        {
            lock (connectionLock)
            {
                if (TrySendCommand(new byte[] { CMD_STOP_SCAN })) {
                    controlRevision++;
                    isScanning = false;
                    CancelPendingControl("Parada solicitada.");
                    parkRequestedUntil = 0;
                    commandedStateAt = Time.unscaledTime;
                    RequestControlStatusSoon();
                }
            }
        }

        public bool SendParkPan()
        {
            if (isScanning || ScannerControlBusy || !HasFreshStatus || status.diagnosticVersion < 5 ||
                status.isScanning || status.panMoving) return false;
            if (status.diagnosticVersion >= 8 && !status.panReferenceValid)
            {
                scannerCommandStatus = "Confirme o zero físico antes de solicitar o retorno do pan.";
                return false;
            }
            if (!TrySendCommand(new byte[] { 0x08 })) return false;
            controlRevision++;
            parkRequestedUntil = Time.unscaledTime + 3f;
            commandedStateAt = Time.unscaledTime;
            scannerCommandStatus = HasKnownPanReference
                ? "Retorno ao zero confirmado solicitado."
                : "Retorno ao zero relativo solicitado; a direção física continua desconhecida.";
            RequestControlStatusSoon();
            return true;
        }

        public bool ConfirmPanZeroReference()
        {
            if (!CanConfirmPanReference)
            {
                scannerCommandStatus = "Pare a cabeça e aguarde o status do firmware v8 para confirmar o zero.";
                return false;
            }
            if (!TrySendCommand(new byte[] { CMD_CONFIRM_PAN_ZERO }))
            {
                scannerCommandStatus = "Não foi possível enviar a confirmação do zero.";
                return false;
            }
            PanZeroConfirmationPending = true;
            controlRevision++;
            controlRequestAt = commandedStateAt = Time.unscaledTime;
            scannerCommandStatus = "Zero solicitado; aguardando a confirmação do scanner.";
            RequestControlStatusSoon();
            return true;
        }

        private void RequestControlStatusSoon()
        {
            CancelCameraRequest();
            nextStatusTime = 0f;
        }

        private void CancelPendingControl(string message)
        {
            if (!ScanStartPending && !PanZeroConfirmationPending) return;
            ScanStartPending = PanZeroConfirmationPending = false;
            scannerCommandStatus = message;
        }

        private void UpdateControlConfirmation(float now)
        {
            if (!ScanStartPending && !PanZeroConfirmationPending) return;
            if (!isConnected)
            {
                CancelPendingControl("Scanner desconectado antes da confirmação.");
                return;
            }
            if (now-controlRequestAt < ControlConfirmationTimeoutSeconds) return;
            bool startTimedOut = ScanStartPending;
            ScanStartPending = PanZeroConfirmationPending = false;
            if (startTimedOut)
            {
                // A sent command is not evidence that acquisition started. Stop an
                // unconfirmed attempt instead of retaining a phantom scanning state.
                TrySendCommand(new byte[] { CMD_STOP_SCAN });
                isScanning = false;
                commandedStateAt = now;
                controlRevision++;
            }
            scannerCommandStatus = startTimedOut
                ? "O scanner não confirmou o início; a tentativa foi interrompida."
                : "O scanner não confirmou o zero. Confira a posição da cabeça e tente novamente.";
        }

        private void ApplyControlStatus(ScannerStatus value, float requestStartedAt, float now, int requestRevision)
        {
            // Ignore an HTTP request begun before a newer TCP command. Its snapshot
            // cannot acknowledge that command, even if its response arrives later.
            // Unity's frame time is equal for Update and a later OnGUI command.
            // Revision also rejects that same-frame pre-command request.
            if (requestRevision != controlRevision || requestStartedAt < commandedStateAt ||
                value.diagnosticVersion < 5) return;
            isScanning = value.isScanning;
            if (ScanStartPending && value.isScanning)
            {
                ScanStartPending = false;
                scannerCommandStatus = "Scan confirmado pelo scanner.";
            }
            if (PanZeroConfirmationPending && value.diagnosticVersion >= 8 &&
                value.panReferenceValid && !value.panReferenceDirty && !value.panReferenceRestored &&
                value.panSteps == 0 && value.panReferenceState == "confirmed" &&
                !value.isScanning && !value.panParking && !value.panMoving)
            {
                PanZeroConfirmationPending = false;
                scannerCommandStatus = "Zero físico confirmado. Refaça a direção no AR.";
                PanZeroReferenceConfirmed?.Invoke();
            }
            else if (PanZeroConfirmationPending && value.panReferenceState == "storage_error")
            {
                PanZeroConfirmationPending = false;
                scannerCommandStatus = "O scanner não conseguiu salvar o zero; confirme novamente.";
            }
        }

        public void SendSetSpeed(float rpm)
        {
            if (!CanSetPanSpeed || !IsFinite(rpm)) return;
            float speed = Mathf.Clamp(rpm, 0.1f, 10.0f);
            byte[] command = new byte[5];
            command[0] = CMD_SET_SPEED;
            BinaryPrimitives.WriteInt32LittleEndian(command.AsSpan(1), BitConverter.SingleToInt32Bits(speed));
            if (TrySendCommand(command)) currentStepperSpeedRpm = speed;
        }

        public void SendSetMode(int mode)
        {
            if (mode != 0 && mode != 1) return;
            if (TrySendCommand(new byte[] { CMD_SET_MODE, (byte)mode })) currentScanMode = mode;
        }

        public bool SendPanEnabled(bool enabled)
        {
            if (!HasFreshStatus || status.diagnosticVersion < 2) return false;
            if (!TrySendCommand(new byte[] {0x07, enabled ? (byte)1 : (byte)0})) return false;
            panEnabled = enabled;
            return true;
        }

        public void SendSetLidarSpeed(float speedPercent)
        {
            if (!IsFinite(speedPercent)) return;
            float clamped = Mathf.Clamp(speedPercent, 0.2f, 1.0f);
            byte pwm = (byte)(clamped * 255f);
            if (TrySendCommand(new byte[] { CMD_SET_LIDAR_SPEED, pwm }))
            {
                currentLidarSpeedPercent = clamped;
            }
        }

        public void SendCommand(byte[] data) => TrySendCommand(data);

        private bool TrySendCommand(byte[] data)
        {
            lock (connectionLock)
            {
                if (!isConnected || tcpClient == null || data == null || data.Length == 0) return false;
                try
                {
                    tcpClient.GetStream().Write(data, 0, data.Length);
                    return true;
                }
                catch (Exception ex)
                {
                    isConnected = false;
                    isScanning = false;
                    tcpClient.Close();
                    Debug.LogWarning($"[TCP Client] Falha ao enviar comando: {ex.Message}");
                    return false;
                }
            }
        }

        private IEnumerator FetchStatus()
        {
            float requestStartedAt = Time.unscaledTime;
            int requestRevision = controlRevision;
            statusRequest = UnityWebRequest.Get($"http://{scannerIp}:8889/status");
            statusRequest.timeout = 2;
            yield return statusRequest.SendWebRequest();
            if (statusRequest.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var value = JsonUtility.FromJson<ScannerStatus>(statusRequest.downloadHandler.text);
                    if (value != null && value.stepsPerRevolution > 0 &&
                        IsFinite(value.panDegrees) && IsFinite(value.lidarRpm))
                    {
                        status = value;
                        thermalFramesPerSecond = hasPreviousThermalStatus &&
                            TryCalculateThermalFrameRate(previousThermalTimestampMs, previousThermalFrames,
                                value.timestampMs, value.thermalFrames, out float measuredRate) ? measuredRate : 0f;
                        if (value.diagnosticVersion >= 14 && value.thermalFrameRateWindowMs >= 500 &&
                            IsFinite(value.thermalFrameRateHz) && value.thermalFrameRateHz >= 0f)
                            thermalFramesPerSecond = value.thermalFrameRateHz;
                        previousThermalFrames = value.thermalFrames;
                        previousThermalTimestampMs = value.timestampMs;
                        hasPreviousThermalStatus = true;
                        if (value.diagnosticVersion >= 2) panEnabled = value.panEnabled;
                        statusReceivedTime = Time.unscaledTime;
                        statusHttpMessage = $"HTTP OK :8889 — firmware diagnóstico v{value.diagnosticVersion}";
                        if (value.diagnosticVersion >= 5)
                        {
                            currentStepperSpeedRpm = value.stepperRpm;
                            currentLidarSpeedPercent = value.lidarPwm / 255f;
                            currentScanMode = value.scanMode;
                            ApplyControlStatus(value, requestStartedAt, Time.unscaledTime, requestRevision);
                        }
                    }
                }
                catch (Exception ex) { statusHttpMessage = $"Status inválido: {ex.Message}"; }
            }
            else statusHttpMessage = $"HTTP :8889/status — {statusRequest.responseCode}: {statusRequest.error}";
            AppendScannerDiagnostic();
            statusRequest.Dispose();
            statusRequest = null;
            statusCoroutine = null;
        }

        public static bool TryCalculateThermalFrameRate(uint previousMs, uint previousFrames,
            uint currentMs, uint currentFrames, out float framesPerSecond)
        {
            framesPerSecond = 0f;
            uint elapsedMs = unchecked(currentMs-previousMs);
            // Reject duplicate timestamps, reset clocks and long gaps; HTTP
            // arrival intervals are unrelated to the sensor's acquisition rate.
            if (elapsedMs == 0 || elapsedMs > 10000 || currentFrames < previousFrames) return false;
            framesPerSecond = (currentFrames-previousFrames)*1000f/elapsedMs;
            return IsFinite(framesPerSecond);
        }

        private void AppendScannerDiagnostic()
        {
            if (scannerDiagnosticLogFailed) return;
            try
            {
                if (scannerDiagnosticLog == null)
                {
                    string directory = Path.Combine(Application.persistentDataPath, "ScannerDiagnostics");
                    Directory.CreateDirectory(directory);
                    ScannerDiagnosticPath = Path.Combine(directory,
                        "Scanner_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff",
                            System.Globalization.CultureInfo.InvariantCulture) + ".jsonl");
                    scannerDiagnosticLog = new StreamWriter(ScannerDiagnosticPath, false,
                        new System.Text.UTF8Encoding(false));
                }
                scannerDiagnosticLog.WriteLine(JsonUtility.ToJson(new ScannerDiagnosticRecord {
                    phoneUtc = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                    phoneFrameTime = Time.unscaledTime, statusFresh = HasFreshStatus,
                    httpMessage = statusHttpMessage, commandStatus = scannerCommandStatus,
                    scanner = status
                }));
                scannerDiagnosticLog.Flush();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                scannerDiagnosticLogFailed = true;
                scannerDiagnosticLog?.Dispose();
                scannerDiagnosticLog = null;
                Debug.LogWarning("[Scanner] Não foi possível salvar o diagnóstico: " + ex.Message);
            }
        }
        public void RequestGeometry() => RequestDiagnostic(false);
        public void DownloadScanCsv() => RequestDiagnostic(true);

        public void RequestImuOrientationReference()
        {
            if (!CanReferenceImuOrientation) return;
            BeginImuOrientationRequest("imu/orientation/reference", "Referência GY-25: mantenha a cabeça parada por 3 segundos.");
        }

        public void RequestImuOrientationMode(bool enabled)
        {
            if (!CanSetPanSpeed || status.diagnosticVersion < 14 || (enabled && !HasUsableImuOrientation)) return;
            BeginImuOrientationRequest("imu/orientation/mode?enabled="+(enabled ? "1" : "0"),
                enabled ? "Habilitando acompanhamento GY-25..." : "Desligando acompanhamento GY-25...");
        }

        public void RequestThermalFrameRate(int framesPerSecond)
        {
            if (!CanSetPanSpeed || status.diagnosticVersion < 14 || (framesPerSecond != 4 && framesPerSecond != 8)) return;
            BeginImuOrientationRequest("thermal/frame-rate?fps="+framesPerSecond,
                "Ajustando a taxa da câmera térmica...", false);
        }

        private void BeginImuOrientationRequest(string route, string message, bool imu = true)
        {
            if (!isActiveAndEnabled) return;
            CancelStatusRequest();
            CancelCameraRequest();
            try
            {
                diagnosticRequest = new UnityWebRequest($"http://{scannerIp}:8889/{route}", "POST")
                    { downloadHandler = new DownloadHandlerBuffer(), timeout = 3 };
                var operation = diagnosticRequest.SendWebRequest();
                calibrationStatus = message;
                diagnosticCoroutine = StartCoroutine(FinishImuOrientationRequest(operation, imu));
            }
            catch (Exception ex)
            {
                CancelDiagnosticRequest();
                calibrationStatus = "GY-25: " + ex.Message;
            }
        }

        private IEnumerator FinishImuOrientationRequest(UnityWebRequestAsyncOperation operation, bool imu)
        {
            try
            {
                yield return operation;
                if (diagnosticRequest.result != UnityWebRequest.Result.Success)
                {
                    calibrationStatus = $"{(imu ? "GY-25" : "Térmica")}: HTTP {diagnosticRequest.responseCode}. {diagnosticRequest.downloadHandler?.text}";
                    yield break;
                }
                try
                {
                    if (imu)
                    {
                        var response = JsonUtility.FromJson<ImuOrientationResponse>(diagnosticRequest.downloadHandler.text);
                        if (response == null || string.IsNullOrEmpty(response.imuOrientationState))
                            throw new InvalidDataException("Resposta GY-25 incompleta.");
                        calibrationStatus = response.imuOrientationState == "reference_collecting"
                            ? "GY-25 coletando referência. Mantenha a cabeça parada por 3 segundos."
                            : response.imuOrientationEnabled ? "GY-25 acompanhando o giro; direção inicial preservada."
                            : "Acompanhamento GY-25 desligado.";
                    }
                    else
                    {
                        var response = JsonUtility.FromJson<ThermalFrameRateResponse>(diagnosticRequest.downloadHandler.text);
                        if (response == null || (response.thermalTargetFrameRateHz != 4 && response.thermalTargetFrameRateHz != 8))
                            throw new InvalidDataException("Resposta da taxa térmica incompleta.");
                        calibrationStatus = $"Térmica: solicitado {response.thermalRequestedFrameRateHz} quadros/s; alvo atual {response.thermalTargetFrameRateHz}.";
                    }
                    // A POST acknowledgement is not an orientation sample.
                    statusReceivedTime = -100f;
                    nextStatusTime = 0f;
                }
                catch (Exception ex) { calibrationStatus = (imu ? "GY-25: " : "Térmica: ") + ex.Message; }
            }
            finally
            {
                diagnosticRequest?.Dispose();
                diagnosticRequest = null;
                diagnosticCoroutine = null;
            }
        }

        private void CancelStatusRequest()
        {
            if (statusCoroutine != null) StopCoroutine(statusCoroutine);
            statusCoroutine = null;
            statusRequest?.Abort();
            statusRequest?.Dispose();
            statusRequest = null;
        }

        public void RequestThermalOrientation(int profile)
        {
            if (!isActiveAndEnabled || DiagnosticsBusy) return;
            if (!HasFreshStatus || status.diagnosticVersion < 7)
            {
                calibrationStatus = "Atualize o firmware do scanner para calibrar a orientação térmica.";
                return;
            }
            if (profile < 0 || profile > 3 || isScanning || status.isScanning)
            {
                calibrationStatus = "Pare a captura antes de mudar a orientação térmica.";
                return;
            }
            try
            {
                // The preview restarts with the newly reported orientation.
                CancelCameraRequest();
                diagnosticRequest = new UnityWebRequest(
                    $"http://{scannerIp}:8889/thermal/orientation?profile={profile}", "POST")
                {
                    downloadHandler = new DownloadHandlerBuffer(),
                    timeout = 4
                };
                var operation = diagnosticRequest.SendWebRequest();
                calibrationStatus = "Salvando orientação térmica...";
                diagnosticCoroutine = StartCoroutine(SaveThermalOrientation(profile, operation));
            }
            catch (Exception ex)
            {
                CancelDiagnosticRequest();
                calibrationStatus = $"Não foi possível salvar a orientação: {ex.Message}";
            }
        }

        private IEnumerator SaveThermalOrientation(int requestedProfile,
            UnityWebRequestAsyncOperation operation)
        {
            try
            {
                yield return operation;
                if (diagnosticRequest.result != UnityWebRequest.Result.Success)
                {
                    string message = diagnosticRequest.downloadHandler?.text;
                    calibrationStatus = $"Orientação térmica HTTP {diagnosticRequest.responseCode}: {message}";
                    yield break;
                }
                var response = JsonUtility.FromJson<ThermalOrientationResponse>(
                    diagnosticRequest.downloadHandler.text);
                if (response == null || response.thermalOrientationProfile != requestedProfile)
                {
                    calibrationStatus = "Scanner respondeu com perfil térmico diferente do solicitado.";
                    yield break;
                }
                status.thermalOrientationProfile = requestedProfile;
                geometry = null;
                nextGeometryTime = 0f;
                nextStatusTime = 0f;
                while (incomingPoints.TryDequeue(out _)) { }
                FindFirstObjectByType<ArScanner.Rendering.ThermalPointCloudRenderer>()?.ClearPointCloud();
                calibrationStatus = "Orientação térmica salva; nuvem anterior limpa. Teste com um alvo quente.";
            }
            finally
            {
                diagnosticRequest?.Dispose();
                diagnosticRequest = null;
                diagnosticCoroutine = null;
            }
        }

        private void RequestDiagnostic(bool csv)
        {
            if (!isActiveAndEnabled || DiagnosticsBusy || ScanStartPending || PanZeroConfirmationPending) return;
            if (!isConnected) { calibrationStatus = "Scanner desconectado."; return; }
            try
            {
                diagnosticRequest = UnityWebRequest.Get($"http://{scannerIp}:8889/{(csv ? "scan.csv" : "geometry")}");
                diagnosticRequest.timeout = csv ? 10 : 3;
                var operation = diagnosticRequest.SendWebRequest();
                calibrationStatus = csv ? "Baixando amostras..." : "Consultando geometria...";
                diagnosticCoroutine = StartCoroutine(FetchDiagnostic(csv, operation));
            }
            catch (Exception ex)
            {
                CancelDiagnosticRequest();
                calibrationStatus = $"Falha no diagnóstico: {ex.Message}";
            }
        }

        private IEnumerator FetchDiagnostic(bool csv, UnityWebRequestAsyncOperation operation)
        {
            try
            {
                yield return operation;
                if (diagnosticRequest.result != UnityWebRequest.Result.Success)
                {
                    calibrationStatus = $"Diagnóstico HTTP {diagnosticRequest.responseCode}: {diagnosticRequest.error}";
                    if (!csv) geometry = null;
                    yield break;
                }
                ApplyDiagnostic(csv, diagnosticRequest.downloadHandler.text);
            }
            finally
            {
                diagnosticRequest?.Dispose();
                diagnosticRequest = null;
                diagnosticCoroutine = null;
            }
        }

        private void ApplyDiagnostic(bool csv, string text)
        {
            try
            {
                if (csv)
                {
                    if (!text.StartsWith("sequence,sample_us,lidar_deg,distance_mm,pan_input_deg") ||
                        !text.Contains("x_mm,y_mm,z_mm"))
                        throw new InvalidDataException("CSV sem XYZ; atualize o firmware de diagnóstico.");
                    string folder = Path.Combine(Application.persistentDataPath, "Scans");
                    Directory.CreateDirectory(folder);
                    string path = Path.Combine(folder, $"ScanRaw_{DateTime.Now:yyyyMMdd_HHmmss_fff}.csv");
                    using (var file = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write)))
                        file.Write(text);
                    lastScanCsvPath = path;
                    calibrationStatus = $"CSV salvo: {path}";
                    Debug.Log($"[Calibration] {calibrationStatus}");
                }
                else
                {
                    var value = JsonUtility.FromJson<ScannerGeometry>(text);
                    if (value == null || value.lidarOrigin == null || value.lidarOrigin.Length != 3 ||
                        value.stepsPerRev <= 0 || !IsFinite(value.lidarOrigin[0]) ||
                        !IsFinite(value.lidarOrigin[1]) || !IsFinite(value.lidarOrigin[2]) ||
                        !IsFinite(value.lidarZeroDeg) || !IsFinite(value.lidarMountYaw) ||
                        Mathf.Abs(value.lidarAngleSign) != 1f || Mathf.Abs(value.panSign) != 1f)
                        throw new InvalidDataException("Geometria inválida.");
                    geometry = value;
                    calibrationStatus = "Geometria recebida. Sentidos e eixos ainda exigem teste físico.";
                }
            }
            catch (Exception ex)
            {
                if (!csv) geometry = null;
                calibrationStatus = $"Falha no diagnóstico: {ex.Message}";
            }
        }

        private void CancelDiagnosticRequest()
        {
            if (diagnosticCoroutine != null) StopCoroutine(diagnosticCoroutine);
            diagnosticCoroutine = null;
            diagnosticRequest?.Abort();
            diagnosticRequest?.Dispose();
            diagnosticRequest = null;
        }

        public void SetCameraPreview(int mode)
        {
            CancelCameraRequest();
            cameraPreviewMode = Mathf.Clamp(mode, 0, 2);
            nextCameraTime = 0;
            if (cameraPreviewTexture != null) Destroy(cameraPreviewTexture);
            cameraPreviewTexture = null;
            CameraThermalMaskedPixels = 0;
            CameraThermalPreviewHasMask = false;
            cameraStatus = mode == 0 ? "Prévia desligada." : "Consultando câmera por HTTP :8889...";
        }

        private string CameraPreviewRoute(int mode)
            => mode == 1 ? "rgb" : status != null && status.diagnosticVersion >= 10
                ? "thermal/masked" : "thermal";

        private IEnumerator FetchCameraPreview(int mode)
        {
            string route = CameraPreviewRoute(mode);
            bool maskedThermal = route == "thermal/masked";
            using (var request = UnityWebRequest.Get($"http://{scannerIp}:8889/{route}"))
            {
                cameraRequest = request;
                try
                {
                    request.timeout = 3;
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        if (cameraPreviewTexture != null) Destroy(cameraPreviewTexture);
                        cameraPreviewTexture = null;
                        CameraThermalMaskedPixels = 0;
                        CameraThermalPreviewHasMask = false;
                        string reason = request.downloadHandler?.text;
                        if (reason != null && reason.Length > 350) reason = reason.Substring(0, 350);
                        cameraStatus = request.responseCode == 503
                            ? mode == 2
                                ? $"Térmica sem quadro completo recente. Estado: {status?.thermalState ?? "não informado"}; erro: {status?.thermalError ?? 0}."
                                : $"HTTP acessível; sensor indisponível: {reason}"
                            : $"/{route}: HTTP {request.responseCode}, {request.error}. {reason}";
                        yield break;
                    }
                    // Decode against the requested route even if a newer
                    // status response arrives while the image is in flight.
                    ApplyCameraPreviewPayload(mode, request.downloadHandler.data, maskedThermal);
                }
                finally { cameraRequest = null; cameraCoroutine = null; }
            }
        }

        private void ApplyCameraPreview(int mode, byte[] bytes)
            => ApplyCameraPreviewPayload(mode, bytes, mode == 2 && CameraPreviewRoute(mode) == "thermal/masked");

        private void ApplyCameraPreviewPayload(int mode, byte[] bytes, bool maskedThermal)
        {
            Texture2D texture = null;
            try
            {
                int maskedPixels = 0;
                if (mode == 1)
                {
                    texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    if (!ImageConversion.LoadImage(texture, bytes)) throw new InvalidDataException("JPEG inválido.");
                }
                else
                {
                    int expectedLength = maskedThermal ? 872 : 776;
                    if (bytes == null || bytes.Length != expectedLength)
                        throw new InvalidDataException($"Térmica: esperado quadro de {expectedLength} bytes para a rota solicitada.");
                    float min = ReadFloat(bytes.AsSpan(0, 4)), max = ReadFloat(bytes.AsSpan(4, 4));
                    if (!IsFinite(min) || !IsFinite(max) || !IsFinite(max-min) || min > max)
                        throw new InvalidDataException("Escala térmica inválida.");
                    if (maskedThermal)
                    {
                        for (int pixel = 0; pixel < 768; ++pixel)
                            if ((bytes[776+(pixel>>3)] & (1<<(pixel&7))) == 0) maskedPixels++;
                        if (maskedPixels == 768)
                            throw new InvalidDataException("Térmica: a máscara não contém pixels válidos.");
                    }
                    cameraMinTemperature = min;
                    cameraMaxTemperature = max;
                    // The same saved orientation profile is used for the
                    // preview and the firmware's point-to-temperature lookup.
                    // Native (col,row) -> upright display (23-row,col), 24×32.
                    texture = new Texture2D(24, 32, maskedThermal ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
                    var colors = new Color32[768];
                    bool mirrorHorizontal = ((status?.thermalOrientationProfile ?? 0) & 2) != 0;
                    for (int row=0; row<24; row++)
                        for (int col=0; col<32; col++)
                        {
                            int pixel = row*32+col;
                            // SetPixels32 starts at the bottom-left of the texture.
                            int displayX = mirrorHorizontal ? row : 23-row;
                            bool valid = !maskedThermal || (bytes[776+(pixel>>3)] & (1<<(pixel&7))) != 0;
                            if (!valid)
                            {
                                colors[(31-col)*24+displayX] = new Color32(0, 0, 0, 0);
                                continue;
                            }
                            byte value = bytes[8+pixel];
                            float temperatureC = min + (max-min) * (value / 255f);
                            colors[(31-col)*24+displayX] =
                                ArScanner.Rendering.ThermalPointCloudRenderer.AbsoluteThermalPalette(temperatureC);
                        }
                    texture.SetPixels32(colors);
                    texture.Apply();
                }
                texture.filterMode = maskedThermal ? FilterMode.Point : FilterMode.Bilinear;
                if (cameraPreviewTexture != null) Destroy(cameraPreviewTexture);
                cameraPreviewTexture = texture;
                CameraThermalMaskedPixels = maskedPixels;
                CameraThermalPreviewHasMask = mode == 2 && maskedThermal;
                cameraPreviewReceivedTime = Time.unscaledTime;
                cameraStatus = mode == 1 ? "RGB: imagem recebida; projeção de cor na nuvem não calibrada."
                    : CameraThermalMaskedPixels > 0 ? ThermalPreviewNotice
                    : $"Térmica: {cameraMinTemperature:F1} a {cameraMaxTemperature:F1} °C; imagem recebida.";
            }
            catch (Exception ex)
            {
                if (texture != null) Destroy(texture);
                if (cameraPreviewTexture != null) Destroy(cameraPreviewTexture);
                cameraPreviewTexture = null;
                CameraThermalMaskedPixels = 0;
                CameraThermalPreviewHasMask = false;
                cameraStatus = ex.Message;
            }
        }

        private void CancelCameraRequest()
        {
            cameraRequest?.Abort();
            if (cameraCoroutine != null) StopCoroutine(cameraCoroutine);
            cameraCoroutine = null;
            cameraRequest?.Dispose();
            cameraRequest = null;
        }

        private void OnDisable() => Disconnect();

        private void OnDestroy()
        {
            Disconnect();
            if (cameraPreviewTexture != null) Destroy(cameraPreviewTexture);
        }
    }
}
