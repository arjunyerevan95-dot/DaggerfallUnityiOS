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
        private const int AddressablesHandoffSchema = 1;
        private const string AddressablesHandoffFileName = "addressables-handoff.json";
        private const string LegacyAddressablesMarkerFileName =
            "ios-addressables-prebuild-succeeded.txt";

        [Serializable]
        private sealed class AddressablesRuntimeSettings
        {
            public string m_buildTarget;
        }

        [Serializable]
        private sealed class AddressablesHandoffContract
        {
            public int schema;
            public string status;
            public string platform;
            public string sourceCommit;
            public string unityVersion;
            public string profileId;
            public string profileName;
            public string buildPath;
            public string settingsPath;
            public string[] catalogPaths;
            public int catalogCount;
            public int bundleCount;
            public string fingerprint;
        }

        private sealed class AddressablesValidationResult
        {
            public string RootPath;
            public string SettingsPath;
            public string[] CatalogPaths;
            public string[] BundlePaths;
            public string Fingerprint;
        }

        [MenuItem("Daggerfall Unity/Build/Export iOS Xcode Project")]
        public static void BuildFromMenu()
        {
            ExportIOSProject(requirePreparedAddressablesHandoff: false);
        }

        public static void BuildFromCommandLine()
        {
            try
            {
                ExportIOSProject(requirePreparedAddressablesHandoff: false);
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
                string projectRoot = GetProjectRoot();
                DeleteStaleAddressablesHandoff(projectRoot);

                bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.iOS,
                    BuildTarget.iOS);
                if (!switched)
                {
                    throw new InvalidOperationException(
                        "Addressables handoff preparation could not switch the active build " +
                        "target to iOS.");
                }

                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
                {
                    throw new InvalidOperationException(
                        "Addressables handoff preparation switched build targets but the active " +
                        $"target is '{EditorUserBuildSettings.activeBuildTarget}', expected 'iOS'.");
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

                string expectedBuildPath = GetExpectedIOSAddressablesBuildPath(projectRoot);
                string actualBuildPath = NormalizeAbsolutePath(
                    projectRoot,
                    Addressables.BuildPath);
                if (!PathsEqual(expectedBuildPath, actualBuildPath))
                {
                    throw new InvalidOperationException(
                        "Addressables 1.22.3 reported an unexpected iOS build path. " +
                        $"Expected '{ToProjectRelativePath(projectRoot, expectedBuildPath)}', " +
                        $"actual '{ToProjectRelativePath(projectRoot, actualBuildPath)}'.");
                }

                AddressablesValidationResult validation = ValidateIOSAddressablesRoot(
                    expectedBuildPath,
                    "prepared iOS Addressables build",
                    projectRoot);
                GetActiveAddressablesProfile(
                    settings,
                    out string profileId,
                    out string profileName);

                AddressablesHandoffContract handoff = new AddressablesHandoffContract
                {
                    schema = AddressablesHandoffSchema,
                    status = "success",
                    platform = BuildTarget.iOS.ToString(),
                    sourceCommit = ResolveSourceCommit(projectRoot),
                    unityVersion = Application.unityVersion,
                    profileId = profileId,
                    profileName = profileName,
                    buildPath = ToProjectRelativePath(projectRoot, validation.RootPath),
                    settingsPath = ToProjectRelativePath(projectRoot, validation.SettingsPath),
                    catalogPaths = validation.CatalogPaths
                        .Select(path => ToProjectRelativePath(projectRoot, path))
                        .OrderBy(path => path, StringComparer.Ordinal)
                        .ToArray(),
                    catalogCount = validation.CatalogPaths.Length,
                    bundleCount = validation.BundlePaths.Length,
                    fingerprint = validation.Fingerprint,
                };

                WriteAddressablesHandoffAtomically(projectRoot, handoff);
                Debug.Log(
                    "Prepared validated iOS Addressables handoff: " +
                    $"{GetAddressablesHandoffRelativePath()}; " +
                    $"commit={handoff.sourceCommit}; unity={handoff.unityVersion}; " +
                    $"profile={handoff.profileName} ({handoff.profileId}); " +
                    $"catalogs={handoff.catalogCount}; bundles={handoff.bundleCount}; " +
                    $"fingerprint={handoff.fingerprint}");
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
            ExportIOSProject(requirePreparedAddressablesHandoff: true);
            Debug.Log("UBA pre-export hook: unsigned iOS Xcode export completed.");
        }
#endif

        private static void ExportIOSProject(bool requirePreparedAddressablesHandoff)
        {
            string projectRoot = GetProjectRoot();

            string configuredExportPath = Environment.GetEnvironmentVariable("IOS_EXPORT_PATH");
            string exportPath = string.IsNullOrWhiteSpace(configuredExportPath)
                ? Path.Combine(projectRoot, DefaultExportPath)
                : Path.GetFullPath(configuredExportPath);

            ConfigureIOSPlayerSettings();
            AddressablesHandoffContract preparedHandoff =
                ConfigureAddressablesForPlayerBuild(
                    projectRoot,
                    requirePreparedAddressablesHandoff);

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

            AddressablesValidationResult exportedAddressables =
                ValidateExportedAddressables(exportPath, projectRoot);
            if (preparedHandoff != null &&
                !string.Equals(
                    preparedHandoff.fingerprint,
                    exportedAddressables.Fingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Prepared and exported Addressables fingerprint mismatch. " +
                    $"Prepared '{preparedHandoff.fingerprint}', " +
                    $"exported '{exportedAddressables.Fingerprint}'. " +
                    $"Handoff: {GetAddressablesHandoffRelativePath()}");
            }

            string evidenceDirectory = Path.Combine(projectRoot, "Build", "uba-ios-bootstrap");
            Directory.CreateDirectory(evidenceDirectory);
            File.WriteAllText(
                Path.Combine(evidenceDirectory, "ios-export-succeeded.txt"),
                $"Unity {Application.unityVersion} exported " +
                $"{ToProjectRelativePath(projectRoot, xcodeProject)}{Environment.NewLine}");
            File.WriteAllText(
                Path.Combine(evidenceDirectory, "ios-addressables-succeeded.txt"),
                FormatAddressablesEvidence(
                    projectRoot,
                    exportedAddressables,
                    "exported iOS player",
                    preparedHandoff) + Environment.NewLine);
        }

        private static AddressablesHandoffContract ConfigureAddressablesForPlayerBuild(
            string projectRoot,
            bool requirePreparedAddressablesHandoff)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                throw new InvalidOperationException("Addressables settings could not be loaded.");

            AddressablesHandoffContract preparedHandoff = null;
            if (requirePreparedAddressablesHandoff)
            {
                preparedHandoff = ReadAndValidateAddressablesHandoff(projectRoot, settings);
                settings.BuildAddressablesWithPlayerBuild =
                    AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
            }
            else
            {
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
                {
                    throw new InvalidOperationException(
                        "A local iOS export requires the active Editor target to be iOS. " +
                        $"Active target: {EditorUserBuildSettings.activeBuildTarget}");
                }

                settings.BuildAddressablesWithPlayerBuild =
                    AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            Debug.Log(
                "Addressables configured for player build; " +
                $"mode={(preparedHandoff != null ? "validated file handoff" : "build with iOS player")}; " +
                $"active data builder index={settings.ActivePlayerDataBuilderIndex}");
            return preparedHandoff;
        }

        private static AddressablesHandoffContract ReadAndValidateAddressablesHandoff(
            string projectRoot,
            AddressableAssetSettings settings)
        {
            string handoffPath = GetAddressablesHandoffPath(projectRoot);
            if (!File.Exists(handoffPath))
            {
                throw new InvalidOperationException(
                    "Addressables handoff gate 1 failed: file is missing: " +
                    GetAddressablesHandoffRelativePath());
            }

            if (new FileInfo(handoffPath).Length == 0)
            {
                throw new InvalidOperationException(
                    "Addressables handoff gate 1 failed: file is empty: " +
                    GetAddressablesHandoffRelativePath());
            }

            string json;
            try
            {
                json = File.ReadAllText(handoffPath);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Addressables handoff gate 2 failed: file could not be read: " +
                    GetAddressablesHandoffRelativePath(),
                    exception);
            }

            AddressablesHandoffContract handoff;
            try
            {
                handoff = JsonUtility.FromJson<AddressablesHandoffContract>(json);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Addressables handoff gate 2 failed: JSON is invalid: " +
                    GetAddressablesHandoffRelativePath(),
                    exception);
            }

            if (handoff == null)
            {
                throw new InvalidOperationException(
                    "Addressables handoff gate 2 failed: JSON did not produce a contract: " +
                    GetAddressablesHandoffRelativePath());
            }

            if (handoff.schema != AddressablesHandoffSchema)
            {
                throw new InvalidOperationException(
                    $"Addressables handoff gate 3 failed: schema is '{handoff.schema}', " +
                    $"expected '{AddressablesHandoffSchema}'.");
            }

            if (!string.Equals(handoff.status, "success", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Addressables handoff gate 4 failed: status is " +
                    $"'{handoff.status ?? "<missing>"}', expected 'success'.");
            }

            string expectedPlatform = BuildTarget.iOS.ToString();
            if (!string.Equals(handoff.platform, expectedPlatform, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Addressables handoff gate 5 failed: platform is " +
                    $"'{handoff.platform ?? "<missing>"}', expected '{expectedPlatform}'.");
            }

            if (!IsFullHexDigest(handoff.sourceCommit, 40))
            {
                throw new InvalidOperationException(
                    "Addressables handoff gate 6 failed: sourceCommit is not a full " +
                    $"40-character SHA: '{handoff.sourceCommit ?? "<missing>"}'.");
            }

            string carrierCommit = ResolveSourceCommit(projectRoot);
            if (!string.Equals(
                    handoff.sourceCommit,
                    carrierCommit,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Addressables handoff gate 6 failed: source commit mismatch. " +
                    $"Handoff '{handoff.sourceCommit}', carrier '{carrierCommit}'.");
            }

            if (!string.Equals(
                    handoff.unityVersion,
                    Application.unityVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Addressables handoff gate 7 failed: Unity version mismatch. " +
                    $"Handoff '{handoff.unityVersion ?? "<missing>"}', " +
                    $"carrier '{Application.unityVersion}'.");
            }

            GetActiveAddressablesProfile(
                settings,
                out string activeProfileId,
                out string activeProfileName);
            if (!string.Equals(
                    handoff.profileId,
                    activeProfileId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    handoff.profileName,
                    activeProfileName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Addressables handoff gate 8 failed: active profile mismatch. " +
                    $"Handoff '{handoff.profileName ?? "<missing>"}' " +
                    $"({handoff.profileId ?? "<missing>"}), carrier " +
                    $"'{activeProfileName}' ({activeProfileId}).");
            }

            string expectedBuildPath = GetExpectedIOSAddressablesBuildPath(projectRoot);
            string recordedBuildPath = ResolveRecordedProjectPath(
                projectRoot,
                expectedBuildPath,
                handoff.buildPath,
                "buildPath",
                allowRoot: true);
            if (!PathsEqual(recordedBuildPath, expectedBuildPath))
            {
                throw new InvalidOperationException(
                    "Addressables handoff gate 9 failed: buildPath does not identify the " +
                    $"expected iOS Addressables root. Handoff '{handoff.buildPath}', expected " +
                    $"'{ToProjectRelativePath(projectRoot, expectedBuildPath)}'.");
            }

            string recordedSettingsPath = ResolveRecordedProjectPath(
                projectRoot,
                expectedBuildPath,
                handoff.settingsPath,
                "settingsPath",
                allowRoot: false);

            if (handoff.catalogPaths == null || handoff.catalogPaths.Length == 0)
            {
                throw new InvalidOperationException(
                    "Addressables handoff gate 10 failed: catalogPaths is missing or empty.");
            }

            string[] recordedCatalogPaths = handoff.catalogPaths
                .Select(path => ResolveRecordedProjectPath(
                    projectRoot,
                    expectedBuildPath,
                    path,
                    "catalogPaths",
                    allowRoot: false))
                .ToArray();
            string[] canonicalRecordedCatalogPaths = recordedCatalogPaths
                .Select(path => ToProjectRelativePath(projectRoot, path))
                .ToArray();
            string[] sortedUniqueCatalogPaths = canonicalRecordedCatalogPaths
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (!canonicalRecordedCatalogPaths.SequenceEqual(
                    sortedUniqueCatalogPaths,
                    StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "Addressables handoff gate 10 failed: catalogPaths must be normalized, " +
                    "unique, and sorted with ordinal ordering.");
            }

            AddressablesValidationResult validation = ValidateIOSAddressablesRoot(
                expectedBuildPath,
                "prepared iOS Addressables build before player export",
                projectRoot);
            if (!PathsEqual(recordedSettingsPath, validation.SettingsPath))
            {
                throw new InvalidOperationException(
                    "Addressables handoff gate 11 failed: settingsPath mismatch. " +
                    $"Handoff '{handoff.settingsPath}', actual " +
                    $"'{ToProjectRelativePath(projectRoot, validation.SettingsPath)}'.");
            }

            string[] actualCatalogPaths = validation.CatalogPaths
                .Select(path => ToProjectRelativePath(projectRoot, path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (!canonicalRecordedCatalogPaths.SequenceEqual(
                    actualCatalogPaths,
                    StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "Addressables handoff gate 12 failed: recorded catalog paths do not " +
                    "match the prepared iOS content. Handoff [" +
                    string.Join(", ", canonicalRecordedCatalogPaths) + "], actual [" +
                    string.Join(", ", actualCatalogPaths) + "].");
            }

            if (handoff.catalogCount != validation.CatalogPaths.Length ||
                handoff.bundleCount != validation.BundlePaths.Length)
            {
                throw new InvalidOperationException(
                    "Addressables handoff gate 13 failed: content counts changed. " +
                    $"Handoff catalogs={handoff.catalogCount}, bundles={handoff.bundleCount}; " +
                    $"actual catalogs={validation.CatalogPaths.Length}, " +
                    $"bundles={validation.BundlePaths.Length}.");
            }

            if (!IsFullHexDigest(handoff.fingerprint, 64) ||
                !string.Equals(
                    handoff.fingerprint,
                    validation.Fingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Addressables handoff gate 14 failed: fingerprint mismatch. " +
                    $"Handoff '{handoff.fingerprint ?? "<missing>"}', " +
                    $"actual '{validation.Fingerprint}'.");
            }

            Debug.Log(
                "Validated Addressables file handoff before iOS export: " +
                $"commit={carrierCommit}; unity={Application.unityVersion}; " +
                $"profile={activeProfileName} ({activeProfileId}); " +
                $"buildPath={handoff.buildPath}; catalogs={handoff.catalogCount}; " +
                $"bundles={handoff.bundleCount}; fingerprint={handoff.fingerprint}; " +
                $"carrierTarget={EditorUserBuildSettings.activeBuildTarget}");
            return handoff;
        }

        private static AddressablesValidationResult ValidateExportedAddressables(
            string exportPath,
            string projectRoot)
        {
            string addressablesRoot = Path.Combine(exportPath, "Data", "Raw", "aa");
            return ValidateIOSAddressablesRoot(
                addressablesRoot,
                "exported iOS player",
                projectRoot);
        }

        private static AddressablesValidationResult ValidateIOSAddressablesRoot(
            string addressablesRoot,
            string context,
            string projectRoot)
        {
            string normalizedRoot = NormalizeAbsolutePath(projectRoot, addressablesRoot);
            string runtimeSettings = Path.Combine(normalizedRoot, "settings.json");
            if (!File.Exists(runtimeSettings) || new FileInfo(runtimeSettings).Length == 0)
            {
                throw new InvalidOperationException(
                    $"{context} is missing non-empty Addressables runtime settings: " +
                    ToProjectRelativePath(projectRoot, runtimeSettings));
            }

            AddressablesRuntimeSettings settings;
            try
            {
                settings = JsonUtility.FromJson<AddressablesRuntimeSettings>(
                    File.ReadAllText(runtimeSettings));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"{context} has invalid Addressables runtime settings JSON: " +
                    ToProjectRelativePath(projectRoot, runtimeSettings),
                    exception);
            }

            string expectedBuildTarget = BuildTarget.iOS.ToString();
            if (settings == null || !string.Equals(
                    settings.m_buildTarget,
                    expectedBuildTarget,
                    StringComparison.Ordinal))
            {
                string actualBuildTarget = settings?.m_buildTarget ?? "<missing>";
                throw new InvalidOperationException(
                    $"{context} contains Addressables runtime data for '{actualBuildTarget}', " +
                    $"expected '{expectedBuildTarget}': " +
                    ToProjectRelativePath(projectRoot, runtimeSettings));
            }

            string[] catalogs = Directory.GetFiles(
                    normalizedRoot,
                    "catalog*.json",
                    SearchOption.AllDirectories)
                .Select(path => NormalizeAbsolutePath(projectRoot, path))
                .OrderBy(
                    path => ToAddressablesRelativePath(normalizedRoot, path),
                    StringComparer.Ordinal)
                .ToArray();
            if (catalogs.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{context} is missing an Addressables content catalog under: " +
                    ToProjectRelativePath(projectRoot, normalizedRoot));
            }

            foreach (string catalog in catalogs)
            {
                if (new FileInfo(catalog).Length == 0)
                {
                    throw new InvalidOperationException(
                        $"{context} contains an empty Addressables catalog: " +
                        ToProjectRelativePath(projectRoot, catalog));
                }

                string catalogText = File.ReadAllText(catalog);
                if (catalogText.IndexOf("StandaloneOSX", StringComparison.Ordinal) >= 0)
                {
                    throw new InvalidOperationException(
                        $"{context} catalog still references StandaloneOSX content: " +
                        ToProjectRelativePath(projectRoot, catalog));
                }

                string normalizedCatalogText = catalogText.Replace('\\', '/');
                if (normalizedCatalogText.IndexOf("/iOS/", StringComparison.Ordinal) < 0 &&
                    normalizedCatalogText.IndexOf("\"iOS/", StringComparison.Ordinal) < 0)
                {
                    throw new InvalidOperationException(
                        $"{context} catalog does not reference iOS content: " +
                        ToProjectRelativePath(projectRoot, catalog));
                }
            }

            string targetContentRoot = Path.Combine(normalizedRoot, expectedBuildTarget);
            string[] bundles = Directory.Exists(targetContentRoot)
                ? Directory.GetFiles(targetContentRoot, "*.bundle", SearchOption.AllDirectories)
                    .Select(path => NormalizeAbsolutePath(projectRoot, path))
                    .OrderBy(
                        path => ToAddressablesRelativePath(normalizedRoot, path),
                        StringComparer.Ordinal)
                    .ToArray()
                : Array.Empty<string>();
            if (bundles.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{context} is missing iOS Addressables bundles under: " +
                    ToProjectRelativePath(projectRoot, targetContentRoot));
            }

            string[] fingerprintFiles = new[] { runtimeSettings }
                .Concat(catalogs)
                .Concat(bundles)
                .ToArray();
            string fingerprint = ComputeAddressablesFingerprint(
                normalizedRoot,
                fingerprintFiles);

            Debug.Log(
                $"Validated {context}: " +
                $"settings={ToProjectRelativePath(projectRoot, runtimeSettings)}; " +
                $"target={settings.m_buildTarget}; catalogs={catalogs.Length}; " +
                $"bundles={bundles.Length}; fingerprint={fingerprint}");

            return new AddressablesValidationResult
            {
                RootPath = normalizedRoot,
                SettingsPath = runtimeSettings,
                CatalogPaths = catalogs,
                BundlePaths = bundles,
                Fingerprint = fingerprint,
            };
        }

        private static string FormatAddressablesEvidence(
            string projectRoot,
            AddressablesValidationResult validation,
            string context,
            AddressablesHandoffContract preparedHandoff)
        {
            StringBuilder evidence = new StringBuilder();
            evidence.AppendLine($"Context: {context}");
            evidence.AppendLine(
                "Addressables root: " +
                ToProjectRelativePath(projectRoot, validation.RootPath));
            evidence.AppendLine(
                "Runtime settings: " +
                ToProjectRelativePath(projectRoot, validation.SettingsPath));
            evidence.AppendLine(
                "Runtime settings SHA-256: " +
                ComputeFileSha256(validation.SettingsPath));
            evidence.AppendLine($"Build target: {BuildTarget.iOS}");
            evidence.AppendLine($"Catalog count: {validation.CatalogPaths.Length}");
            foreach (string catalog in validation.CatalogPaths)
            {
                evidence.AppendLine(
                    "Catalog: " +
                    ToProjectRelativePath(projectRoot, catalog) +
                    " SHA-256: " +
                    ComputeFileSha256(catalog));
            }

            evidence.AppendLine($"iOS bundle count: {validation.BundlePaths.Length}");
            evidence.AppendLine($"Content fingerprint: {validation.Fingerprint}");
            if (preparedHandoff != null)
            {
                evidence.AppendLine(
                    "Prepared handoff: " + GetAddressablesHandoffRelativePath());
                evidence.AppendLine(
                    "Prepared handoff fingerprint: " + preparedHandoff.fingerprint);
            }

            return evidence.ToString().TrimEnd('\r', '\n');
        }

        private static string ComputeAddressablesFingerprint(
            string addressablesRoot,
            string[] files)
        {
            StringBuilder manifest = new StringBuilder();
            foreach (string file in files.OrderBy(
                         path => ToAddressablesRelativePath(addressablesRoot, path),
                         StringComparer.Ordinal))
            {
                string relativePath = ToAddressablesRelativePath(addressablesRoot, file);
                manifest.Append(relativePath);
                manifest.Append('=');
                manifest.Append(ComputeFileSha256(file));
                manifest.Append('\n');
            }

            using (SHA256 manifestHash = SHA256.Create())
            {
                return ToHex(
                    manifestHash.ComputeHash(
                        Encoding.UTF8.GetBytes(manifest.ToString())));
            }
        }

        private static string ComputeFileSha256(string path)
        {
            using (SHA256 fileHash = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return ToHex(fileHash.ComputeHash(stream));
        }

        private static string ToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void GetActiveAddressablesProfile(
            AddressableAssetSettings settings,
            out string profileId,
            out string profileName)
        {
            profileId = settings.activeProfileId;
            profileName = settings.profileSettings.GetProfileName(profileId);
            if (string.IsNullOrWhiteSpace(profileId) ||
                string.IsNullOrWhiteSpace(profileName))
            {
                throw new InvalidOperationException(
                    "Addressables active profile identity is incomplete. " +
                    $"ID='{profileId ?? "<missing>"}', name='{profileName ?? "<missing>"}'.");
            }
        }

        private static string GetExpectedIOSAddressablesBuildPath(string projectRoot)
        {
            return NormalizeAbsolutePath(
                projectRoot,
                Path.Combine(
                    Addressables.LibraryPath,
                    "aa",
                    BuildTarget.iOS.ToString()));
        }

        private static string GetProjectRoot()
        {
            return Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Unable to resolve the Unity project root.");
        }

        private static string GetAddressablesHandoffRelativePath()
        {
            return "Build/uba-ios-bootstrap/" + AddressablesHandoffFileName;
        }

        private static string GetAddressablesHandoffPath(string projectRoot)
        {
            return Path.Combine(
                projectRoot,
                "Build",
                "uba-ios-bootstrap",
                AddressablesHandoffFileName);
        }

        private static void DeleteStaleAddressablesHandoff(string projectRoot)
        {
            string handoffPath = GetAddressablesHandoffPath(projectRoot);
            string handoffDirectory = Path.GetDirectoryName(handoffPath)
                ?? throw new InvalidOperationException(
                    "Unable to resolve the Addressables handoff directory.");
            Directory.CreateDirectory(handoffDirectory);

            string[] stalePaths =
            {
                handoffPath,
                handoffPath + ".tmp",
                Path.Combine(handoffDirectory, LegacyAddressablesMarkerFileName),
            };
            foreach (string stalePath in stalePaths)
            {
                if (File.Exists(stalePath))
                    File.Delete(stalePath);
            }
        }

        private static void WriteAddressablesHandoffAtomically(
            string projectRoot,
            AddressablesHandoffContract handoff)
        {
            string handoffPath = GetAddressablesHandoffPath(projectRoot);
            string temporaryPath = handoffPath + ".tmp";
            if (File.Exists(handoffPath))
            {
                throw new InvalidOperationException(
                    "Refusing to overwrite an Addressables handoff that appeared after stale " +
                    $"state was cleared: {GetAddressablesHandoffRelativePath()}");
            }

            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);

            byte[] json = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                .GetBytes(JsonUtility.ToJson(handoff, prettyPrint: true) + Environment.NewLine);
            try
            {
                using (FileStream stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    stream.Write(json, 0, json.Length);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporaryPath, handoffPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private static string ResolveSourceCommit(string projectRoot)
        {
            string environmentCommit = Environment.GetEnvironmentVariable("GIT_COMMIT");
            if (!string.IsNullOrWhiteSpace(environmentCommit) &&
                !IsFullHexDigest(environmentCommit, 40))
            {
                throw new InvalidOperationException(
                    "GIT_COMMIT is present but is not a full 40-character SHA: " +
                    environmentCommit);
            }

            string gitCommit = TryResolveGitHead(projectRoot, out string gitError);
            if (!string.IsNullOrWhiteSpace(environmentCommit) &&
                !string.IsNullOrWhiteSpace(gitCommit) &&
                !string.Equals(
                    environmentCommit,
                    gitCommit,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Source identity disagreement between GIT_COMMIT and the checkout. " +
                    $"GIT_COMMIT='{environmentCommit}', git HEAD='{gitCommit}'.");
            }

            string resolved = !string.IsNullOrWhiteSpace(gitCommit)
                ? gitCommit
                : environmentCommit;
            if (!IsFullHexDigest(resolved, 40))
            {
                throw new InvalidOperationException(
                    "Unable to resolve a full source commit SHA from the checkout or " +
                    $"GIT_COMMIT. git evidence: {gitError ?? "<none>"}");
            }

            if (string.IsNullOrWhiteSpace(gitCommit))
            {
                Debug.LogWarning(
                    "Resolved source identity from GIT_COMMIT because git HEAD was " +
                    $"unavailable: {gitError ?? "<no detail>"}");
            }

            return resolved.ToLowerInvariant();
        }

        private static string TryResolveGitHead(string projectRoot, out string error)
        {
            error = null;
            try
            {
                System.Diagnostics.ProcessStartInfo startInfo =
                    new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = "rev-parse HEAD",
                        WorkingDirectory = projectRoot,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    };

                using (System.Diagnostics.Process process =
                       System.Diagnostics.Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        error = "git process did not start";
                        return null;
                    }

                    if (!process.WaitForExit(10000))
                    {
                        process.Kill();
                        error = "git rev-parse HEAD timed out";
                        return null;
                    }

                    string output = process.StandardOutput.ReadToEnd().Trim();
                    string standardError = process.StandardError.ReadToEnd().Trim();
                    if (process.ExitCode != 0)
                    {
                        error =
                            $"git rev-parse HEAD exited {process.ExitCode}: {standardError}";
                        return null;
                    }

                    if (!IsFullHexDigest(output, 40))
                    {
                        error = $"git rev-parse HEAD returned invalid output: '{output}'";
                        return null;
                    }

                    return output;
                }
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return null;
            }
        }

        private static bool IsFullHexDigest(string value, int expectedLength)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                value.Length == expectedLength &&
                value.All(Uri.IsHexDigit);
        }

        private static string ResolveRecordedProjectPath(
            string projectRoot,
            string expectedRoot,
            string recordedPath,
            string fieldName,
            bool allowRoot)
        {
            if (string.IsNullOrWhiteSpace(recordedPath))
            {
                throw new InvalidOperationException(
                    $"Addressables handoff path gate failed: {fieldName} is missing.");
            }

            if (!string.Equals(recordedPath, recordedPath.Trim(), StringComparison.Ordinal) ||
                recordedPath.IndexOf('\\') >= 0 ||
                Path.IsPathRooted(recordedPath) ||
                recordedPath.StartsWith("/", StringComparison.Ordinal) ||
                (recordedPath.Length >= 2 &&
                 char.IsLetter(recordedPath[0]) &&
                 recordedPath[1] == ':'))
            {
                throw new InvalidOperationException(
                    $"Addressables handoff path gate failed: {fieldName} must be a normalized " +
                    $"project-relative path: '{recordedPath}'.");
            }

            string[] segments = recordedPath.Split('/');
            if (segments.Any(
                    segment => string.IsNullOrEmpty(segment) ||
                    string.Equals(segment, ".", StringComparison.Ordinal) ||
                    string.Equals(segment, "..", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Addressables handoff path gate failed: {fieldName} contains an empty or " +
                    $"traversal segment: '{recordedPath}'.");
            }

            string resolved = NormalizeAbsolutePath(
                projectRoot,
                recordedPath.Replace('/', Path.DirectorySeparatorChar));
            bool isExpectedRoot = PathsEqual(resolved, expectedRoot);
            if ((!allowRoot && isExpectedRoot) ||
                (!isExpectedRoot && !IsPathUnderRoot(resolved, expectedRoot)))
            {
                throw new InvalidOperationException(
                    $"Addressables handoff path gate failed: {fieldName} escapes the expected " +
                    $"iOS Addressables root '{ToProjectRelativePath(projectRoot, expectedRoot)}': " +
                    $"'{recordedPath}'.");
            }

            string canonical = ToProjectRelativePath(projectRoot, resolved);
            if (!string.Equals(recordedPath, canonical, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Addressables handoff path gate failed: {fieldName} is not canonical. " +
                    $"Recorded '{recordedPath}', canonical '{canonical}'.");
            }

            return resolved;
        }

        private static string NormalizeAbsolutePath(string projectRoot, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("Cannot normalize an empty filesystem path.");

            string combined = Path.IsPathRooted(path)
                ? path
                : Path.Combine(projectRoot, path);
            string fullPath = Path.GetFullPath(combined);
            string filesystemRoot = Path.GetPathRoot(fullPath);
            while (fullPath.Length > filesystemRoot.Length &&
                   (fullPath.EndsWith(
                        Path.DirectorySeparatorChar.ToString(),
                        StringComparison.Ordinal) ||
                    fullPath.EndsWith(
                        Path.AltDirectorySeparatorChar.ToString(),
                        StringComparison.Ordinal)))
            {
                fullPath = fullPath.Substring(0, fullPath.Length - 1);
            }

            return fullPath;
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                NormalizeAbsolutePath(GetProjectRoot(), left),
                NormalizeAbsolutePath(GetProjectRoot(), right),
                GetPathComparison());
        }

        private static bool IsPathUnderRoot(string path, string root)
        {
            string normalizedPath = NormalizeAbsolutePath(GetProjectRoot(), path);
            string normalizedRoot = NormalizeAbsolutePath(GetProjectRoot(), root);
            string rootPrefix = normalizedRoot + Path.DirectorySeparatorChar;
            return normalizedPath.StartsWith(rootPrefix, GetPathComparison());
        }

        private static string ToProjectRelativePath(string projectRoot, string path)
        {
            string normalizedRoot = NormalizeAbsolutePath(projectRoot, projectRoot);
            string normalizedPath = NormalizeAbsolutePath(projectRoot, path);
            if (!IsPathUnderRoot(normalizedPath, normalizedRoot))
            {
                throw new InvalidOperationException(
                    $"Path is outside the Unity project root: {normalizedPath}");
            }

            string rootPrefix = normalizedRoot + Path.DirectorySeparatorChar;
            return normalizedPath.Substring(rootPrefix.Length).Replace('\\', '/');
        }

        private static string ToAddressablesRelativePath(
            string addressablesRoot,
            string path)
        {
            string normalizedRoot = NormalizeAbsolutePath(GetProjectRoot(), addressablesRoot);
            string normalizedPath = NormalizeAbsolutePath(GetProjectRoot(), path);
            if (!IsPathUnderRoot(normalizedPath, normalizedRoot))
            {
                throw new InvalidOperationException(
                    $"Addressables fingerprint path escapes its root: {normalizedPath}");
            }

            string rootPrefix = normalizedRoot + Path.DirectorySeparatorChar;
            return normalizedPath.Substring(rootPrefix.Length).Replace('\\', '/');
        }

        private static StringComparison GetPathComparison()
        {
            return Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
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
