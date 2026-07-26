// Project: Daggerfall Unity iOS
// Purpose: Earliest runtime build identity and subsystem initialization evidence.

#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace DaggerfallUnityIOS.Runtime
{
    internal static class IOSBuildIdentity
    {
        private const string LogFileName = "DaggerfallUnity-iOS.log";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void RecordRuntimeEvidence()
        {
            WriteDiagnostic(
                "Build identity: commit " + IOSBuildIdentityValues.SourceCommit +
                "; Unity build " + IOSBuildIdentityValues.BuildNumber);
            WriteDiagnostic("Unity version: " + Application.unityVersion);
            WriteDiagnostic("StreamingAssets path: " + Application.streamingAssetsPath);

            string rawPath = Path.Combine(Application.dataPath, "Raw");
            string addressablesRuntimePath = Addressables.RuntimePath;
            WriteDiagnostic("Data/Raw path: " + rawPath);
            WriteDiagnostic("Addressables runtime path: " + addressablesRuntimePath);
            RecordCatalogPaths(addressablesRuntimePath);

            WarmLocalizationGeneric();
            BeginAddressablesDiagnostics();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BeginLocalizationBeforeSceneLoad()
        {
            BeginLocalizationDiagnostics();
        }

        private static void RecordCatalogPaths(string addressablesRuntimePath)
        {
            try
            {
                if (!Directory.Exists(addressablesRuntimePath))
                {
                    WriteDiagnostic("Addressables runtime directory exists: false");
                    return;
                }

                WriteDiagnostic("Addressables runtime directory exists: true");
                string settingsPath = Path.Combine(addressablesRuntimePath, "settings.json");
                WriteDiagnostic(
                    "Addressables settings path: " + settingsPath +
                    "; exists=" + File.Exists(settingsPath));

                string[] catalogs = Directory.GetFiles(
                    addressablesRuntimePath,
                    "catalog.*",
                    SearchOption.AllDirectories);
                WriteDiagnostic("Addressables catalog file count: " + catalogs.Length);
                foreach (string catalog in catalogs)
                    WriteDiagnostic("Addressables catalog path: " + catalog);
            }
            catch (Exception exception)
            {
                WriteDiagnostic("Addressables path inspection failed: " + exception);
            }
        }

        private static void BeginAddressablesDiagnostics()
        {
            try
            {
                AsyncOperationHandle<IResourceLocator> operation = Addressables.InitializeAsync();
                WriteDiagnostic(
                    "Addressables initialization: requested; isDone=" + operation.IsDone);
                operation.Completed += OnAddressablesInitialized;
            }
            catch (Exception exception)
            {
                WriteDiagnostic("Addressables initialization threw synchronously: " + exception);
            }
        }

        private static void OnAddressablesInitialized(
            AsyncOperationHandle<IResourceLocator> operation)
        {
            WriteDiagnostic(
                "Addressables initialization: " + operation.Status +
                FormatOperationException(operation.OperationException));

            try
            {
                int locatorCount = 0;
                foreach (IResourceLocator locator in Addressables.ResourceLocators)
                {
                    locatorCount++;
                    ResourceLocatorInfo locatorInfo = Addressables.GetLocatorInfo(locator);
                    IResourceLocation catalogLocation = locatorInfo?.CatalogLocation;
                    WriteDiagnostic(
                        "Loaded catalog locator: " + locator.LocatorId +
                        "; catalog=" +
                        (catalogLocation == null ? "<runtime settings>" : catalogLocation.InternalId));
                }

                WriteDiagnostic("Loaded catalog locator count: " + locatorCount);
            }
            catch (Exception exception)
            {
                WriteDiagnostic("Addressables locator inspection failed: " + exception);
            }
        }

        private static void BeginLocalizationDiagnostics()
        {
            try
            {
                WriteDiagnostic("Localization settings present: " + LocalizationSettings.HasSettings);
                if (!LocalizationSettings.HasSettings)
                    return;

                AsyncOperationHandle<LocalizationSettings> operation =
                    LocalizationSettings.InitializationOperation;
                WriteDiagnostic(
                    "Localization initialization: requested; isDone=" + operation.IsDone);
                operation.Completed += OnLocalizationInitialized;
            }
            catch (Exception exception)
            {
                WriteDiagnostic("Localization initialization threw synchronously: " + exception);
            }
        }

        private static void OnLocalizationInitialized(
            AsyncOperationHandle<LocalizationSettings> operation)
        {
            WriteDiagnostic(
                "Localization initialization: " + operation.Status +
                FormatOperationException(operation.OperationException));

            if (operation.Status != AsyncOperationStatus.Succeeded)
                return;

            try
            {
                Locale selectedLocale = LocalizationSettings.SelectedLocale;
                WriteDiagnostic(
                    "Localization selected locale: " +
                    (selectedLocale == null ? "<null>" : selectedLocale.Identifier.ToString()));
                WriteDiagnostic(
                    "Localization available locale count: " +
                    LocalizationSettings.AvailableLocales.Locales.Count);
            }
            catch (Exception exception)
            {
                WriteDiagnostic("Localization state inspection failed: " + exception);
            }
        }

        private static string FormatOperationException(Exception exception)
        {
            return exception == null ? string.Empty : "; exception=" + exception;
        }

        private static void WarmLocalizationGeneric()
        {
            // Localization loads Locale assets by label and its missing-key path creates this
            // closed generic operation. A direct call makes that AOT instantiation observable
            // to IL2CPP; the generated player is still inspected for the resulting symbol.
            AsyncOperationHandle<IList<Locale>> operation =
                Addressables.ResourceManager.CreateCompletedOperation<IList<Locale>>(
                    new List<Locale>(),
                    string.Empty);
            Addressables.Release(operation);
        }

        private static void WriteDiagnostic(string message)
        {
            string prefixedMessage = "[DFU iOS] " + message;
            Debug.Log(prefixedMessage);

            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                string logPath = Path.Combine(Application.persistentDataPath, LogFileName);
                string line =
                    DateTime.UtcNow.ToString("O") + " [Log] " + prefixedMessage;
                File.AppendAllText(
                    logPath,
                    line + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[DFU iOS] Could not persist runtime evidence: " + exception.Message);
            }
        }
    }
}
#endif
