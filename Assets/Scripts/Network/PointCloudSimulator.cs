using UnityEngine;

namespace ArScanner.Network
{
    [DefaultExecutionOrder(-150)]
    public class PointCloudSimulator : MonoBehaviour
    {
        [Header("Controle da Simulação")]
        [Tooltip("Habilita a injeção contínua de pontos sintéticos no receptor TCP")]
        public bool isSimulationActive = false;

        [Header("Taxa de Geração")]
        [Range(100, 4000)]
        public int pointsPerSecond = 1400;

        [Header("Geometria da Sala Virtual e Alvo Térmico")]
        public Vector3 roomDimensions = new Vector3(4.0f, 2.6f, 5.0f);
        public Vector3 heatSourceCenter = new Vector3(0.6f, 1.2f, 2.2f); // Objeto quente atrás do ponto de vista
        public float heatSourceRadius = 0.35f;
        public float ambientTemperature = 21.5f;
        public float heatSourceTemperature = 38.2f;

        [Header("Dinâmica Mecânica")]
        public float baseRpm = 15.0f;        // NEMA 14 Pancake
        public float lidarRpm = 300.0f;     // Roborock LDS motor

        private PointCloudTcpReceiver tcpReceiver;
        private UwbDataReceiver uwbReceiver;
        private ArScanner.Spatial.UwbAnchorManager anchorManager;

        private float currentBaseAngle = 0f;
        private float currentLidarAngle = 0f;
        private float pointAccumulator = 0f;
        private readonly Vector3 roomCenter = new Vector3(0f, 1.35f, 1.7f);

        public void ResetSimulation()
        {
            currentBaseAngle = 0f;
            currentLidarAngle = 0f;
            pointAccumulator = 0f;
        }

        private void Awake()
        {
            tcpReceiver = GetComponent<PointCloudTcpReceiver>();
            uwbReceiver = GetComponent<UwbDataReceiver>();
            anchorManager = GetComponent<ArScanner.Spatial.UwbAnchorManager>();
        }

