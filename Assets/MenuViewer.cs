using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using ArScanner.Network;
using ArScanner.Spatial;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tela de entrada: a base UWB é verificada pela USB-C e o scanner pela rede Wi-Fi.
/// A cena antiga continha um Canvas e um segundo menu OnGUI sobreposto. Toda a
/// interface é criada aqui, dentro de um único Canvas, respeitando a área segura.
/// </summary>
public class MenuViewer : MonoBehaviour
{
    [Header("Canvas legado (mantido apenas para compatibilidade com a cena)")]
    public Text textoStatus;
    public Button botaoIniciar;

    [Header("Scanner Wi-Fi")]
    public string scannerIp = "192.168.4.1";
    public int scannerPort = 8888;

    public bool isScannerAvailable;

    private static readonly Color BackgroundColor = Html("#081320");
    private static readonly Color CardColor = Html("#14283B");
    private static readonly Color PrimaryColor = Html("#31C7E9");
    private static readonly Color MainTextColor = Html("#F4FAFD");
    private static readonly Color SubTextColor = Html("#ADC2CF");
    private static readonly Color GoodColor = Html("#73DBAD");
    private static readonly Color WaitingColor = Html("#FFCE78");

    private TcpClient probeClient;
    private UwbDataReceiver uwbReceiver;
    private bool isLoading;
    private bool canStart;
    private RectTransform safeAreaRoot;
    private RectTransform scrollRectTransform;
    private RectTransform actionRoot;
    private Text scannerStateText;
    private Text scannerDetailText;
    private Text baseStateText;
    private Text baseDetailText;
    private Text footerText;
    private Text startText;
    private Button startButton;
    private Button usbRetryButton;
    private RectTransform calibrationPanel;
    private LayoutElement calibrationLayout;
    private GameObject calibrationDetails;
    private Text calibrationTitleText;
    private Text nearCaptureText;
    private Text farCaptureText;
    private Text calibrationMessageText;
    private string calibrationNotice;
    private bool calibrationNoticeIsSuccess;
    private InputField nearDistanceInput;
    private InputField farDistanceInput;
    private bool calibrationExpanded;
    private bool nearCaptured;
    private bool farCaptured;
    private Vector3 nearRaw;
    private Vector3 farRaw;
    private const int CalibrationSamples = 20;
    private readonly List<Vector3> rangeCapture = new List<Vector3>(CalibrationSamples);
    private bool captureActive;
    private bool captureNear;
    private long lastCaptureDiagnosticsId = -1;
    private static Font menuFont;
    private Rect lastSafeArea;
    private int lastScreenWidth;
    private int lastScreenHeight;
    private float nextUiRefresh;

    private void Start()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
#endif

        // A checagem USB começa no menu, antes da navegação para a cena AR.
        uwbReceiver = GetComponent<UwbDataReceiver>();
        if (uwbReceiver == null) uwbReceiver = gameObject.AddComponent<UwbDataReceiver>();
        uwbReceiver.transportMode = UwbTransportMode.USB;

        menuFont = textoStatus != null && textoStatus.font != null
            ? textoStatus.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (textoStatus != null) textoStatus.gameObject.SetActive(false);
        if (botaoIniciar != null) botaoIniciar.gameObject.SetActive(false);

