using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ArScanner.Network;
using UnityEngine;

namespace ArScanner.Rendering
{
    [RequireComponent(typeof(PointCloudTcpReceiver))]
    public class ThermalPointCloudRenderer : MonoBehaviour
    {
        public const float DefaultMountYawOffset = 200f;
        // Gray marks LiDAR geometry outside the thermal image or without a fresh frame.
        private static readonly Color32 GeometryColor = new Color32(235, 235, 235, 255);

        [Header("Capacidade e Performance")]
        [Range(1000, 150000)]
        public int maxPoints = 80000;
        public float voxelGridSize = 0.025f; // 2.5cm
        public int maxPointsPerFrame = 1200;

        [Header("Tamanho do Ponto")]
        [Range(0.005f, 0.1f)]
        public float pointSize = 0.00625f;

        [Tooltip("Amplia apenas a representação de pontos com vizinhança planar medida. Não altera o voxel ou as posições exportadas.")]
        public bool adaptivePointSizing = true;
        private float lastPointSize = 0.00625f;
        private bool lastAdaptivePointSizing = true;

        [Range(0f, 1f)]
        public float pointOpacity = 1.0f;
        private float lastOpacity = 1.0f;

        [Header("Qualidade LiDAR")]
        [Tooltip("Descarta apenas retornos marcados pelo LiDAR como sinal fraco. Esse aviso não comprova erro de distância.")]
        public bool rejectWeakLidarReturns = false;
        public long weakLidarDiscardedPoints;

        [Header("Cor Térmica")]
        public bool showThermalColors = true;
        public bool useAbsoluteThermalScale = true;
        public float thermalDisplayMinC = 18.0f;
        public float thermalDisplayMaxC = 40.0f;
        private bool lastThermalColors = true;
        private bool lastAbsoluteThermalScale = true;
        private float lastThermalMinC = 18.0f, lastThermalMaxC = 40.0f;

        [Header("LOD de Superfícies")]
        public bool enableSurfaceLod = true;
        public float lodNearMeters = .8f;
        public float lodFarMeters = 2.5f;
        public float lodTileSize = .16f;
        public float lodPlaneTolerance = .018f;
        public float lodMaxThermalSpreadC = 2.5f;
        public int surfaceLodPolygons;
        public int surfaceLodMergedPoints;
        private bool lastSurfaceLodEnabled = true;
        private bool lodDirty = true;
        private float nextLodTime;
        private Vector3 lastLodCameraPosition;
        private GameObject lodObject;
        private MeshFilter lodFilter;
        private Mesh lodMesh;

        [Header("Orientação da Nuvem")]
        [Tooltip("Ajuste de inclinação angular (Pitch) em graus (+90° gira a varredura para cima)")]
        public float pointPitchOffset = 0.0f;
        [Tooltip("Extrínseco fixo de montagem LiDAR → base. A direção da base no AR é alinhada separadamente.")]
        public float pointYawOffset = DefaultMountYawOffset;
        public float pointRollOffset = 0.0f;
        [Tooltip("Espelha a varredura vertical ao redor do centro óptico do LiDAR para conferir chão/teto sem alterar o firmware.")]
        public bool invertVerticalLidar = true;
        [Tooltip("Altura do centro óptico acima do eixo de pan, medida na montagem.")]
        public float lidarOpticalHeight = 0.05f;
        public bool showScannerAxes = true;
        private Transform axesRoot;
        private readonly LineRenderer[] scannerAxes = new LineRenderer[3];

        [Header("Estatísticas em Tempo Real")]
        public int activePointsCount = 0;
        public float observedMinTemp = 999f;
        public float observedMaxTemp = -999f;
        public int activeThermalPointsCount;
        public bool isPaused = false;

        [Header("Referências")]
        public Transform pointCloudRoot;
        public ParticleSystem targetParticleSystem;

        private PointCloudTcpReceiver receiver;
        private ArScanner.Spatial.UwbAnchorManager spatial;

