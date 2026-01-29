using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Globalization;
using System.Collections.Concurrent;
using System.Collections.Generic;

public class ViewerReceiver : MonoBehaviour
{
    [Header("Visualização")]
    public GameObject pontoPrefab; // Arraste seu Prefab de Triângulo/Quad aqui

    [Header("Configurações de Memória")]
    [Range(100, 3000)]
    public int maximoPontos = 1000000; // Limite de triângulos na tela

    [Tooltip("Distância mínima em metros entre dois pontos (Filtro de Grade)")]
    public float distanciaMinima = 0.05f;

    [Header("Rede")]
    public int porta = 8080;

    // Variáveis internas
    private UdpClient udpServer;
    private Thread threadRecebimento;
    private ConcurrentQueue<Pose> filaDeEntrada = new ConcurrentQueue<Pose>();

    // Estruturas do Sistema de Grade (Voxel Grid)
    struct PontoAtivo { public GameObject obj; public Vector3Int gradeKey; }
    private Queue<PontoAtivo> pontosVivos = new Queue<PontoAtivo>();
    private HashSet<Vector3Int> gradeOcupada = new HashSet<Vector3Int>();

    void Start()
    {
        // Impede a tela de desligar enquanto usa o app
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        udpServer = new UdpClient(porta);
        threadRecebimento = new Thread(ReceberDados);
        threadRecebimento.IsBackground = true;
        threadRecebimento.Start();
    }

    void Update()
    {
        ProcessarFila();
    }

    void ProcessarFila()
    {
        // Enquanto houver dados chegando da rede, processa na Main Thread
        while (filaDeEntrada.TryDequeue(out Pose dadosPonto))
        {
            // 1. Calcula a chave da grade (Voxel)
            Vector3Int chaveGrade = new Vector3Int(
                Mathf.RoundToInt(dadosPonto.position.x / distanciaMinima),
                Mathf.RoundToInt(dadosPonto.position.y / distanciaMinima),
                Mathf.RoundToInt(dadosPonto.position.z / distanciaMinima)
            );

            // 2. Filtro: Se já existe algo nesta célula, ignora
            if (gradeOcupada.Contains(chaveGrade)) continue;

            // 3. Instancia o objeto visual
            // Importante: Usa a ROTAÇÃO que veio do S23 para alinhar à superfície
            GameObject novoObj = Instantiate(pontoPrefab, dadosPonto.position, dadosPonto.rotation);

            // Ajuste de escala (opcional: ajuste conforme o tamanho do seu quad)
            float tamanho = 0.08f;
            novoObj.transform.localScale = new Vector3(tamanho, tamanho, 1f);

            // 4. Registra no sistema de gerenciamento
            PontoAtivo novoPonto = new PontoAtivo { obj = novoObj, gradeKey = chaveGrade };
            pontosVivos.Enqueue(novoPonto);
            gradeOcupada.Add(chaveGrade);

            // 5. Verifica limite máximo (FIFO)
            if (pontosVivos.Count > maximoPontos)
            {
                RemoverPontoMaisAntigo();
            }
        }
    }

    void RemoverPontoMaisAntigo()
    {
        if (pontosVivos.Count > 0)
        {
            PontoAtivo pontoMorto = pontosVivos.Dequeue();
            if (pontoMorto.obj != null) Destroy(pontoMorto.obj);
            gradeOcupada.Remove(pontoMorto.gradeKey);
        }
    }

    // Thread separada para ler UDP sem travar o jogo
    void ReceberDados()
    {
        while (true)
        {
            try
            {
                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, porta);
                byte[] dados = udpServer.Receive(ref remoteEP);
                string texto = Encoding.UTF8.GetString(dados);

                string[] listaDePontos = texto.Split(';');

                foreach (string pontoTexto in listaDePontos)
                {
                    if (string.IsNullOrEmpty(pontoTexto)) continue;

                    string[] v = pontoTexto.Split(',');

                    // Valida se temos os 7 valores (Posição + Rotação)
                    if (v.Length >= 7)
                    {
                        Vector3 pos = new Vector3(
                            float.Parse(v[0], CultureInfo.InvariantCulture),
                            float.Parse(v[1], CultureInfo.InvariantCulture),
                            float.Parse(v[2], CultureInfo.InvariantCulture)
                        );

                        Quaternion rot = new Quaternion(
                            float.Parse(v[3], CultureInfo.InvariantCulture),
                            float.Parse(v[4], CultureInfo.InvariantCulture),
                            float.Parse(v[5], CultureInfo.InvariantCulture),
                            float.Parse(v[6], CultureInfo.InvariantCulture)
                        );

                        filaDeEntrada.Enqueue(new Pose(pos, rot));
                    }
                }
            }
            catch
            {
                // Silencia erros de timeout ou pacote corrompido para manter o loop vivo
            }
        }
    }

    private void OnDestroy()
    {
        if (udpServer != null) udpServer.Close();
        if (threadRecebimento != null) threadRecebimento.Abort();
    }
}