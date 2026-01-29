using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class MenuScanner : MonoBehaviour
{
    [Header("UI")]
    public Text textoStatus;
    public Button botaoIniciar; // Arraste o botão aqui

    private UdpClient udpDiscovery;
    private Thread threadBroadcast;
    private bool conectado = false;
    private string ipEncontrado = "";

    void Start()
    {
        botaoIniciar.interactable = false; // Só libera quando achar o S9

        // Inicia o sistema de "Grito"
        udpDiscovery = new UdpClient();
        udpDiscovery.EnableBroadcast = true; // Permite falar com todos da rede

        threadBroadcast = new Thread(LoopDiscovery);
        threadBroadcast.IsBackground = true;
        threadBroadcast.Start();
    }

    void Update()
    {
        if (conectado)
        {
            textoStatus.text = "Visualizador Encontrado!\nIP: " + ipEncontrado;
            textoStatus.color = Color.green;
            botaoIniciar.interactable = true;
        }
    }

    // Função do Botão Iniciar
    public void CarregarCenaDoScanner()
    {
        // Salva o IP descoberto na memória global
        GlobalData.IpAlvo = ipEncontrado;

        // Fecha a thread de busca
        if (udpDiscovery != null) udpDiscovery.Close();
        if (threadBroadcast != null) threadBroadcast.Abort();

        // Carrega a cena AR (Use o nome exato da sua cena antiga)
        SceneManager.LoadScene("CenaScanner");
    }

    void LoopDiscovery()
    {
        // Configura para enviar para TODOS (255.255.255.255)
        IPEndPoint broadcastEP = new IPEndPoint(IPAddress.Broadcast, GlobalData.PortaDescoberta);

        // Escuta respostas na mesma porta
        udpDiscovery.Client.Bind(new IPEndPoint(IPAddress.Any, GlobalData.PortaDescoberta));

        while (!conectado)
        {
            try
            {
                // 1. GRITA: "SOU_SCANNER"
                byte[] msg = Encoding.UTF8.GetBytes("SOU_SCANNER");
                udpDiscovery.Send(msg, msg.Length, broadcastEP);

                // 2. ESCUTA SE ALGUÉM RESPONDEU (Timeout curto para não travar o grito)
                if (udpDiscovery.Available > 0)
                {
                    IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                    byte[] dadosRecebidos = udpDiscovery.Receive(ref remoteEP);
                    string resposta = Encoding.UTF8.GetString(dadosRecebidos);

                    if (resposta == "SOU_VIEWER")
                    {
                        // OPA! O S9 respondeu. Pegamos o IP dele.
                        ipEncontrado = remoteEP.Address.ToString();
                        conectado = true;
                    }
                }

                Thread.Sleep(1000); // Grita a cada 1 segundo
            }
            catch { }
        }
    }
}