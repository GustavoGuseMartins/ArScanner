using System;
using System.IO.Ports;
using System.Threading;
using UnityEngine;

namespace ArScanner.Network
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
    public struct UwbPositionData
    {
        public float tagX;
        public float tagY;
        public float tagZ;
        public uint timestampMs;
    }

    public class UwbDataReceiver : MonoBehaviour
    {
        [Header("Configuração Serial / UWB Base")]
        public string portName = "COM3";
        public int baudRate = 115200;

        public Vector3 LatestPosition { get; private set; }
        public bool IsConnected { get; private set; }

        private SerialPort serialPort;
        private Thread readThread;
        private bool isRunning = false;

        private void Start()
        {
            StartReceiver();
        }

        public void StartReceiver()
        {
            try
            {
                serialPort = new SerialPort(portName, baudRate);
                serialPort.Open();
                IsConnected = true;
                isRunning = true;

                readThread = new Thread(ReadLoop) { IsBackground = true };
                readThread.Start();
                Debug.Log($"[UWB Receiver] Porta {portName} aberta com sucesso!");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UWB Receiver] Não foi possível abrir porta {portName}: {ex.Message}");
            }
        }

        private void ReadLoop()
        {
            int packetSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(UwbPositionData));
            byte[] buffer = new byte[packetSize];

            while (isRunning && serialPort != null && serialPort.IsOpen)
            {
                try
                {
                    int bytesRead = 0;
                    while (bytesRead < packetSize)
                    {
                        int r = serialPort.Read(buffer, bytesRead, packetSize - bytesRead);
                        if (r <= 0) break;
                        bytesRead += r;
                    }

                    if (bytesRead == packetSize)
                    {
                        UwbPositionData data = ByteArrayToStructure<UwbPositionData>(buffer);
                        LatestPosition = new Vector3(data.tagX, data.tagY, data.tagZ);
                    }
                }
                catch
                {
                    // Ignora erros de timeout de leitura temporários
                }
            }
        }

        private static T ByteArrayToStructure<T>(byte[] bytes) where T : struct
        {
            System.Runtime.InteropServices.GCHandle handle = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
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
            if (serialPort != null && serialPort.IsOpen) serialPort.Close();
        }
    }
}
