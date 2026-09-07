using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

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

    public enum UwbTransportMode
    {
        UDP,
        TCP,
        Simulated
    }

    public class UwbDataReceiver : MonoBehaviour
    {
        [Header("Modo de Transporte UWB")]
        [Tooltip("Modo de recepção dos dados ToF da base UWB")]
        public UwbTransportMode transportMode = UwbTransportMode.UDP;

        [Header("Configuração UDP (Padrão Android / Wi-Fi)")]
        [Tooltip("Porta UDP na qual a base UWB ou bridge transmite os pacotes UwbPositionPacket")]
        public int udpPort = 9999;

        [Header("Configuração TCP (Opcional)")]
        public string tcpHost = "192.168.4.2";
        public int tcpPort = 9999;

        [Header("Posição e Diagnóstico")]
        public Vector3 LatestPosition = Vector3.zero;
        public float DistanceAnchor1 = 0f;
        public float DistanceAnchor2 = 0f;
        public float DistanceAnchor3 = 0f;
        public bool IsConnected = false;
        public long PacketsReceived = 0;

        private UdpClient udpClient;
        private TcpClient tcpClient;
        private Thread receiveThread;
        private volatile bool isRunning = false;

        private void Start()
        {
            StartReceiver();
        }

        public void StartReceiver()
        {
            StopReceiver();
            isRunning = true;

            switch (transportMode)
            {
                case UwbTransportMode.UDP:
                    StartUdpReceiver();
                    break;
                case UwbTransportMode.TCP:
                    StartTcpReceiver();
                    break;
                case UwbTransportMode.Simulated:
                    IsConnected = true;
                    break;
            }
        }

        public void StopReceiver()
        {
            isRunning = false;

            try
            {
                if (udpClient != null)
                {
                    udpClient.Close();
                    udpClient = null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UWB UDP] Erro ao encerrar socket: {ex.Message}");
            }

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
                Debug.LogWarning($"[UWB TCP] Erro ao encerrar socket: {ex.Message}");
            }

            if (receiveThread != null && receiveThread.IsAlive)
            {
                receiveThread.Abort();
                receiveThread = null;
            }

            IsConnected = false;
        }

        private void StartUdpReceiver()
        {
            try
            {
                udpClient = new UdpClient(udpPort);
                receiveThread = new Thread(UdpLoop)
                {
                    Name = "UwbUdpReceiverThread",
                    IsBackground = true
                };
                receiveThread.Start();
                IsConnected = true;
                Debug.Log($"[UWB Receiver] Escutando pacotes UWB na porta UDP {udpPort} (Cross-Platform / Android)...");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UWB Receiver] Falha ao iniciar UDP {udpPort}: {ex.Message}");
            }
        }

        private void StartTcpReceiver()
        {
            receiveThread = new Thread(TcpLoop)
            {
                Name = "UwbTcpReceiverThread",
                IsBackground = true
            };
            receiveThread.Start();
        }

        private void UdpLoop()
        {
            int packetSize = Marshal.SizeOf(typeof(UwbPositionData));
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, udpPort);

            while (isRunning && udpClient != null)
            {
                try
                {
                    byte[] data = udpClient.Receive(ref remoteEP);
                    if (data.Length >= packetSize)
                    {
                        UwbPositionData posPkt = ByteArrayToStructure<UwbPositionData>(data);
                        ProcessPacket(posPkt);
                    }
                }
                catch (SocketException)
                {
                    // Socket encerrado intencionalmente
                }
                catch (Exception ex)
                {
                    if (isRunning)
                    {
                        Debug.LogWarning($"[UWB UDP Loop] Erro: {ex.Message}");
                    }
                }
            }
        }

        private void TcpLoop()
        {
            int packetSize = Marshal.SizeOf(typeof(UwbPositionData));
            byte[] buffer = new byte[packetSize];

            while (isRunning)
            {
                try
                {
                    tcpClient = new TcpClient();
                    var ar = tcpClient.BeginConnect(tcpHost, tcpPort, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3)) || !tcpClient.Connected)
                    {
                        throw new SocketException((int)SocketError.TimedOut);
                    }
                    tcpClient.EndConnect(ar);

                    NetworkStream stream = tcpClient.GetStream();
                    IsConnected = true;

                    while (isRunning && tcpClient.Connected)
                    {
                        int bytesRead = 0;
                        while (bytesRead < packetSize && isRunning)
                        {
                            int r = stream.Read(buffer, bytesRead, packetSize - bytesRead);
                            if (r <= 0) break;
                            bytesRead += r;
                        }

                        if (bytesRead == packetSize)
                        {
                            UwbPositionData posPkt = ByteArrayToStructure<UwbPositionData>(buffer);
                            ProcessPacket(posPkt);
                        }
                    }
                }
                catch
                {
                    IsConnected = false;
                    if (isRunning) Thread.Sleep(2000);
                }
                finally
                {
                    if (tcpClient != null)
                    {
                        tcpClient.Close();
                        tcpClient = null;
                    }
                }
            }
        }

        private void ProcessPacket(UwbPositionData data)
        {
            // Coordenadas métricas em espaço Unity (X = direita, Y = altura, Z = profundidade)
            LatestPosition = new Vector3(data.tagX, data.tagY, data.tagZ);
            DistanceAnchor1 = data.distAnchor1;
            DistanceAnchor2 = data.distAnchor2;
            DistanceAnchor3 = data.distAnchor3;
            PacketsReceived++;
            IsConnected = true;
        }

        public void SetSimulatedPosition(Vector3 simPos)
        {
            LatestPosition = simPos;
            IsConnected = true;
            PacketsReceived++;
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
            StopReceiver();
        }
    }
}
