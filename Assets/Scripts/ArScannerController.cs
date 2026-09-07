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

        private void Awake()
        {
            tcpReceiver = GetComponent<PointCloudTcpReceiver>();
            uwbReceiver = GetComponent<UwbDataReceiver>();
            pointRenderer = GetComponent<ThermalPointCloudRenderer>();
            anchorManager = GetComponent<UwbAnchorManager>();
            simulator = GetComponent<PointCloudSimulator>();
            hud = GetComponent<ArScannerHUD>();

            // Configuração de referências mútuas
            pointRenderer.pointCloudRoot = anchorManager.pointCloudRootContainer;
            hud.tcpReceiver = tcpReceiver;
            hud.uwbReceiver = uwbReceiver;
            hud.pointRenderer = pointRenderer;
            hud.anchorManager = anchorManager;
            hud.simulator = simulator;

            if (simulator != null)
            {
                if (GlobalData.IsSimulationMode)
                {
                    simulator.isSimulationActive = true;
                }
#if UNITY_EDITOR
                else if (startInSimulatorModeOnEditor)
                {
                    simulator.isSimulationActive = true;
                }
#endif
            }
        }

        private void Start()
        {
            Debug.Log("[ArScannerController] Sistema ArScanner inicializado com sucesso!");
        }
    }
}
