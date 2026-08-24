using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace ArScanner.Network
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
    public struct ScanPointData
    {
        public float baseAngleDeg;
        public float lidarAngleDeg;
        public float distanceMm;
        public float temperatureC;
        public byte r, g, b;
        public uint timestampMs;
    }

    public class PointCloudTcpReceiver : MonoBehaviour
    {
        [Header("Configurações TCP")]
        public string scannerIp = "192.168.4.1";
        public int scannerPort = 8888;
        public bool autoConnect = true;

        [Header("Status de Conexão")]
        public bool isConnected = false;
        public int pointsInBuffer = 0;

        private TcpClient tcpClient;
        private Thread receiveThread;
        private bool isRunning = false;

        // Fila thread-safe de pacotes recebidos
        public ConcurrentQueue<ScanPointData> incomingPoints = new ConcurrentQueue<ScanPointData>();

        private void Start()
        {
            if (autoConnect)
            {
                ConnectToScanner();
            }
        }

        public void ConnectToScanner()
        {
            if (isRunning) return;

            isRunning = true;
            receiveThread = new Thread(ReceiveLoop)
            {
                IsBackground = true
            };
            receiveThread.Start();
        }

        private void ReceiveLoop()
        {
            while (isRunning)
            {
                try
                {
                    Debug.Log($"[TCP Client] Conectando ao Scanner em {scannerIp}:{scannerPort}...");
                    tcpClient = new TcpClient();
                    tcpClient.Connect(scannerIp, scannerPort);
                    NetworkStream stream = tcpClient.GetStream();
                    isConnected = true;
                    Debug.Log("[TCP Client] Conectado com sucesso ao Scanner!");

                    int packetSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(ScanPointData));
                    byte[] buffer = new byte[packetSize];

                    while (isRunning && tcpClient.Connected)
                    {
                        int bytesRead = 0;
                        while (bytesRead < packetSize)
                        {
                            int read = stream.Read(buffer, bytesRead, packetSize - bytesRead);
                            if (read <= 0) break;
                            bytesRead += read;
                        }

                        if (bytesRead == packetSize)
                        {
                            ScanPointData data = ByteArrayToStructure<ScanPointData>(buffer);
                            incomingPoints.Enqueue(data);
                            pointsInBuffer = incomingPoints.Count;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[TCP Client] Conexão TCP desconectada: {ex.Message}. Reconectando em 2s...");
                    isConnected = false;
                    Thread.Sleep(2000);
                }
            }
        }

        private static T ByteArrayToStructure<T>(byte[] bytes) where T : struct
        {
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                return (T)System.Runtime.InteropServices.Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
            }
            finally
            {
                handle.Free();
            }
        }

        private void OnDestroy()
        {
            isRunning = false;
            if (tcpClient != null) tcpClient.Close();
            if (receiveThread != null && receiveThread.IsAlive) receiveThread.Abort();
        }
    }
}