        private struct StoredPoint
        {
            public Vector3 worldPosition;
            public Vector3 viewDirection;
            public Color32 rgb;
            public float temperature;
            public byte surfaceFlags;
            public Vector3Int voxelKey;
            public byte observations;
        }

        private readonly Dictionary<Vector3Int, int> voxelToIndex = new Dictionary<Vector3Int, int>();
        private StoredPoint[] pointsBuffer;
        private ParticleSystem.Particle[] particlesBuffer;
        private bool[] lodCovered;
        private float[] supportedPointSpacing;
        private bool bufferDirty = false;
        private int nextReplacementIndex = 0;
        private Material pointMaterial;
        private Material surfaceMaterial;
        private Vector3 lastVisibilityCameraPosition;
        private bool hasVisibilityCameraPosition;
        private float nextVisibilityTime;
        private float metersPerPixelAtUnitDistance;

        private void Awake()
        {
            receiver = GetComponent<PointCloudTcpReceiver>();
            spatial = GetComponent<ArScanner.Spatial.UwbAnchorManager>();
            if (pointCloudRoot == null) pointCloudRoot = this.transform;
            maxPoints = Mathf.Clamp(maxPoints, 1000, 150000);
            voxelGridSize = Mathf.Max(0.001f, voxelGridSize);

            EnsureParticleSystemSetup();
            lodObject = new GameObject("SurfaceLodMesh");
            lodFilter = lodObject.AddComponent<MeshFilter>();
            lodObject.AddComponent<MeshRenderer>().sharedMaterial = surfaceMaterial ?? pointMaterial;

            if (pointMaterial != null)
            {
                axesRoot = new GameObject("ScannerAxes").transform;
                axesRoot.SetParent(pointCloudRoot, false);
                Vector3[] directions = { Vector3.right, Vector3.up, Vector3.forward };
                Color[] colors = { Color.red, Color.green, Color.blue };
                for (int i = 0; i < 3; i++)
                {
                    var axis = new GameObject("XYZ"[i].ToString()).AddComponent<LineRenderer>();
                    axis.transform.SetParent(axesRoot, false);
                    axis.useWorldSpace = false;
                    axis.positionCount = 2;
                    axis.SetPosition(0, Vector3.zero);
                    axis.SetPosition(1, directions[i] * .3f);
                    axis.startWidth = .006f;
                    axis.endWidth = .002f;
                    axis.startColor = axis.endColor = colors[i];
                    axis.sharedMaterial = surfaceMaterial ?? pointMaterial;
                    scannerAxes[i] = axis;
                }
            }
            pointsBuffer = new StoredPoint[maxPoints];
            particlesBuffer = new ParticleSystem.Particle[maxPoints];
            lodCovered = new bool[maxPoints];
            supportedPointSpacing = new float[maxPoints];
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
            // Pontos já observados permanecem no mundo quando o scanner se move.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSize = pointSize;
            main.startLifetime = float.MaxValue;

            var emission = targetParticleSystem.emission;
            emission.enabled = false;

            var shape = targetParticleSystem.shape;
            shape.enabled = false;

            var psRenderer = targetParticleSystem.GetComponent<ParticleSystemRenderer>();
            psRenderer.renderMode = ParticleSystemRenderMode.Billboard;

            Shader defaultShader = Resources.Load<Shader>("ArScannerPointCloud");
            if (defaultShader == null) defaultShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (defaultShader == null) defaultShader = Shader.Find("Particles/Standard Unlit");
            if (defaultShader != null)
            {
                pointMaterial = new Material(defaultShader);
                pointMaterial.SetFloat("_ZWrite", 1f);
                pointMaterial.SetFloat("_RoundPoints", 1f);
                pointMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 2;
                psRenderer.sharedMaterial = pointMaterial;
                surfaceMaterial = new Material(defaultShader);
                surfaceMaterial.SetFloat("_Cull", 2f); // Back
                surfaceMaterial.SetFloat("_ZWrite", 1f);
                surfaceMaterial.SetFloat("_RoundPoints", 0f);
                surfaceMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 1;
            }
        }

