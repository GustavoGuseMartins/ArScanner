#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ArScanner;
using ArScanner.Network;
using ArScanner.Rendering;
using ArScanner.Spatial;
using ArScanner.UI;

namespace ArScanner.EditorTools
{
    public static class SceneSetupHelper
    {
        [MenuItem("ArScanner/1. Abrir Menu Inicial (MenuViewer)")]
        public static void OpenMenuViewerScene()
        {
            string path = "Assets/Scenes/MenuViewer.unity";
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(path);
                Debug.Log($"[ArScanner] Cena aberta: {path}");
            }
        }

        [MenuItem("ArScanner/2. Abrir Cena do Visualizador (CenaViewer)")]
        public static void OpenCenaViewerScene()
        {
            string path = "Assets/Scenes/CenaViewer.unity";
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(path);
                Debug.Log($"[ArScanner] Cena aberta: {path}");
            }
        }

        [MenuItem("ArScanner/3. Injetar ArScanner na Cena Aberta")]
        public static void SetupArScannerInCurrentScene()
        {
            // Verifica se já existe um ArScannerManager na cena
            ArScannerController existing = Object.FindFirstObjectByType<ArScannerController>();
            if (existing != null)
            {
                Debug.LogWarning("[ArScanner] ArScannerManager já existe nesta cena!");
                Selection.activeGameObject = existing.gameObject;
                return;
            }

            GameObject arScannerObj = new GameObject("ArScannerManager");
            arScannerObj.transform.position = Vector3.zero;

            // Root Container para a nuvem de pontos
            GameObject pointCloudRoot = new GameObject("PointCloudRoot");
            pointCloudRoot.transform.SetParent(arScannerObj.transform, false);

            // Particle System para desenho procedural de pontos
            GameObject psObj = new GameObject("PointCloudParticles");
            psObj.transform.SetParent(pointCloudRoot.transform, false);
            ParticleSystem ps = psObj.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = 80000;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startSize = 0.025f;
            main.startLifetime = float.MaxValue;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = false;

            var psRenderer = ps.GetComponent<ParticleSystemRenderer>();
            psRenderer.renderMode = ParticleSystemRenderMode.Billboard;

            Shader defaultShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (defaultShader == null) defaultShader = Shader.Find("Particles/Standard Unlit");
            if (defaultShader != null)
            {
                psRenderer.sharedMaterial = new Material(defaultShader);
            }

            // Adiciona módulos do ArScanner
            PointCloudTcpReceiver tcpReceiver = arScannerObj.AddComponent<PointCloudTcpReceiver>();
            UwbDataReceiver uwbReceiver = arScannerObj.AddComponent<UwbDataReceiver>();
            ThermalPointCloudRenderer renderer = arScannerObj.AddComponent<ThermalPointCloudRenderer>();
            UwbAnchorManager anchorManager = arScannerObj.AddComponent<UwbAnchorManager>();
            PointCloudSimulator simulator = arScannerObj.AddComponent<PointCloudSimulator>();
            ArScannerHUD hud = arScannerObj.AddComponent<ArScannerHUD>();
            ArScannerController controller = arScannerObj.AddComponent<ArScannerController>();

            // Configura referências mútuas
            renderer.pointCloudRoot = pointCloudRoot.transform;
            renderer.targetParticleSystem = ps;

            anchorManager.pointCloudRootContainer = pointCloudRoot.transform;
            Camera cam = Camera.main;
            if (cam != null)
            {
                anchorManager.arCameraTransform = cam.transform;
                if (cam.GetComponent<EditorCameraController>() == null)
                {
                    cam.gameObject.AddComponent<EditorCameraController>();
                }
            }

            hud.tcpReceiver = tcpReceiver;
            hud.uwbReceiver = uwbReceiver;
            hud.pointRenderer = renderer;
            hud.anchorManager = anchorManager;
            hud.simulator = simulator;

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[ArScanner] Módulos do ArScanner injetados com sucesso na cena ativa!");
            Selection.activeGameObject = arScannerObj;
        }

        [MenuItem("ArScanner/4. Atualizar Build Settings (MenuViewer + CenaViewer)")]
        public static void ConfigureBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene("Assets/Scenes/MenuViewer.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/CenaViewer.unity", true)
            };
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[ArScanner] Build Settings configurado com MenuViewer (0) e CenaViewer (1)!");
        }
    }
}
#endif
