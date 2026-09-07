using System;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MenuViewer : MonoBehaviour
{
    [Header("UI Canvas (Opcional se atribuido no Inspector)")]
    public Text textoStatus;
    public Button botaoIniciar;

    [Header("Configuracao de Enderecos")]
    public string scannerIp = "192.168.4.1";
    public int scannerPort = 8888;

    [Header("Status em Tempo Real")]
    public bool isUsbConnected = false;
    public bool isScannerAvailable = false;

    private Thread checkThread;
    private volatile bool isChecking = true;

    private void Start()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        if (botaoIniciar != null)
        {
            botaoIniciar.interactable = false;
            botaoIniciar.onClick.AddListener(IniciarComHardware);
        }

        checkThread = new Thread(BackgroundCheckLoop)
        {
            Name = "HardwareVerificationThread",
            IsBackground = true
        };
        checkThread.Start();
    }

    private void Update()
    {
        bool bothConnected = isUsbConnected && isScannerAvailable;

        if (botaoIniciar != null)
        {
            botaoIniciar.interactable = bothConnected;
        }

        if (textoStatus != null)
        {
            if (bothConnected)
            {
                textoStatus.text = "Tudo Pronto!\nUSB-C: OK | Scanner Wi-Fi: OK";
                textoStatus.color = Color.green;
            }
            else if (!isUsbConnected && !isScannerAvailable)
            {
                textoStatus.text = "Aguardando Hardware...\nConecte o USB-C e o Wi-Fi";
                textoStatus.color = Color.yellow;
            }
            else if (!isUsbConnected)
            {
                textoStatus.text = "Scanner Encontrado!\nConecte o cabo USB-C da placa UWB";
                textoStatus.color = new Color(1f, 0.5f, 0f);
            }
            else
            {
                textoStatus.text = "USB-C Conectado!\nConecte a rede Wi-Fi 'ArScanner_Net'";
                textoStatus.color = new Color(1f, 0.5f, 0f);
            }
        }
    }

    private void BackgroundCheckLoop()
    {
        while (isChecking)
        {
            // 1. Checa conexao USB-C
            isUsbConnected = CheckUsbConnection();

            // 2. Checa se o ESP32 do Scanner responde na rede TCP Wi-Fi
            isScannerAvailable = CheckScannerNetwork();

            Thread.Sleep(1500);
        }
    }

    private bool CheckUsbConnection()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var usbManager = currentActivity.Call<AndroidJavaObject>("getSystemService", "usb"))
            {
                if (usbManager != null)
                {
                    var deviceList = usbManager.Call<AndroidJavaObject>("getDeviceList");
                    if (deviceList != null)
                    {
                        int count = deviceList.Call<int>("size");
                        return count > 0;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[MenuViewer] Erro ao checar USB Android: " + ex.Message);
        }
        return false;
#else
        return true;
#endif
    }

    private bool CheckScannerNetwork()
    {
        try
        {
            using (TcpClient client = new TcpClient())
            {
                var result = client.BeginConnect(scannerIp, scannerPort, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(800));
                if (success && client.Connected)
                {
                    client.EndConnect(result);
                    return true;
                }
            }
        }
        catch
        {
        }
        return false;
    }

    public void IniciarComHardware()
    {
        GlobalData.IsSimulationMode = false;
        GlobalData.IpAlvo = scannerIp;
        CarregarCenaViewer();
    }

    public void IniciarModoSimulacao()
    {
        GlobalData.IsSimulationMode = true;
        CarregarCenaViewer();
    }

    private void CarregarCenaViewer()
    {
        isChecking = false;
        if (checkThread != null && checkThread.IsAlive)
        {
            checkThread.Abort();
        }

        if (Application.CanStreamedLevelBeLoaded("CenaViewer"))
        {
            SceneManager.LoadScene("CenaViewer");
        }
        else if (Application.CanStreamedLevelBeLoaded("CenaViewerTCC"))
        {
            SceneManager.LoadScene("CenaViewerTCC");
        }
        else
        {
            SceneManager.LoadScene(1);
        }
    }

    private void OnDestroy()
    {
        isChecking = false;
        if (checkThread != null && checkThread.IsAlive)
        {
            checkThread.Abort();
        }
    }

    private void OnGUI()
    {
        float scale = Screen.height / 720.0f;
        scale = Mathf.Clamp(scale, 1.0f, 2.5f);

        GUI.skin.label.fontSize = Mathf.RoundToInt(13 * scale);
        GUI.skin.button.fontSize = Mathf.RoundToInt(14 * scale);
        GUI.skin.box.fontSize = Mathf.RoundToInt(15 * scale);

        float panelWidth = Mathf.Min(Screen.width * 0.92f, 480f * scale);
        float panelHeight = Mathf.Min(Screen.height * 0.88f, 450f * scale);
        float panelX = (Screen.width - panelWidth) * 0.5f;
        float panelY = (Screen.height - panelHeight) * 0.5f;

        GUILayout.BeginArea(new Rect(panelX, panelY, panelWidth, panelHeight), GUI.skin.box);

        GUILayout.Space(8 * scale);
        GUILayout.Label("<b><color=#00E5FF>SISTEMA AR SCANNER 3D - TCC</color></b>", CenteredLabel());
        GUILayout.Label("<color=#CCCCCC>Verificacao de Hardware e Conexoes</color>", CenteredLabel());
        GUILayout.Space(12 * scale);

        // Card 1: USB-C
        string usbColor = isUsbConnected ? "#00FF66" : "#FF5555";
        string usbStatus = isUsbConnected ? "CONECTADO (OTG OK)" : "DESCONECTADO";
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label($"<b>Placa Visualizador UWB (USB-C):</b> <color={usbColor}>● {usbStatus}</color>");
        if (!isUsbConnected)
        {
            GUILayout.Label("<color=#AAAAAA><size=11>Conecte o cabo USB-C do ESP32 na porta do celular.</size></color>");
        }
        GUILayout.EndVertical();

        GUILayout.Space(8 * scale);

        // Card 2: Wi-Fi Scanner
        string wifiColor = isScannerAvailable ? "#00FF66" : "#FF5555";
        string wifiStatus = isScannerAvailable ? "DISPONIVEL (192.168.4.1)" : "NAO ENCONTRADO";
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label($"<b>Scanner 3D Aereo (Wi-Fi):</b> <color={wifiColor}>● {wifiStatus}</color>");
        if (!isScannerAvailable)
        {
            GUILayout.Label("<color=#AAAAAA><size=11>Conecte o celular na rede Wi-Fi 'ArScanner_Net'.</size></color>");
        }
        GUILayout.EndVertical();

        GUILayout.Space(15 * scale);

        // Botao 1: Iniciar com Hardware Real
        bool canStartReal = isUsbConnected && isScannerAvailable;
        GUI.enabled = canStartReal;
        string btnRealText = canStartReal ? ">> INICIAR VISUALIZADOR AR" : "[ Aguardando Hardware Conectar... ]";
        if (GUILayout.Button(btnRealText, GUILayout.Height(45 * scale)))
        {
            IniciarComHardware();
        }
        GUI.enabled = true;

        GUILayout.Space(10 * scale);

        // Botao 2: Modo Demonstracao / Simulacao
        GUI.backgroundColor = new Color(0.1f, 0.75f, 0.35f);
        if (GUILayout.Button(">> MODO SIMULACAO (TESTAR SEM HARDWARE)", GUILayout.Height(48 * scale)))
        {
            IniciarModoSimulacao();
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Label("<color=#AAAAAA><size=11>Testar a visao AR com nuvem 3D e calor simulados.</size></color>", CenteredLabel());

        GUILayout.EndArea();
    }

    private GUIStyle CenteredLabel()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter
        };
        return style;
    }
}
