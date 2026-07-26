// Project: Daggerfall Unity iOS
// Purpose: Reproducible unsigned iOS Xcode export for the bootstrap milestone.

#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;

namespace DaggerfallUnityIOS.Editor
{
    public static class IOSBuild
    {
        private const string DefaultExportPath = "Build/iOS";
        private const string BundleIdentifier = "com.arjukstudios.daggerfallunityios";
        private const string MinimumIOSVersion = "15.0";

        [Serializable]
        private sealed class AddressablesRuntimeSettings
        {
            public string m_buildTarget;
        }

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

        public static void BuildIOSAddressablesFromCommandLine()
        {
            try
            {
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
                {
                    throw new InvalidOperationException(
                        "Addressables must be built by an Editor started with '-buildTarget iOS'. " +
                        $"Active target: {EditorUserBuildSettings.activeBuildTarget}");
                }

                AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
                if (settings == null)
                    throw new InvalidOperationException("Addressables settings could not be loaded.");

                AddressableAssetSettings.BuildPlayerContent(
                    out AddressablesPlayerBuildResult result);
                if (result == null || !string.IsNullOrEmpty(result.Error))
                {
                    throw new InvalidOperationException(
                        "iOS Addressables content build failed: " +
                        (result?.Error ?? "<no build result>"));
                }

                string evidence = ValidateIOSAddressablesRoot(
                    Addressables.BuildPath,
                    "prepared iOS Addressables build",
                    out _);
                string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                    ?? throw new InvalidOperationException("Unable to resolve the Unity project root.");
                string evidenceDirectory =
                    Path.Combine(projectRoot, "Build", "uba-ios-bootstrap");
                Directory.CreateDirectory(evidenceDirectory);
                File.WriteAllText(
                    Path.Combine(
                        evidenceDirectory,
                        "ios-addressables-prebuild-succeeded.txt"),
                    evidence + Environment.NewLine);
                Debug.Log("Prepared platform-correct iOS Addressables content.");
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
            ConfigureAddressablesForPlayerBuild(projectRoot);

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

            string addressablesEvidence = ValidateExportedAddressables(
                exportPath,
                out string exportedAddressablesFingerprint);
            ValidatePreparedAddressablesFingerprint(
                projectRoot,
                exportedAddressablesFingerprint);

            string evidenceDirectory = Path.Combine(projectRoot, "Build", "uba-ios-bootstrap");
            Directory.CreateDirectory(evidenceDirectory);
            File.WriteAllText(
                Path.Combine(evidenceDirectory, "ios-export-succeeded.txt"),
                $"Unity {Application.unityVersion} exported {xcodeProject}{Environment.NewLine}");
            File.WriteAllText(
                Path.Combine(evidenceDirectory, "ios-addressables-succeeded.txt"),
                addressablesEvidence + Environment.NewLine);
        }

        private static void ConfigureAddressablesForPlayerBuild(string projectRoot)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                throw new InvalidOperationException("Addressables settings could not be loaded.");

            bool usePreparedIOSContent = string.Equals(
                Environment.GetEnvironmentVariable("IOS_PREBUILT_ADDRESSABLES"),
                "1",
                StringComparison.Ordinal);
            if (usePreparedIOSContent)
            {
                ValidateIOSAddressablesRoot(
                    Addressables.BuildPath,
                    "prepared iOS Addressables build before player export",
                    out string preparedAddressablesFingerprint);
                ValidatePreparedAddressablesFingerprint(
                    projectRoot,
                    preparedAddressablesFingerprint);
                settings.BuildAddressablesWithPlayerBuild =
                    AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
            }
            else
            {
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
                {
                    throw new InvalidOperationException(
                        "The active Editor target is not iOS and no validated prebuilt iOS " +
                        "Addressables content was provided. Start Unity with '-buildTarget iOS'.");
                }

                settings.BuildAddressablesWithPlayerBuild =
                    AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"Addressables configured for player build; " +
                $"mode={(usePreparedIOSContent ? "validated prebuilt iOS" : "build with iOS player")}; " +
                $"Active data builder index: {settings.ActivePlayerDataBuilderIndex}");
        }

        private static string ValidateExportedAddressables(
            string exportPath,
            out string fingerprint)
        {
            string addressablesRoot = Path.Combine(exportPath, "Data", "Raw", "aa");
            return ValidateIOSAddressablesRoot(
                addressablesRoot,
                "exported iOS player",
                out fingerprint);
        }

        private static string ValidateIOSAddressablesRoot(
            string addressablesRoot,
            string context,
            out string fingerprint)
        {
            string runtimeSettings = Path.Combine(addressablesRoot, "settings.json");

            if (!File.Exists(runtimeSettings) || new FileInfo(runtimeSettings).Length == 0)
            {
                throw new InvalidOperationException(
                    $"{context} is missing Addressables runtime settings: {runtimeSettings}");
            }

            AddressablesRuntimeSettings settings =
                JsonUtility.FromJson<AddressablesRuntimeSettings>(File.ReadAllText(runtimeSettings));
            string expectedBuildTarget = BuildTarget.iOS.ToString();
            if (settings == null || !string.Equals(
                    settings.m_buildTarget,
                    expectedBuildTarget,
                    StringComparison.Ordinal))
            {
                string actualBuildTarget = settings?.m_buildTarget ?? "<missing>";
                throw new InvalidOperationException(
                    $"{context} contains Addressables runtime data for '{actualBuildTarget}', " +
                    $"expected '{expectedBuildTarget}': {runtimeSettings}");
            }

            string[] catalogs = Directory.GetFiles(
                addressablesRoot,
                "catalog.json",
                SearchOption.AllDirectories);

            if (catalogs.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{context} is missing an Addressables content catalog under: " +
                    addressablesRoot);
            }