        private void Update()
        {
            if (!isSimulationActive || tcpReceiver == null) return;

            // Se o hardware real estiver conectado via TCP, desativa injeção sintética
            if (tcpReceiver.isConnected) return;

            float time = Time.time;

            // Simula hover suave do Drone via UWB
            Vector3 simulatedDronePos = new Vector3(
                Mathf.Sin(time * 0.5f) * 0.12f,
                1.35f + Mathf.Sin(time * 1.1f) * 0.04f,
                1.7f + Mathf.Cos(time * 0.4f) * 0.12f
            );
            if (uwbReceiver != null)
            {
                uwbReceiver.SetSimulatedPosition(simulatedDronePos);
            }

            // Simula oscilação de atitude em voo (Pitch e Roll do MPU6050)
            float simPitch = Mathf.Sin(time * 0.8f) * 2.5f;
            float simRoll = Mathf.Cos(time * 0.6f) * 1.8f;
            if (anchorManager != null)
            {
                anchorManager.UpdateDroneAttitude(simPitch, simRoll, 0f);
            }

            // Atualização dos ângulos dos motores
            float dt = Time.deltaTime;
            currentBaseAngle = Mathf.Repeat(currentBaseAngle + (baseRpm * 360f / 60f) * dt, 360f);
            currentLidarAngle = Mathf.Repeat(currentLidarAngle + (lidarRpm * 360f / 60f) * dt, 360f);

            pointAccumulator += pointsPerSecond * dt;
            int pointsToEmit = Mathf.FloorToInt(pointAccumulator);
            pointAccumulator -= pointsToEmit;

            for (int i = 0; i < pointsToEmit; i++)
            {
                float stepFraction = (float)i / pointsToEmit;
                float baseAngle = Mathf.Repeat(currentBaseAngle + (baseRpm * 360f / 60f) * dt * stepFraction, 360f);
                float lidarAngle = Mathf.Repeat(currentLidarAngle + (lidarRpm * 360f / 60f) * dt * stepFraction, 360f);

                float baseRad = baseAngle * Mathf.Deg2Rad;
                float lidarRad = lidarAngle * Mathf.Deg2Rad;

                Vector3 rayDir = new Vector3(
                    Mathf.Sin(lidarRad) * Mathf.Cos(baseRad),
                    Mathf.Cos(lidarRad),
                    Mathf.Sin(lidarRad) * Mathf.Sin(baseRad)
                ).normalized;

                float distanceMeters = RaycastBox(simulatedDronePos - roomCenter, rayDir, roomDimensions);
                // Alvo sintético dentro da sala: escolhe a primeira interseção visível.
                float sphereDistance = RaycastSphere(simulatedDronePos, rayDir, heatSourceCenter, heatSourceRadius);
                bool hitsHeatSource = sphereDistance > 0f && sphereDistance < distanceMeters;
                if (hitsHeatSource) distanceMeters = sphereDistance;
                distanceMeters += Random.Range(-0.004f, 0.004f); // Ruído realista de laser
                if (distanceMeters < 0.15f) distanceMeters = 0.15f;

                Vector3 hitPoint = rayDir * distanceMeters;

                // Checa se o feixe atinge a fonte de calor cilíndrica/humana
                float temp = ambientTemperature + Random.Range(-0.3f, 0.3f);
                byte flags = 1; // Flag padrão = Superfície Planar/Parede fria

                byte r = 210, g = 215, b = 220; // Cor da parede (RGB realista)

                // Se atingir o chão
                if ((simulatedDronePos + hitPoint).y <= roomCenter.y - roomDimensions.y * 0.5f + 0.05f)
                {
                    r = 135; g = 105; b = 75; // Piso amadeirado/marrom
                }

                if (hitsHeatSource)
                {
                    temp = heatSourceTemperature + Random.Range(-0.2f, 0.2f);
                    flags = 2; // Hotspot / Objeto de interesse
                    r = 255; g = 110; b = 60; // Cor visual do objeto quente
                }

                ScanPointData pkt = new ScanPointData
                {
                    posX_mm = hitPoint.x * 1000.0f,
                    posY_mm = hitPoint.y * 1000.0f,
                    posZ_mm = hitPoint.z * 1000.0f,
                    temperatureC = temp,
                    r = r,
                    g = g,
                    b = b,
                    surfaceFlags = flags,
                    pitchCentiDeg = (short)(simPitch * 100f),
                    rollCentiDeg = (short)(simRoll * 100f),
                    timestampMs = (uint)(Time.time * 1000f)
                };

                tcpReceiver.EnqueuePoint(pkt);
            }
        }

        private static float RaycastSphere(Vector3 origin, Vector3 direction, Vector3 center, float radius)
        {
            Vector3 offset = origin - center;
            float b = Vector3.Dot(offset, direction);
            float discriminant = b * b - (offset.sqrMagnitude - radius * radius);
            if (discriminant < 0f) return -1f;
            float distance = -b - Mathf.Sqrt(discriminant);
            return distance > 0f ? distance : -1f;
        }

        private float RaycastBox(Vector3 origin, Vector3 dir, Vector3 boxSize)
        {
            Vector3 half = boxSize * 0.5f;
            Vector3 min = -half;
            Vector3 max = half;

            float tMin = 0.001f;
            float tMax = 20.0f;

            for (int i = 0; i < 3; i++)
            {
                float invD = 1.0f / (Mathf.Abs(dir[i]) > 1e-6f ? dir[i] : 1e-6f);
                float t0 = (min[i] - origin[i]) * invD;
                float t1 = (max[i] - origin[i]) * invD;

                if (invD < 0.0f)
                {
                    float temp = t0;
                    t0 = t1;
                    t1 = temp;
                }

                tMin = t0 > tMin ? t0 : tMin;
                tMax = t1 < tMax ? t1 : tMax;

                if (tMax <= tMin) return 3.0f;
            }

            return tMax > 0f ? tMax : 3.0f;
        }
    }
}
