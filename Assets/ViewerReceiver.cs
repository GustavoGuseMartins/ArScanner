using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Globalization;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine.XR.ARFoundation; // <--- NECESSÁRIO

public class ViewerReceiver : MonoBehaviour
{
    [Header("Visualização")]
    public GameObject pontoPrefab;

    [Header("AR Core")]
    public ARAnchorManager anchorManager; // <--- ARRASTE O MANAGER AQUI NO INSPECTOR

    [Header("Configurações de Memória")]
    [Range(100, 300000)] // Aumentei um pouco o range do slider
    public int maximoPontos = 100000;

    [Tooltip("Distância mínima em metros entre dois pontos (Filtro de Grade)")]
    public float distanciaMinima = 0.05f;

    [Header("Rede")]
    public int porta = 8080;

    // Variáveis internas
    private UdpClient udpServer;
    private Thread threadRecebimento;
    private ConcurrentQueue<Pose> filaDeEntrada = new ConcurrentQueue<Pose>();

    // Estruturas do Sistema de Grade
    struct PontoAtivo { public GameObject obj; public Vector3Int gradeKey; }
    private Queue<PontoAtivo> pontosVivos = new Queue<PontoAtivo>();
    private HashSet<Vector3Int> gradeOcupada = new HashSet<Vector3Int>();

    // Variável para segurar o mundo no lugar
    private Transform mundoAncora; // <--- O PAI DE TODOS OS PONTOS

    void Start()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        // --- CRIAÇÃO DA ÂNCORA (O PREGO NO MUNDO) ---
        CriarAncoraRaiz();

        udpServer = new UdpClient(porta);
        threadRecebimento = new Thread(ReceberDados);
        threadRecebimento.IsBackground = true;
        threadRecebimento.Start();
    }

    void CriarAncoraRaiz()
    {
        // Cria um objeto vazio no 0,0,0 (onde o S9 iniciou o app)
        GameObject raiz = new GameObject("RaizDoMundoAR");
        raiz.transform.position = Vector3.zero;
        raiz.transform.rotation = Quaternion.identity;

        // Tenta adicionar o componente ARAnchor via script
        // Isso diz ao ARCore: "Mantenha este objeto fixo no mundo físico, custe o que custar"
        if (anchorManager != null)
        {
            raiz.AddComponent<ARAnchor>();
        }
        else
        {
            Debug.LogWarning("ARAnchorManager não foi definido no Inspector!");
        }

        mundoAncora = raiz.transform;
    }

    void Update()
    {
        ProcessarFila();
    }

    void ProcessarFila()
    {
        while (filaDeEntrada.TryDequeue(out Pose dadosPonto))
        {
            Vector3Int chaveGrade = new Vector3Int(
                Mathf.RoundToInt(dadosPonto.position.x / distanciaMinima),
                Mathf.RoundToInt(dadosPonto.position.y / distanciaMinima),
                Mathf.RoundToInt(dadosPonto.position.z / distanciaMinima)
            );

            if (gradeOcupada.Contains(chaveGrade)) continue;

            // --- MUDANÇA CRUCIAL AQUI ---

            // 1. Instancia o objeto (sem posição ainda)
            GameObject novoObj = Instantiate(pontoPrefab);

            // 2. Define o PAI como sendo a Âncora
            novoObj.transform.SetParent(mundoAncora, false);

            // 3. Define a posição LOCAL (relativa à âncora)
            // Isso garante que se a âncora corrigir o drift, o ponto vem junto
            novoObj.transform.localPosition = dadosPonto.position;
            novoObj.transform.localRotation = dadosPonto.rotation;

            // Ajuste de escala
            float tamanho = 0.08f;
            novoObj.transform.localScale = new Vector3(tamanho, tamanho, 1f);

            // -----------------------------

            PontoAtivo novoPonto = new PontoAtivo { obj = novoObj, gradeKey = chaveGrade };
            pontosVivos.Enqueue(novoPonto);
            gradeOcupada.Add(chaveGrade);

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
                // Silencia erros
            }
        }
    }

    private void OnDestroy()
    {
        if (udpServer != null) udpServer.Close();
        if (threadRecebimento != null) threadRecebimento.Abort();
    }
}