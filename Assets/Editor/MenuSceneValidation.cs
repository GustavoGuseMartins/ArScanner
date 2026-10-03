#if UNITY_EDITOR
using System;
using System.Linq;
using ArScanner.Network;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ArScanner.EditorTools
{
    public static class MenuSceneValidation
    {
        private const string CheckKey = "ArScanner.MenuRuntimeCheck";
        private static int frames;

        public static void CheckMenuInPlayMode()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/MenuViewer.unity");
            SessionState.SetBool(CheckKey, true);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        private static void Register()
        {
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(CheckKey, false)) return;
            frames = 0;
            EditorApplication.update += CheckFrame;
        }

        private static void CheckFrame()
        {
            if (++frames < 35) return;
            EditorApplication.update -= CheckFrame;
            SessionState.SetBool(CheckKey, false);
            try
            {
                var menu = UnityEngine.Object.FindAnyObjectByType<MenuViewer>();
                Require(menu != null, "MenuViewer script missing.");
                Require(menu.GetComponent<UwbDataReceiver>()?.transportMode == UwbTransportMode.USB,
                    "The menu did not start USB diagnostics before AR.");
                Require(menu.textoStatus == null || !menu.textoStatus.gameObject.activeSelf,
                    "Legacy status overlaps the new menu.");
                Require(menu.botaoIniciar == null || !menu.botaoIniciar.gameObject.activeSelf,
                    "Legacy start button overlaps the new menu.");
                var buttons = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                    .Where(button => button.name == "Iniciar").ToArray();
                Require(buttons.Length == 1, "The menu must have exactly one main start button.");
                Require(!buttons[0].interactable,
                    "Opening AR must wait for the scanner Wi-Fi connection.");
                var scroll = UnityEngine.Object.FindAnyObjectByType<ScrollRect>();
                Require(scroll != null && !Overlaps((RectTransform)buttons[0].transform,
                    (RectTransform)scroll.transform),
                    "Main button overlaps the connection or calibration panel.");
                var receiver = menu.GetComponent<UwbDataReceiver>();
                var flags = System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                var diagnostics = new UwbDataReceiver.BaseDiagnostics {
                    kind="uwb_status",version=1,radioMask=7,state=1,d1=0f,d2=0f,d3=0f};
                typeof(UwbDataReceiver).GetProperty("Diagnostics",flags).SetValue(receiver,diagnostics);
                typeof(UwbDataReceiver).GetProperty("HasFreshDiagnostics",flags).SetValue(receiver,true);
                typeof(UwbDataReceiver).GetField("lastDiagnosticsTicks",flags)
                    .SetValue(receiver,System.Diagnostics.Stopwatch.GetTimestamp());
                typeof(UwbDataReceiver).GetField("lastUsbDataTicks",flags)
                    .SetValue(receiver,System.Diagnostics.Stopwatch.GetTimestamp());
                menu.isScannerAvailable = true;
                var refresh = typeof(MenuViewer).GetMethod("RefreshStatuses",flags);
                refresh.Invoke(menu,null);
                Require(buttons[0].interactable,
                    "Scanner Wi-Fi must allow manual AR placement without tag distances.");
                menu.isScannerAvailable = false;
                refresh.Invoke(menu,null);
                Require(!buttons[0].interactable,
                    "USB base telemetry cannot substitute for the scanner Wi-Fi connection.");
                menu.isScannerAvailable = true;
                diagnostics.d1 = diagnostics.d2 = diagnostics.d3 = 1.2f;
                refresh.Invoke(menu,null);
                Require(buttons[0].interactable,
                    "Three fresh tag distances plus scanner Wi-Fi must enable Start.");
                Debug.Log("[Menu validation] PASSED: one AR button, manual fallback without UWB, Wi-Fi gate, no legacy overlap.");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        private static bool Overlaps(RectTransform a, RectTransform b)
        {
            var ca = new Vector3[4];
            var cb = new Vector3[4];
            a.GetWorldCorners(ca);
            b.GetWorldCorners(cb);
            return ca[0].x < cb[2].x && ca[2].x > cb[0].x &&
                ca[0].y < cb[2].y && ca[2].y > cb[0].y;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new BuildFailedException(message);
        }
    }
}
#endif
