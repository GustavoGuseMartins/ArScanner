using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ArScanner.Network;
using UnityEngine;

namespace ArScanner.Rendering
{
    public enum VisualizationMode
    {
        ThermalOnly,        // Espectro térmico puro (Ironbow, Jet, Turbo)
        RgbPhotoReal,       // Cores reais da câmera óptica OV2640
        HybridThermalXRay   // Cor real RGB com destaque térmico translúcido através de paredes
    }

    public enum ThermalColormap
    {
        Ironbow,
        Jet,
        Turbo,
        Hot
    }

    [RequireComponent(typeof(PointCloudTcpReceiver))]
    public class ThermalPointCloudRenderer : MonoBehaviour
    {
        [Header("Modo de Visualização")]
        public VisualizationMode visualMode = VisualizationMode.HybridThermalXRay;
        public ThermalColormap colormap = ThermalColormap.Ironbow;

        [Header("Visão Através de Paredes (X-Ray)")]
        [Tooltip("Opacidade de superfícies frias/paredes (0.0 = invisível/ver através, 1.0 = sólido)")]
        [Range(0.0f, 1.0f)]
        public float wallOpacity = 0.25f;

        [Tooltip("Temperatura a partir da qual um ponto é considerado Hotspot e atravessa paredes")]
        public float hotspotThresholdTemp = 28.0f;

        [Header("LOD Planar (Otimização de Superfícies Amplas / Armários)")]
        [Tooltip("Ativa a redução poligonal/planar de pontos coplanares repetidos")]
        public bool enablePlanarLOD = true;
        [Tooltip("Fator de salto em superfícies planas para economizar pontos")]
        [Range(1, 4)]
        public int planarDecimationStep = 2; // Pula feixes redundantemente coplanares

        [Header("Capacidade e Performance")]
        [Range(1000, 150000)]
        public int maxPoints = 80000;
        public float voxelGridSize = 0.025f; // 2.5cm
        public int maxPointsPerFrame = 1200;

        [Header("Tamanho do Ponto")]
        [Range(0.005f, 0.1f)]
        public float pointSize = 0.025f;

        [Header("Escala Térmica")]
        public float minTemperature = 18.0f;
        public float maxTemperature = 42.0f;

        [Header("Estatísticas em Tempo Real")]
        public int activePointsCount = 0;
        public float observedMinTemp = 999f;
        public float observedMaxTemp = -999f;
        public bool isPaused = false;

        [Header("Referências")]
        public Transform pointCloudRoot;
        public ParticleSystem targetParticleSystem;

        private PointCloudTcpReceiver receiver;

        private struct StoredPoint
        {
            public Vector3 localPosition;
            public Color32 color;
            public float temperature;
            public byte surfaceFlags;
        }

        private readonly Dictionary<Vector3Int, int> voxelToIndex = new Dictionary<Vector3Int, int>();
        private StoredPoint[] pointsBuffer;
        private ParticleSystem.Particle[] particlesBuffer;
        private bool bufferDirty = false;
        private int planarCounter = 0;

        private void Awake()
        {
            receiver = GetComponent<PointCloudTcpReceiver>();
            if (pointCloudRoot == null) pointCloudRoot = this.transform;

            EnsureParticleSystemSetup();

            pointsBuffer = new StoredPoint[maxPoints];
            particlesBuffer = new ParticleSystem.Particle[maxPoints];
        }

