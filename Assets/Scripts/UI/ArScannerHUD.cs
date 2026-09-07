using System.Collections;
using ArScanner.Network;
using ArScanner.Rendering;
using ArScanner.Spatial;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ArScanner.UI
{
    public class ArScannerHUD : MonoBehaviour
    {
        [Header("Referências dos Módulos")]
        public PointCloudTcpReceiver tcpReceiver;
        public UwbDataReceiver uwbReceiver;
        public ThermalPointCloudRenderer pointRenderer;
        public UwbAnchorManager anchorManager;
        public PointCloudSimulator simulator;

        [Header("Configuração Visual")]
        public bool showImGuiHUD = true;

        // Variáveis de diagnóstico
        private float fps = 60f;
        private float fpsUpdateTimer = 0f;
        private string feedbackMessage = "";
        private float feedbackTimer = 0f;

        private void Awake()
        {
            if (tcpReceiver == null) tcpReceiver = FindFirstObjectByType<PointCloudTcpReceiver>();
            if (uwbReceiver == null) uwbReceiver = FindFirstObjectByType<UwbDataReceiver>();
            if (pointRenderer == null) pointRenderer = FindFirstObjectByType<ThermalPointCloudRenderer>();
            if (anchorManager == null) anchorManager = FindFirstObjectByType<UwbAnchorManager>();
            if (simulator == null) simulator = FindFirstObjectByType<PointCloudSimulator>();
        }

        private void Update()
        {
            // Cálculo suave de FPS
            fpsUpdateTimer += Time.unscaledDeltaTime;
            if (fpsUpdateTimer >= 0.25f)
            {
                fps = 1.0f / Time.unscaledDeltaTime;
                fpsUpdateTimer = 0f;
            }

            if (feedbackTimer > 0f)
            {
                feedbackTimer -= Time.deltaTime;
                if (feedbackTimer <= 0f) feedbackMessage = "";
            }
        }

        public void ShowFeedback(string msg)
        {
            feedbackMessage = msg;
            feedbackTimer = 3.5f;
        }

        private void OnGUI()
        {
            if (!showImGuiHUD) return;

            GUI.skin.label.fontSize = 12;
            GUI.skin.button.fontSize = 12;

            // Painel Superior Esquerdo: Diagnóstico e Conexões
            GUILayout.BeginArea(new Rect(15, 15, 340, 240), GUI.skin.box);
            GUILayout.Label("<b><color=#00E5FF>SISTEMA AR SCANNER 3D - TCC UEPG</color></b>");
            
            // Scanner TCP Status
            if (tcpReceiver != null)
            {
                string tcpStatus = tcpReceiver.isConnected
                    ? $"<color=#00FF66>● CONECTADO ({tcpReceiver.packetsPerSecond:F0} PPS)</color>"
                    : "<color=#FF4444>○ DESCONECTADO</color>";
                GUILayout.Label($"<b>Scanner ESP32-S3:</b> {tcpStatus}");
                GUILayout.Label($"<b>Host:</b> {tcpReceiver.scannerIp}:{tcpReceiver.scannerPort}");
            }

            // UWB Status
            if (uwbReceiver != null)
            {
                string uwbStatus = uwbReceiver.IsConnected
                    ? "<color=#00FF66>● ATIVO (ToF OK)</color>"
                    : "<color=#FFBB00>○ AGUARDANDO BASE</color>";
                GUILayout.Label($"<b>Base UWB (3x DWM):</b> {uwbStatus}");
                Vector3 p = uwbReceiver.LatestPosition;
                GUILayout.Label($"<b>Posição Tag:</b> X:{p.x:F2}m Y:{p.y:F2}m Z:{p.z:F2}m");
            }

            // Estatísticas de Renderização
            if (pointRenderer != null)
            {
                GUILayout.Label($"<b>Pontos Ativos:</b> {pointRenderer.activePointsCount:N0} / {pointRenderer.maxPoints:N0}");
                GUILayout.Label($"<b>Temp Observada:</b> {pointRenderer.observedMinTemp:F1}°C a {pointRenderer.observedMaxTemp:F1}°C");
                GUILayout.Label($"<b>Modo:</b> {pointRenderer.visualMode}");
            }

            GUILayout.Label($"<b>Performance:</b> {fps:F0} FPS");
            GUILayout.EndArea();

            // Painel Superior Direito: Controles Interativos e Raio-X
            GUILayout.BeginArea(new Rect(Screen.width - 265, 15, 250, 420), GUI.skin.box);
            GUILayout.Label("<b><color=#FFD700>CONTROLES DO SISTEMA</color></b>");

            // Alternador de Modo de Visualização (Raio-X, Térmico, RGB)
            if (pointRenderer != null)
            {
                if (GUILayout.Button($"👁 Visual: {pointRenderer.visualMode}"))
                {
                    int nextMode = ((int)pointRenderer.visualMode + 1) % 3;
                    pointRenderer.SetVisualizationMode((VisualizationMode)nextMode);
                    ShowFeedback($"Modo de Visualização: {pointRenderer.visualMode}");
                }

                // Controle de Opacidade das Paredes (Raio-X)
                GUILayout.Label($"<b>Transparência Parede:</b> {(1f - pointRenderer.wallOpacity) * 100f:F0}%");
                float newOpacity = GUILayout.HorizontalSlider(pointRenderer.wallOpacity, 0.0f, 1.0f);
                if (Mathf.Abs(newOpacity - pointRenderer.wallOpacity) > 0.01f)
                {
                    pointRenderer.SetWallOpacity(newOpacity);
                }

                // Alternador de LOD Planar
                string lodText = pointRenderer.enablePlanarLOD ? "LOD Planar: <color=#00FF66>ATIVO</color>" : "LOD Planar: <color=#AAAAAA>OFF</color>";
                if (GUILayout.Button(lodText))
                {
                    pointRenderer.enablePlanarLOD = !pointRenderer.enablePlanarLOD;
                    ShowFeedback(pointRenderer.enablePlanarLOD ? "LOD Planar Ativado (Otimização ON)" : "LOD Planar Desativado");
                }
            }

            // Botão Simulador
            if (simulator != null)
            {
                string simText = simulator.isSimulationActive ? "Simulador: <color=#00FF66>LIGADO</color>" : "Simulador: <color=#AAAAAA>DESLIGADO</color>";
                if (GUILayout.Button(simText))
                {
                    simulator.isSimulationActive = !simulator.isSimulationActive;
                    ShowFeedback(simulator.isSimulationActive ? "Modo Simulação Ativado!" : "Modo Simulação Desativado.");
                }
            }

            // Botão Limpar Nuvem
            if (GUILayout.Button("🗑 Limpar Nuvem de Pontos"))
            {
                if (pointRenderer != null)
                {
                    pointRenderer.ClearPointCloud();
                    ShowFeedback("Nuvem de pontos resetada!");
                }
            }

            // Botão Pausar / Retomar
            if (pointRenderer != null)
            {
                string pauseText = pointRenderer.isPaused ? "▶ Retomar Ingestão" : "⏸ Pausar Ingestão";
                if (GUILayout.Button(pauseText))
                {
                    pointRenderer.isPaused = !pointRenderer.isPaused;
                }
            }

            // Alternador de Paleta Térmica
            if (pointRenderer != null && pointRenderer.visualMode != VisualizationMode.RgbPhotoReal)
            {
                if (GUILayout.Button($"🎨 Paleta: {pointRenderer.colormap}"))
                {
                    int nextVal = ((int)pointRenderer.colormap + 1) % 4;
                    pointRenderer.colormap = (ThermalColormap)nextVal;
                    pointRenderer.RecalculateColors();
                    ShowFeedback($"Paleta Térmica: {pointRenderer.colormap}");
                }
            }

            // Resetar Âncora UWB
            if (GUILayout.Button("🎯 Resetar Origem Âncora"))
            {
                if (anchorManager != null)
                {
                    anchorManager.ResetOriginToCurrent();
                    ShowFeedback("Origem UWB zerada na posição atual!");
                }
            }

            // Botão Exportar PLY
            if (GUILayout.Button("💾 Exportar Nuvem (.PLY)"))
            {
                if (pointRenderer != null)
                {
                    string path = pointRenderer.ExportToPLY();
                    if (!string.IsNullOrEmpty(path))
                    {
                        ShowFeedback($"Salvo: {System.IO.Path.GetFileName(path)}");
                    }
                    else
                    {
                        ShowFeedback("Nenhum ponto para exportar.");
                    }
                }
            }

            // Botão Voltar ao Menu
            if (GUILayout.Button("⬅ Menu Inicial"))
            {
                if (Application.CanStreamedLevelBeLoaded("MenuViewer"))
                {
                    SceneManager.LoadScene("MenuViewer");
                }
                else
                {
                    SceneManager.LoadScene(0);
                }
            }

            GUILayout.EndArea();

            // Mensagem de Feedback Flutuante no Rodapé
            if (!string.IsNullOrEmpty(feedbackMessage))
            {
                GUI.color = Color.cyan;
                GUI.Box(new Rect(Screen.width * 0.5f - 180, Screen.height - 70, 360, 40), $"<b>{feedbackMessage}</b>");
                GUI.color = Color.white;
            }
        }
    }
}
