using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;
using System.Globalization;

public class ScannerSender : MonoBehaviour
{
    [Header("Configurações de Rede")]
    public string ipDoReceptor = "192.168.18.144"; // <--- CONFIRA SEU IP AQUI
    public int porta = 8080;

    [Header("Sensores AR")]
    public ARRaycastManager raycastManager;
    public ARSession arSession; // Arraste o AR Session da hierarquia para cá

    // Configurações do "Spray"
    // 20 é o limite seguro para enviar Posição + Rotação sem estourar o Wi-Fi (MTU)
    private const int PONTOS_POR_PACOTE = 20;

    [Tooltip("O quão espalhado é o jato de tinta (em pixels)")]
    public float raioDoSpray = 350f;

    // Variáveis internas
    private UdpClient udpClient;
    private List<ARRaycastHit> hits = new List<ARRaycastHit>();
    private StringBuilder pacoteBuilder = new StringBuilder();

    void Start()
    {
        // Impede a tela de desligar/bloquear
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        udpClient = new UdpClient();
    }

    void Update()
    {
        // Só escaneia se o ARCore estiver rastreando o mundo corretamente
        if (ARSession.state != ARSessionState.SessionTracking) return;

        // Limpa o construtor de texto para o novo frame
        pacoteBuilder.Clear();
        int pontosValidos = 0;

        // Tenta coletar N pontos neste frame (Loop do Spray)
        for (int i = 0; i < PONTOS_POR_PACOTE; i++)
        {
            Pose? poseEncontrada = DispararRaioAleatorio();

            if (poseEncontrada.HasValue)
            {
                // Formata os dados: PX,PY,PZ,RX,RY,RZ,RW;
                // Usa InvariantCulture para garantir PONTO (.) e não VÍRGULA

                // Posição
                pacoteBuilder.Append(poseEncontrada.Value.position.x.ToString("F3", CultureInfo.InvariantCulture)).Append(",");
                pacoteBuilder.Append(poseEncontrada.Value.position.y.ToString("F3", CultureInfo.InvariantCulture)).Append(",");
                pacoteBuilder.Append(poseEncontrada.Value.position.z.ToString("F3", CultureInfo.InvariantCulture)).Append(",");

                // Rotação (Quaternion)
                pacoteBuilder.Append(poseEncontrada.Value.rotation.x.ToString("F3", CultureInfo.InvariantCulture)).Append(",");
                pacoteBuilder.Append(poseEncontrada.Value.rotation.y.ToString("F3", CultureInfo.InvariantCulture)).Append(",");
                pacoteBuilder.Append(poseEncontrada.Value.rotation.z.ToString("F3", CultureInfo.InvariantCulture)).Append(",");
                pacoteBuilder.Append(poseEncontrada.Value.rotation.w.ToString("F3", CultureInfo.InvariantCulture));

                // Finaliza o ponto com ponto e vírgula
                pacoteBuilder.Append(";");

                pontosValidos++;
            }
        }

        // Se coletamos algum ponto válido, envia o pacote via UDP
        if (pontosValidos > 0)
        {
            EnviarPacote(pacoteBuilder.ToString());
        }
    }

    Pose? DispararRaioAleatorio()
    {
        // Centro da tela
        Vector2 centro = new Vector2(Screen.width / 2, Screen.height / 2);

        // Gera um desvio aleatório (Spray)
        Vector2 aleatorio = Random.insideUnitCircle * raioDoSpray;
        Vector2 mira = centro + aleatorio;

        // Raycast contra Planos detectados e Pontos de Referência (Feature Points)
        if (raycastManager.Raycast(mira, hits, TrackableType.PlaneWithinPolygon | TrackableType.FeaturePoint))
        {
            // Retorna a Pose (Posição + Rotação da superfície)
            return hits[0].pose;
        }
        return null; // Não bateu em nada
    }

    void EnviarPacote(string mensagem)
    {
        try
        {
            byte[] dados = Encoding.UTF8.GetBytes(mensagem);
            udpClient.Send(dados, dados.Length, ipDoReceptor, porta);
        }
        catch
        {
            // Ignora erros de rede momentâneos para não travar o app
        }
    }
}