        private void EnsureParticleSystemSetup()
        {
            if (targetParticleSystem == null)
            {
                targetParticleSystem = GetComponentInChildren<ParticleSystem>();
                if (targetParticleSystem == null)
                {
                    GameObject psObj = new GameObject("PointCloudParticleSystem");
                    psObj.transform.SetParent(pointCloudRoot != null ? pointCloudRoot : transform, false);
                    targetParticleSystem = psObj.AddComponent<ParticleSystem>();
                }
            }

            var main = targetParticleSystem.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = maxPoints;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startSize = pointSize;
            main.startLifetime = float.MaxValue;

            var emission = targetParticleSystem.emission;
            emission.enabled = false;

            var shape = targetParticleSystem.shape;
            shape.enabled = false;

            var psRenderer = targetParticleSystem.GetComponent<ParticleSystemRenderer>();
            psRenderer.renderMode = ParticleSystemRenderMode.Billboard;

            Shader defaultShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (defaultShader == null) defaultShader = Shader.Find("Particles/Standard Unlit");
            if (defaultShader != null)
            {
                psRenderer.sharedMaterial = new Material(defaultShader);
            }
        }

        private void Update()
        {
            if (receiver == null || isPaused) return;

            int processed = 0;
            while (processed < maxPointsPerFrame && receiver.incomingPoints.TryDequeue(out ScanPointData pointData))
            {
                ProcessScanPoint(pointData);
                processed++;
            }

            if (bufferDirty)
            {
                UpdateParticles();
                bufferDirty = false;
            }
        }

        private void ProcessScanPoint(ScanPointData data)
        {
            // Coordenadas calculadas no ESP32-S3 (mm -> metros)
            Vector3 localPos = new Vector3(data.posX_mm / 1000.0f, data.posY_mm / 1000.0f, data.posZ_mm / 1000.0f);

            float sqrDist = localPos.sqrMagnitude;
            if (sqrDist < 0.02f || sqrDist > 400.0f) return; // Fora do alcance seguro (15cm a 20m)

            // Registro de extremos térmicos
            if (data.temperatureC < observedMinTemp) observedMinTemp = data.temperatureC;
            if (data.temperatureC > observedMaxTemp) observedMaxTemp = data.temperatureC;

            // Otimização LOD Planar: se for superfície planar (parede/armário) e não for quente, decima pontos coplanares
            bool isHotspot = (data.temperatureC >= hotspotThresholdTemp) || (data.surfaceFlags == 2);
            if (enablePlanarLOD && (data.surfaceFlags == 1) && !isHotspot)
            {
                planarCounter++;
                if (planarCounter % planarDecimationStep != 0)
                {
                    return; // Descarta ponto redundante na mesma face plana
                }
            }

            // Determina a cor com base no modo visual e transparência de paredes
            Color32 color = CalculatePointColor(data, isHotspot);

            // Voxel Grid Hash Key
            Vector3Int voxelKey = new Vector3Int(
                Mathf.FloorToInt(localPos.x / voxelGridSize),
                Mathf.FloorToInt(localPos.y / voxelGridSize),
                Mathf.FloorToInt(localPos.z / voxelGridSize)
            );

            if (voxelToIndex.TryGetValue(voxelKey, out int existingIndex))
            {
                // Atualiza célula existente
                pointsBuffer[existingIndex].color = color;
                pointsBuffer[existingIndex].temperature = data.temperatureC;
                pointsBuffer[existingIndex].surfaceFlags = data.surfaceFlags;
                particlesBuffer[existingIndex].startColor = color;
                bufferDirty = true;
            }
            else if (activePointsCount < maxPoints)
            {
                // Novo ponto na nuvem
                int newIndex = activePointsCount;
                pointsBuffer[newIndex] = new StoredPoint
                {
                    localPosition = localPos,
                    color = color,
                    temperature = data.temperatureC,
                    surfaceFlags = data.surfaceFlags
                };

                particlesBuffer[newIndex].position = localPos;
                particlesBuffer[newIndex].startColor = color;
                particlesBuffer[newIndex].startSize = isHotspot ? pointSize * 1.3f : pointSize;
                particlesBuffer[newIndex].remainingLifetime = float.MaxValue;

                voxelToIndex[voxelKey] = newIndex;
                activePointsCount++;
                bufferDirty = true;
            }
        }

