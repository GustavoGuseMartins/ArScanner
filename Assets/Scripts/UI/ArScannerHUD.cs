using System.Collections;
using ArScanner.Network;
using ArScanner.Rendering;
using ArScanner.Spatial;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        private Vector2 scrollPosRight = Vector2.zero;
        private float controlsContentWidth;
        private bool thermalPreviewExpanded;
        private bool showDiagnostics;
        private bool showSensorDetails;
        private bool showMountingControls;
        private bool showExperimentControls;
        private bool showThermalCalibration;
        private bool adjustPositionExpanded;
        private bool controlsCollapsed;
        private static readonly float[] PointSizeMultipliers = { .25f, .5f, 1f, 2f, 3f, 4f };
        private static readonly string[] PointSizeLabels = { "×1/4", "×1/2", "×1", "×2", "×3", "×4" };

        public struct HudLayout
        {
            public float scale;
            public Rect safe, connections, controls, center, preview, feedback;
        }

        // Screen.safeArea starts at bottom-left; IMGUI starts at top-left.
        public static HudLayout CalculateLayout(float screenWidth, float screenHeight, Rect safeArea, bool collapsed)
        {
            screenWidth = Mathf.Max(1f, screenWidth);
            screenHeight = Mathf.Max(1f, screenHeight);
            if (safeArea.width <= 0f || safeArea.height <= 0f) safeArea = new Rect(0, 0, screenWidth, screenHeight);
            float scale = Mathf.Max(.01f, Mathf.Min(safeArea.width / 820f, safeArea.height / 720f));
            Rect safe = new Rect(safeArea.x / scale, (screenHeight-safeArea.yMax) / scale,
                safeArea.width / scale, safeArea.height / scale);
            const float margin = 16f, gap = 12f;
            float rightWidth = collapsed ? 150f : Mathf.Clamp(safe.width*.25f, 300f, 400f);
            float leftWidth = Mathf.Clamp(safe.width*.2f, 220f, 300f);
            Rect controls = new Rect(safe.xMax-margin-rightWidth, safe.y+margin, rightWidth,
                collapsed ? 46f : safe.height-2*margin);
            Rect connections = new Rect(safe.x+margin, safe.y+margin, leftWidth, 215f);
            Rect center = Rect.MinMaxRect(connections.xMax+gap, safe.y+margin,
                controls.x-gap, safe.yMax-margin);
            float previewWidth = Mathf.Min(510f, center.width);
            float previewHeight = Mathf.Min(540f, center.height);
            Rect preview = new Rect(center.center.x-previewWidth*.5f, center.y+Mathf.Min(55f, center.height-previewHeight),
                previewWidth, previewHeight);
            return new HudLayout { scale=scale, safe=safe, controls=controls, connections=connections,
                center=center, preview=preview,
                feedback=new Rect(center.center.x-Mathf.Min(480f, center.width)*.5f, safe.yMax-margin-60f,
                    Mathf.Min(480f, center.width), 60f) };
        }

        // Estilos SAO Tech Theme
        private GUIStyle saoBoxStyle;
        private GUIStyle saoBtnStyle;
        private GUIStyle saoBtnDangerStyle;
        private GUIStyle saoBtnActiveStyle;
        private GUIStyle saoToggleStyle;
        private GUIStyle saoLabelStyle;
        private GUIStyle saoLabelTitleStyle;
        private GUIStyle saoLabelValueStyle;
        private bool stylesInitialized = false;

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

        private Texture2D MakeTex(int width, int height, Color col, bool withBorder = false, Color borderCol = default)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
            {
                if (withBorder)
                {
                    int x = i % width;
                    int y = i / width;
                    if (x < 2 || x > width - 3 || y < 2 || y > height - 3)
                        pix[i] = borderCol;
                    else
                        pix[i] = col;
                }
                else
                {
                    pix[i] = col;
                }
            }
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        private void InitStyles()
        {
            if (stylesInitialized) return;

            // Cores Temáticas SAO/Tech
            Color panelBg = new Color(0.12f, 0.15f, 0.2f, 0.85f); // Cinza/Azul escuro tech
            Color panelBorder = new Color(0.3f, 0.7f, 0.9f, 0.5f);
            Color btnNormalBg = new Color(0.2f, 0.25f, 0.35f, 0.9f);
            Color btnHoverBg = new Color(0.4f, 0.6f, 0.9f, 1f);
            Color btnDangerBg = new Color(0.8f, 0.3f, 0.1f, 0.9f); // Laranja avermelhado SAO
            Color btnDangerHover = new Color(1.0f, 0.4f, 0.2f, 1f);
            Color btnActiveBg = new Color(1f, 0.65f, 0.1f, 0.9f); // Laranja Tech Action

            saoBoxStyle = new GUIStyle(GUI.skin.box);
            saoBoxStyle.normal.background = MakeTex(64, 64, panelBg, true, panelBorder);
            saoBoxStyle.padding = new RectOffset(14, 14, 14, 14);
            saoBoxStyle.normal.textColor = Color.white;
            saoBoxStyle.fontSize = 15;

            saoBtnStyle = new GUIStyle(GUI.skin.button);
            saoBtnStyle.normal.background = MakeTex(64, 64, btnNormalBg);
            saoBtnStyle.hover.background = MakeTex(64, 64, btnHoverBg);
            saoBtnStyle.active.background = MakeTex(64, 64, btnActiveBg);
            saoBtnStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            saoBtnStyle.hover.textColor = Color.white;
            saoBtnStyle.padding = new RectOffset(8, 8, 8, 8);
            saoBtnStyle.alignment = TextAnchor.MiddleCenter;
            saoBtnStyle.fontSize = 16;
            saoBtnStyle.wordWrap = true;

            saoBtnDangerStyle = new GUIStyle(saoBtnStyle);
            saoBtnDangerStyle.normal.background = MakeTex(64, 64, btnDangerBg);
            saoBtnDangerStyle.hover.background = MakeTex(64, 64, btnDangerHover);
            saoBtnDangerStyle.normal.textColor = Color.white;

            saoBtnActiveStyle = new GUIStyle(saoBtnStyle);
            saoBtnActiveStyle.normal.background = MakeTex(64, 64, btnActiveBg);
            saoBtnActiveStyle.hover.background = MakeTex(64, 64, btnHoverBg);
            saoBtnActiveStyle.normal.textColor = Color.black;

            saoToggleStyle = new GUIStyle(GUI.skin.toggle);
            saoToggleStyle.fontSize = 15;
            saoToggleStyle.wordWrap = true;
            saoToggleStyle.normal.textColor = new Color(.85f, .9f, .95f);

            saoLabelStyle = new GUIStyle(GUI.skin.label);
            saoLabelStyle.normal.textColor = new Color(0.85f, 0.9f, 0.95f);
            saoLabelStyle.fontSize = 15;
            saoLabelStyle.wordWrap = true;

            saoLabelTitleStyle = new GUIStyle(saoLabelStyle);
            saoLabelTitleStyle.normal.textColor = new Color(0.0f, 0.9f, 1.0f); // Cyan
            saoLabelTitleStyle.fontStyle = FontStyle.Bold;
            saoLabelTitleStyle.fontSize = 18;

            saoLabelValueStyle = new GUIStyle(saoLabelStyle);
            saoLabelValueStyle.normal.textColor = new Color(1.0f, 0.82f, 0.25f); // Golden/Orange
            saoLabelValueStyle.fontSize = 15;

            stylesInitialized = true;
        }

        private bool CanChangeScannerSetup => tcpReceiver != null && tcpReceiver.HasFreshStatus &&
            !tcpReceiver.isScanning && !tcpReceiver.status.isScanning && !tcpReceiver.status.panMoving &&
            !tcpReceiver.ScannerControlBusy && !tcpReceiver.DiagnosticsBusy;
        private bool CanChangeLidarQualityFilter => CanChangeScannerSetup &&
            tcpReceiver.HasLidarQualityStatus && pointRenderer != null;

        private void OnGUI()
        {
            if (!showImGuiHUD) return;
            InitStyles();
            HudLayout layout = CalculateLayout(Screen.width, Screen.height, Screen.safeArea, controlsCollapsed);
            float scale = layout.scale;
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
            GUI.skin.button = saoBtnStyle;
            GUI.skin.box = saoBoxStyle;
            GUI.skin.label = saoLabelStyle;
            GUI.skin.toggle = saoToggleStyle;

            DrawConnections(layout.connections);
            if (controlsCollapsed)
            {
                if (GUI.Button(layout.controls, "Mostrar controles")) controlsCollapsed = false;
            }
            else DrawMainControls(layout.controls);
            if (!thermalPreviewExpanded)
            {
                Vector2 crossCenter = new Vector2(Screen.width/scale*.5f, Screen.height/scale*.5f);
                Color previous = GUI.color;
                GUI.color = new Color(.2f, .9f, 1f, .9f);
                GUI.DrawTexture(new Rect(crossCenter.x-12, crossCenter.y-1, 24, 2), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(crossCenter.x-1, crossCenter.y-12, 2, 24), Texture2D.whiteTexture);
                GUI.color = previous;
            }
            DrawThermalPreview(layout.preview);
            if (!string.IsNullOrEmpty(feedbackMessage))
                GUI.Box(layout.feedback, feedbackMessage, saoBoxStyle);
            GUI.matrix = previousMatrix;
        }

        private void DrawConnections(Rect area)
        {
            GUILayout.BeginArea(area, saoBoxStyle);
            GUILayout.Label("CONEXÕES", saoLabelTitleStyle);
            if (tcpReceiver != null)
            {
                GUILayout.Label(tcpReceiver.isConnected ? "Scanner Wi-Fi conectado" : "Scanner Wi-Fi desconectado",
                    saoLabelValueStyle);
                if (tcpReceiver.isConnected && !tcpReceiver.HasFreshStatus)
                    GUILayout.Label("Aguardando o estado do scanner.", saoLabelStyle);
            }
            if (uwbReceiver != null)
            {
                bool simulation = uwbReceiver.transportMode == UwbTransportMode.Simulated;
                GUILayout.Label(simulation ? "Simulação no editor" : uwbReceiver.UsbHasRecentData
                    ? "Base USB-C recebendo dados" : "Base USB-C sem dados recentes", saoLabelStyle);
            }
            if (pointRenderer != null)
                GUILayout.Label($"Nuvem: {pointRenderer.activePointsCount:N0} pontos", saoLabelStyle);
            GUILayout.EndArea();
        }

        private void DrawMainControls(Rect area)
        {
            controlsContentWidth = Mathf.Max(1f, area.width - 28f - 20f);
            GUILayout.BeginArea(area, saoBoxStyle);
            GUILayout.BeginHorizontal();
            bool capturing = tcpReceiver != null && tcpReceiver.HasFreshStatus && tcpReceiver.status.isScanning;
            string captureTitle = capturing ? "CAPTURANDO" :
                tcpReceiver != null && tcpReceiver.ScanStartPending ? "INICIANDO CAPTURA" : "CAPTURA PARADA";
            GUILayout.Label(captureTitle, saoLabelTitleStyle);
            if (GUILayout.Button("Recolher", GUILayout.Width(90), GUILayout.Height(30))) controlsCollapsed = true;
            GUILayout.EndHorizontal();
            // All interactive sections share this scroll, including diagnostic adjustments.
            scrollPosRight = GUILayout.BeginScrollView(scrollPosRight);
            DrawAcquisitionControls();
            DrawQuickControls();
            DrawDisplayControls();
            DrawConnectionControls();
            DrawPanReference();
            DrawPositionControls();
            DrawDirectionControls();
            DrawImuOrientationControls();
            DrawCloudAndThermalControls();
            GUILayout.Space(8);
            if (GUILayout.Button(showDiagnostics ? "Ocultar diagnóstico e ajustes" : "Diagnóstico e ajustes", GUILayout.Height(34)))
                showDiagnostics = !showDiagnostics;
            if (showDiagnostics) DrawDiagnostics();
            if (GUILayout.Button("Voltar ao menu", GUILayout.Height(34)))
            {
                tcpReceiver?.SendStopScan();
                if (Application.CanStreamedLevelBeLoaded("MenuViewer")) SceneManager.LoadScene("MenuViewer");
                else SceneManager.LoadScene(0);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawConnectionControls()
        {
            if (tcpReceiver != null && !tcpReceiver.isConnected &&
                GUILayout.Button("Reconectar scanner", GUILayout.Height(32)))
                tcpReceiver.ConnectToScanner();
            if (uwbReceiver != null && uwbReceiver.transportMode != UwbTransportMode.Simulated &&
                !uwbReceiver.UsbHasRecentData && GUILayout.Button("Autorizar USB novamente", GUILayout.Height(32)))
                uwbReceiver.RequestUsbPermissionAgain();
        }

        private void DrawDisplayControls()
        {
            if (pointRenderer == null) return;
            GUILayout.Space(8);
            GUILayout.Label("VISUALIZAÇÃO", saoLabelTitleStyle);
            bool lod = GUILayout.Toggle(pointRenderer.enableSurfaceLod, "Unir pontos em superfícies (LOD)");
            if (lod != pointRenderer.enableSurfaceLod) pointRenderer.SetSurfaceLodEnabled(lod);
            GUILayout.Label(lod ? "LOD ativo: calcula superfícies em segundo plano."
                : "Somente pontos: cálculo de superfícies desligado.", saoLabelStyle);
            GUILayout.Label($"Tamanho dos pontos: ×{pointRenderer.PointSizeMultiplier:0.##} ({pointRenderer.pointSize*1000f:0.##} mm)", saoLabelStyle);
            for (int row = 0; row < 2; row++)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < 3; col++)
                {
                    int i = row*3+col;
                    bool selected = (!pointRenderer.enableSurfaceLod || !pointRenderer.adaptivePointSizing) &&
                        Mathf.Approximately(pointRenderer.PointSizeMultiplier, PointSizeMultipliers[i]);
                    if (GUILayout.Button(PointSizeLabels[i], selected ? saoBtnActiveStyle : saoBtnStyle, GUILayout.Height(32)))
                        pointRenderer.SetPointSizeMultiplier(PointSizeMultipliers[i]);
                }
                GUILayout.EndHorizontal();
            }
            bool adaptive = GUILayout.Toggle(pointRenderer.adaptivePointSizing, "Adaptar pontos restantes com LOD");
            pointRenderer.adaptivePointSizing = adaptive;
            bool hideBack = GUILayout.Toggle(pointRenderer.hideBackFacingPoints, "Ocultar pontos vistos por trás (experimental)");
            if (hideBack != pointRenderer.hideBackFacingPoints) pointRenderer.SetHideBackFacingPoints(hideBack);
            if (hideBack) GUILayout.Label("Considera o lado observado pelo scanner; a direção medida não é a normal real da superfície.", saoLabelStyle);
            pointRenderer.showScannerAxes = GUILayout.Toggle(pointRenderer.showScannerAxes, "Mostrar indicador do eixo");
            pointRenderer.useAbsoluteThermalScale = GUILayout.Toggle(pointRenderer.useAbsoluteThermalScale, "Escala fixa de cores por temperatura");
            if (!pointRenderer.useAbsoluteThermalScale)
            {
                GUILayout.Label("Contraste térmico de exibição (não muda as medições)", saoLabelStyle);
                pointRenderer.thermalDisplayMinC = GUILayout.HorizontalSlider(pointRenderer.thermalDisplayMinC, 0, 35);
                pointRenderer.thermalDisplayMaxC = GUILayout.HorizontalSlider(pointRenderer.thermalDisplayMaxC, pointRenderer.thermalDisplayMinC+1, 65);
            }
        }

        private void DrawQuickControls()
        {
            if (pointRenderer != null)
            {
                GUILayout.Label($"Opacidade dos pontos: {pointRenderer.pointOpacity*100:F0}%", saoLabelStyle);
                pointRenderer.pointOpacity = GUILayout.HorizontalSlider(pointRenderer.pointOpacity, .1f, 1f);
            }
            if (tcpReceiver == null) return;
            GUILayout.Label($"Velocidade do giro: {tcpReceiver.currentStepperSpeedRpm:F1} RPM", saoLabelStyle);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && tcpReceiver.CanSetPanSpeed;
            float speed = GUILayout.HorizontalSlider(tcpReceiver.currentStepperSpeedRpm, .5f, 10f);
            if (Mathf.Abs(speed-tcpReceiver.currentStepperSpeedRpm) > .05f) tcpReceiver.SendSetSpeed(speed);
            GUI.enabled = enabled;
            if (!tcpReceiver.CanSetPanSpeed) GUILayout.Label("Pare a captura para ajustar o giro.", saoLabelStyle);
            GUILayout.Space(6);
        }

        private void DrawImuOrientationControls()
        {
            if (tcpReceiver == null || !tcpReceiver.HasFreshStatus || tcpReceiver.status.diagnosticVersion < 14) return;
            var s = tcpReceiver.status;
            bool aligned = anchorManager != null && anchorManager.PreviewHeadingAligned;
            GUILayout.Space(6);
            GUILayout.Label("GY-25: acompanhamento do giro (teste)", saoLabelStyle);
            GUILayout.Label("Giro dos pontos: " + DescribePointYawSource(s), saoLabelValueStyle);
            GUILayout.Label($"Inclinação dos pontos: {(s.imuTiltApplied ? "habilitada" : "desligada")}", saoLabelStyle);
            if (!aligned)
            {
                GUILayout.Label("Primeiro fixe o eixo e alinhe a direção como de costume.", saoLabelStyle);
                return;
            }
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && tcpReceiver.CanReferenceImuOrientation;
            if (GUILayout.Button("Referenciar GY-25 com scanner parado (3 s)", GUILayout.Height(36)))
                tcpReceiver.RequestImuOrientationReference();
            GUI.enabled = enabled && tcpReceiver.CanSetPanSpeed &&
                (s.imuOrientationEnabled || tcpReceiver.CanEnableImuOrientation);
            bool following = GUILayout.Toggle(s.imuOrientationEnabled, "Acompanhar giro com GY-25");
            if (following != s.imuOrientationEnabled) tcpReceiver.RequestImuOrientationMode(following);
            GUI.enabled = enabled;
            if (s.imuOrientationState == "reference_collecting")
                GUILayout.Label($"Coletando referência; repouso detectado: {s.imuStationaryMs / 1000f:F1} s. Mantenha a cabeça parada.", saoLabelValueStyle);
            else if (s.imuOrientationState == "reference_timeout")
                GUILayout.Label("Não houve repouso suficiente para concluir. Apoie a cabeça e tente referenciar novamente.", saoLabelValueStyle);
            else if (tcpReceiver.HasValidImuOrientationReference)
            {
                if (!s.imuOrientationEnabled)
                    GUILayout.Label("Referência GY-25 concluída. Ative o acompanhamento com a cabeça parada.", saoLabelValueStyle);
                GUILayout.Label($"Giro da base desde a referência: {s.imuRelativeBaseYawDeg:F1}°; " +
                    $"inclinação {s.imuPitchDeg:F1}° / {s.imuRollDeg:F1}°.", saoLabelStyle);
            }
            else GUILayout.Label("Referência GY-25 indisponível: pare e referencie novamente.", saoLabelStyle);
            if (s.imuOrientationEnabled)
                GUILayout.Label("A captura pausa se a referência ou a leitura contínua forem perdidas. A direção inicial continua manual.", saoLabelStyle);
            if (!tcpReceiver.calibrationStatus.StartsWith("GY-25 coletando referência", System.StringComparison.Ordinal) ||
                s.imuOrientationState == "reference_collecting")
                GUILayout.Label(tcpReceiver.calibrationStatus, saoLabelStyle);
        }

        private void DrawPanReference()
        {
            if (tcpReceiver == null) return;
            if (tcpReceiver.HasFreshStatus && tcpReceiver.status.diagnosticVersion >= 8)
            {
                GUILayout.Label(tcpReceiver.HasKnownPanReference ? "Zero do pan salvo no scanner." : "Zero do pan ainda não confirmado.",
                    saoLabelValueStyle);
                if (!tcpReceiver.HasKnownPanReference)
                {
                    GUILayout.Label("Com a cabeça parada, alinhe-a à frente física escolhida da base. Confirme essa posição como zero; depois defina a direção no AR.", saoLabelStyle);
                    bool enabled = GUI.enabled;
                    GUI.enabled = enabled && tcpReceiver.CanConfirmPanReference;
                    if (GUILayout.Button("Confirmar zero do pan", GUILayout.Height(40)))
                    {
                        tcpReceiver.ConfirmPanZeroReference();
                        ShowFeedback(tcpReceiver.scannerCommandStatus);
                    }
                    GUI.enabled = enabled;
                }
            }
            else if (tcpReceiver.HasFreshStatus)
                GUILayout.Label("Firmware anterior: o pan usa o zero relativo à partida.", saoLabelStyle);
            if (tcpReceiver.PanZeroConfirmationPending)
                GUILayout.Label("Aguardando o scanner salvar o zero do pan...", saoLabelValueStyle);
            if (tcpReceiver.IsParking)
            {
                GUILayout.Label("Cabeça retornando ao zero do pan.", saoLabelValueStyle);
                if (GUILayout.Button("Cancelar retorno / parar", GUILayout.Height(34))) tcpReceiver.SendStopScan();
            }
            else if (tcpReceiver.HasFreshStatus && Mathf.Abs(Mathf.DeltaAngle(tcpReceiver.status.panDegrees, 0)) > .5f)
            {
                bool enabled = GUI.enabled;
                GUI.enabled = enabled && CanChangeScannerSetup && tcpReceiver.HasUsablePanReference;
                if (GUILayout.Button(tcpReceiver.HasKnownPanReference ? "Retornar cabeça ao zero salvo" : "Retornar ao zero relativo", GUILayout.Height(34)))
                    ShowFeedback(tcpReceiver.SendParkPan() ? "Retorno solicitado; aguarde a cabeça parar." : tcpReceiver.scannerCommandStatus);
                GUI.enabled = enabled;
            }
        }

        private void DrawPositionControls()
        {
            if (anchorManager == null) return;
            GUILayout.Space(8);
            GUILayout.Label("1. FIXAR O EIXO", saoLabelTitleStyle);
            GUILayout.Label(anchorManager.PoseLocalizationStatus, saoLabelStyle);
            bool fixedPosition = anchorManager.localPreviewWithoutUwb ? anchorManager.PreviewPlaced : anchorManager.StationaryPosePinned;
            if (fixedPosition && !adjustPositionExpanded)
            {
                if (GUILayout.Button("Corrigir posição do eixo", GUILayout.Height(32))) adjustPositionExpanded = true;
                return;
            }
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && CanChangeScannerSetup;
            if (!anchorManager.scannerStationary && GUILayout.Button("Scanner parado: voltar à localização", GUILayout.Height(36)))
            {
                anchorManager.SetScannerStationary(true);
                pointRenderer?.ClearPointCloud();
            }
            if (anchorManager.autoUwbPositioning && !anchorManager.localPreviewWithoutUwb)
            {
                if (anchorManager.StationaryPosePinned && GUILayout.Button("Refazer posição UWB", GUILayout.Height(36)))
                {
                    anchorManager.ResumeAutomaticUwb(); pointRenderer?.ClearPointCloud();
                }
                else if (!anchorManager.StationaryPosePinned)
                {
                    GUI.enabled = enabled && CanChangeScannerSetup && anchorManager.CanConfirmPoseCandidate;
                    if (GUILayout.Button("Fixar eixo UWB estimado", GUILayout.Height(38)))
                    {
                        bool confirmed = anchorManager.TryConfirmPoseCandidate(out string message);
                        if (confirmed) { pointRenderer?.ClearPointCloud(); adjustPositionExpanded = false; }
                        ShowFeedback(message);
                    }
                }
                GUI.enabled = enabled && CanChangeScannerSetup;
            }
            GUILayout.Label("Para marcar pelo AR, mire a cruz na superfície do apoio diretamente abaixo do eixo de giro.", saoLabelStyle);
            if (GUILayout.Button(anchorManager.localPreviewWithoutUwb && anchorManager.PreviewPlaced
                ? "Corrigir posição do eixo no apoio" : "Marcar eixo no apoio pelo AR", GUILayout.Height(40)))
            {
                if (anchorManager.PlacePreviewAtScreenCenter(out string message))
                { pointRenderer?.ClearPointCloud(); adjustPositionExpanded = false; }
                ShowFeedback(message);
            }
            GUI.enabled = enabled;
        }

        private void DrawDirectionControls()
        {
            if (anchorManager == null) return;
            GUILayout.Space(8);
            GUILayout.Label("2. DEFINIR A DIREÇÃO NO AR", saoLabelTitleStyle);
            GUILayout.Label(anchorManager.PreviewHeadingAligned ? "Direção no AR definida." : "Direção no AR desconhecida; o indicador mostra só a vertical.", saoLabelValueStyle);
            bool positionFixed = anchorManager.localPreviewWithoutUwb
                ? anchorManager.PreviewPlaced : anchorManager.StationaryPosePinned;
            if (!anchorManager.HasPoseEstimate || !positionFixed)
            {
                GUILayout.Label("Fixe o eixo antes de definir a direção.", saoLabelStyle);
                return;
            }
            if (!anchorManager.PreviewHeadingAligned)
                GUILayout.Label("Com a cabeça parada, mire a cruz em um ponto livre do apoio, 30–50 cm à frente do eixo, na direção física da frente do LiDAR. Toque em Definir direção.", saoLabelStyle);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && CanChangeScannerSetup && tcpReceiver.HasUsablePanReference &&
                anchorManager.HasPoseEstimate && positionFixed && (anchorManager.localPreviewWithoutUwb ||
                    (anchorManager.HasValidatedMultiviewPose && anchorManager.HasAutomaticSupportHeight));
            if (GUILayout.Button(anchorManager.PreviewHeadingAligned ? "Corrigir direção no AR" : "Definir direção no AR", GUILayout.Height(40)))
            {
                if (anchorManager.AlignPreviewForwardAtScreenCenter(tcpReceiver.status.panDegrees, out string message))
                    pointRenderer?.ClearPointCloud();
                ShowFeedback(message);
            }
            GUI.enabled = enabled;
            if (!anchorManager.PreviewHeadingAligned && anchorManager.HasPoseEstimate)
                GUILayout.Label(anchorManager.HeadingStatus, saoLabelStyle);
        }

        private void DrawAcquisitionControls()
        {
            if (tcpReceiver == null) return;
            GUILayout.Space(8);
            GUILayout.Label("3. CAPTURAR", saoLabelTitleStyle);
            if (tcpReceiver.ScanStartPending)
            {
                GUILayout.Label("Aguardando a confirmação de início do scanner.", saoLabelValueStyle);
                if (GUILayout.Button("Cancelar início / parar", saoBtnDangerStyle, GUILayout.Height(44))) tcpReceiver.SendStopScan();
            }
            else if (tcpReceiver.isScanning)
            {
                if (GUILayout.Button("Pausar captura", saoBtnDangerStyle, GUILayout.Height(44))) tcpReceiver.SendStopScan();
            }
            else
            {
                bool ready = anchorManager != null && anchorManager.CanAcceptPoints;
                bool enabled = GUI.enabled;
                GUI.enabled = enabled && ready && tcpReceiver.HasUsablePanReference &&
                    tcpReceiver.ImuAllowsScanStart &&
                    !tcpReceiver.ScannerControlBusy && !tcpReceiver.DiagnosticsBusy;
                if (GUILayout.Button("Iniciar / retomar captura", saoBtnActiveStyle, GUILayout.Height(44)))
                {
                    tcpReceiver.SendStartScan();
                    ShowFeedback(tcpReceiver.scannerCommandStatus);
                }
                GUI.enabled = enabled;
                if (!ready && anchorManager != null) GUILayout.Label(anchorManager.AcquisitionStatus, saoLabelStyle);
                if (tcpReceiver.HasFreshStatus && !tcpReceiver.ImuAllowsScanStart)
                    GUILayout.Label("GY-25 aguardando referência estável. Pare e referencie novamente.", saoLabelStyle);
            }
            if (!string.IsNullOrEmpty(tcpReceiver.scannerCommandStatus))
                GUILayout.Label(tcpReceiver.scannerCommandStatus, saoLabelStyle);
            if (tcpReceiver.HasFreshStatus && tcpReceiver.status.isScanning &&
                anchorManager != null && !anchorManager.CanAcceptPoints)
                GUILayout.Label("O scanner está varrendo, mas a nuvem não recebe novos pontos. " +
                    "Pause a captura e confirme novamente a posição e a direção.", saoLabelValueStyle);
        }

        private void DrawCloudAndThermalControls()
        {
            GUILayout.Space(8);
            if (pointRenderer != null)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Exportar PLY", GUILayout.Height(36)))
                {
                    string path = pointRenderer.ExportToPLY();
                    ShowFeedback(string.IsNullOrEmpty(path) ? "A nuvem está vazia." : $"Exportado: {System.IO.Path.GetFileName(path)}");
                }
                if (GUILayout.Button("Limpar nuvem", GUILayout.Height(36))) pointRenderer.ClearPointCloud();
                GUILayout.EndHorizontal();
                pointRenderer.showThermalColors = GUILayout.Toggle(pointRenderer.showThermalColors, "Colorir pontos com temperatura válida");
                bool qualityEnabled = GUI.enabled;
                GUI.enabled = qualityEnabled && CanChangeLidarQualityFilter;
                bool rejectWeak = GUILayout.Toggle(pointRenderer.rejectWeakLidarReturns, "Ignorar retornos com sinal fraco");
                if (rejectWeak != pointRenderer.rejectWeakLidarReturns)
                {
                    pointRenderer.SetRejectWeakLidarReturns(rejectWeak);
                    ShowFeedback("Regra de sinal fraco alterada; nuvem limpa. Nenhum limite de distância foi aplicado.");
                }
                GUI.enabled = qualityEnabled;
                if (pointRenderer.rejectWeakLidarReturns)
                    GUILayout.Label($"Sinal fraco: {pointRenderer.weakLidarDiscardedPoints} retornos ignorados nesta nuvem. O aviso não comprova erro de distância.", saoLabelStyle);
                if (tcpReceiver == null || !tcpReceiver.HasLidarQualityStatus)
                    GUILayout.Label("A opção de sinal fraco exige uma consulta recente ao scanner com suporte à qualidade LiDAR.", saoLabelStyle);
                else if (!CanChangeScannerSetup)
                    GUILayout.Label("Pare a captura para alterar a regra; a mudança limpa a nuvem.", saoLabelStyle);
                else GUILayout.Label("Ao alterar a regra, a nuvem é limpa. Compare o resultado com a opção desligada.", saoLabelStyle);
                if (pointRenderer.activeThermalPointsCount > 0)
                    GUILayout.Label($"Temperaturas: {pointRenderer.observedMinTemp:F1}–{pointRenderer.observedMaxTemp:F1} °C", saoLabelStyle);
                if (pointRenderer.showThermalColors)
                    GUILayout.Label("Sem temperatura: ponto cinza claro.", saoLabelStyle);
            }
            if (tcpReceiver == null) return;
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && (thermalPreviewExpanded || tcpReceiver.isConnected);
            if (GUILayout.Button(thermalPreviewExpanded ? "Fechar imagem térmica" : "Ver imagem térmica", GUILayout.Height(36)))
            {
                thermalPreviewExpanded = !thermalPreviewExpanded;
                tcpReceiver.SetCameraPreview(thermalPreviewExpanded ? 2 : 0);
            }
            GUI.enabled = enabled;
            if (tcpReceiver.HasFreshStatus)
            {
                if (tcpReceiver.status.diagnosticVersion >= 14)
                {
                    string reduced = tcpReceiver.status.thermalTargetFrameRateHz <
                        tcpReceiver.status.thermalRequestedFrameRateHz ? "; redução automática ativa" : "";
                    GUILayout.Label($"Térmica: {tcpReceiver.thermalFramesPerSecond:F1} quadros/s{reduced}.", saoLabelStyle);
                }
                GUILayout.Label(tcpReceiver.HasFreshThermalFrame ? "Imagem térmica disponível."
                    : "Câmera térmica: aguardando um quadro completo; detalhes no diagnóstico.", saoLabelStyle);
            }
        }

        private void DrawDiagnostics()
        {
            GUILayout.Label("DIAGNÓSTICO E AJUSTES", saoLabelTitleStyle);
            GUILayout.Label($"Exibição: {fps:F0} quadros/s", saoLabelStyle);
            if (tcpReceiver != null)
            {
                GUILayout.Label(tcpReceiver.statusHttpMessage, saoLabelStyle);
                GUILayout.Label($"Recepção: {tcpReceiver.packetsPerSecond:F0} pacotes/s; descartados {tcpReceiver.droppedPoints}", saoLabelStyle);
                bool enabled = GUI.enabled;
                GUI.enabled = enabled && tcpReceiver.isConnected && !tcpReceiver.ScannerControlBusy && !tcpReceiver.DiagnosticsBusy;
                if (GUILayout.Button("Atualizar geometria")) tcpReceiver.RequestGeometry();
                if (GUILayout.Button("Salvar amostras do scanner (CSV)")) tcpReceiver.DownloadScanCsv();
                GUI.enabled = enabled;
                GUILayout.Label(tcpReceiver.calibrationStatus, saoLabelStyle);
                GUILayout.Label("CSV do scanner: últimas amostras locais, antes do alinhamento AR.", saoLabelStyle);
                if (!string.IsNullOrEmpty(tcpReceiver.ScannerDiagnosticPath))
                    GUILayout.Label($"Registro da sessão: {System.IO.Path.GetFileName(tcpReceiver.ScannerDiagnosticPath)}", saoLabelStyle);
            }
            if (uwbReceiver != null)
            {
                GUILayout.Label(uwbReceiver.BaseStatus, saoLabelStyle);
                GUILayout.Label(uwbReceiver.UsbStatus, saoLabelStyle);
                if (uwbReceiver.HasFreshDiagnostics)
                {
                    var d = uwbReceiver.Diagnostics;
                    GUILayout.Label($"UWB bruto: {d.d1:F3} / {d.d2:F3} / {d.d3:F3} m", saoLabelStyle);
                    GUILayout.Label($"Etapas: {UwbDataReceiver.StageName(d.s1)} / {UwbDataReceiver.StageName(d.s2)} / {UwbDataReceiver.StageName(d.s3)}", saoLabelStyle);
                }
            }
            if (anchorManager != null)
            {
                GUILayout.Label(anchorManager.PoseStatus, saoLabelStyle);
                GUILayout.Label(anchorManager.AutoUwbStatus, saoLabelStyle);
                GUILayout.Label(anchorManager.RangePoseStatus, saoLabelStyle);
                GUILayout.Label(anchorManager.HasSavedUwbCalibration ? "Perfil de alcance UWB salvo no celular." : "Sem perfil de alcance UWB salvo; a calibração de duas distâncias fica no menu.", saoLabelStyle);
                if (anchorManager.PoseCandidateCount > 0)
                    GUILayout.Label($"Candidata: {anchorManager.PoseCandidateCount} ciclos, {anchorManager.PoseCandidateViews} vistas; dispersão {anchorManager.PoseCandidateSpreadMeters*100:F1} cm", saoLabelStyle);
            }
            if (GUILayout.Button(showSensorDetails ? "Ocultar sensores" : "Sensores e câmeras")) showSensorDetails = !showSensorDetails;
            if (showSensorDetails) DrawSensorDiagnostics();
            if (GUILayout.Button(showMountingControls ? "Ocultar montagem física" : "Montagem física")) showMountingControls = !showMountingControls;
            if (showMountingControls) DrawMountingControls();
            if (GUILayout.Button(showExperimentControls ? "Ocultar testes de bancada" : "Testes de bancada e experimentos")) showExperimentControls = !showExperimentControls;
            if (showExperimentControls) DrawExperiments();
        }

        private void DrawSensorDiagnostics()
        {
            if (tcpReceiver == null) return;
            var s = tcpReceiver.HasFreshStatus ? tcpReceiver.status : null;
            if (s != null)
            {
                GUILayout.Label($"LiDAR {s.lidarRpm:F1} RPM; pan {s.panDegrees:F1}° / {s.panSteps} pulsos", saoLabelStyle);
                if (s.diagnosticVersion >= 12)
                {
                    GUILayout.Label($"LiDAR desde a inicialização: {s.lidarValidSamples} retornos válidos, {s.lidarWeakSamples} com sinal fraco; " +
                        $"{s.lidarInvalidSamples} inválidos e {s.lidarChecksumErrors} pacotes com erro de integridade.", saoLabelStyle);
                    GUILayout.Label("Sinal fraco é um aviso do sensor. A regra opcional atua sobre esses retornos e preserva a faixa de leitura.", saoLabelStyle);
                }
                GUILayout.Label($"Referência pan: {s.panReferenceState}; válida {s.panReferenceValid}; restaurada {s.panReferenceRestored}; checkpoint pendente {s.panReferenceDirty}", saoLabelStyle);
                GUILayout.Label($"Térmica: iniciada {s.thermalReady}; idade {s.thermalAgeMs} ms; erro {s.thermalError}; {s.thermalFrames} quadros", saoLabelStyle);
                if (s.diagnosticVersion >= 14)
                    GUILayout.Label($"Térmica: {tcpReceiver.thermalFramesPerSecond:F1} quadros/s medidos; " +
                        $"solicitado {s.thermalRequestedFrameRateHz}, alvo {s.thermalTargetFrameRateHz}; janela {s.thermalFrameRateWindowMs} ms.", saoLabelStyle);
                GUILayout.Label($"Térmica: {s.thermalState}; subpáginas {s.thermalSubpageMask}/3; erro bruto {s.thermalRawError}; " +
                    $"leitura {s.thermalReadDurationMs:F0} ms, montagem {s.thermalFrameSpanMs} ms", saoLabelStyle);
                GUILayout.Label($"Térmica: duplicadas {s.thermalDuplicateSubpages}; timeouts {s.thermalFrameTimeouts}; " +
                    $"erros de leitura {s.thermalReadErrors}; atrasos {s.thermalOverruns}; inválidos {s.thermalInvalidFrames}", saoLabelStyle);
                GUILayout.Label($"Tag {s.tagReady}; I2C IMU {s.imuI2cAck}, MLX {s.thermalI2cAck}", saoLabelStyle);
                DrawImuDiagnostics(s);
                GUILayout.Label(s.rgbMessage ?? "RGB: sem estado.", saoLabelStyle);
                if (s.rgbReady && GUILayout.Button("Ver RGB do scanner (diagnóstico)"))
                {
                    thermalPreviewExpanded = false;
                    tcpReceiver.SetCameraPreview(1);
                }
                if (GUILayout.Button(showThermalCalibration ? "Ocultar orientação térmica" : "Calibrar orientação térmica"))
                    showThermalCalibration = !showThermalCalibration;
                if (showThermalCalibration && s.diagnosticVersion >= 7)
                {
                    GUILayout.Label("Use um alvo quente pequeno para conferir lado e posição. Alterar a orientação limpa a nuvem.", saoLabelStyle);
                    bool enabled = GUI.enabled;
                    GUI.enabled = enabled && CanChangeScannerSetup;
                    if (GUILayout.Button("Inverter lado da câmera térmica")) tcpReceiver.RequestThermalOrientation(s.thermalOrientationProfile ^ 1);
                    if (GUILayout.Button("Espelhar imagem térmica")) tcpReceiver.RequestThermalOrientation(s.thermalOrientationProfile ^ 2);
                    GUI.enabled = enabled;
                }
                bool zeroEnabled = GUI.enabled;
                GUI.enabled = zeroEnabled && tcpReceiver.CanConfirmPanReference;
                if (GUILayout.Button("Reconfirmar zero do pan"))
                {
                    tcpReceiver.ConfirmPanZeroReference();
                    ShowFeedback(tcpReceiver.scannerCommandStatus);
                }
                GUI.enabled = zeroEnabled;
                GUILayout.Label("Salve o zero só com a cabeça parada e alinhada à frente física escolhida da base. Esse zero persiste no scanner; o yaw no AR é definido separadamente para a sessão.", saoLabelStyle);
            }
            // A failed image or stale status must never trap the user in preview mode.
            if (tcpReceiver.cameraPreviewMode == 1)
            {
                if (tcpReceiver.cameraPreviewTexture != null)
                {
                    float width = Mathf.Min(controlsContentWidth, 275f);
                    float height = width * tcpReceiver.cameraPreviewTexture.height / tcpReceiver.cameraPreviewTexture.width;
                    GUILayout.Label(tcpReceiver.cameraPreviewTexture, GUILayout.Width(width), GUILayout.Height(height));
                }
                else GUILayout.Label(tcpReceiver.cameraStatus, saoLabelStyle);
                if (GUILayout.Button("Fechar RGB")) tcpReceiver.SetCameraPreview(0);
            }
        }

        private void DrawImuDiagnostics(PointCloudTcpReceiver.ScannerStatus s)
        {
            GUILayout.Label("GY-25 / MPU6050: " + DescribeImuState(s), saoLabelStyle);
            string reading = tcpReceiver.HasUsableImuSnapshot ? "atual" :
                tcpReceiver.HasReadableImuSnapshot ? "atrasada" : "indisponível";
            GUILayout.Label("Leitura: " + reading + "; giro dos pontos: " + DescribePointYawSource(s), saoLabelStyle);
            if (s.diagnosticVersion >= 11)
            {
                string identity = s.imuIdentity < 0 ? "não lida" : $"0x{s.imuIdentity:X2}";
                GUILayout.Label($"Identidade: {identity}; inicializações {s.imuInitAttempts}; falhas de leitura {s.imuReadErrors}", saoLabelStyle);
                GUILayout.Label($"Bias em repouso: {(s.imuBiasCalibrated ? "concluído" : "não confirmado")}; " +
                    $"inclinação dos pontos: {(s.imuTiltApplied ? "habilitada" : "desligada")}", saoLabelStyle);
            }
            else GUILayout.Label($"Inclinação dos pontos: {(s.imuCalibrated ? "habilitada" : "desligada")}; detalhes da inicialização exigem firmware v11.", saoLabelStyle);

            string age = s.imuAgeMs == uint.MaxValue ? "sem amostra válida" : $"{s.imuAgeMs} ms no scanner";
            GUILayout.Label($"Última consulta há {tcpReceiver.StatusSnapshotAgeSeconds:F1} s; idade da IMU: {age}.", saoLabelStyle);
            if (s.imuSampleIntervalUs > 0)
                GUILayout.Label($"Último intervalo: {s.imuSampleIntervalUs / 1000f:F1} ms; lacunas de integração: {s.imuIntegrationGaps}.", saoLabelStyle);
            if (s.imuOrientationInvalidations > 0)
            {
                string reason = s.imuOrientationInvalidReason == "invalid_gravity" ? "aceleração fora da faixa" :
                    s.imuOrientationInvalidReason == "sample_gap" ? "intervalo entre leituras" :
                    s.imuOrientationInvalidReason == "read_failed" ? "falha de leitura" : "leitura de giro inválida";
                GUILayout.Label($"Última perda da referência GY-25: {reason}.", saoLabelValueStyle);
                if (s.imuOrientationInvalidSampleValid)
                    GUILayout.Label($"Aceleração no evento: {s.imuOrientationInvalidGravityNormG:F2} g; " +
                        $"intervalo {s.imuOrientationInvalidSampleIntervalUs / 1000f:F1} ms.", saoLabelStyle);
            }
            if (tcpReceiver.HasReadableImuSnapshot)
            {
                var raw = s.imuRaw;
                float gravity = new Vector3(raw.accelX, raw.accelY, raw.accelZ).magnitude;
                GUILayout.Label($"Sensor X/Y/Z — aceleração {raw.accelX:F2}/{raw.accelY:F2}/{raw.accelZ:F2} g (módulo {gravity:F2} g)", saoLabelStyle);
                GUILayout.Label($"Sensor X/Y/Z — giro {raw.gyroX:F1}/{raw.gyroY:F1}/{raw.gyroZ:F1} °/s", saoLabelStyle);
                GUILayout.Label($"Ângulos do sensor X/Y/Z: {raw.angleX:F1}/{raw.angleY:F1}/{raw.angleZ:F1}°. A direção no AR permanece no alinhamento manual.", saoLabelStyle);
                if (!tcpReceiver.HasUsableImuSnapshot)
                    GUILayout.Label("Leitura atrasada: serve para inspeção, sem acompanhar a rotação em tempo real.", saoLabelStyle);
            }
            else GUILayout.Label("Sem amostra IMU válida na última consulta; zeros não representam uma cabeça nivelada.", saoLabelStyle);

            if (tcpReceiver.geometry != null)
            {
                var g = tcpReceiver.geometry;
                string[] axes = { "X", "Y", "Z" };
                string pitchAxis = g.imuPitchAxis >= 0 && g.imuPitchAxis < axes.Length ? axes[g.imuPitchAxis] : "inválido";
                string rollAxis = g.imuRollAxis >= 0 && g.imuRollAxis < axes.Length ? axes[g.imuRollAxis] : "inválido";
                GUILayout.Label($"Montagem recebida: pitch ← {pitchAxis} × {g.imuPitchSign:F0}; roll ← {rollAxis} × {g.imuRollSign:F0}; " +
                    $"IMU {(g.imuOnHead ? "na cabeça" : "na base")}. Confira os eixos físicos antes de habilitar compensação.", saoLabelStyle);
            }
            GUILayout.Label("SDA/SCL usa o modo I2C do GY-25. Confira a alimentação principal e o seletor físico; o app não identifica a posição da ponte.", saoLabelStyle);
        }

        private string DescribePointYawSource(PointCloudTcpReceiver.ScannerStatus s)
        {
            if (!tcpReceiver.HasFreshStatus) return "aguardando consulta atual";
            if (s.diagnosticVersion < 14 || !s.imuOrientationEnabled) return "motor (GY-25 desligada)";
            if (!tcpReceiver.HasUsableImuOrientation) return "GY-25 sem referência ou leitura válida";
            return s.isScanning ? "GY-25 aplicada" : "GY-25 pronta; captura parada";
        }

        private static string DescribeImuState(PointCloudTcpReceiver.ScannerStatus s)
        {
            if (s.diagnosticVersion < 11 || string.IsNullOrEmpty(s.imuState))
                return s.imuReady ? "inicializada; confira a idade da leitura" : "não inicializada; confira alimentação e modo I2C";
            switch (s.imuState)
            {
                case "not_initialized": return "aguardando inicialização";
                case "identity_read_failed": return "falha ao ler a identidade; confira alimentação e modo I2C";
                case "unexpected_identity": return "identidade diferente da MPU6050 esperada";
                case "configuration_failed": return "falha ao configurar o sensor";
                case "calibration_read_failed": return "falha de leitura durante a calibração em repouso";
                case "calibration_moving": return "calibração recusada por movimento; mantenha a cabeça parada";
                case "calibration_unstable": return "leituras instáveis durante a calibração em repouso";
                case "ready": return "inicializada; montagem física ainda precisa de conferência";
                case "sample_read_failed": return "falha de leitura; aguardando nova amostra válida";
                default: return "estado de diagnóstico: " + s.imuState;
            }
        }

        private void DrawMountingControls()
        {
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && CanChangeScannerSetup;
            if (anchorManager != null)
            {
                GUILayout.Label($"Altura do eixo sobre o apoio: {anchorManager.previewOriginHeight*100:F1} cm", saoLabelStyle);
                float height = GUILayout.HorizontalSlider(anchorManager.previewOriginHeight, .02f, .5f);
                if (Mathf.Abs(height-anchorManager.previewOriginHeight) > .001f)
                {
                    if (anchorManager.localPreviewWithoutUwb)
                        anchorManager.localPreviewPosition += Vector3.up*(height-anchorManager.previewOriginHeight);
                    anchorManager.previewOriginHeight = height;
                    anchorManager.InvalidateHeading();
                    if (!anchorManager.localPreviewWithoutUwb) anchorManager.ResumeAutomaticUwb();
                    pointRenderer?.ClearPointCloud();
                }
                if (GUILayout.Button($"Girar referência da placa USB: {anchorManager.baseRotationEuler.z:F0}°"))
                {
                    anchorManager.RotateBoardInPhone90();
                    pointRenderer?.ClearPointCloud();
                }
            }
            if (pointRenderer != null)
            {
                GUILayout.Label($"Montagem fixa LiDAR → base: yaw {pointRenderer.pointYawOffset:F0}°", saoLabelStyle);
                float yaw = GUILayout.HorizontalSlider(pointRenderer.pointYawOffset, 0, 360);
                if (Mathf.Abs(yaw-pointRenderer.pointYawOffset) > .5f)
                {
                    pointRenderer.pointYawOffset = yaw;
                    anchorManager?.InvalidateHeading();
                    pointRenderer.ClearPointCloud();
                }
                GUILayout.Label($"Montagem: pitch {pointRenderer.pointPitchOffset:F0}°, roll {pointRenderer.pointRollOffset:F0}°", saoLabelStyle);
                float pitch = GUILayout.HorizontalSlider(pointRenderer.pointPitchOffset, 0, 360);
                float roll = GUILayout.HorizontalSlider(pointRenderer.pointRollOffset, -180, 180);
                if (Mathf.Abs(pitch-pointRenderer.pointPitchOffset) > .5f || Mathf.Abs(roll-pointRenderer.pointRollOffset) > .5f)
                {
                    pointRenderer.pointPitchOffset = pitch; pointRenderer.pointRollOffset = roll;
                    pointRenderer.ClearPointCloud();
                }
                bool invert = GUILayout.Toggle(pointRenderer.invertVerticalLidar, "Inverter vertical LiDAR (teste chão/teto)");
                if (invert != pointRenderer.invertVerticalLidar)
                {
                    pointRenderer.invertVerticalLidar = invert; pointRenderer.ClearPointCloud();
                }
                if (GUILayout.Button("Restaurar montagem: yaw 200°, pitch/roll 0°"))
                {
                    pointRenderer.pointPitchOffset = pointRenderer.pointRollOffset = 0;
                    pointRenderer.pointYawOffset = ThermalPointCloudRenderer.DefaultMountYawOffset;
                    anchorManager?.InvalidateHeading(); pointRenderer.ClearPointCloud();
                }
            }
            GUI.enabled = enabled;
        }

        private void DrawExperiments()
        {
            GUILayout.Label("Opções de teste; não fazem parte da captura normal com o scanner parado.", saoLabelStyle);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && CanChangeScannerSetup;
            if (anchorManager != null)
            {
                if (GUILayout.Button("Recomeçar localização UWB automática"))
                {
                    anchorManager.ResumeAutomaticUwb(); pointRenderer?.ClearPointCloud();
                }
                if (GUILayout.Button(anchorManager.scannerStationary ? "Testar deslocamento livre (sem captura)" : "Encerrar teste de deslocamento"))
                {
                    anchorManager.SetScannerStationary(!anchorManager.scannerStationary); pointRenderer?.ClearPointCloud();
                }
                bool automatic = GUILayout.Toggle(anchorManager.autoAlignHeading && anchorManager.autoSupportHeadingFallback,
                    "Testar direção automática por ponto no apoio");
                if (automatic != (anchorManager.autoAlignHeading && anchorManager.autoSupportHeadingFallback))
                {
                    anchorManager.autoAlignHeading = anchorManager.autoSupportHeadingFallback = automatic;
                    anchorManager.InvalidateHeading(); pointRenderer?.ClearPointCloud();
                }
                var observer = anchorManager.GetComponent<ScannerVisualPoseObserver>();
                if (observer != null)
                {
                    bool cover = GUILayout.Toggle(anchorManager.enableExperimentalVisualPose,
                        "Testar reconhecimento da tampa laranja");
                    if (cover != anchorManager.enableExperimentalVisualPose)
                    {
                        observer.SetNaturalCoverRecognition(cover);
                        anchorManager.InvalidateHeading(); pointRenderer?.ClearPointCloud();
                    }
                    if (anchorManager.enableExperimentalVisualPose) GUILayout.Label(observer.Status, saoLabelStyle);
                }
            }
            if (tcpReceiver != null)
            {
                if (GUILayout.Button(tcpReceiver.panEnabled ? "Bancada 2D: desligar giro do pan" : "Bancada 3D: habilitar giro do pan"))
                {
                    if (tcpReceiver.SendPanEnabled(!tcpReceiver.panEnabled)) pointRenderer?.ClearPointCloud();
                }
                GUILayout.Label($"Motor LiDAR: {tcpReceiver.currentLidarSpeedPercent*100:F0}%", saoLabelStyle);
                float speed = GUILayout.HorizontalSlider(tcpReceiver.currentLidarSpeedPercent, .2f, 1);
                if (Mathf.Abs(speed-tcpReceiver.currentLidarSpeedPercent) > .03f) tcpReceiver.SendSetLidarSpeed(speed);
                if (GUILayout.Button(tcpReceiver.currentScanMode == 1 ? "Modo 180°: mudar para 360°" : "Modo 360°: mudar para 180°"))
                    tcpReceiver.SendSetMode(tcpReceiver.currentScanMode == 0 ? 1 : 0);
            }
            GUI.enabled = enabled;
            if (simulator != null && Application.isEditor && GUILayout.Button(simulator.isSimulationActive ? "Encerrar simulação do editor" : "Iniciar simulação no editor"))
            {
                var controller = GetComponent<ArScanner.ArScannerController>();
                if (controller != null) controller.SetSimulationMode(!simulator.isSimulationActive);
            }
        }

        private void DrawThermalPreview(Rect panel)
        {
            if (!thermalPreviewExpanded || tcpReceiver == null || tcpReceiver.cameraPreviewMode != 2) return;
            GUI.Box(panel, "", saoBoxStyle);
            GUI.Label(new Rect(panel.x+12, panel.y+8, panel.width-24, 38), "TÉRMICA 24 × 32", saoLabelTitleStyle);
            bool freshImage = tcpReceiver.HasFreshThermalPreview;
            bool partial = tcpReceiver.CameraThermalMaskedPixels > 0;
            if (freshImage)
            {
                Rect image = new Rect(panel.x+12, panel.y+54, panel.width-24, panel.height-(partial ? 160 : 135));
                GUI.DrawTexture(image, Texture2D.blackTexture);
                GUI.DrawTexture(image, tcpReceiver.cameraPreviewTexture, ScaleMode.ScaleToFit);
            }
            else
            {
                string detail = tcpReceiver.HasFreshStatus
                    ? $"Estado: {tcpReceiver.status.thermalState ?? "aguardando quadro"}; último quadro completo há {tcpReceiver.status.thermalAgeMs} ms."
                    : "Sem confirmação recente do scanner.";
                GUI.Label(new Rect(panel.x+12, panel.y+70, panel.width-24, panel.height-90),
                    $"Imagem térmica indisponível. {detail}\n{tcpReceiver.cameraStatus}", saoLabelStyle);
            }
            if (freshImage)
            {
                float bottomOffset = partial ? 100 : 62;
                GUI.Label(new Rect(panel.x+12, panel.yMax-bottomOffset, panel.width-24, partial ? 44 : 36),
                    $"{tcpReceiver.cameraMinTemperature:F1}–{tcpReceiver.cameraMaxTemperature:F1} °C | {tcpReceiver.thermalFramesPerSecond:F1} quadros/s", saoLabelValueStyle);
                if (partial)
                    GUI.Label(new Rect(panel.x+12, panel.yMax-52, panel.width-24, 44),
                        tcpReceiver.ThermalPreviewNotice, saoLabelStyle);
            }
        }
    }
}
