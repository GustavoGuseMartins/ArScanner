#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ArScanner.EditorTools
{
    public static class AndroidBuildScript
    {
        [MenuItem("ArScanner/Build Android APK")]
        public static void BuildAndroidApk()
        {
            // O ESP32 atende HTTP na rede local, inclusive nos APKs de release.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.Android.forceInternetPermission = true;

            string[] scenes = new string[]
            {
                "Assets/Scenes/MenuViewer.unity",
                "Assets/Scenes/CenaViewer.unity"
            };

            string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "Builds");
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            string apkPath = Path.Combine(outputDir, "ScannerAR.apk");
            // Batch validation can produce a named APK without replacing the user's last build.
            string[] arguments = Environment.GetCommandLineArgs();
            int outputArg = Array.IndexOf(arguments, "-arscannerOutput");
            if (outputArg >= 0 && outputArg + 1 < arguments.Length)
            {
                apkPath = Path.GetFullPath(arguments[outputArg + 1]);
                Directory.CreateDirectory(Path.GetDirectoryName(apkPath));
            }

            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
            buildPlayerOptions.scenes = scenes;
            buildPlayerOptions.locationPathName = apkPath;
            buildPlayerOptions.target = BuildTarget.Android;
            buildPlayerOptions.options = BuildOptions.None;

            Debug.Log($"[Build] Iniciando compilação do APK para: {apkPath}...");
            BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[Build OK] APK gerado com sucesso em: {apkPath} ({summary.totalSize / (1024 * 1024)} MB)");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }
            else
            {
                Debug.LogError($"[Build ERRO] Falha ao compilar APK: {summary.totalErrors} erros. Status: {summary.result}");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
            }
        }
    }
}
#endif