        private Color32 CalculatePointColor(ScanPointData data, bool isHotspot)
        {
            Color32 baseColor;

            switch (visualMode)
            {
                case VisualizationMode.RgbPhotoReal:
                    baseColor = new Color32(data.r, data.g, data.b, 255);
                    break;

                case VisualizationMode.ThermalOnly:
                    baseColor = EvaluateThermalColor(data.temperatureC);
                    break;

                case VisualizationMode.HybridThermalXRay:
                default:
                    if (isHotspot)
                    {
                        // Destaque térmico vivo (alaranjado/amarelo/branco)
                        baseColor = EvaluateThermalColor(data.temperatureC);
                    }
                    else
                    {
                        // Cor real RGB com saturação normal
                        baseColor = new Color32(data.r, data.g, data.b, 255);
                    }
                    break;
            }

            // Aplicação da Transparência de Paredes (X-Ray)
            if (!isHotspot && (data.surfaceFlags == 1 || data.temperatureC < hotspotThresholdTemp))
            {
                // Aplica a opacidade configurada na parede fria (permite ver através dela)
                byte alpha = (byte)(Mathf.Clamp01(wallOpacity) * 255f);
                baseColor.a = alpha;
            }
            else
            {
                baseColor.a = 255; // Hotspots sempre 100% visíveis
            }

            return baseColor;
        }

        private Color32 EvaluateThermalColor(float tempC)
        {
            float t = Mathf.InverseLerp(minTemperature, maxTemperature, tempC);
            switch (colormap)
            {
                case ThermalColormap.Ironbow: return EvaluateIronbow(t);
                case ThermalColormap.Jet: return EvaluateJet(t);
                case ThermalColormap.Turbo: return EvaluateTurbo(t);
                case ThermalColormap.Hot: return EvaluateHot(t);
                default: return EvaluateIronbow(t);
            }
        }

        private Color32 EvaluateIronbow(float t)
        {
            t = Mathf.Clamp01(t);
            float r, g, b;
            if (t < 0.2f) { float lt = t / 0.2f; r = Mathf.Lerp(0f, 0.2f, lt); g = 0f; b = Mathf.Lerp(0.2f, 0.6f, lt); }
            else if (t < 0.45f) { float lt = (t - 0.2f) / 0.25f; r = Mathf.Lerp(0.2f, 0.8f, lt); g = Mathf.Lerp(0f, 0.1f, lt); b = Mathf.Lerp(0.6f, 0.5f, lt); }
            else if (t < 0.75f) { float lt = (t - 0.45f) / 0.3f; r = Mathf.Lerp(0.8f, 1.0f, lt); g = Mathf.Lerp(0.1f, 0.65f, lt); b = Mathf.Lerp(0.5f, 0.05f, lt); }
            else { float lt = (t - 0.75f) / 0.25f; r = 1.0f; g = Mathf.Lerp(0.65f, 1.0f, lt); b = Mathf.Lerp(0.05f, 1.0f, lt); }
            return new Color32((byte)(r * 255f), (byte)(g * 255f), (byte)(b * 255f), 255);
        }

        private Color32 EvaluateJet(float t)
        {
            t = Mathf.Clamp01(t);
            float r = Mathf.Clamp01(1.5f - Mathf.Abs(t * 4.0f - 3.0f));
            float g = Mathf.Clamp01(1.5f - Mathf.Abs(t * 4.0f - 2.0f));
            float b = Mathf.Clamp01(1.5f - Mathf.Abs(t * 4.0f - 1.0f));
            return new Color32((byte)(r * 255f), (byte)(g * 255f), (byte)(b * 255f), 255);
        }