        private void Update()
        {
            UpdateScannerAxes();
            int processed = 0;
            while (receiver != null && !isPaused && processed < maxPointsPerFrame && receiver.incomingPoints.TryDequeue(out ScanPointData pointData))
            {
                ProcessScanPoint(pointData);
                processed++;
            }

            pointOpacity = Mathf.Clamp01(pointOpacity);
            thermalDisplayMaxC = Mathf.Max(thermalDisplayMinC + 1f, thermalDisplayMaxC);
            if (!Mathf.Approximately(lastOpacity, pointOpacity) ||
                lastThermalColors != showThermalColors ||
                lastAbsoluteThermalScale != useAbsoluteThermalScale ||
                !Mathf.Approximately(lastThermalMinC, thermalDisplayMinC) ||
                !Mathf.Approximately(lastThermalMaxC, thermalDisplayMaxC))
            {
                lastOpacity = pointOpacity;
                lastThermalColors = showThermalColors;
                lastAbsoluteThermalScale = useAbsoluteThermalScale;
                lastThermalMinC = thermalDisplayMinC;
                lastThermalMaxC = thermalDisplayMaxC;
                for (int i = 0; i < activePointsCount; i++)
                {
                    Color32 c = PointColor(pointsBuffer[i].temperature,
                        pointsBuffer[i].surfaceFlags);
                    c.a = (byte)(255 * pointOpacity);
                    particlesBuffer[i].startColor = c;
                }
                bufferDirty = true;
                lodDirty = true;
            }

            if (lastSurfaceLodEnabled != enableSurfaceLod)
            {
                lastSurfaceLodEnabled = enableSurfaceLod;
                lodDirty = true;
                nextLodTime = 0f;
            }
            Vector3 cameraPosition = spatial != null && spatial.arCameraTransform != null
                ? spatial.arCameraTransform.position : Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            if (!Mathf.Approximately(lastPointSize,pointSize) || lastAdaptivePointSizing!=adaptivePointSizing)
            {
                if (lastAdaptivePointSizing!=adaptivePointSizing)
                {
                    lodDirty=true;
                    nextLodTime=0f;
                }
                lastPointSize=pointSize;
                lastAdaptivePointSizing=adaptivePointSizing;
                RefreshPointVisibility(cameraPosition);
            }
            if (Vector3.Distance(cameraPosition,lastLodCameraPosition) > .5f) lodDirty = true;
            if (lodDirty && Time.unscaledTime >= nextLodTime) RebuildSurfaceLod(cameraPosition);
            if (Time.unscaledTime >= nextVisibilityTime &&
                (!hasVisibilityCameraPosition ||
                 Vector3.Distance(cameraPosition,lastVisibilityCameraPosition) > .05f))
            {
                RefreshPointVisibility(cameraPosition);
                lastVisibilityCameraPosition = cameraPosition;
                hasVisibilityCameraPosition = true;
                nextVisibilityTime = Time.unscaledTime + .2f;
            }
            if (bufferDirty)
            {
                UpdateParticles();
                bufferDirty = false;
            }
        }

        private void UpdateScannerAxes()
        {
            if (axesRoot == null) return;
            // Position is meaningful before heading; horizontal arrows are not.
            bool placed = spatial != null && (spatial.HasPoseEstimate ||
                (spatial.localPreviewWithoutUwb && spatial.PreviewPlaced));
            bool directionKnown = spatial != null && spatial.PreviewHeadingAligned &&
                (receiver == null || receiver.HasUsablePanReference);
            if (receiver != null && receiver.status.imuOrientationEnabled && !receiver.HasUsableImuOrientation)
                directionKnown=false;
            axesRoot.gameObject.SetActive(showScannerAxes && placed);
            for (int axis = 0; axis < scannerAxes.Length; axis++)
                if (scannerAxes[axis] != null)
                    scannerAxes[axis].enabled = axis == 1 || directionKnown;
            // Firmware already includes pan in points. Only the head indicator
            // rotates here; the unknown-heading indicator stays vertical in AR.
            float pan = receiver != null && receiver.HasFreshStatus
                ? receiver.status.panDegrees : 0f;
            if (receiver != null && receiver.HasUsableImuOrientation)
                pan=receiver.status.imuRelativeHeadYawDeg;
            if (directionKnown)
                axesRoot.localRotation = Quaternion.Euler(pointPitchOffset, pointYawOffset, pointRollOffset) *
                    Quaternion.Euler(0f, pan, 0f);
            else axesRoot.rotation = Quaternion.identity;
        }

