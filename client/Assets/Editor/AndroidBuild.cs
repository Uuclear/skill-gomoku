#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Gomoku.EditorTools
{
    /// <summary>
    /// Applies the agreed Android configuration and produces the installable APK.
    /// minSdk 24 / targetSdk 34, portrait, single Boot scene. Settings are also
    /// enforced on every editor load so a fresh checkout compiles straight into
    /// the correct build without manual player-setting edits.
    /// </summary>
    public static class AndroidBuild
    {
        const string BootScenePath = "Assets/Scenes/Boot.unity";
        const string OutputPath = "Builds/gomoku.apk";

        [InitializeOnLoadMethod]
        static void EnforceSettings()
        {
            EditorApplication.delayCall += () =>
            {
                PlayerSettings.companyName = "Gomoku";
                PlayerSettings.productName = "技能五子棋";
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
                PlayerSettings.allowedAutorotateToPortrait = true;
                PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
                PlayerSettings.allowedAutorotateToLandscapeLeft = false;
                PlayerSettings.allowedAutorotateToLandscapeRight = false;

                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
                PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)34;
                PlayerSettings.Android.androidIsGame = true;
                PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
                // NativeWebSocket talks over raw .NET sockets, which Unity's
                // "Auto" internet-access detection misses under IL2CPP — the
                // APK then ships without android.permission.INTERNET and every
                // connect fails with EPERM. Force the permission explicitly.
                PlayerSettings.Android.forceInternetPermission = true;

                EditorSceneManager.playModeStartScene =
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(BootScenePath);
            };
        }

        [MenuItem("Build/Android APK")]
        public static void BuildApk()
        {
            Directory.CreateDirectory("Builds");
            EnsureBootSceneInBuildSettings();

            // IL2CPP fat APK: ARM64 for real phones, x86_64 for the official
            // Android emulator (the AVD ships an x86_64 system image).
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures =
                AndroidArchitecture.ARM64 | AndroidArchitecture.X86_64;
            PlayerSettings.Android.forceInternetPermission = true;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { BootScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
                Debug.Log($"APK built: {Path.GetFullPath(OutputPath)} ({summary.totalSize / 1024 / 1024} MB)");
            else
                throw new UnityEditor.Build.BuildFailedException(
                    $"APK build failed: {summary.result}, {summary.totalErrors} errors");
        }

        static void EnsureBootSceneInBuildSettings()
        {
            var scenes = new[] { new EditorBuildSettingsScene(BootScenePath, true) };
            EditorBuildSettings.scenes = scenes;
        }
    }
}
#endif
