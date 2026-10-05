using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ArScanner.Network;
using UnityEngine;

namespace ArScanner.Rendering
{
    [RequireComponent(typeof(PointCloudTcpReceiver))]
    public class ThermalPointCloudRenderer : MonoBehaviour
    {
        public const float DefaultMountYawOffset = 200f;
        public const float DefaultPointSize = 0.0125f;
        // Gray marks LiDAR geometry outside the thermal image or without a fresh frame.
        private static readonly Color32 GeometryColor = new Color32(235, 235, 235, 255);

        [Header("Capacidade e Performance")]
        [Range(1000, 150000)]
        public int maxPoints = 80000;
        public float voxelGridSize = 0.025f; // 2.5cm
        [Tooltip("Zona morta (em metros) para filtrar ruído do LiDAR em pontos estáveis e evitar trepidação/flickering")]
        public float pointMergeDeadband = 0.015f; // 1.5cm
        public int maxPointsPerFrame = 1200;

        [Header("Tamanho do Quad")]
        [Range(0.003125f, 0.05f)]
        public float pointSize = DefaultPointSize;
        public float PointSizeMultiplier => pointSize / DefaultPointSize;

        [Tooltip("Com LOD ativo, amplia a representação de pontos com vizinhança planar medida. A seleção de tamanho explícito desativa este ajuste.")]
        public bool adaptivePointSizing = true;
        private float lastPointSize = DefaultPointSize;
        private bool lastAdaptivePointSizing = true;
        [Tooltip("Experimental: oculta pontos individuais vistos do lado oposto ao scanner na aquisição. Esse lado não é uma normal de superfície medida.")]
        public bool hideBackFacingPoints = false;
        private bool lastHideBackFacingPoints;

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
        public bool enableSurfaceLod = false;
        public float lodNearMeters = .45f;
        public float lodFarMeters = 2.5f;
        public float lodTileSize = .24f;
        public float lodPlaneTolerance = .038f;
        public float lodMaxThermalSpreadC = 2.5f;
        public int surfaceLodPolygons;
        public int surfaceLodMergedPoints;
        private bool lastSurfaceLodEnabled = false;
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
            public float publishedTemperature;
            public byte surfaceFlags;
            public Vector3Int voxelKey;
            public byte observations;
            public uint identity;
        }

        private readonly Dictionary<Vector3Int, int> voxelToIndex = new Dictionary<Vector3Int, int>();
        private StoredPoint[] pointsBuffer;
        private ParticleSystem.Particle[] particlesBuffer;
        private bool[] lodCovered;
        private int[] lodPointRectangles;
        private float[] supportedPointSpacing;
        private bool bufferDirty = false;
        private int nextReplacementIndex = 0;
        private Material pointMaterial;
        private Material surfaceMaterial;
        private Vector3 lastVisibilityCameraPosition;
        private bool hasVisibilityCameraPosition;
        private float nextVisibilityTime;
        private float metersPerPixelAtUnitDistance;
        private int lodGeneration;
        private bool hasLodDisplay;
        private LodJob lodJob;
        private SurfaceLodBuilder.RectangleInfo[] appliedLodRectangles;
        private bool[] appliedLodActiveRectangles;
        private Vector3[] appliedLodVertices;
        private Bounds appliedLodBounds;
        private bool lodMeshDirty;
        private uint nextPointIdentity;
        private LodSettings lastLodSettings;

        // A worker owns only an immutable copy of observations and plain geometry.
        // Meshes, transforms and particles are created or updated on the main thread.
        private sealed class LodJob
        {
            public Task<SurfaceLodBuilder.GeometryData> task;
            public SurfaceLodBuilder.Sample[] samples;
            public uint[] identities;
            public LodSettings settings;
            public Vector3 camera;
            public int generation;
        }

        private struct LodSettings : IEquatable<LodSettings>
        {
            public bool surface,thermal,absolute,invert,panValid;
            public float near,far,tile,plane,spread,opacity,minTemp,maxTemp,voxel,pitch,yaw,roll,opticalHeight;
            public int thermalProfile;
            public uint panCalibration,imuGeneration;
            public bool Equals(LodSettings other) => surface==other.surface &&
                thermal==other.thermal && absolute==other.absolute && invert==other.invert && panValid==other.panValid &&
                near==other.near && far==other.far && tile==other.tile && plane==other.plane && spread==other.spread &&
                opacity==other.opacity && minTemp==other.minTemp && maxTemp==other.maxTemp && voxel==other.voxel &&
                pitch==other.pitch && yaw==other.yaw && roll==other.roll && opticalHeight==other.opticalHeight &&
                thermalProfile==other.thermalProfile && panCalibration==other.panCalibration && imuGeneration==other.imuGeneration;
        }

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
            lodObject.SetActive(enableSurfaceLod);

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
            lodPointRectangles = new int[maxPoints];
            for (int i=0;i<lodPointRectangles.Length;i++) lodPointRectangles[i]=-1;
            supportedPointSpacing = new float[maxPoints];
            lastSurfaceLodEnabled = enableSurfaceLod;
            lodDirty = enableSurfaceLod;
            lastLodSettings = CaptureLodSettings();
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
            psRenderer.alignment = ParticleSystemRenderSpace.View;

