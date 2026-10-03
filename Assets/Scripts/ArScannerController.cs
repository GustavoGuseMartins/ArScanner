using ArScanner.Network;
using ArScanner.Rendering;
using ArScanner.Spatial;
using ArScanner.UI;
using UnityEngine;

namespace ArScanner
{
    [RequireComponent(typeof(PointCloudTcpReceiver))]
    [RequireComponent(typeof(UwbDataReceiver))]
    [RequireComponent(typeof(ThermalPointCloudRenderer))]
    [RequireComponent(typeof(UwbAnchorManager))]
    [RequireComponent(typeof(PointCloudSimulator))]
    [RequireComponent(typeof(ArScannerHUD))]
    [DefaultExecutionOrder(-200)]
    public class ArScannerController : MonoBehaviour
    {
        [Header("Configuração Rápida de Inicialização")]
        [Tooltip("Iniciar imediatamente no modo simulação se estiver no Editor")]
        public bool startInSimulatorModeOnEditor = true;

        private PointCloudTcpReceiver tcpReceiver;
        private UwbDataReceiver uwbReceiver;
        private ThermalPointCloudRenderer pointRenderer;
        private UwbAnchorManager anchorManager;
        private PointCloudSimulator simulator;
        private ArScannerHUD hud;
        private UwbTransportMode hardwareTransport;
        private bool hardwareAnchorMountedOnPhone;
        private bool hasStarted;

        private void Awake()
        {
            tcpReceiver = GetComponent<PointCloudTcpReceiver>();
            uwbReceiver = GetComponent<UwbDataReceiver>();
            pointRenderer = GetComponent<ThermalPointCloudRenderer>();
            anchorManager = GetComponent<UwbAnchorManager>();
            if (GetComponent<ScannerVisualPoseObserver>() == null)
                gameObject.AddComponent<ScannerVisualPoseObserver>();
            simulator = GetComponent<PointCloudSimulator>();
            hud = GetComponent<ArScannerHUD>();
            anchorManager.InitializeReferences();
            hardwareTransport = uwbReceiver.transportMode == UwbTransportMode.Simulated
                ? UwbTransportMode.USB : uwbReceiver.transportMode;
            hardwareAnchorMountedOnPhone = anchorManager.anchorMountedOnPhone;
            tcpReceiver.scannerIp = GlobalData.IpAlvo;
            tcpReceiver.scannerPort = GlobalData.Porta;

            // Configuração de referências mútuas
            pointRenderer.pointCloudRoot = anchorManager.pointCloudRootContainer;
            hud.tcpReceiver = tcpReceiver;
            hud.uwbReceiver = uwbReceiver;
            hud.pointRenderer = pointRenderer;
            hud.anchorManager = anchorManager;
            hud.simulator = simulator;

            bool simulate = GlobalData.IsSimulationMode;
#if UNITY_EDITOR
            simulate |= !GlobalData.HasViewerModeSelection && startInSimulatorModeOnEditor;
#endif
            SetSimulationMode(simulate);
        }

        private void Start()
        {
            hasStarted = true;
            Debug.Log("[ArScannerController] Sistema ArScanner inicializado com sucesso!");
        }

        public void SetSimulationMode(bool active)
        {
            simulator.isSimulationActive = active;
            tcpReceiver.autoConnect = !active;
            tcpReceiver.Disconnect();
            uwbReceiver.StopReceiver();
            uwbReceiver.transportMode = active ? UwbTransportMode.Simulated : hardwareTransport;
            anchorManager.anchorMountedOnPhone = active ? false : hardwareAnchorMountedOnPhone;
            pointRenderer.ClearPointCloud();
            simulator.ResetSimulation();
            if (hasStarted)
            {
                uwbReceiver.StartReceiver();
                if (!active) tcpReceiver.ConnectToScanner();
            }
        }
    }
}