        private void ProcessScanPoint(ScanPointData data)
        {
            if (spatial != null && !spatial.CanAcceptPoints) return;
            if (rejectWeakLidarReturns && data.HasWeakLidarSignal)
            {
                weakLidarDiscardedPoints++;
                return;
            }
            // Coordenadas calculadas no ESP32-S3 (mm -> metros)
            Vector3 localPos = new Vector3(data.posX_mm / 1000.0f, data.posY_mm / 1000.0f, data.posZ_mm / 1000.0f);
            // A hipótese de sentido do ângulo vertical ainda não foi confirmada
            // com um alvo conhecido. Espelhar em torno do centro óptico preserva
            // seus 50 mm acima do pan e permite a comparação no próprio visor.
            if (invertVerticalLidar)
                localPos.y = 2f * lidarOpticalHeight - localPos.y;
            if (pointPitchOffset != 0.0f || pointYawOffset != 0.0f || pointRollOffset != 0.0f)
            {
                localPos = Quaternion.Euler(pointPitchOffset, pointYawOffset, pointRollOffset) * localPos;
            }

            float sqrDist = localPos.sqrMagnitude;
            if (float.IsNaN(sqrDist) || float.IsInfinity(sqrDist) ||
                ((data.surfaceFlags & ScanPointData.ThermalUnavailableFlag) == 0 &&
                    (float.IsNaN(data.temperatureC) || float.IsInfinity(data.temperatureC))) ||
                sqrDist < 0.0225f || sqrDist > 400.0f) return;
            Vector3 worldPos = pointCloudRoot != null ? pointCloudRoot.TransformPoint(localPos) : localPos;
            Vector3 scannerPosition = pointCloudRoot != null ? pointCloudRoot.position : transform.position;
            Vector3 viewDirection = (scannerPosition - worldPos).normalized;
            Vector3 cameraPosition = spatial != null && spatial.arCameraTransform != null
                ? spatial.arCameraTransform.position : Camera.main != null ? Camera.main.transform.position : scannerPosition;

            // Registro de extremos térmicos
            if (HasThermalMeasurement(data))
            {
                if (data.temperatureC < observedMinTemp) observedMinTemp = data.temperatureC;
                if (data.temperatureC > observedMaxTemp) observedMaxTemp = data.temperatureC;
            }

            // Voxel Grid Hash Key
            Vector3Int voxelKey = new Vector3Int(
                Mathf.FloorToInt(worldPos.x / Mathf.Max(0.001f, voxelGridSize)),
                Mathf.FloorToInt(worldPos.y / Mathf.Max(0.001f, voxelGridSize)),
                Mathf.FloorToInt(worldPos.z / Mathf.Max(0.001f, voxelGridSize))
            );

            if (voxelToIndex.TryGetValue(voxelKey, out int existingIndex))
            {
                // Independent LiDAR returns have no persistent point identity.
                // Average only samples of the same world voxel; a position
                // Kalman filter across unrelated rays would join surfaces.
                StoredPoint existing = pointsBuffer[existingIndex];
                int observations = Mathf.Min(32, existing.observations + 1);
                existing.worldPosition = Vector3.Lerp(existing.worldPosition, worldPos,
                    1f / observations);
                existing.observations = (byte)observations;
                pointsBuffer[existingIndex] = existing;
                bool wasThermal = (pointsBuffer[existingIndex].surfaceFlags & ScanPointData.ThermalUnavailableFlag) == 0 &&
                    !float.IsNaN(pointsBuffer[existingIndex].temperature);
                // Um novo feixe sem térmica não apaga uma medição real do mesmo voxel.
                if (!HasThermalMeasurement(data) &&
                    (pointsBuffer[existingIndex].surfaceFlags & ScanPointData.ThermalUnavailableFlag) == 0)
                {
                    data.temperatureC = pointsBuffer[existingIndex].temperature;
                    data.surfaceFlags = (byte)((data.surfaceFlags & ~(ScanPointData.ThermalUnavailableFlag | 2)) |
                        (pointsBuffer[existingIndex].surfaceFlags & 2));
                }
                // Atualiza célula existente
                pointsBuffer[existingIndex].rgb = new Color32(data.r, data.g, data.b, 255);
                pointsBuffer[existingIndex].temperature = HasThermalMeasurement(data) ? data.temperatureC : float.NaN;
                pointsBuffer[existingIndex].surfaceFlags = data.surfaceFlags;
                if (wasThermal != HasThermalMeasurement(data))
                    activeThermalPointsCount += HasThermalMeasurement(data) ? 1 : -1;
                Color32 color = PointColor(pointsBuffer[existingIndex].temperature, data.surfaceFlags);
                color.a = (byte)(255 * pointOpacity);
                particlesBuffer[existingIndex].startColor = color;
                particlesBuffer[existingIndex].startSize =
                    VisibleFromCamera(existing.viewDirection, existing.worldPosition, cameraPosition) &&
                    !lodCovered[existingIndex] ? DisplayPointSize(existingIndex,cameraPosition) : 0f;
                particlesBuffer[existingIndex].position = existing.worldPosition;
                bufferDirty = true;
                lodDirty = true;
            }
            else
            {
                int targetIndex;
                if (activePointsCount < pointsBuffer.Length)
                {
                    targetIndex = activePointsCount++;
                }
                else
                {
                    // Buffer circular quando atinge maxPoints:
                    targetIndex = nextReplacementIndex;
                    nextReplacementIndex = (nextReplacementIndex + 1) % pointsBuffer.Length;

                    // Remove chave antiga do dicionário para evitar crescimento de memória (Memory Leak Zero)
                    voxelToIndex.Remove(pointsBuffer[targetIndex].voxelKey);
                    if ((pointsBuffer[targetIndex].surfaceFlags & ScanPointData.ThermalUnavailableFlag) == 0 &&
                        !float.IsNaN(pointsBuffer[targetIndex].temperature)) activeThermalPointsCount--;
                }

                if (HasThermalMeasurement(data)) activeThermalPointsCount++;

                pointsBuffer[targetIndex] = new StoredPoint
                {
                    worldPosition = worldPos,
                    viewDirection = viewDirection,
                    rgb = new Color32(data.r, data.g, data.b, 255),
                    temperature = HasThermalMeasurement(data) ? data.temperatureC : float.NaN,
                    surfaceFlags = data.surfaceFlags,
                    voxelKey = voxelKey,
                    observations = 1
                };

                Color32 color = PointColor(pointsBuffer[targetIndex].temperature, data.surfaceFlags);
                color.a = (byte)(255 * pointOpacity);
                particlesBuffer[targetIndex].position = worldPos;
                particlesBuffer[targetIndex].startColor = color;
                lodCovered[targetIndex] = false;
                supportedPointSpacing[targetIndex] = 0f;
                particlesBuffer[targetIndex].startSize =
                    VisibleFromCamera(viewDirection, worldPos, cameraPosition) ? DisplayPointSize(targetIndex,cameraPosition) : 0f;
                particlesBuffer[targetIndex].remainingLifetime = float.MaxValue;

                voxelToIndex[voxelKey] = targetIndex;
                bufferDirty = true;
                lodDirty = true;
            }
        }