        BuildMenu();
        RefreshCalibrationTitle();
        UpdateSafeArea();
        RefreshStatuses();
        StartCoroutine(ProbeNetworkCoroutine());
    }

    private IEnumerator ProbeNetworkCoroutine()
    {
        while (!isLoading)
        {
            probeClient = new TcpClient();
            IAsyncResult result = null;
            isScannerAvailable = false;
            try { result = probeClient.BeginConnect(scannerIp, scannerPort, null, null); }
            catch (SocketException) { }
            catch (ArgumentException) { }

            if (result != null)
            {
                float deadline = Time.realtimeSinceStartup + 0.6f;
                while (!result.IsCompleted && Time.realtimeSinceStartup < deadline)
                    yield return null;
                try
                {
                    if (result.IsCompleted)
                    {
                        probeClient.EndConnect(result);
                        isScannerAvailable = probeClient.Connected;
                    }
                }
                catch (SocketException) { isScannerAvailable = false; }
            }

            probeClient.Close();
            probeClient = null;
            yield return new WaitForSecondsRealtime(1.5f);
        }
    }

    private void Update()
    {
        UpdateRangeCapture();
        if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight || Screen.safeArea != lastSafeArea)
            UpdateSafeArea();

        if (Time.unscaledTime >= nextUiRefresh)
        {
            nextUiRefresh = Time.unscaledTime + 0.25f;
            RefreshStatuses();
        }
    }

    private void RefreshStatuses()
    {
        if (scannerStateText == null || uwbReceiver == null) return;

        scannerStateText.text = isScannerAvailable ? "Conectado por Wi-Fi" : "Aguardando scanner";
        scannerStateText.color = isScannerAvailable ? GoodColor : WaitingColor;
        scannerDetailText.text = isScannerAvailable
            ? $"Scanner encontrado em {scannerIp}:{scannerPort}."
            : "Conecte o celular à rede ArScanner_Net e ligue o scanner.";

        bool usbOnline = uwbReceiver.UsbHasRecentData && uwbReceiver.HasFreshDiagnostics;
        UwbDataReceiver.BaseDiagnostics diagnostic = uwbReceiver.Diagnostics;
        bool radiosReady = usbOnline && diagnostic != null && diagnostic.radioMask == 7;
        bool rangesReady = radiosReady && uwbReceiver.HasFreshThreeRanges;
        if (usbOnline && diagnostic != null)
        {
            int measured = (diagnostic.d1 > 0 ? 1 : 0) +
                (diagnostic.d2 > 0 ? 1 : 0) + (diagnostic.d3 > 0 ? 1 : 0);
            baseStateText.text = rangesReady ? "Base e tag medindo por USB-C" :
                radiosReady ? "Base USB-C: aguardando a tag" : "Base USB-C: verifique os rádios";
            baseStateText.color = rangesReady ? GoodColor : WaitingColor;
            baseDetailText.text = $"Rádios: {CountBits(diagnostic.radioMask)}/3  •  Distâncias: {measured}/3\n" +
                (measured == 3
                    ? $"{diagnostic.d1:F2} m   {diagnostic.d2:F2} m   {diagnostic.d3:F2} m"
                    : "As medidas aparecem assim que a tag do scanner responde.");
        }
        else
        {
            baseStateText.text = "Aguardando base USB-C";
            baseStateText.color = WaitingColor;
            baseDetailText.text = uwbReceiver.UsbStatus;
        }

        // Abrir AR não inicia os motores. A captura verifica pose e direção no
        // visualizador; a marcação manual continua disponível sem medidas UWB.
        canStart = isScannerAvailable;
        startButton.interactable = canStart && !isLoading;
        bool hasSavedCalibration = UwbRangeCalibrationProfile.HasSavedCalibration;
        startText.text = "ABRIR SCANNER AR";
        footerText.text = !string.IsNullOrEmpty(calibrationNotice) ? calibrationNotice : canStart
            ? hasSavedCalibration && rangesReady
                ? "Localize e confirme o eixo no AR antes de iniciar o scan."
                : "Marque o eixo pelo AR. Localização UWB exige perfil salvo e três distâncias recentes."
            : "Conecte o celular à rede ArScanner_Net e ligue o scanner para abrir o AR.";
        footerText.color = !string.IsNullOrEmpty(calibrationNotice)
            ? calibrationNoticeIsSuccess ? GoodColor : WaitingColor
            : !hasSavedCalibration ? WaitingColor : SubTextColor;
        usbRetryButton.gameObject.SetActive(!usbOnline);
    }

    private void BuildMenu()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObject = new GameObject("Menu Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform background = NewRect(canvas.transform, "Fundo");
        Stretch(background);
        background.gameObject.AddComponent<Image>().color = BackgroundColor;

        safeAreaRoot = NewRect(background, "Área segura");
        Stretch(safeAreaRoot);

        RectTransform accent = NewRect(safeAreaRoot, "Linha de destaque");
        AnchorAtTop(accent, 0f, 8f);
        accent.gameObject.AddComponent<Image>().color = PrimaryColor;

        scrollRectTransform = NewRect(safeAreaRoot, "Informações e conexões");
        ScrollRect scroll = scrollRectTransform.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 50f;

        RectTransform viewport = NewRect(scrollRectTransform, "Viewport");
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        scroll.viewport = viewport;

        RectTransform content = NewRect(viewport, "Conteúdo");
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 16, 16);
        layout.spacing = 20f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content;

        RectTransform header = NewRect(content, "Cabeçalho");
        PreferredHeight(header, 194f);
        Text eyebrow = NewText(header, "SISTEMA DE MAPEAMENTO", 24, FontStyle.Bold, PrimaryColor);
        TopInset(eyebrow.rectTransform, 4f, 36f, 0f);
        Text title = NewText(header, "AR SCANNER", 61, FontStyle.Bold, MainTextColor);
        TopInset(title.rectTransform, 40f, 78f, 0f);
        Text subtitle = NewText(header, "Scanner Wi-Fi e posicionamento UWB por USB-C", 27,
            FontStyle.Normal, SubTextColor);
        TopInset(subtitle.rectTransform, 128f, 62f, 0f);

        RectTransform scannerCard = NewCard(content, "Scanner 3D", "01  SCANNER 3D", out scannerStateText,
            out scannerDetailText);
        PreferredHeight(scannerCard, 170f);

        RectTransform baseCard = NewCard(content, "Base UWB", "02  BASE UWB • 3 RÁDIOS", out baseStateText,
            out baseDetailText);
        PreferredHeight(baseCard, 214f);
        usbRetryButton = NewButton(baseCard, "Autorizar USB novamente", "AUTORIZAR USB NOVAMENTE", false);
        RectTransform usbRetryRect = (RectTransform)usbRetryButton.transform;
        usbRetryRect.anchorMin = new Vector2(0f, 0f);
        usbRetryRect.anchorMax = new Vector2(0f, 0f);
        usbRetryRect.pivot = new Vector2(0f, 0f);
        usbRetryRect.anchoredPosition = new Vector2(26f, 10f);
        usbRetryRect.sizeDelta = new Vector2(370f, 40f);
        usbRetryButton.onClick.AddListener(() => uwbReceiver?.RequestUsbPermissionAgain());

        BuildCalibrationPanel(content);

        actionRoot = NewRect(safeAreaRoot, "Ação principal");
        startButton = NewButton(actionRoot, "Iniciar", "INICIAR", true);
        startButton.onClick.AddListener(IniciarComHardware);
        startText = startButton.GetComponentInChildren<Text>();
        footerText = NewText(actionRoot, "", 22, FontStyle.Normal, SubTextColor);
        footerText.alignment = TextAnchor.MiddleCenter;
    }

    private void BuildCalibrationPanel(Transform content)
    {
        calibrationPanel = NewRect(content, "Calibração UWB");
        calibrationPanel.gameObject.AddComponent<Image>().color = CardColor;
        calibrationLayout = calibrationPanel.gameObject.AddComponent<LayoutElement>();
        calibrationLayout.preferredHeight = 68f;

        Button expandButton = NewButton(calibrationPanel, "Abrir calibração", "", false);
        RectTransform expandRect = (RectTransform)expandButton.transform;
        expandRect.anchorMin = new Vector2(0f, 1f);
        expandRect.anchorMax = Vector2.one;
        expandRect.pivot = new Vector2(0.5f, 1f);
        expandRect.anchoredPosition = Vector2.zero;
        expandRect.sizeDelta = new Vector2(0f, 68f);
        calibrationTitleText = expandButton.GetComponentInChildren<Text>();
        calibrationTitleText.alignment = TextAnchor.MiddleLeft;
        calibrationTitleText.rectTransform.offsetMin = new Vector2(26f, 0f);
        expandButton.onClick.AddListener(ToggleCalibration);

        calibrationDetails = NewRect(calibrationPanel, "Passos de calibração").gameObject;
        RectTransform detailsRect = (RectTransform)calibrationDetails.transform;
        detailsRect.anchorMin = new Vector2(0f, 1f);
        detailsRect.anchorMax = Vector2.one;
        detailsRect.pivot = new Vector2(0.5f, 1f);
        detailsRect.anchoredPosition = new Vector2(0f, -68f);
        detailsRect.sizeDelta = new Vector2(0f, 520f);

        Text explanation = NewText(detailsRect,
            "Motores parados. Alinhe a tag diante do ponto médio das antenas da base e meça até a tag; capture perto e longe.",
            23, FontStyle.Normal, SubTextColor);
        TopInset(explanation.rectTransform, 8f, 62f, 26f);

        Text nearLabel = NewText(detailsRect, "1  POSIÇÃO PRÓXIMA (METROS)", 22, FontStyle.Bold, PrimaryColor);
        TopInset(nearLabel.rectTransform, 78f, 35f, 26f);
        nearDistanceInput = NewDistanceInput(detailsRect, 114f, "Ex.: 0,80");
        Button nearCaptureButton = NewButton(detailsRect, "Capturar perto", "CAPTURAR", false);
        PlaceCalibrationButton(nearCaptureButton, 114f);
        nearCaptureButton.onClick.AddListener(() => CaptureRanges(true));
        nearCaptureText = NewText(detailsRect, "Nenhuma medida capturada.", 21, FontStyle.Normal, SubTextColor);
        TopInset(nearCaptureText.rectTransform, 162f, 32f, 26f);

        Text farLabel = NewText(detailsRect, "2  POSIÇÃO DISTANTE (METROS)", 22, FontStyle.Bold, PrimaryColor);
        TopInset(farLabel.rectTransform, 204f, 35f, 26f);
        farDistanceInput = NewDistanceInput(detailsRect, 240f, "Ex.: 1,50");
        Button farCaptureButton = NewButton(detailsRect, "Capturar longe", "CAPTURAR", false);
        PlaceCalibrationButton(farCaptureButton, 240f);
        farCaptureButton.onClick.AddListener(() => CaptureRanges(false));
        farCaptureText = NewText(detailsRect, "Nenhuma medida capturada.", 21, FontStyle.Normal, SubTextColor);
        TopInset(farCaptureText.rectTransform, 288f, 32f, 26f);

        Button saveButton = NewButton(detailsRect, "Salvar perfil", "SALVAR CALIBRAÇÃO", true);
        RectTransform saveRect = (RectTransform)saveButton.transform;
        TopInset(saveRect, 335f, 56f, 26f);
        saveRect.sizeDelta = new Vector2(-52f, 56f);
        saveButton.onClick.AddListener(SaveCalibration);

        calibrationMessageText = NewText(detailsRect, "", 20, FontStyle.Normal, SubTextColor);
        TopInset(calibrationMessageText.rectTransform, 398f, 55f, 26f);
        Button resetButton = NewButton(detailsRect, "Redefinir calibração UWB", "REDEFINIR PERFIL", false);
        RectTransform resetRect = (RectTransform)resetButton.transform;
        TopInset(resetRect, 458f, 45f, 26f);
        resetRect.sizeDelta = new Vector2(-52f, 45f);
        resetButton.onClick.AddListener(ResetCalibration);
        calibrationDetails.SetActive(false);
    }

    private InputField NewDistanceInput(Transform parent, float top, string example)
    {
        RectTransform rect = NewRect(parent, "Distância física");
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(26f, -top);
        rect.sizeDelta = new Vector2(200f, 42f);
        rect.gameObject.AddComponent<Image>().color = Html("#203C52");
        InputField input = rect.gameObject.AddComponent<InputField>();
        input.contentType = InputField.ContentType.DecimalNumber;
        input.keyboardType = TouchScreenKeyboardType.DecimalPad;
        Text value = NewText(rect, "", 24, FontStyle.Normal, MainTextColor);
        Stretch(value.rectTransform);
        value.rectTransform.offsetMin = new Vector2(12f, 0f);
        value.rectTransform.offsetMax = new Vector2(-12f, 0f);
        value.alignment = TextAnchor.MiddleLeft;
        value.raycastTarget = true;
        input.textComponent = value;
        Text placeholder = NewText(rect, example, 22, FontStyle.Normal, SubTextColor);
        Stretch(placeholder.rectTransform);
        placeholder.rectTransform.offsetMin = new Vector2(12f, 0f);
        placeholder.rectTransform.offsetMax = new Vector2(-12f, 0f);
        placeholder.alignment = TextAnchor.MiddleLeft;
        input.placeholder = placeholder;
        return input;
    }

    private static void PlaceCalibrationButton(Button button, float top)
    {
        RectTransform rect = (RectTransform)button.transform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(246f, -top);
        rect.sizeDelta = new Vector2(205f, 42f);
    }

    private void ToggleCalibration()
    {
        calibrationExpanded = !calibrationExpanded;
        calibrationDetails.SetActive(calibrationExpanded);
        calibrationLayout.preferredHeight = calibrationExpanded ? 588f : 68f;
        LayoutRebuilder.MarkLayoutForRebuild(calibrationPanel);
        RefreshCalibrationTitle();
    }

    private void RefreshCalibrationTitle()
    {
        if (calibrationTitleText == null) return;
        float[] scales = new float[3];
        float[] offsets = new float[3];
        UwbRangeCalibrationProfile.Load(scales, offsets, out bool saved);
        calibrationTitleText.text = saved
            ? "CALIBRAÇÃO UWB  •  perfil salvo" + (calibrationExpanded ? "  ▲" : "  ▼")
            : "CALIBRAÇÃO UWB  •  sem perfil validado" + (calibrationExpanded ? "  ▲" : "  ▼");
    }

    private void CaptureRanges(bool near)
    {
        if (uwbReceiver == null || !uwbReceiver.UsbHasRecentData || !uwbReceiver.HasFreshThreeRanges ||
            uwbReceiver.Diagnostics == null)
        {
            calibrationMessageText.text = "Aguarde três medidas UWB recentes pela USB-C para capturar.";
            calibrationMessageText.color = WaitingColor;
            return;
        }
        captureActive = true;
        captureNear = near;
        lastCaptureDiagnosticsId = uwbReceiver.AppliedDiagnosticsId;
        rangeCapture.Clear();
        if (near) { nearCaptured = false; nearCaptureText.text = "Capturando: 0/20 leituras"; }
        else { farCaptured = false; farCaptureText.text = "Capturando: 0/20 leituras"; }
        calibrationMessageText.text = "Mantenha base e scanner imóveis por alguns segundos.";
        calibrationMessageText.color = SubTextColor;
        calibrationNotice = "Captura UWB em andamento: mantenha placa e scanner imóveis.";
        calibrationNoticeIsSuccess = false;
        RecordCalibrationEvent("capture_start", near ? "near" : "far");
    }

    private void UpdateRangeCapture()
    {
        if (!captureActive || uwbReceiver == null || !uwbReceiver.UsbHasRecentData ||
            !uwbReceiver.HasFreshThreeRanges || uwbReceiver.Diagnostics == null ||
            lastCaptureDiagnosticsId == uwbReceiver.AppliedDiagnosticsId) return;
        lastCaptureDiagnosticsId = uwbReceiver.AppliedDiagnosticsId;
        var d = uwbReceiver.Diagnostics;
        rangeCapture.Add(new Vector3(d.d1,d.d2,d.d3));
        Text progress = captureNear ? nearCaptureText : farCaptureText;
        progress.text = $"Capturando: {rangeCapture.Count}/{CalibrationSamples} leituras";
        if (rangeCapture.Count < CalibrationSamples) return;
        captureActive = false;
        if (!TryTrimmedMean(rangeCapture,out Vector3 average))
        {
            CalibrationError("Medidas instáveis. Mantenha as placas imóveis e repita esta posição.");
            progress.text = "Captura instável; repetir.";
            return;
        }
        if (captureNear) { nearRaw = average; nearCaptured = true; }
        else { farRaw = average; farCaptured = true; }
        progress.text = $"Média estável: {average.x:F2} / {average.y:F2} / {average.z:F2} m";
        calibrationMessageText.text = "Captura concluída. Informe a distância física e capture o outro ponto.";
        calibrationMessageText.color = GoodColor;
        calibrationNotice = "Captura UWB concluída; salve as duas posições para ativar o perfil.";
        calibrationNoticeIsSuccess = false;
        RecordCalibrationEvent(captureNear ? "capture_near" : "capture_far",
            string.Format(CultureInfo.InvariantCulture, "{0:F4},{1:F4},{2:F4}", average.x, average.y, average.z));
    }

    private static bool TryTrimmedMean(List<Vector3> values, out Vector3 average)
    {
        average = Vector3.zero;
        if (values == null || values.Count < CalibrationSamples) return false;
        for (int axis = 0; axis < 3; axis++)
        {
            var samples = new float[values.Count];
            for (int i = 0; i < values.Count; i++)
                samples[i] = axis == 0 ? values[i].x : axis == 1 ? values[i].y : values[i].z;
            Array.Sort(samples);
            int trim = samples.Length / 5;
            if (samples[samples.Length-trim-1] - samples[trim] > .20f) return false;
            float sum = 0f;
            for (int i = trim; i < samples.Length-trim; i++) sum += samples[i];
            float mean = sum / (samples.Length-2*trim);
            if (axis == 0) average.x = mean;
            else if (axis == 1) average.y = mean;
            else average.z = mean;
        }
        return true;
    }

    private void SaveCalibration()
    {
        RecordCalibrationEvent("save_attempt",
            "near=" + nearDistanceInput.text + ";far=" + farDistanceInput.text +
            ";nearCaptured=" + nearCaptured + ";farCaptured=" + farCaptured);
        if (!nearCaptured || !farCaptured)
        {
            CalibrationError("Capture primeiro os dois conjuntos de medidas.");
            return;
        }
        if (!TryReadMeters(nearDistanceInput.text, out float nearMeters) ||
            !TryReadMeters(farDistanceInput.text, out float farMeters))
        {
            CalibrationError("Digite as duas distâncias físicas em metros.");
            return;
        }
        if (!UwbRangeCalibrationProfile.TrySaveTwoPoint(nearMeters, nearRaw, farMeters, farRaw,
            out string error))
        {
            CalibrationError(error);
            return;
        }
        float[] scales = new float[3];
        float[] offsets = new float[3];
        UwbRangeCalibrationProfile.Load(scales, offsets, out bool saved);
        if (!saved)
        {
            CalibrationError("O perfil não pôde ser lido após salvar. Repita a calibração.");
            return;
        }
        calibrationMessageText.text = "Perfil salvo. A calibração será usada nas próximas sessões.";
        calibrationMessageText.color = GoodColor;
        calibrationNotice = "Perfil UWB salvo e confirmado. Posicionamento automático liberado para teste.";
        calibrationNoticeIsSuccess = true;
        RecordCalibrationEvent("save_success", string.Format(CultureInfo.InvariantCulture,
            "scale={0:F6}/{1:F6}/{2:F6};offset={3:F6}/{4:F6}/{5:F6}",
            scales[0], scales[1], scales[2], offsets[0], offsets[1], offsets[2]));
        RefreshCalibrationTitle();
    }

    private void ResetCalibration()
    {
        UwbRangeCalibrationProfile.Reset();
        captureActive = nearCaptured = farCaptured = false;
        nearCaptureText.text = farCaptureText.text = "Nenhuma medida capturada.";
        calibrationMessageText.text = "Perfil removido. Distâncias brutas serão mostradas até nova calibração; marque o scanner no AR para escanear parado.";
        calibrationMessageText.color = WaitingColor;
        calibrationNotice = "Perfil UWB removido. O eixo automático ficará indisponível.";
        calibrationNoticeIsSuccess = false;
        RecordCalibrationEvent("reset", "profile_removed");
        RefreshCalibrationTitle();
    }

    private void CalibrationError(string message)
    {
        calibrationMessageText.text = message;
        calibrationMessageText.color = WaitingColor;
        calibrationNotice = "Calibração UWB não salva: " + message;
        calibrationNoticeIsSuccess = false;
        RecordCalibrationEvent("error", message);
    }

    private static void RecordCalibrationEvent(string kind, string detail)
    {
        try
        {
            string directory = Path.Combine(Application.persistentDataPath, "UwbCalibrationEvents");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "events.csv");
            if (!File.Exists(path)) File.WriteAllText(path, "utc,event,detail\n");
            string safeDetail = (detail ?? "").Replace("\"", "\"\"");
            File.AppendAllText(path, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
                "," + kind + ",\"" + safeDetail + "\"\n");
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Debug.LogWarning("[UWB Calibration] Não foi possível registrar evento: " + ex.Message);
        }
    }

    private static bool TryReadMeters(string text, out float value)
    {
        return float.TryParse(text?.Trim().Replace(',', '.'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out value) && value > 0f;
    }

    private static RectTransform NewCard(Transform parent, string name, string heading,
        out Text state, out Text detail)
    {
        RectTransform card = NewRect(parent, name);
        card.gameObject.AddComponent<Image>().color = CardColor;

        RectTransform edge = NewRect(card, "Linha");
        edge.anchorMin = new Vector2(0f, 0f);
        edge.anchorMax = new Vector2(0f, 1f);
        edge.pivot = new Vector2(0f, 0.5f);
        edge.anchoredPosition = Vector2.zero;
        edge.sizeDelta = new Vector2(6f, 0f);
        edge.gameObject.AddComponent<Image>().color = PrimaryColor;

        Text headingText = NewText(card, heading, 25, FontStyle.Bold, PrimaryColor);
        TopInset(headingText.rectTransform, 19f, 35f, 26f);
        state = NewText(card, "Verificando...", 35, FontStyle.Bold, WaitingColor);
        TopInset(state.rectTransform, 57f, 48f, 26f);
        detail = NewText(card, "", 24, FontStyle.Normal, SubTextColor);
        TopInset(detail.rectTransform, 107f, 52f, 26f);
        return card;
    }

    private static Button NewButton(Transform parent, string name, string label, bool primary)
    {
        RectTransform rect = NewRect(parent, name);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = primary ? PrimaryColor : Html("#253F55");
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.disabledColor = primary ? Html("#355C6B") : Html("#253F55");
        colors.pressedColor = primary ? Html("#1AA9CC") : Html("#355C6B");
        button.colors = colors;
        Text text = NewText(rect, label, primary ? 36 : 20, FontStyle.Bold,
            primary ? BackgroundColor : MainTextColor);
        Stretch(text.rectTransform);
        text.alignment = TextAnchor.MiddleCenter;
        return button;
    }

    private static Text NewText(Transform parent, string value, int fontSize, FontStyle style, Color color)
    {
        RectTransform rect = NewRect(parent, "Texto");
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = menuFont != null ? menuFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.text = value;
        text.raycastTarget = false;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private static RectTransform NewRect(Transform parent, string name)
    {
        GameObject element = new GameObject(name, typeof(RectTransform));
        element.layer = 5;
        RectTransform rect = element.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void AnchorAtTop(RectTransform rect, float top, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -top);
        rect.sizeDelta = new Vector2(0f, height);
    }

    private static void TopInset(RectTransform rect, float top, float height, float left)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(left / 2f, -top);
        rect.sizeDelta = new Vector2(-left, height);
    }

    private static void PreferredHeight(RectTransform rect, float height)
    {
        LayoutElement element = rect.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = height;
    }

    private void UpdateSafeArea()
    {
        if (safeAreaRoot == null || Screen.width <= 0 || Screen.height <= 0) return;
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
        lastSafeArea = Screen.safeArea;
        Rect area = lastSafeArea;
        safeAreaRoot.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
        safeAreaRoot.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
        safeAreaRoot.offsetMin = Vector2.zero;
        safeAreaRoot.offsetMax = Vector2.zero;

        Canvas.ForceUpdateCanvases();
        float width = Mathf.Max(280f, Mathf.Min(980f, safeAreaRoot.rect.width - 24f));
        float height = safeAreaRoot.rect.height;
        float actionHeight = height < 900f ? 124f : 156f;

        scrollRectTransform.anchorMin = new Vector2(0.5f, 0f);
        scrollRectTransform.anchorMax = new Vector2(0.5f, 1f);
        scrollRectTransform.pivot = new Vector2(0.5f, 0.5f);
        scrollRectTransform.sizeDelta = new Vector2(width, -(42f + actionHeight));
        scrollRectTransform.anchoredPosition = new Vector2(0f, (actionHeight - 42f) * 0.5f);

        actionRoot.anchorMin = new Vector2(0.5f, 0f);
        actionRoot.anchorMax = new Vector2(0.5f, 0f);
        actionRoot.pivot = new Vector2(0.5f, 0f);
        actionRoot.sizeDelta = new Vector2(width - 36f, actionHeight);
        actionRoot.anchoredPosition = Vector2.zero;

        RectTransform buttonRect = (RectTransform)startButton.transform;
        buttonRect.anchorMin = new Vector2(0f, 0f);
        buttonRect.anchorMax = new Vector2(1f, 0f);
        buttonRect.pivot = new Vector2(0.5f, 0f);
        buttonRect.sizeDelta = new Vector2(0f, height < 900f ? 70f : 88f);
        buttonRect.anchoredPosition = new Vector2(0f, 48f);

        RectTransform footerRect = footerText.rectTransform;
        footerRect.anchorMin = Vector2.zero;
        footerRect.anchorMax = new Vector2(1f, 0f);
        footerRect.pivot = new Vector2(0.5f, 0f);
        footerRect.sizeDelta = new Vector2(0f, 38f);
        footerRect.anchoredPosition = new Vector2(0f, 4f);
    }

    // Alias legado de cena, preservado para referências serializadas.
    public void CarregarCenaDoViewer() => IniciarComHardware();

    public void IniciarComHardware()
    {
        if (isLoading || !canStart) return;
        GlobalData.IsSimulationMode = false;
        GlobalData.HasViewerModeSelection = true;
        GlobalData.IpAlvo = scannerIp;
        GlobalData.Porta = scannerPort;
        CarregarCenaViewer();
    }

    // Entrada de desenvolvimento preservada na API, sem duplicar o botão principal.
    public void IniciarModoSimulacao()
    {
        if (isLoading) return;
        GlobalData.IsSimulationMode = true;
        GlobalData.HasViewerModeSelection = true;
        CarregarCenaViewer();
    }

    private void CarregarCenaViewer()
    {
        isLoading = true;
        StopAllCoroutines();
        probeClient?.Close();
        probeClient = null;

        if (Application.CanStreamedLevelBeLoaded("CenaViewer"))
            SceneManager.LoadScene("CenaViewer");
        else if (Application.CanStreamedLevelBeLoaded("CenaViewerTCC"))
            SceneManager.LoadScene("CenaViewerTCC");
        else
            SceneManager.LoadScene(1);
    }

    private void OnDestroy() => probeClient?.Close();

    private static Color Html(string value)
    {
        ColorUtility.TryParseHtmlString(value, out Color color);
        return color;
    }

    private static int CountBits(int value)
    {
        int count = 0;
        for (int i = 0; i < 3; i++) if ((value & (1 << i)) != 0) count++;
        return count;
    }
}
