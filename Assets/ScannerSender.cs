using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine.UI; // <--- NECESSÁRIO PARA MEXER NO TEXTO DO BOTÃO

public class ScannerSender : MonoBehaviour
{
    [Header("UI de Controle")]
    public Text textoDoBotao; // Arraste o Texto de dentro do botão aqui

    [Header("Configurações de Rede")]
    public string ipDoReceptor = "192.168.0.XX";
    public int porta = 8080;

    [Header("Sensores AR")]
    public ARRaycastManager raycastManager;
    public ARSession arSession;

    private const int PONTOS_POR_PACOTE = 20;
    public float raioDoSpray = 350f;

    // Variáveis internas
    private UdpClient udpClient;
    private List<ARRaycastHit> hits = new List<ARRaycastHit>();
    private StringBuilder pacoteBuilder = new StringBuilder();

    // --- NOVA VARIÁVEL DE CONTROLE ---
    private bool escaneamentoAtivo = true; // Começa ligado

    void Start()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        udpClient = new UdpClient();

        if (!string.IsNullOrEmpty(GlobalData.IpAlvo))
        {   
            ipDoReceptor = GlobalData.IpAlvo;
        }

        AtualizarTextoBotao(); // Garante que o texto comece certo
    }

    void Update()
    {
        // 1. Se o AR não estiver pronto OU o escaneamento estiver pausado, não faz nada
        if (ARSession.state != ARSessionState.SessionTracking || !escaneamentoAtivo) return;

        pacoteBuilder.Clear();
        int pontosValidos = 0;

        for (int i = 0; i < PONTOS_POR_PACOTE; i++)
        {
            Pose? poseEncontrada = DispararRaioAleatorio();

            if (poseEncontrada.HasValue)
            {
                pacoteBuilder.Append(poseEncontrada.Value.position.x.ToString("F3", CultureInfo.InvariantCulture)).Append(",");
                pacoteBuilder.Append(poseEncontrada.Value.position.y.ToString("F3", CultureInfo.InvariantCulture)).Append(",");
                pacoteBuilder.Append(poseEncontrada.Value.position.z.ToString("F3", CultureInfo.InvariantCulture)).Append(",");

                pacoteBuilder.Append(poseEncontrada.Value.rotation.x.ToString("F3", CultureInfo.InvariantCulture)).Append(",");
                pacoteBuilder.Append(poseEncontrada.Value.rotation.y.ToString("F3", CultureInfo.InvariantCulture)).Append(",");
                pacoteBuilder.Append(poseEncontrada.Value.rotation.z.ToString("F3", CultureInfo.InvariantCulture)).Append(",");
                pacoteBuilder.Append(poseEncontrada.Value.rotation.w.ToString("F3", CultureInfo.InvariantCulture));

                pacoteBuilder.Append(";");
                pontosValidos++;
            }
        }

        if (pontosValidos > 0)
        {
            EnviarPacote(pacoteBuilder.ToString());
        }
    }

    // --- NOVA FUNÇÃO PARA O BOTÃO ---
    public void AlternarEscaneamento()
    {
        escaneamentoAtivo = !escaneamentoAtivo; // Inverte (se true vira false, se false vira true)
        AtualizarTextoBotao();
    }

    void AtualizarTextoBotao()
    {
        if (textoDoBotao != null)
        {
            if (escaneamentoAtivo)
            {
                textoDoBotao.text = "PAUSAR SCAN";
                textoDoBotao.color = Color.red; // Visual de "Parar"
            }
            else
            {
                textoDoBotao.text = "RETOMAR SCAN";
                textoDoBotao.color = Color.green; // Visual de "Continuar"
            }
        }
    }

    Pose? DispararRaioAleatorio()
    {
        Vector2 centro = new Vector2(Screen.width / 2, Screen.height / 2);
        Vector2 aleatorio = Random.insideUnitCircle * raioDoSpray;
        Vector2 mira = centro + aleatorio;

        if (raycastManager.Raycast(mira, hits, TrackableType.PlaneWithinPolygon | TrackableType.FeaturePoint))
        {
            return hits[0].pose;
        }
        return null;
    }

    void EnviarPacote(string mensagem)
    {
        try
        {
            byte[] dados = Encoding.UTF8.GetBytes(mensagem);
            udpClient.Send(dados, dados.Length, ipDoReceptor, porta);
        }
        catch { }
    }
}