        private static bool HasThermalMeasurement(ScanPointData data)
        {
            return (data.surfaceFlags & ScanPointData.ThermalUnavailableFlag) == 0 &&
                !float.IsNaN(data.temperatureC) && !float.IsInfinity(data.temperatureC);
        }

        private void UpdateParticles()
        {
            if (targetParticleSystem != null && activePointsCount > 0)
            {
                targetParticleSystem.SetParticles(particlesBuffer, activePointsCount);
            }
        }

        private static bool VisibleFromCamera(Vector3 observedFront, Vector3 position,
            Vector3 cameraPosition)
        {
            return observedFront.sqrMagnitude < .01f ||
                Vector3.Dot(observedFront, cameraPosition - position) > .01f;
        }

        private void RefreshPointVisibility(Vector3 cameraPosition)
        {
            UpdateDisplayScale();
            for (int i = 0; i < activePointsCount; i++)
                particlesBuffer[i].startSize = !lodCovered[i] &&
                    VisibleFromCamera(pointsBuffer[i].viewDirection,
                        pointsBuffer[i].worldPosition, cameraPosition) ? DisplayPointSize(i,cameraPosition) : 0f;
            bufferDirty = true;
        }

        private float DisplayPointSize(int index,Vector3 cameraPosition)
        {
            if (!adaptivePointSizing) return pointSize;
            float metersPerPixel = Vector3.Distance(pointsBuffer[index].worldPosition,cameraPosition)*metersPerPixelAtUnitDistance;
            bool thermal = (pointsBuffer[index].surfaceFlags & ScanPointData.ThermalUnavailableFlag)==0 &&
                !float.IsNaN(pointsBuffer[index].temperature) && !float.IsInfinity(pointsBuffer[index].temperature);
            return AdaptivePointDiameter(pointSize,supportedPointSpacing[index],voxelGridSize,
                metersPerPixel,thermal);
        }

