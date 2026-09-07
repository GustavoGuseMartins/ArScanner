using ArScanner.Network;
using UnityEngine;
#if UNITY_XR_ARFOUNDATION
using UnityEngine.XR.ARFoundation;
#endif

namespace ArScanner.Spatial
{
    [RequireComponent(typeof(UwbDataReceiver))]
    public class UwbAnchorManager : MonoBehaviour
    {
        [Header("Configuração de Montagem UWB")]
        [Tooltip("Verdadeiro se a placa de 3 âncoras UWB estiver fisicamente conectada ao celular via USB-C")]
        public bool anchorMountedOnPhone = true;

        [Tooltip("Offset físico entre a antena central das âncoras UWB e a lente da câmera do celular")]
        public Vector3 uwbToCameraOffset = new Vector3(0f, -0.05f, 0.02f); // 5cm abaixo da câmera

        [Header("Referências Espaciais")]
        [Tooltip("Transform raiz sob o qual a nuvem de pontos é gerada (representa a posição do Scanner)")]
        public Transform pointCloudRootContainer;

        [Tooltip("Transform de referência do visor AR (Câmera do Celular / AR Camera rastreada pelo ARCore)")]
        public Transform arCameraTransform;

        [Header("Atitude do Drone (MPU6050 no Scanner)")]
        [Tooltip("Ângulos de inclinação do Scanner medidos pelo MPU6050 em voo")]
        public float dronePitchDeg = 0f;
        public float droneRollDeg = 0f;
        public float droneYawDeg = 0f;

        [Header("Filtro e Suavização de Movimento (UWB)")]
        [Tooltip("Velocidade de interpolação Lerp para amortecer o jitter de rádio do UWB")]
        [Range(1.0f, 30.0f)]
        public float positionLerpSpeed = 8.0f;

        [Tooltip("Distância mínima em metros para registrar movimento da tag (Deadband de ruído ToF)")]
        public float positionDeadband = 0.008f; // 8 milímetros

        [Header("Calibração de Orientação e Offset")]
        [Tooltip("Offset de rotação em graus (Yaw) da base física das âncoras")]
        public float yawOffsetDegrees = 0f;

        [Tooltip("Offset de translação física da base")]
        public Vector3 baseOriginOffset = Vector3.zero;

        [Header("Status de Rastreamento")]
        public Vector3 currentSmoothedTagPosition = Vector3.zero;
        public Vector3 droneWorldPosition = Vector3.zero;
        public bool isTracking = false;

        private UwbDataReceiver uwbReceiver;
        private Vector3 rawPreviousPosition = Vector3.zero;

        private void Awake()
        {
            uwbReceiver = GetComponent<UwbDataReceiver>();
            if (arCameraTransform == null && Camera.main != null)
            {
                arCameraTransform = Camera.main.transform;
            }
        }

        private void Start()
        {
            // Cria container raiz se não definido
            if (pointCloudRootContainer == null)
            {
                GameObject rootGo = new GameObject("UwbPointCloudRootContainer");
                pointCloudRootContainer = rootGo.transform;
            }

            TryCreateArAnchor();
        }

        private void TryCreateArAnchor()
        {
#if UNITY_XR_ARFOUNDATION
            var anchorManager = FindFirstObjectByType<ARAnchorManager>();
            if (anchorManager != null && pointCloudRootContainer != null)
            {
                if (pointCloudRootContainer.GetComponent<ARAnchor>() == null)
                {
                    pointCloudRootContainer.gameObject.AddComponent<ARAnchor>();
                    Debug.Log("[UWB Spatial] ARAnchor anexado ao pointCloudRootContainer!");
                }
            }
#endif
        }

        private void Update()
        {
            if (uwbReceiver == null || !uwbReceiver.IsConnected)
            {
                isTracking = false;
                return;
            }

            Vector3 rawPos = uwbReceiver.LatestPosition;

            // Filtro de deadband para ruído estático de rádio ToF
            if (Vector3.Distance(rawPos, rawPreviousPosition) > positionDeadband)
            {
                rawPreviousPosition = rawPos;
            }

            // Aplica rotação de calibração da base (Yaw)
            Quaternion baseRotation = Quaternion.Euler(0f, yawOffsetDegrees, 0f);
            Vector3 calibratedPos = baseRotation * rawPreviousPosition + baseOriginOffset;

            // Suavização via Lerp
            currentSmoothedTagPosition = Vector3.Lerp(
                currentSmoothedTagPosition,
                calibratedPos,
                Time.deltaTime * positionLerpSpeed
            );

            isTracking = true;

            // Fusão Espacial 3D:
            // 1. Se os 3 UWBs estão presos ao celular (USB-C), a posição relativa medida move junto com o celular!
            //    O ARCore (VIO da câmera + giroscópio/acelerômetro do celular) fornece a pose absoluta do celular no mundo real.
            //    A transformação arCameraTransform.TransformPoint posiciona a Tag do Scanner com precisão métrica milimétrica no mundo físico.
            if (anchorMountedOnPhone && arCameraTransform != null)
            {
                Vector3 localUwbPos = currentSmoothedTagPosition + uwbToCameraOffset;
                droneWorldPosition = arCameraTransform.TransformPoint(localUwbPos);
            }
            else
            {
                droneWorldPosition = currentSmoothedTagPosition;
            }

            // 2. Aplica a rotação de atitude do Scanner (medida pelo MPU6050 na PCB do drone)
            //    Isso compensa o Pitch e Roll do drone em voo, mantendo paredes e chão escaneados sempre nivelados!
            Quaternion droneAttitude = Quaternion.Euler(dronePitchDeg, droneYawDeg, droneRollDeg);

            if (pointCloudRootContainer != null)
            {
                pointCloudRootContainer.position = droneWorldPosition;
                if (anchorMountedOnPhone && arCameraTransform != null)
                {
                    pointCloudRootContainer.rotation = arCameraTransform.rotation * baseRotation * droneAttitude;
                }
                else
                {
                    pointCloudRootContainer.rotation = baseRotation * droneAttitude;
                }
            }
        }

        public void UpdateDroneAttitude(float pitch, float roll, float yaw)
        {
            dronePitchDeg = pitch;
            droneRollDeg = roll;
            droneYawDeg = yaw;
        }

        public void ResetOriginToCurrent()
        {
            if (uwbReceiver != null)
            {
                baseOriginOffset = -uwbReceiver.LatestPosition;
                Debug.Log($"[UWB Spatial] Origem resetada com offset: {baseOriginOffset}");
            }
        }

        public void SetYawOffset(float yaw)
        {
            yawOffsetDegrees = yaw;
        }
    }
}