        private Color32 EvaluateTurbo(float t)
        {
            t = Mathf.Clamp01(t);
            float r = 0.1357f + t * (4.5974f + t * (-42.3278f + t * (130.5887f + t * (-150.5665f + t * 58.1375f))));
            float g = 0.0914f + t * (2.1856f + t * (4.8052f + t * (-14.0195f + t * (4.2109f + t * 2.7747f))));
            float b = 0.1067f + t * (12.5732f + t * (-86.0744f + t * (246.5765f + t * (-282.8483f + t * 110.2641f))));
            return new Color32((byte)(Mathf.Clamp01(r) * 255f), (byte)(Mathf.Clamp01(g) * 255f), (byte)(Mathf.Clamp01(b) * 255f), 255);
        }

        private Color32 EvaluateHot(float t)
        {
            t = Mathf.Clamp01(t);
            float r = Mathf.Clamp01(t * 2.5f);
            float g = Mathf.Clamp01((t - 0.35f) * 2.5f);
            float b = Mathf.Clamp01((t - 0.75f) * 4.0f);
            return new Color32((byte)(r * 255f), (byte)(g * 255f), (byte)(b * 255f), 255);
        }

        private void UpdateParticles()
        {
            if (targetParticleSystem != null && activePointsCount > 0)
            {
                targetParticleSystem.SetParticles(particlesBuffer, activePointsCount);
            }
        }

        public void SetWallOpacity(float opacity)
        {
            wallOpacity = opacity;
            RecalculateColors();
        }

        public void SetVisualizationMode(VisualizationMode mode)
        {
            visualMode = mode;
            RecalculateColors();
        }

        public void RecalculateColors()
        {
            for (int i = 0; i < activePointsCount; i++)
            {
                ScanPointData dummy = new ScanPointData
                {
                    temperatureC = pointsBuffer[i].temperature,
                    r = pointsBuffer[i].color.r,
                    g = pointsBuffer[i].color.g,
                    b = pointsBuffer[i].color.b,
                    surfaceFlags = pointsBuffer[i].surfaceFlags
                };

                bool isHotspot = (dummy.temperatureC >= hotspotThresholdTemp) || (dummy.surfaceFlags == 2);
                Color32 c = CalculatePointColor(dummy, isHotspot);
                pointsBuffer[i].color = c;
                particlesBuffer[i].startColor = c;
            }
            UpdateParticles();
        }

        public void ClearPointCloud()
        {
            activePointsCount = 0;
            voxelToIndex.Clear();
            observedMinTemp = 999f;
            observedMaxTemp = -999f;
            if (targetParticleSystem != null) targetParticleSystem.Clear();
        }

        public string ExportToPLY()
        {
            if (activePointsCount == 0) return null;

            string fileName = $"Scan3D_{DateTime.Now:yyyyMMdd_HHmmss}.ply";
            string folderPath = Path.Combine(Application.persistentDataPath, "Scans");
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
            string filePath = Path.Combine(folderPath, fileName);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("ply");
            sb.AppendLine("format ascii 1.0");
            sb.AppendLine("comment ArScanner TCC Multi-Modal Export (RGB + Thermal + Flags)");
            sb.AppendLine($"element vertex {activePointsCount}");
            sb.AppendLine("property float x");
            sb.AppendLine("property float y");
            sb.AppendLine("property float z");
            sb.AppendLine("property uchar red");
            sb.AppendLine("property uchar green");
            sb.AppendLine("property uchar blue");
            sb.AppendLine("property float temperature");
            sb.AppendLine("property uchar flags");
            sb.AppendLine("end_header");

            for (int i = 0; i < activePointsCount; i++)
            {
                Vector3 p = pointsBuffer[i].localPosition;
                Color32 c = pointsBuffer[i].color;
                float temp = pointsBuffer[i].temperature;
                byte f = pointsBuffer[i].surfaceFlags;
                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0:F4} {1:F4} {2:F4} {3} {4} {5} {6:F2} {7}",
                    p.x, p.y, p.z, c.r, c.g, c.b, temp, f));
            }

            File.WriteAllText(filePath, sb.ToString());
            return filePath;
        }
    }
}
