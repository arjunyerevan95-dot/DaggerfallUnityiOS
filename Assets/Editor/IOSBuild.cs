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

#if UNITY_CLOUD_BUILD
        // Configured in Unity Build Automation as the Pre-Export Method.
        // This runs after script compilation and before the macOS carrier export.
        public static void BuildFromCloudPreExport()
        {
            Debug.Log("UBA pre-export hook: starting bounded unsigned iOS Xcode export.");
            ExportIOSProject();
            Debug.Log("UBA pre-export hook: unsigned iOS Xcode export completed.");
        }
#endif

        private static void ExportIOSProject()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Unable to resolve the Unity project root.");

            string configuredExportPath = Environment.GetEnvironmentVariable("IOS_EXPORT_PATH");
            string exportPath = string.IsNullOrWhiteSpace(configuredExportPath)
                ? Path.Combine(projectRoot, DefaultExportPath)
                : Path.GetFullPath(configuredExportPath);

            ConfigureIOSPlayerSettings();

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

            Debug.Log(
                $"Exporting unsigned iOS Xcode project to: {exportPath}. " +
                $"Current carrier target: {EditorUserBuildSettings.activeBuildTarget}");

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

            string xcodeProject = Path.Combine(exportPath, "Unity-iPhone.xcodeproj");
            if (!Directory.Exists(xcodeProject))
            {
                throw new InvalidOperationException(
                    $"Unity reported success but did not produce the expected Xcode project: {xcodeProject}");
            }

            string evidenceDirectory = Path.Combine(projectRoot, "Build", "uba-ios-bootstrap");
            Directory.CreateDirectory(evidenceDirectory);
            File.WriteAllText(
                Path.Combine(evidenceDirectory, "ios-export-succeeded.txt"),
                $"Unity {Application.unityVersion} exported {xcodeProject}{Environment.NewLine}");
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
