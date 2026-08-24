using ArScanner.Network;
using UnityEngine;

namespace ArScanner.Spatial
{
    [RequireComponent(typeof(UwbDataReceiver))]
    public class UwbAnchorManager : MonoBehaviour
    {
        [Header("Referências Espaciais")]
        public Transform pointCloudRootContainer;
        public float positionLerpSpeed = 5.0f;

        private UwbDataReceiver uwbReceiver;

        private void Awake()
        {
            uwbReceiver = GetComponent<UwbDataReceiver>();
        }

        private void Update()
        {
            if (uwbReceiver != null && uwbReceiver.IsConnected && pointCloudRootContainer != null)
            {
                // Aplica suavização (Lerp) na atualização da posição da nuvem de pontos baseada no UWB
                Vector3 targetPos = uwbReceiver.LatestPosition;
                pointCloudRootContainer.position = Vector3.Lerp(pointCloudRootContainer.position, targetPos, Time.deltaTime * positionLerpSpeed);
            }
        }
    }
}