        private void UpdateDisplayScale()
        {
            Camera camera=spatial!=null && spatial.arCameraTransform!=null
                ? spatial.arCameraTransform.GetComponent<Camera>() : Camera.main;
            metersPerPixelAtUnitDistance=camera!=null && camera.pixelHeight>0 && !camera.orthographic
                ? 2f*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f)/camera.pixelHeight : 0f;
        }

        // A point with no measured planar neighbors stays small. Supported
        // splats cannot reach the nearest other observation or exceed 3.5 cm.
        // An isolated thermal sample is never expanded by the visibility floor.
        public static float AdaptivePointDiameter(float requestedSize,float supportedSpacing,
            float voxelSize,float metersPerPixel,bool hasThermal)
        {
            requestedSize=Mathf.Max(.001f,requestedSize);
            if (supportedSpacing>0f && !float.IsNaN(supportedSpacing) && !float.IsInfinity(supportedSpacing))
            {
                float cap=Mathf.Min(.035f,Mathf.Min(voxelSize*1.4f,supportedSpacing*.9f));
                return Mathf.Min(cap,Mathf.Max(requestedSize,Mathf.Max(supportedSpacing*.8f,metersPerPixel*1.5f)));
            }
            if (hasThermal) return requestedSize;
            float isolatedCap=Mathf.Max(requestedSize,Mathf.Min(voxelSize*.75f,requestedSize*2f));
            return Mathf.Min(isolatedCap,Mathf.Max(requestedSize,metersPerPixel*1.5f));
        }

