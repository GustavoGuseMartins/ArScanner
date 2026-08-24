using System.Collections.Generic;
using ArScanner.Network;
using UnityEngine;

namespace ArScanner.Rendering
{
    [RequireComponent(typeof(PointCloudTcpReceiver))]
    public class ThermalPointCloudRenderer : MonoBehaviour
    {
        [Header("Configurações de Renderização")]
        public GameObject pointPrefab;
        public Gradient thermalGradient;
        public float minTemperature = 15.0f; // Azul/Frio
        public float maxTemperature = 45.0f; // Vermelho/Quente
        public Transform pointCloudRoot;

        [Header("Limites de Performance")]
        public int maxPointsToProcessPerFrame = 50;

        private PointCloudTcpReceiver receiver;

        private void Awake()
        {
            receiver = GetComponent<PointCloudTcpReceiver>();
            if (pointCloudRoot == null) pointCloudRoot = this.transform;

            if (thermalGradient == null)
            {
                thermalGradient = new Gradient();
                GradientColorKey[] colorKeys = new GradientColorKey[3];
                colorKeys[0] = new GradientColorKey(Color.blue, 0.0f);
                colorKeys[1] = new GradientColorKey(Color.green, 0.5f);
                colorKeys[2] = new GradientColorKey(Color.red, 1.0f);

                GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
                alphaKeys[0] = new GradientAlphaKey(1.0f, 0.0f);
                alphaKeys[1] = new GradientAlphaKey(1.0f, 1.0f);

                thermalGradient.SetKeys(colorKeys, alphaKeys);
            }
        }

        private void Update()
        {
            if (receiver == null) return;

            int processed = 0;
            while (processed < maxPointsToProcessPerFrame && receiver.incomingPoints.TryDequeue(out ScanPointData pointData))
            {
                InstantiatePoint(pointData);
                processed++;
            }
        }

        private void InstantiatePoint(ScanPointData data)
        {
            // Converter de Coordenadas Esféricas (BaseAngle, LidarAngle, Distância) para Cartesianas (X, Y, Z)
            float distMeters = data.distanceMm / 1000.0f;
            float baseRad = data.baseAngleDeg * Mathf.Deg2Rad;
            float lidarRad = data.lidarAngleDeg * Mathf.Deg2Rad;

            float x = distMeters * Mathf.Sin(lidarRad) * Mathf.Cos(baseRad);
            float y = distMeters * Mathf.Cos(lidarRad);
            float z = distMeters * Mathf.Sin(lidarRad) * Mathf.Sin(baseRad);

            Vector3 localPos = new Vector3(x, y, z);
            Vector3 worldPos = pointCloudRoot.TransformPoint(localPos);

            // Mapeamento da temperatura para a cor do gradiente térmico
            float t = Mathf.InverseLerp(minTemperature, maxTemperature, data.temperatureC);
            Color pointColor = thermalGradient.Evaluate(t);

            if (pointPrefab != null)
            {
                GameObject go = Instantiate(pointPrefab, worldPos, Quaternion.identity, pointCloudRoot);
                Renderer ren = go.GetComponent<Renderer>();
                if (ren != null)
                {
                    ren.material.color = pointColor;
                }
            }
        }
    }
}