            foreach (string catalog in catalogs)
            {
                string catalogText = File.ReadAllText(catalog);
                if (catalogText.Contains("StandaloneOSX"))
                {
                    throw new InvalidOperationException(
                        $"{context} catalog still references StandaloneOSX content: {catalog}");
                }

                if (!catalogText.Contains("/iOS/"))
                {
                    throw new InvalidOperationException(
                        $"{context} catalog does not reference iOS content: {catalog}");
                }
            }

            string targetContentRoot = Path.Combine(addressablesRoot, expectedBuildTarget);
            string[] bundles = Directory.Exists(targetContentRoot)
                ? Directory.GetFiles(targetContentRoot, "*.bundle", SearchOption.AllDirectories)
                : Array.Empty<string>();
            if (bundles.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{context} is missing iOS Addressables bundles under: {targetContentRoot}");
            }

            Debug.Log(
                $"Validated {context}: {runtimeSettings}; " +
                $"target={settings.m_buildTarget}; catalogs={catalogs.Length}; bundles={bundles.Length}");

            string[] fingerprintFiles = new[] { runtimeSettings }
                .Concat(catalogs)
                .Concat(bundles)
                .ToArray();
            fingerprint = ComputeAddressablesFingerprint(
                addressablesRoot,
                fingerprintFiles);

            return
                $"Context: {context}{Environment.NewLine}" +
                $"Addressables root: {addressablesRoot}{Environment.NewLine}" +
                $"Runtime settings: {runtimeSettings}{Environment.NewLine}" +
                $"Build target: {settings.m_buildTarget}{Environment.NewLine}" +
                $"Catalog count: {catalogs.Length}{Environment.NewLine}" +
                $"iOS bundle count: {bundles.Length}{Environment.NewLine}" +
                $"Content fingerprint: {fingerprint}";
        }

        private static string ComputeAddressablesFingerprint(
            string addressablesRoot,
            string[] files)
        {
            string rootPrefix = addressablesRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            StringBuilder manifest = new StringBuilder();
            foreach (string file in files.OrderBy(path => path, StringComparer.Ordinal))
            {
                string relativePath = file.StartsWith(rootPrefix, StringComparison.Ordinal)
                    ? file.Substring(rootPrefix.Length)
                    : file;
                relativePath = relativePath.Replace('\\', '/');

                using (SHA256 fileHash = SHA256.Create())
                {
                    byte[] digest;
                    using (FileStream stream = File.OpenRead(file))
                        digest = fileHash.ComputeHash(stream);
                    manifest.Append(relativePath);
                    manifest.Append('=');
                    manifest.Append(ToHex(digest));
                    manifest.Append('\n');
                }
            }

            using (SHA256 manifestHash = SHA256.Create())
            {
                return ToHex(
                    manifestHash.ComputeHash(
                        Encoding.UTF8.GetBytes(manifest.ToString())));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void ValidatePreparedAddressablesFingerprint(
            string projectRoot,
            string actualFingerprint)
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable("IOS_PREBUILT_ADDRESSABLES"),
                    "1",
                    StringComparison.Ordinal))
            {
                return;
            }

            string markerPath = Path.Combine(
                projectRoot,
                "Build",
                "uba-ios-bootstrap",
                "ios-addressables-prebuild-succeeded.txt");
            if (!File.Exists(markerPath))
            {
                throw new InvalidOperationException(
                    "Missing prepared Addressables evidence marker: " + markerPath);
            }

            const string prefix = "Content fingerprint: ";
            string fingerprintLine = File.ReadLines(markerPath)
                .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
            string expectedFingerprint = fingerprintLine?.Substring(prefix.Length);
            if (string.IsNullOrWhiteSpace(expectedFingerprint) ||
                !string.Equals(
                    expectedFingerprint,
                    actualFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Prepared and consumed Addressables fingerprints differ. " +
                    $"Expected '{expectedFingerprint ?? "<missing>"}', " +
                    $"actual '{actualFingerprint}'. Evidence: {markerPath}");
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

            string buildNumber =
                Environment.GetEnvironmentVariable("BUILD_NUMBER") ??
                Environment.GetEnvironmentVariable("CLOUD_BUILD_NUMBER") ??
                Environment.GetEnvironmentVariable("UNITY_CLOUD_BUILD_NUMBER") ??
                Environment.GetEnvironmentVariable("BUILD_ID");
            if (!string.IsNullOrWhiteSpace(buildNumber))
            {
                PlayerSettings.iOS.buildNumber = buildNumber;
                Debug.Log("Configured iOS CFBundleVersion from Unity build number: " + buildNumber);
            }

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