        private void RebuildSurfaceLod(Vector3 cameraPosition)
        {
            UpdateDisplayScale();
            lodDirty = false;
            nextLodTime = Time.unscaledTime + 2f;
            lastLodCameraPosition = cameraPosition;
            if (lodMesh != null) { Destroy(lodMesh); lodMesh = null; }
            surfaceLodPolygons = 0;
            surfaceLodMergedPoints = 0;
            if (lodObject == null) return;
            lodObject.SetActive(enableSurfaceLod);
            bool[] covered = null;
            if ((enableSurfaceLod || adaptivePointSizing) && activePointsCount >= 6)
            {
                var samples = new SurfaceLodBuilder.Sample[activePointsCount];
                for (int i = 0; i < activePointsCount; i++)
                    samples[i] = new SurfaceLodBuilder.Sample {
                        position=pointsBuffer[i].worldPosition,
                        viewDirection=pointsBuffer[i].viewDirection,
                        color=WithOpacity(PointColor(pointsBuffer[i].temperature,pointsBuffer[i].surfaceFlags)),
                        temperature=pointsBuffer[i].temperature,
                        hasThermal=(pointsBuffer[i].surfaceFlags & ScanPointData.ThermalUnavailableFlag)==0 &&
                            !float.IsNaN(pointsBuffer[i].temperature) && !float.IsInfinity(pointsBuffer[i].temperature)
                    };
                lodMesh = SurfaceLodBuilder.Build(samples,cameraPosition,enableSurfaceLod ? lodNearMeters : float.PositiveInfinity,lodFarMeters,
                    lodTileSize,lodPlaneTolerance,lodMaxThermalSpreadC,
                    out covered,out surfaceLodPolygons,out float[] spacing);
                Array.Copy(spacing,supportedPointSpacing,activePointsCount);
                lodFilter.sharedMesh = lodMesh;
            }
            else
            {
                lodFilter.sharedMesh = null;
                Array.Clear(supportedPointSpacing,0,activePointsCount);
            }
            for (int i = 0; i < activePointsCount; i++)
            {
                bool merged = covered != null && covered[i];
                lodCovered[i] = merged;
                particlesBuffer[i].startSize = !merged &&
                    VisibleFromCamera(pointsBuffer[i].viewDirection,
                        pointsBuffer[i].worldPosition, cameraPosition) ? DisplayPointSize(i,cameraPosition) : 0f;
                if (merged) surfaceLodMergedPoints++;
            }
            bufferDirty = true;
        }

        private Color32 WithOpacity(Color32 color)
        {
            color.a = (byte)(255*pointOpacity);
            return color;
        }

        public static Color32 ThermalPalette(float temperatureC, float minC, float maxC)
        {
            float t = Mathf.Clamp01((temperatureC-minC)/Mathf.Max(1f, maxC-minC));
            Color c;
            if (t < .25f) c = Color.Lerp(new Color(0f, .02f, .28f), Color.cyan, t*4f);
            else if (t < .5f) c = Color.Lerp(Color.cyan, Color.yellow, (t-.25f)*4f);
            else if (t < .75f) c = Color.Lerp(Color.yellow, Color.red, (t-.5f)*4f);
            else c = Color.Lerp(Color.red, Color.white, (t-.75f)*4f);
            return (Color32)c;
        }

        // Absolute labels make colors comparable between scans. A warm body
        // should remain red even when the rest of the room is relatively cool.
        public static Color32 AbsoluteThermalPalette(float temperatureC)
        {
            Color deepBlue = new Color(0f, .02f, .28f);
            if (temperatureC <= 20f) return (Color32)deepBlue;
            if (temperatureC < 27f) return (Color32)Color.Lerp(deepBlue, Color.cyan, (temperatureC-20f)/7f);
            if (temperatureC < 31f) return (Color32)Color.Lerp(Color.cyan, Color.yellow, (temperatureC-27f)/4f);
            if (temperatureC < 35f) return (Color32)Color.Lerp(Color.yellow, Color.red, (temperatureC-31f)/4f);
            if (temperatureC < 45f) return (Color32)Color.Lerp(Color.red, Color.white, (temperatureC-35f)/10f);
            return (Color32)Color.white;
        }

        private Color32 PointColor(float temperatureC, byte flags)
        {
            // Firmware calibration currently registers the thermal pixels against
            // the inverted vertical LiDAR geometry used by the default viewer.
            return showThermalColors && invertVerticalLidar &&
                (flags & ScanPointData.ThermalUnavailableFlag) == 0 &&
                !float.IsNaN(temperatureC) && !float.IsInfinity(temperatureC)
                ? (useAbsoluteThermalScale
                    ? AbsoluteThermalPalette(temperatureC)
                    : ThermalPalette(temperatureC, thermalDisplayMinC, thermalDisplayMaxC))
                : GeometryColor;
        }

