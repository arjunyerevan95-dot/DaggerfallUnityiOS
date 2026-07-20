// Project: Daggerfall Unity iOS
// Purpose: Reproducible unsigned iOS Xcode export for the bootstrap milestone.

#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace DaggerfallUnityIOS.Editor
{
    public static class IOSBuild
    {
        private const string DefaultExportPath = "Build/iOS";
        private const string BundleIdentifier = "com.arjukstudios.daggerfallunityios";
        private const string MinimumIOSVersion = "15.0";

        [MenuItem("Daggerfall Unity/Build/Export iOS Xcode Project")]
        public static void BuildFromMenu()
        {
            ExportIOSProject();
        }

        public static void BuildFromCommandLine()
        {
            try
            {
                ExportIOSProject();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void ExportIOSProject()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Unable to resolve the Unity project root.");

            string configuredExportPath = Environment.GetEnvironmentVariable("IOS_EXPORT_PATH");
            string exportPath = string.IsNullOrWhiteSpace(configuredExportPath)
                ? Path.Combine(projectRoot, DefaultExportPath)
                : Path.GetFullPath(configuredExportPath);

            ConfigureIOSPlayerSettings();

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS))
            {
                throw new InvalidOperationException("Unity could not switch to the iOS build target.");
            }

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled scenes are configured in Editor Build Settings.");
            }

            if (Directory.Exists(exportPath))
            {
                Directory.Delete(exportPath, recursive: true);
            }

            Directory.CreateDirectory(exportPath);

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = exportPath,
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options = BuildOptions.None,
            };

            Debug.Log($"Exporting unsigned iOS Xcode project to: {exportPath}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log(
                $"iOS export result={summary.result}, " +
                $"errors={summary.totalErrors}, warnings={summary.totalWarnings}, " +
                $"duration={summary.totalTime}");

            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"iOS export failed with {summary.totalErrors} error(s). Review the Unity build log.");
            }
        }

        private static void ConfigureIOSPlayerSettings()
        {
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, BundleIdentifier);
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.iOS, ScriptingImplementation.IL2CPP);

            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
            PlayerSettings.iOS.targetOSVersionString = MinimumIOSVersion;
            PlayerSettings.iOS.requiresPersistentWiFi = false;
            PlayerSettings.statusBarHidden = true;

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            PlayerSettings.MTRendering = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.iOS, false);
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.iOS,
                new[] { GraphicsDeviceType.Metal });

            EditorUserBuildSettings.iOSXcodeBuildConfig = XcodeBuildConfig.Release;
        }
    }
}
#endif
