using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class MenuViewer : MonoBehaviour
{
    [Header("UI")]
    public Text textoStatus;
    public Button botaoIniciar;

    private UdpClient udpListener;
    private Thread threadListening;
    private bool conectado = false;
    private string ipScanner = "";

    void Start()
    {
        botaoIniciar.interactable = false;

        udpListener = new UdpClient(GlobalData.PortaDescoberta);
        udpListener.EnableBroadcast = true;

        threadListening = new Thread(LoopOuvinte);
        threadListening.IsBackground = true;
        threadListening.Start();
    }

    void Update()
    {
        if (conectado)
        {
            textoStatus.text = "Scanner Encontrado!\nIP: " + ipScanner;
            textoStatus.color = Color.green;
            botaoIniciar.interactable = true;
        }
    }

    public void CarregarCenaDoViewer()
    {
        // O Viewer na verdade não precisa saber o IP do Scanner para receber dados (ele só escuta),
        // mas é bom ter caso precisemos enviar comandos de volta.
        GlobalData.IpAlvo = ipScanner;

        if (udpListener != null) udpListener.Close();
        if (threadListening != null) threadListening.Abort();

        SceneManager.LoadScene("CenaViewer");
    }

    void LoopOuvinte()
    {
        while (!conectado)
        {
            try
            {
                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                byte[] dados = udpListener.Receive(ref remoteEP); // Bloqueia até chegar algo
                string msg = Encoding.UTF8.GetString(dados);

                if (msg == "SOU_SCANNER")
                {
                    // Achamos o S23!
                    ipScanner = remoteEP.Address.ToString();

                    // Responde para ele saber que existimos
                    byte[] resposta = Encoding.UTF8.GetBytes("SOU_VIEWER");
                    udpListener.Send(resposta, resposta.Length, remoteEP); // Manda direto pro IP dele

                    conectado = true;
                }
            }
            catch { }
        }
    }
}