        // ARCore pode refinar a pose da âncora após o celular se deslocar. Os
        // pontos usam simulationSpace.World, então precisam seguir essa revisão.
        // Movimento real do scanner via UWB não chama este método.
        public bool RebaseWorldPoints(Pose oldAnchor, Pose newAnchor)
        {
            if (activePointsCount == 0) return true;
            if (Vector3.Distance(oldAnchor.position, newAnchor.position) < .001f &&
                Quaternion.Angle(oldAnchor.rotation, newAnchor.rotation) < .05f) return false;
            Quaternion deltaRotation = newAnchor.rotation * Quaternion.Inverse(oldAnchor.rotation);
            voxelToIndex.Clear();
            for (int i = 0; i < activePointsCount; i++)
            {
                Vector3 position = newAnchor.position + deltaRotation *
                    (pointsBuffer[i].worldPosition - oldAnchor.position);
                pointsBuffer[i].worldPosition = position;
                pointsBuffer[i].viewDirection = deltaRotation * pointsBuffer[i].viewDirection;
                particlesBuffer[i].position = position;
                Vector3Int key = new Vector3Int(
                    Mathf.FloorToInt(position.x / voxelGridSize),
                    Mathf.FloorToInt(position.y / voxelGridSize),
                    Mathf.FloorToInt(position.z / voxelGridSize));
                pointsBuffer[i].voxelKey = key;
                voxelToIndex[key] = i;
            }
            bufferDirty = true;
            lodDirty = true;
            nextLodTime = 0f;
            return true;
        }

        public void SetRejectWeakLidarReturns(bool reject)
        {
            if (rejectWeakLidarReturns == reject) return;
            rejectWeakLidarReturns = reject;
            // Não mistura pontos coletados com regras diferentes nem pacotes antigos.
            ClearPointCloud();
        }

        public void ClearPointCloud()
        {
            activePointsCount = 0;
            weakLidarDiscardedPoints = 0;
            hasVisibilityCameraPosition = false;
            nextReplacementIndex = 0;
            voxelToIndex.Clear();
            bufferDirty = false;
            if (receiver != null)
                while (receiver.incomingPoints.TryDequeue(out _)) { }
            observedMinTemp = 999f;
            observedMaxTemp = -999f;
            activeThermalPointsCount = 0;
            surfaceLodPolygons = surfaceLodMergedPoints = 0;
            lodDirty = true;
            nextLodTime = 0f;
            if (lodMesh != null) lodMesh.Clear();
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
            sb.AppendLine("comment flags bit 2: thermal unavailable; temperature nan means not measured");
            sb.AppendLine("comment flags bit 3: LiDAR weak signal warning; not a confirmed range error");
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
                Vector3 p = pointsBuffer[i].worldPosition;
                Color32 c = PointColor(pointsBuffer[i].temperature, pointsBuffer[i].surfaceFlags);
                float temp = pointsBuffer[i].temperature;
                string temperatureText = float.IsNaN(temp) ? "nan" : temp.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                byte f = pointsBuffer[i].surfaceFlags;
                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0:F4} {1:F4} {2:F4} {3} {4} {5} {6} {7}",
                    p.x, p.y, p.z, c.r, c.g, c.b, temperatureText, f));
            }

            File.WriteAllText(filePath, sb.ToString());
            return filePath;
        }

        private void OnDestroy()
        {
            if (axesRoot != null) Destroy(axesRoot.gameObject);
            if (lodMesh != null) Destroy(lodMesh);
            if (lodObject != null) Destroy(lodObject);
            if (pointMaterial != null) Destroy(pointMaterial);
            if (surfaceMaterial != null) Destroy(surfaceMaterial);
        }
    }
}