            Shader defaultShader = Resources.Load<Shader>("ArScannerPointCloud");
            if (defaultShader == null) defaultShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (defaultShader == null) defaultShader = Shader.Find("Particles/Standard Unlit");
            if (defaultShader != null)
            {
                pointMaterial = new Material(defaultShader);
                pointMaterial.SetFloat("_Cull", 0f); // Camera-facing quads remain visible.
                pointMaterial.SetFloat("_ZWrite", 1f);
                pointMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 2;
                psRenderer.sharedMaterial = pointMaterial;
                surfaceMaterial = new Material(defaultShader);
                surfaceMaterial.SetFloat("_Cull", 2f); // Back
                surfaceMaterial.SetFloat("_ZWrite", 1f);
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
                    pointsBuffer[i].publishedTemperature = pointsBuffer[i].temperature;
                }
                bufferDirty = true;
                lodDirty = true;
            }

            if (lastSurfaceLodEnabled != enableSurfaceLod)
                SetSurfaceLodEnabled(enableSurfaceLod);
            Vector3 cameraPosition = CurrentCameraPosition();
            LodSettings currentSettings=CaptureLodSettings();
            if (!lastLodSettings.Equals(currentSettings))
            {
                lastLodSettings=currentSettings;
                InvalidateSurfaceLod();
            }
            if (!Mathf.Approximately(lastPointSize,pointSize) || lastAdaptivePointSizing!=adaptivePointSizing ||
                lastHideBackFacingPoints!=hideBackFacingPoints)
            {
                lastPointSize=pointSize;
                lastAdaptivePointSizing=adaptivePointSizing;
                lastHideBackFacingPoints=hideBackFacingPoints;
                RefreshPointVisibility(cameraPosition);
            }
            if (Vector3.Distance(cameraPosition,lastLodCameraPosition) > .5f) lodDirty = true;
            CompleteSurfaceLodBuild(cameraPosition);
            if (enableSurfaceLod && lodDirty && lodJob==null && Time.unscaledTime >= nextLodTime) RebuildSurfaceLod(cameraPosition);
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
                ApplySurfaceLodChanges(cameraPosition);
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
            if (receiver != null && receiver.status != null && receiver.status.imuOrientationEnabled &&
                !receiver.HasUsableImuOrientation)
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
            // Display extrinsics must not turn an invalid zero/nonfinite packet
            // into an apparently valid coordinate. Valid returns have no range cap.
            float rawDistanceSquared=localPos.sqrMagnitude;
            if (rawDistanceSquared<=0f || float.IsNaN(rawDistanceSquared) || float.IsInfinity(rawDistanceSquared)) return;
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
                sqrDist <= 0f) return;
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
                Vector3 previousPosition = particlesBuffer[existingIndex].position;
                bool wasThermal = PointHasThermal(existing);
                bool newHasThermal = HasThermalMeasurement(data);

                int observations = Mathf.Min(32, existing.observations + 1);
                existing.observations = (byte)observations;

                float posDelta = Vector3.Distance(existing.worldPosition, worldPos);
                if (observations < 4 || posDelta >= pointMergeDeadband)
                    existing.worldPosition = Vector3.Lerp(existing.worldPosition, worldPos, 1f / observations);
                // Small refinements accumulate against the quad last published.
                bool positionChanged = Vector3.Distance(previousPosition, existing.worldPosition) > 0.002f;
                // Substituicao e fusao de dados termicos:
                // Quando a camera passa sobre pontos lidos anteriormente sem termica,
                // substitui e atualiza imediatamente para a medicao termica.
                bool thermalStateChanged = false;
                if (!wasThermal && newHasThermal)
                {
                    existing.temperature = data.temperatureC;
                    existing.surfaceFlags = (byte)(data.surfaceFlags & ~ScanPointData.ThermalUnavailableFlag);
                    existing.rgb = new Color32(data.r, data.g, data.b, 255);
                    existing.viewDirection = viewDirection;
                    activeThermalPointsCount++;
                    thermalStateChanged = true;
                }
                else if (wasThermal && newHasThermal)
                {
                    // Ambos termicos: filtro EMA de temperatura para evitar cintilacao brusca
                    existing.temperature = Mathf.Lerp(existing.temperature, data.temperatureC, 0.25f);
                    existing.rgb = new Color32(data.r, data.g, data.b, 255);
                    existing.surfaceFlags = (byte)((existing.surfaceFlags & ~2) | (data.surfaceFlags & 2));
                    // Compare with the last displayed value so gradual changes
                    // eventually publish while subthreshold noise remains stable.
                    if (Mathf.Abs(existing.temperature - existing.publishedTemperature) > 0.3f)
                        thermalStateChanged = true;
                }
                else if (wasThermal && !newHasThermal)
                {
                    // Feixe atual sem termica: PRESERVA medicao termica anterior (nao apaga)
                }
                else
                {
                    existing.rgb = new Color32(data.r, data.g, data.b, 255);
                    existing.surfaceFlags = data.surfaceFlags;
                }

                pointsBuffer[existingIndex] = existing;

                // So invalida observacoes LOD e marca buffers como dirty se houve alteracao real
                if (positionChanged || thermalStateChanged)
                {
                    UpdateLodObservation(existingIndex, previousPosition, pointsBuffer[existingIndex]);
                    Color32 color = PointColor(pointsBuffer[existingIndex].temperature, existing.surfaceFlags);
                    color.a = (byte)(255 * pointOpacity);
                    particlesBuffer[existingIndex].startColor = color;
                    pointsBuffer[existingIndex].publishedTemperature = existing.temperature;
                    particlesBuffer[existingIndex].startSize =
                        !lodCovered[existingIndex] ? DisplayPointSize(existingIndex, cameraPosition) : 0f;
                    particlesBuffer[existingIndex].position = existing.worldPosition;
                    bufferDirty = true;
                    lodDirty = true;
                }
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

                    RemoveLodSupport(targetIndex,pointsBuffer[targetIndex].worldPosition);

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
                    publishedTemperature = HasThermalMeasurement(data) ? data.temperatureC : float.NaN,
                    surfaceFlags = data.surfaceFlags,
                    voxelKey = voxelKey,
                    observations = 1,
                    identity = ++nextPointIdentity
                };

                Color32 color = PointColor(pointsBuffer[targetIndex].temperature, data.surfaceFlags);
                color.a = (byte)(255 * pointOpacity);
                particlesBuffer[targetIndex].position = worldPos;
                particlesBuffer[targetIndex].startColor = color;
                lodCovered[targetIndex] = false;
                lodPointRectangles[targetIndex] = -1;
                supportedPointSpacing[targetIndex] = 0f;
                UpdateLodObservation(targetIndex,worldPos,pointsBuffer[targetIndex]);
                particlesBuffer[targetIndex].startSize =
                    !lodCovered[targetIndex] ? DisplayPointSize(targetIndex,cameraPosition) : 0f;
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

        private void RefreshPointVisibility(Vector3 cameraPosition)
        {
            RemoveNearSurfaceLod(cameraPosition);
            ApplySurfaceLodChanges(cameraPosition);
            RefreshPointSizes(cameraPosition);
        }

        private Vector3 CurrentCameraPosition() => spatial != null && spatial.arCameraTransform != null
            ? spatial.arCameraTransform.position : Camera.main != null ? Camera.main.transform.position : Vector3.zero;

        private void RefreshPointSizes(Vector3 cameraPosition)
        {
            if (particlesBuffer==null) return;
            UpdateDisplayScale();
            for (int i = 0; i < activePointsCount; i++)
                particlesBuffer[i].startSize = !lodCovered[i] ? DisplayPointSize(i,cameraPosition) : 0f;
            bufferDirty = true;
        }

        private float DisplayPointSize(int index,Vector3 cameraPosition)
        {
            if (hideBackFacingPoints)
            {
                // Opt-in acquisition-side estimate; this is not a fitted normal.
                Vector3 observedSide=pointsBuffer[index].viewDirection;
                Vector3 cameraSide=cameraPosition-pointsBuffer[index].worldPosition;
                if (observedSide.sqrMagnitude>1e-8f && cameraSide.sqrMagnitude>1e-8f &&
                    Vector3.Dot(observedSide.normalized,cameraSide.normalized)<-.05f) return 0f;
            }
            // Points-only mode needs no planar analysis or adaptive coverage.
            if (!enableSurfaceLod || !adaptivePointSizing) return pointSize;
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
            if (!enableSurfaceLod)
            {
                lodJob=null;
                lodDirty=false;
                if (lodObject!=null) lodObject.SetActive(false);
                ClearSurfaceLodDisplay();
                return;
            }
            if (lodJob!=null || lodObject==null) return;
            lodDirty = false;
            nextLodTime = Time.unscaledTime + 2f;
            lastLodCameraPosition = cameraPosition;
            lodObject.SetActive(enableSurfaceLod);
            if (activePointsCount<6)
            {
                ClearSurfaceLodDisplay();
                return;
            }
            var samples = new SurfaceLodBuilder.Sample[activePointsCount];
            for (int i = 0; i < activePointsCount; i++)
                samples[i] = new SurfaceLodBuilder.Sample {
                    position=pointsBuffer[i].worldPosition,
                    viewDirection=pointsBuffer[i].viewDirection,
                    color=WithOpacity(PointColor(pointsBuffer[i].temperature,pointsBuffer[i].surfaceFlags)),
                    temperature=pointsBuffer[i].temperature,
                    hasThermal=PointHasThermal(pointsBuffer[i])
                };
            var identities=new uint[activePointsCount];
            for (int i=0;i<identities.Length;i++) identities[i]=pointsBuffer[i].identity;
            var job=new LodJob {samples=samples,identities=identities,settings=CaptureLodSettings(),camera=cameraPosition,generation=lodGeneration};
            job.task=Task.Run(()=>SurfaceLodBuilder.BuildGeometryData(job.samples,job.camera,
                job.settings.near,job.settings.far,
                job.settings.tile,job.settings.plane,job.settings.spread));
            // Observe faults even if this component is destroyed before the worker finishes.
            job.task.ContinueWith(completed=>{ _=completed.Exception; },TaskContinuationOptions.OnlyOnFaulted);
            lodJob=job;
        }

        private LodSettings CaptureLodSettings()
        {
            var status=receiver!=null ? receiver.status : null;
            return new LodSettings {
                surface=enableSurfaceLod,thermal=showThermalColors,
                absolute=useAbsoluteThermalScale,invert=invertVerticalLidar,near=lodNearMeters,far=lodFarMeters,
                tile=lodTileSize,plane=lodPlaneTolerance,spread=lodMaxThermalSpreadC,opacity=pointOpacity,
                minTemp=thermalDisplayMinC,maxTemp=thermalDisplayMaxC,voxel=voxelGridSize,
                pitch=pointPitchOffset,yaw=pointYawOffset,roll=pointRollOffset,opticalHeight=lidarOpticalHeight,
                thermalProfile=status!=null ? status.thermalOrientationProfile : -1,
                panCalibration=status!=null ? status.panReferenceCalibrationVersion : 0,
                imuGeneration=status!=null ? status.imuOrientationGeneration : 0,
                panValid=status!=null && status.panReferenceValid
            };
        }

        private static bool PointHasThermal(StoredPoint point) =>
            (point.surfaceFlags & ScanPointData.ThermalUnavailableFlag)==0 &&
            !float.IsNaN(point.temperature) && !float.IsInfinity(point.temperature);

        private static bool MatchesLodSample(SurfaceLodBuilder.Sample sample,StoredPoint point,LodSettings settings)
        {
            // Unchanged observations retain their known coverage directly.
            // Refined observations are checked against their rectangle's actual
            // plane, front and temperature range instead of rejecting the job.
            if (Vector3.Distance(sample.position,point.worldPosition)>Mathf.Max(.001f,settings.plane*.5f) ||
                Vector3.Dot(sample.viewDirection,point.viewDirection)<.98f || sample.hasThermal!=PointHasThermal(point)) return false;
            return !sample.hasThermal || Mathf.Abs(sample.temperature-point.temperature)<=Mathf.Min(.5f,Mathf.Max(0f,settings.spread)*.25f);
        }

        private void CompleteSurfaceLodBuild(Vector3 cameraPosition)
        {
            if (!enableSurfaceLod)
            {
                if (lodJob!=null || hasLodDisplay) InvalidateSurfaceLod();
                return;
            }
            LodJob job=lodJob;
            if (job==null || !job.task.IsCompleted) return;
            lodJob=null;
            if (job.task.IsFaulted || job.task.IsCanceled)
            {
                if (job.task.IsFaulted) Debug.LogException(job.task.Exception.GetBaseException(),this);
                lodDirty=true; nextLodTime=Time.unscaledTime+2f;
                return;
            }
            LodSettings settings=CaptureLodSettings();
            bool compatible=job.generation==lodGeneration && job.settings.Equals(settings) &&
                activePointsCount>=job.samples.Length && lodObject!=null &&
                Vector3.Distance(job.camera,cameraPosition)<=.5f;
            if (!compatible)
            {
                lodDirty=true; nextLodTime=0f;
                return;
            }
            var data=job.task.Result;
            float supportMargin=settings.plane;
            foreach (var rectangle in data.rectangles)
                supportMargin=Mathf.Max(supportMargin,Mathf.Max(rectangle.supportRadiusU,rectangle.supportRadiusV));
            Bounds resultBounds=GeometryBounds(data.vertices,supportMargin);
            var activeRectangles=new bool[data.polygonCount];
            for (int i=0;i<activeRectangles.Length;i++) activeRectangles[i]=true;
            // A changed slot invalidates only the rectangles that used its old
            // observation. Other surfaces remain useful while the cloud grows.
            for (int i=0;i<job.samples.Length;i++)
            {
                bool sameIdentity=job.identities!=null && i<job.identities.Length &&
                    job.identities[i]==pointsBuffer[i].identity;
                if (!sameIdentity)
                {
                    DeactivateRectangle(activeRectangles,data.coveredRectangle[i]);
                    DeactivateIntersectingRectangles(data.vertices,data.rectangles,activeRectangles,job.samples[i].position,settings.plane);
                }
                else if (!MatchesLodSample(job.samples[i],pointsBuffer[i],settings))
                {
                    int rectangle=data.coveredRectangle[i];
                    if (rectangle>=0 && !CanRepresentPoint(data.vertices,data.rectangles,rectangle,pointsBuffer[i],settings))
                        DeactivateRectangle(activeRectangles,rectangle);
                    for (int affected=0;affected<activeRectangles.Length;affected++)
                        if (activeRectangles[affected] &&
                            CanInfluenceRectangle(data.vertices,data.rectangles,affected,job.samples[i].position,settings.plane) &&
                            !CanInfluenceRectangle(data.vertices,data.rectangles,affected,pointsBuffer[i].worldPosition,settings.plane))
                            activeRectangles[affected]=false;
                }
            }
            // New and refined observations must not be hidden beneath a stale
            // temperature, missing-data classification or acquisition front.
            for (int i=0;i<activePointsCount;i++)
            {
                bool unchanged=i<job.samples.Length && job.identities!=null && i<job.identities.Length &&
                    job.identities[i]==pointsBuffer[i].identity && MatchesLodSample(job.samples[i],pointsBuffer[i],settings);
                if (!unchanged)
                    DeactivateIncompatibleRectangles(data.vertices,data.rectangles,activeRectangles,pointsBuffer[i],settings);
            }
            for (int rectangle=0;rectangle<activeRectangles.Length;rectangle++)
                if (DistanceToRectangleSq(data.vertices,rectangle*4,cameraPosition)<settings.near*settings.near)
                    activeRectangles[rectangle]=false;
            Mesh completedMesh=SurfaceLodBuilder.CreateMesh(data);
            if (lodMesh!=null) DestroyRuntimeResource(lodMesh);
            lodMesh=completedMesh;
            lodFilter.sharedMesh=lodMesh;
            lodObject.SetActive(enableSurfaceLod);
            surfaceLodPolygons=0;
            if (enableSurfaceLod)
                for (int i=0;i<activeRectangles.Length;i++) if (activeRectangles[i]) surfaceLodPolygons++;
            surfaceLodMergedPoints=0;
            // The cloud may have grown while the worker ran. New observations
            // retain quads and cannot be indexed by an older coverage array.
            Array.Clear(lodCovered,0,activePointsCount);
            Array.Clear(supportedPointSpacing,0,activePointsCount);
            for (int i=0;i<activePointsCount;i++)
            {
                bool sameIdentity=i<job.samples.Length && job.identities!=null && i<job.identities.Length &&
                    job.identities[i]==pointsBuffer[i].identity;
                if (sameIdentity && (data.coveredRectangle[i]<0 || activeRectangles[data.coveredRectangle[i]]))
                    supportedPointSpacing[i]=data.spacing[i];
                int rectangle=sameIdentity && MatchesLodSample(job.samples[i],pointsBuffer[i],settings)
                    ? data.coveredRectangle[i] : FindRepresentingRectangle(data.vertices,data.rectangles,activeRectangles,pointsBuffer[i],settings);
                if (rectangle>=0 && !activeRectangles[rectangle]) rectangle=-1;
                lodPointRectangles[i]=enableSurfaceLod ? rectangle : -1;
                lodCovered[i]=enableSurfaceLod && rectangle>=0;
                if (lodCovered[i]) surfaceLodMergedPoints++;
            }
            appliedLodRectangles=data.rectangles;
            appliedLodActiveRectangles=activeRectangles;
            appliedLodVertices=data.vertices;
            appliedLodBounds=resultBounds;
            lodMeshDirty=true;
            hasLodDisplay=true;
            if (activePointsCount!=job.samples.Length || surfaceLodPolygons<data.polygonCount ||
                Vector3.Distance(job.camera,cameraPosition)>.05f) lodDirty=true;
            // All visibility and adaptive sizing use the current camera, not the
            // transform captured when the worker started.
            RefreshPointVisibility(cameraPosition);
        }

        private void InvalidateSurfaceLod()
        {
            lodGeneration++;
            if (!enableSurfaceLod) lodJob=null;
            lodDirty=enableSurfaceLod; nextLodTime=0f;
            ClearSurfaceLodDisplay();
        }

        public void SetSurfaceLodEnabled(bool enabled)
        {
            if (enableSurfaceLod==enabled && lastSurfaceLodEnabled==enabled &&
                (enabled || (lodJob==null && !hasLodDisplay))) return;
            enableSurfaceLod=lastSurfaceLodEnabled=enabled;
            lastLodSettings.surface=enabled;
            // Detached workers own only managed snapshots. Their faults are
            // observed, and their old generation can never publish a mesh.
            lodJob=null;
            InvalidateSurfaceLod();
            if (lodObject!=null) lodObject.SetActive(enabled);
            UpdateParticles();
        }

        public void SetPointSizeMultiplier(float multiplier)
        {
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier)) return;
            pointSize=DefaultPointSize*Mathf.Clamp(multiplier,.25f,4f);
            // Explicit sizes stay exact, including when LOD is active.
            adaptivePointSizing=lastAdaptivePointSizing=false;
            lastPointSize=pointSize;
            RefreshPointSizes(CurrentCameraPosition());
            UpdateParticles();
        }

        public void SetHideBackFacingPoints(bool hide)
        {
            if (hideBackFacingPoints==hide && lastHideBackFacingPoints==hide) return;
            hideBackFacingPoints=lastHideBackFacingPoints=hide;
            RefreshPointSizes(CurrentCameraPosition());
            UpdateParticles();
        }

        private static bool DeactivateRectangle(bool[] active,int rectangle)
        {
            if (active==null || rectangle<0 || rectangle>=active.Length || !active[rectangle]) return false;
            active[rectangle]=false;
            return true;
        }

        private void DeactivateAppliedRectangle(int rectangle)
        {
            if (!DeactivateRectangle(appliedLodActiveRectangles,rectangle)) return;
            surfaceLodPolygons=Mathf.Max(0,surfaceLodPolygons-1);
            lodMeshDirty=bufferDirty=lodDirty=true;
            nextLodTime=0f;
        }

        private static void DeactivateIntersectingRectangles(Vector3[] vertices,SurfaceLodBuilder.RectangleInfo[] rectangles,
            bool[] active,Vector3 point,float tolerance)
        {
            if (vertices==null || active==null) return;
            for (int rectangle=0;rectangle<active.Length;rectangle++)
                if (active[rectangle] && CanInfluenceRectangle(vertices,rectangles,rectangle,point,tolerance))
                    active[rectangle]=false;
        }

        private static void DeactivateIncompatibleRectangles(Vector3[] vertices,SurfaceLodBuilder.RectangleInfo[] rectangles,
            bool[] active,StoredPoint point,LodSettings settings)
        {
            if (vertices==null || active==null) return;
            for (int rectangle=0;rectangle<active.Length;rectangle++)
                if (active[rectangle] && CanInfluenceRectangle(vertices,rectangles,rectangle,point.worldPosition,settings.plane) &&
                    !HasCompatiblePlaneAndThermal(vertices,rectangles,rectangle,point,settings))
                    active[rectangle]=false;
        }

        private static bool CanInfluenceRectangle(Vector3[] vertices,SurfaceLodBuilder.RectangleInfo[] rectangles,
            int rectangle,Vector3 point,float tolerance)
        {
            int start=rectangle*4;
            Vector3 origin=vertices[start],u=vertices[start+1]-origin,v=vertices[start+3]-origin,d=point-origin;
            float lengthU=u.magnitude,lengthV=v.magnitude;
            if (lengthU<.00001f || lengthV<.00001f) return false;
            Vector3 axisU=u/lengthU,axisV=v/lengthV,normal=Vector3.Cross(axisU,axisV);
            if (Mathf.Abs(Vector3.Dot(normal,d))>tolerance) return false;
            float projectedU=Vector3.Dot(d,axisU),projectedV=Vector3.Dot(d,axisV);
            float du=projectedU-Mathf.Clamp(projectedU,0f,lengthU),dv=projectedV-Mathf.Clamp(projectedV,0f,lengthV);
            var info=rectangles[rectangle];
            return du*du/(info.supportRadiusU*info.supportRadiusU)+dv*dv/(info.supportRadiusV*info.supportRadiusV)<=1f;
        }

        private static bool CanRepresentPoint(Vector3[] vertices,SurfaceLodBuilder.RectangleInfo[] rectangles,
            int rectangle,StoredPoint point,LodSettings settings)
        {
            if (vertices==null || rectangles==null || rectangle<0 || rectangle>=rectangles.Length) return false;
            return DistanceToRectangleSq(vertices,rectangle*4,point.worldPosition)<=settings.plane*settings.plane &&
                HasCompatiblePlaneAndThermal(vertices,rectangles,rectangle,point,settings);
        }

        private static bool HasCompatiblePlaneAndThermal(Vector3[] vertices,SurfaceLodBuilder.RectangleInfo[] rectangles,
            int rectangle,StoredPoint point,LodSettings settings)
        {
            int start=rectangle*4;
            Vector3 normal=Vector3.Cross(vertices[start+1]-vertices[start],vertices[start+3]-vertices[start]).normalized;
            if (Vector3.Dot(normal,point.viewDirection)<=0f ||
                Mathf.Abs(Vector3.Dot(normal,point.worldPosition-vertices[start]))>settings.plane) return false;
            var info=rectangles[rectangle];
            bool thermal=PointHasThermal(point);
            if (info.hasThermal && !thermal) return true;
            return info.hasThermal==thermal && (!thermal ||
                Mathf.Max(info.maximumTemperature,point.temperature)-Mathf.Min(info.minimumTemperature,point.temperature)<=settings.spread);
        }

        private static int FindRepresentingRectangle(Vector3[] vertices,SurfaceLodBuilder.RectangleInfo[] rectangles,
            bool[] active,StoredPoint point,LodSettings settings)
        {
            if (vertices==null || active==null) return -1;
            for (int rectangle=0;rectangle<active.Length;rectangle++)
            {
                if (!active[rectangle] || !CanRepresentPoint(vertices,rectangles,rectangle,point,settings)) continue;
                int start=rectangle*4;
                Vector3 origin=vertices[start],u=vertices[start+1]-origin,v=vertices[start+3]-origin,d=point.worldPosition-origin;
                float projectedU=Vector3.Dot(d,u)/u.sqrMagnitude,projectedV=Vector3.Dot(d,v)/v.sqrMagnitude;
                // A nearby continuation is compatible but keeps its own quad
                // until the newly measured area has entered a later rebuild.
                if (projectedU>=-.0001f && projectedU<=1.0001f && projectedV>=-.0001f && projectedV<=1.0001f)
                    return rectangle;
            }
            return -1;
        }

        private void UpdateLodObservation(int index,Vector3 previousPosition,StoredPoint point)
        {
            if (appliedLodVertices==null || appliedLodActiveRectangles==null) return;
            var settings=CaptureLodSettings();
            int previousRectangle=lodPointRectangles[index];
            if (previousRectangle>=0 && !CanRepresentPoint(appliedLodVertices,appliedLodRectangles,previousRectangle,point,settings))
                DeactivateAppliedRectangle(previousRectangle);
            if (appliedLodBounds.Contains(point.worldPosition) || appliedLodBounds.Contains(previousPosition))
                for (int rectangle=0;rectangle<appliedLodActiveRectangles.Length;rectangle++)
                    if (appliedLodActiveRectangles[rectangle] &&
                        ((CanInfluenceRectangle(appliedLodVertices,appliedLodRectangles,rectangle,point.worldPosition,settings.plane) &&
                            !HasCompatiblePlaneAndThermal(appliedLodVertices,appliedLodRectangles,rectangle,point,settings)) ||
                         (CanInfluenceRectangle(appliedLodVertices,appliedLodRectangles,rectangle,previousPosition,settings.plane) &&
                            !CanInfluenceRectangle(appliedLodVertices,appliedLodRectangles,rectangle,point.worldPosition,settings.plane))))
                        DeactivateAppliedRectangle(rectangle);
            int represented=appliedLodBounds.Contains(point.worldPosition)
                ? FindRepresentingRectangle(appliedLodVertices,appliedLodRectangles,appliedLodActiveRectangles,point,settings) : -1;
            bool covered=enableSurfaceLod && represented>=0;
            if (lodCovered[index]!=covered) surfaceLodMergedPoints+=covered ? 1 : -1;
            lodCovered[index]=covered;
            lodPointRectangles[index]=covered ? represented : -1;
        }

        private void RemoveLodSupport(int index,Vector3 position)
        {
            DeactivateAppliedRectangle(lodPointRectangles[index]);
            if (appliedLodVertices!=null && appliedLodActiveRectangles!=null && appliedLodBounds.Contains(position))
                for (int rectangle=0;rectangle<appliedLodActiveRectangles.Length;rectangle++)
                    if (appliedLodActiveRectangles[rectangle] &&
                        CanInfluenceRectangle(appliedLodVertices,appliedLodRectangles,rectangle,position,lodPlaneTolerance))
                        DeactivateAppliedRectangle(rectangle);
            if (lodCovered[index]) surfaceLodMergedPoints--;
            lodCovered[index]=false;
            lodPointRectangles[index]=-1;
            supportedPointSpacing[index]=0f;
        }

        private void RemoveNearSurfaceLod(Vector3 cameraPosition)
        {
            if (!enableSurfaceLod || appliedLodVertices==null || appliedLodActiveRectangles==null) return;
            for (int rectangle=0;rectangle<appliedLodActiveRectangles.Length;rectangle++)
                if (appliedLodActiveRectangles[rectangle] &&
                    DistanceToRectangleSq(appliedLodVertices,rectangle*4,cameraPosition)<lodNearMeters*lodNearMeters)
                    DeactivateAppliedRectangle(rectangle);
        }

        // Publish removals once after the frame's observations have been drained.
        // Vertex buffers and unaffected rectangles are retained; only indices
        // and quads covered by retired rectangles change.
        private void ApplySurfaceLodChanges(Vector3 cameraPosition)
        {
            if (!lodMeshDirty) return;
            lodMeshDirty=false;
            if (lodMesh!=null)
            {
                var indices=new List<int>(surfaceLodPolygons*6);
                for (int rectangle=0;rectangle<appliedLodActiveRectangles.Length;rectangle++)
                {
                    if (!appliedLodActiveRectangles[rectangle]) continue;
                    int start=rectangle*4;
                    indices.Add(start); indices.Add(start+1); indices.Add(start+2);
                    indices.Add(start); indices.Add(start+2); indices.Add(start+3);
                }
                if (indices.Count==0)
                {
                    DestroyRuntimeResource(lodMesh); lodMesh=null;
                    lodFilter.sharedMesh=null;
                }
                else lodMesh.SetTriangles(indices,0);
            }
            for (int i=0;i<activePointsCount;i++)
            {
                int rectangle=lodPointRectangles[i];
                if (rectangle<0 || appliedLodActiveRectangles[rectangle]) continue;
                if (lodCovered[i]) surfaceLodMergedPoints--;
                lodCovered[i]=false;
                lodPointRectangles[i]=-1;
                supportedPointSpacing[i]=0f;
                particlesBuffer[i].startSize=DisplayPointSize(i,cameraPosition);
            }
            bufferDirty=true;
        }

        private void ClearSurfaceLodDisplay()
        {
            if (lodMesh!=null) {DestroyRuntimeResource(lodMesh); lodMesh=null;}
            if (lodFilter!=null) lodFilter.sharedMesh=null;
            surfaceLodPolygons=surfaceLodMergedPoints=0;
            appliedLodRectangles=null;
            appliedLodActiveRectangles=null;
            appliedLodVertices=null;
            lodMeshDirty=false;
            hasLodDisplay=false;
            if (lodCovered==null || particlesBuffer==null) return;
            Array.Clear(lodCovered,0,activePointsCount);
            for (int i=0;i<activePointsCount;i++) lodPointRectangles[i]=-1;
            Array.Clear(supportedPointSpacing,0,activePointsCount);
            RefreshPointVisibility(CurrentCameraPosition());
        }

        private static Bounds GeometryBounds(Vector3[] vertices,float tolerance)
        {
            if (vertices.Length==0) return default;
            var bounds=new Bounds(vertices[0],Vector3.zero);
            for (int i=1;i<vertices.Length;i++) bounds.Encapsulate(vertices[i]);
            bounds.Expand(Mathf.Max(.001f,tolerance)*2f);
            return bounds;
        }

        private static float DistanceToRectangleSq(Vector3[] vertices,int start,Vector3 point)
        {
            // Builder emits four orthogonal corners for every rectangle.
            Vector3 origin=vertices[start],u=vertices[start+1]-origin,v=vertices[start+3]-origin,d=point-origin;
            float uu=u.sqrMagnitude,vv=v.sqrMagnitude;
            if (uu<1e-10f || vv<1e-10f) return float.PositiveInfinity;
            Vector3 closest=origin+u*Mathf.Clamp01(Vector3.Dot(d,u)/uu)+v*Mathf.Clamp01(Vector3.Dot(d,v)/vv);
            return (closest-point).sqrMagnitude;
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
            InvalidateSurfaceLod();
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
            InvalidateSurfaceLod();
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

        private static void DestroyRuntimeResource(UnityEngine.Object resource)
        {
            if (resource==null) return;
#if UNITY_EDITOR
            // Validation also exercises this component outside Play Mode, where
            // delayed destruction is forbidden. These are owned runtime objects.
            if (!Application.isPlaying) {DestroyImmediate(resource); return;}
#endif
            Destroy(resource);
        }

        private void OnDestroy()
        {
            lodGeneration++;
            lodJob=null; // The remaining worker owns managed snapshot data only.
            if (axesRoot != null) DestroyRuntimeResource(axesRoot.gameObject);
            if (lodMesh != null) DestroyRuntimeResource(lodMesh);
            if (lodObject != null) DestroyRuntimeResource(lodObject);
            if (pointMaterial != null) DestroyRuntimeResource(pointMaterial);
            if (surfaceMaterial != null) DestroyRuntimeResource(surfaceMaterial);
        }
    }
}
