using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace ArScanner.Network
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ScanPointData
    {
        public float posX_mm;           // Coordenada X calculada no ESP32-S3 (mm)
        public float posY_mm;           // Coordenada Y calculada no ESP32-S3 (mm)
        public float posZ_mm;           // Coordenada Z calculada no ESP32-S3 (mm)
        public float temperatureC;      // Temperatura da MLX90640 no ponto (°C)
        public byte r;                  // Cor real amostrada da OV2640
        public byte g;
        public byte b;
        public byte surfaceFlags;       // Flags: 0=Livre, 1=Planar/Parede, 2=Hotspot (>28°C)
        public short pitchCentiDeg;     // Pitch do drone em centigraus (MPU6050)
        public short rollCentiDeg;      // Roll do drone em centigraus (MPU6050)
        public uint timestampMs;        // Timestamp do pacote
    }

    public class PointCloudTcpReceiver : MonoBehaviour
    {
        [Header("Configurações TCP")]
        [Tooltip("IP do Access Point criado pelo ESP32-S3 (ArScanner_Net)")]
        public string scannerIp = "192.168.4.1";
        public int scannerPort = 8888;
        public bool autoConnect = true;
        public float reconnectInterval = 2.0f;

        [Header("Diagnóstico e Status")]
        public bool isConnected = false;
        public int pointsInBuffer = 0;
        public long totalPointsReceived = 0;
        public float packetsPerSecond = 0f;

        // Fila thread-safe de pontos recebidos
        public ConcurrentQueue<ScanPointData> incomingPoints = new ConcurrentQueue<ScanPointData>();

        private TcpClient tcpClient;
        private Thread receiveThread;
        private volatile bool isRunning = false;

        private int ppsCounter = 0;
        private float ppsTimer = 0f;

        private void Start()
        {
            if (autoConnect)
            {
                ConnectToScanner();
            }
        }

        private void Update()
        {
            pointsInBuffer = incomingPoints.Count;

            // Calcula taxa de pacotes por segundo (PPS) para o HUD
            ppsTimer += Time.deltaTime;
            if (ppsTimer >= 1.0f)
            {
                packetsPerSecond = ppsCounter / ppsTimer;
                ppsCounter = 0;
                ppsTimer = 0f;
            }
        }

        public void ConnectToScanner()
        {
            if (isRunning) return;

            isRunning = true;
            receiveThread = new Thread(ReceiveLoop)
            {
                Name = "PointCloudTcpReceiverThread",
                IsBackground = true
            };
            receiveThread.Start();
        }

        public void Disconnect()
        {
            isRunning = false;
            try
            {
                if (tcpClient != null)
                {
                    tcpClient.Close();
                    tcpClient = null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TCP Client] Erro ao fechar socket: {ex.Message}");
            }

            isConnected = false;
        }

        private void ReceiveLoop()
        {
            int packetSize = Marshal.SizeOf(typeof(ScanPointData));
            // Buffer capaz de ler múltiplos pontos (lotes de 15 a 50 pontos por leitura de stream)
            byte[] buffer = new byte[packetSize * 30];

            while (isRunning)
            {
                try
                {
                    Debug.Log($"[TCP Client] Conectando ao ESP32-S3 em {scannerIp}:{scannerPort}...");
                    tcpClient = new TcpClient();
                    tcpClient.NoDelay = true;
                    
                    var connectResult = tcpClient.BeginConnect(scannerIp, scannerPort, null, null);
                    bool success = connectResult.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3));

                    if (!success || !tcpClient.Connected)
                    {
                        throw new SocketException((int)SocketError.TimedOut);
                    }

                    tcpClient.EndConnect(connectResult);
                    NetworkStream stream = tcpClient.GetStream();
                    stream.ReadTimeout = 4000;
                    isConnected = true;
                    Debug.Log("[TCP Client] Conexão TCP estabelecida com sucesso!");

                    int leftover = 0;
                    byte[] singlePacket = new byte[packetSize];

                    while (isRunning && tcpClient.Connected)
                    {
                        int bytesRead = stream.Read(buffer, leftover, buffer.Length - leftover);
                        if (bytesRead <= 0)
                        {
                            throw new IOException("Conexão remota encerrada pelo Scanner.");
                        }

                        int totalBytes = bytesRead + leftover;
                        int offset = 0;

                        while (totalBytes - offset >= packetSize)
                        {
                            Buffer.BlockCopy(buffer, offset, singlePacket, 0, packetSize);
                            ScanPointData point = ByteArrayToStructure<ScanPointData>(singlePacket);
                            incomingPoints.Enqueue(point);
                            totalPointsReceived++;
                            ppsCounter++;
                            offset += packetSize;
                        }

                        leftover = totalBytes - offset;
                        if (leftover > 0)
                        {
                            Buffer.BlockCopy(buffer, offset, buffer, 0, leftover);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (isRunning)
                    {
                        isConnected = false;
                        Debug.LogWarning($"[TCP Client] Desconectado: {ex.Message}. Reconectando em {reconnectInterval}s...");
                        Thread.Sleep((int)(reconnectInterval * 1000));
                    }
                }
                finally
                {
                    if (tcpClient != null)
                    {
                        tcpClient.Close();
                        tcpClient = null;
                    }
                    isConnected = false;
                }
            }
        }

        public void EnqueuePoint(ScanPointData point)
        {
            incomingPoints.Enqueue(point);
            totalPointsReceived++;
            ppsCounter++;
        }

        private static T ByteArrayToStructure<T>(byte[] bytes) where T : struct
        {
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                return (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
            }
            finally
            {
                handle.Free();
            }
        }

        private void OnDestroy()
        {
            Disconnect();
            if (receiveThread != null && receiveThread.IsAlive)
            {
                receiveThread.Abort();
            }
        }
    }
}
