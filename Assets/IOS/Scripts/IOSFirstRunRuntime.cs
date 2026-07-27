// Project: Daggerfall Unity iOS
// Purpose: Visible first-run diagnostics and sandbox game-data import for iOS.

#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Utility;

namespace DaggerfallUnityIOS.Runtime
{
    public sealed class IOSFirstRunRuntime : MonoBehaviour
    {
        private const string DataDirectoryName = "Daggerfall";
        private const string LogFileName = "DaggerfallUnity-iOS.log";
        private const string ImportedMarkerName = "ios-data-import-succeeded.txt";
        private const int MaxRecentLogLines = 6;

        private enum RuntimeState
        {
            Booting,
            NeedsData,
            PickingFile,
            Importing,
            Ready,
            Error,
        }

        private static IOSFirstRunRuntime instance;

        private readonly object logSync = new object();
        private readonly Queue<string> recentLogLines = new Queue<string>();

        private StreamWriter logWriter;
        private string logPath;
        private RuntimeState state = RuntimeState.Booting;
        private string statusText = "Starting Daggerfall Unity...";
        private string lastError = string.Empty;
        private float importProgress;
        private bool showOverlay = true;
        private volatile bool capturedError;

        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle detailStyle;
        private GUIStyle buttonStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (instance != null)
                return;

            GameObject host = new GameObject("DaggerfallUnityIOSRuntime");
            instance = host.AddComponent<IOSFirstRunRuntime>();
            DontDestroyOnLoad(host);
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeLog();
            Application.logMessageReceivedThreaded += HandleUnityLog;
            SceneManager.sceneLoaded += HandleSceneLoaded;

            LogDiagnostic("Runtime diagnostics installed.");
            LogDiagnostic("Unity " + Application.unityVersion + ", OS " + SystemInfo.operatingSystem);
            LogDiagnostic("Persistent data: " + Application.persistentDataPath);
            LogDiagnostic("Temporary cache: " + Application.temporaryCachePath);

            StartCoroutine(EvaluateDataState());
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;

            Application.logMessageReceivedThreaded -= HandleUnityLog;
            SceneManager.sceneLoaded -= HandleSceneLoaded;

            lock (logSync)
            {
                if (logWriter != null)
                {
                    logWriter.Flush();
                    logWriter.Dispose();
                    logWriter = null;
                }
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                FlushLog();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            LogDiagnostic("Scene loaded: " + scene.name + " (" + scene.buildIndex + ")");
        }

        private IEnumerator EvaluateDataState()
        {
            state = RuntimeState.Booting;
            statusText = "Checking Daggerfall game data...";
            showOverlay = true;

            float deadline = Time.realtimeSinceStartup + 15f;
            while (!DaggerfallUnity.HasInstance && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (!DaggerfallUnity.HasInstance)
            {
                Fail("Daggerfall Unity did not initialize its startup scene.", null);
                yield break;
            }

            string arena2Path;
            if (!TryResolveExistingArena2(out arena2Path))
            {
                state = RuntimeState.NeedsData;
                statusText = "Daggerfall game data is not installed. Select a ZIP containing an arena2 folder.";
                importProgress = 0f;
                showOverlay = true;
                LogDiagnostic("No valid Daggerfall game-data path was found.");
                yield break;
            }

            state = RuntimeState.Ready;
            statusText = "Game data found. Opening the Daggerfall Unity main menu...";
            importProgress = 1f;
            LogDiagnostic("Validated existing arena2 path: " + arena2Path);

            DirectoryInfo parent = Directory.GetParent(arena2Path);
            string rootPath = parent != null ? parent.FullName : Paths.PersistentDataPath;
            string activationError;
            if (!TryActivatePath(rootPath, arena2Path, out activationError))
            {
                Fail("Existing Daggerfall data could not be activated. " + activationError, null);
                yield break;
            }

            capturedError = false;
            yield return new WaitForSecondsRealtime(1f);
            showOverlay = false;
            LoadGameScene();
        }

        private bool TryResolveExistingArena2(out string arena2Path)
        {
            arena2Path = string.Empty;
            List<string> candidates = new List<string>();

            try
            {
                if (!string.IsNullOrEmpty(DaggerfallUnity.Settings.MyDaggerfallPath))
                    candidates.Add(DaggerfallUnity.Settings.MyDaggerfallPath);
            }
            catch (Exception ex)
            {
                LogDiagnostic("Could not read configured data path: " + ex.Message);
            }

            string sandboxDataPath = Path.Combine(Paths.PersistentDataPath, DataDirectoryName);
            if (!candidates.Contains(sandboxDataPath))
                candidates.Add(sandboxDataPath);

            foreach (string candidate in candidates)
            {
                try
                {
                    if (string.IsNullOrEmpty(candidate) || !Directory.Exists(candidate))
                        continue;

                    string testedPath = DaggerfallUnity.TestArena2Exists(candidate);
                    if (string.IsNullOrEmpty(testedPath))
                        continue;

                    DFValidator.ValidationResults validationResults;
                    DFValidator.ValidateArena2Folder(testedPath, out validationResults, true);
                    if (!validationResults.AppearsValid)
                        continue;

                    arena2Path = testedPath;
                    return true;
                }
                catch (Exception ex)
                {
                    LogDiagnostic("Could not inspect data candidate " + candidate + ": " + ex.Message);
                }
            }

            return false;
        }

        private void BeginFilePick()
        {
            if (state == RuntimeState.PickingFile || state == RuntimeState.Importing)
                return;

            state = RuntimeState.PickingFile;
            statusText = "Choose a ZIP containing your legally obtained Daggerfall game data.";
            lastError = string.Empty;
            showOverlay = true;

            try
            {
                string zipType = global::NativeFilePicker.ConvertExtensionToFileType("zip");
                if (string.IsNullOrEmpty(zipType))
                    zipType = "public.zip-archive";

                LogDiagnostic("Opening iOS document picker for type: " + zipType);
                global::NativeFilePicker.PickFile(OnFilePicked, zipType);
            }
            catch (Exception ex)
            {
                Fail("Could not open the iOS document picker.", ex);
            }
        }

        private void OnFilePicked(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                state = RuntimeState.NeedsData;
                statusText = "No file was selected. Choose a ZIP containing an arena2 folder.";
                LogDiagnostic("Document picker was cancelled.");
                return;
            }

            LogDiagnostic("Document picker returned: " + filePath);

            if (!File.Exists(filePath))
            {
                Fail("The selected file is no longer accessible.", null);
                return;
            }

            if (!string.Equals(Path.GetExtension(filePath), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                Fail("The selected file is not a ZIP archive.", null);
                return;
            }

            StartCoroutine(ImportArchive(filePath));
        }

        private IEnumerator ImportArchive(string filePath)
        {
            state = RuntimeState.Importing;
            statusText = "Preparing import...";
            importProgress = 0f;
            lastError = string.Empty;
            showOverlay = true;

            string outputPath = Path.Combine(Paths.PersistentDataPath, DataDirectoryName);
            string cachePath = Path.Combine(Application.temporaryCachePath, "DaggerfallArena2UnzippedIOS");
            string error;

            if (!TryPrepareCache(cachePath, out error))
            {
                Fail("Game-data import failed. " + error, null);
                yield break;
            }

            LogDiagnostic("Importing game data from: " + filePath);
            LogDiagnostic("Import destination: " + outputPath);

            statusText = "Unzipping game data... This can take a moment.";
            yield return null;

            if (!TryUnzip(filePath, cachePath, out error))
            {
                CleanupCache(cachePath);
                Fail("Game-data import failed. " + error, null);
                yield break;
            }

            importProgress = 0.25f;
            statusText = "Validating arena2 data...";
            yield return null;

            string sourcePath;
            string destinationPath;
            string candidateArena2Path;
            DFValidator.ValidationResults validationResults;
            if (!TryGetValidImportSource(
                cachePath,
                outputPath,
                out sourcePath,
                out destinationPath,
                out candidateArena2Path,
                out validationResults,
                out error))
            {
                CleanupCache(cachePath);
                string validationMessage = string.IsNullOrEmpty(error)
                    ? GetValidationFailureText(validationResults)
                    : error;
                Fail("Game-data import failed. " + validationMessage, null);
                yield break;
            }

            LogDiagnostic("Validated archive arena2 path: " + candidateArena2Path);
            LogDiagnostic("Copy source: " + sourcePath);
            LogDiagnostic("Copy destination: " + destinationPath);

            if (!TryPrepareOutput(outputPath, out error))
            {
                CleanupCache(cachePath);
                Fail("Game-data import failed. " + error, null);
                yield break;
            }

            string[] allFiles;
            if (!TryListFiles(sourcePath, out allFiles, out error))
            {
                CleanupCache(cachePath);
                Fail("Game-data import failed. " + error, null);
                yield break;
            }

            statusText = "Copying game data...";
            int total = allFiles.Length;
            for (int i = 0; i < total; i++)
            {
                if (!TryCopyFile(sourcePath, destinationPath, allFiles[i], out error))
                {
                    CleanupCache(cachePath);
                    Fail("Game-data import failed. " + error, null);
                    yield break;
                }

                importProgress = total > 0
                    ? 0.25f + 0.70f * (i + 1) / total
                    : 0.95f;

                if (i % 10 == 0)
                    yield return null;
            }

            statusText = "Final validation...";
            importProgress = 0.97f;
            yield return null;

            string importedArena2Path;
            if (!TryValidateImportedPath(outputPath, out importedArena2Path, out error))
            {
                CleanupCache(cachePath);
                Fail("Game-data import failed. " + error, null);
                yield break;
            }

            if (!TryActivatePath(outputPath, importedArena2Path, out error))
            {
                CleanupCache(cachePath);
                Fail("Game-data import failed. " + error, null);
                yield break;
            }

            TryWriteImportMarker(importedArena2Path);
            CleanupCache(cachePath);

            importProgress = 1f;
            state = RuntimeState.Ready;
            statusText = "Import complete. Opening the Daggerfall Unity main menu...";
            LogDiagnostic("Game-data import succeeded: " + importedArena2Path);
            FlushLog();

            yield return new WaitForSecondsRealtime(1f);
            capturedError = false;
            showOverlay = false;
            LoadGameScene();
        }

        private bool TryPrepareCache(string cachePath, out string error)
        {
            error = string.Empty;
            try
            {
                if (Directory.Exists(cachePath))
                    Directory.Delete(cachePath, true);
                Directory.CreateDirectory(cachePath);
                return true;
            }
            catch (Exception ex)
            {
                error = "Could not prepare the temporary import folder: " + ex.Message;
                return false;
            }
        }

        private bool TryUnzip(string filePath, string cachePath, out string error)
        {
            error = string.Empty;
            try
            {
                ZipFileUtils.UnzipFile(filePath, cachePath);
                importProgress = 0.25f;
                return true;
            }
            catch (Exception ex)
            {
                error = "Could not unzip the selected archive: " + ex.Message;
                return false;
            }
        }

        private bool TryPrepareOutput(string outputPath, out string error)
        {
            error = string.Empty;
            try
            {
                if (Directory.Exists(outputPath))
                    Directory.Delete(outputPath, true);
                Directory.CreateDirectory(outputPath);
                return true;
            }
            catch (Exception ex)
            {
                error = "Could not prepare the app data folder: " + ex.Message;
                return false;
            }
        }

        private bool TryListFiles(string sourcePath, out string[] files, out string error)
        {
            files = null;
            error = string.Empty;
            try
            {
                files = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);
                return true;
            }
            catch (Exception ex)
            {
                error = "Could not enumerate imported files: " + ex.Message;
                return false;
            }
        }

        private bool TryCopyFile(string sourcePath, string destinationPath, string sourceFile, out string error)
        {
            error = string.Empty;
            try
            {
                string relativePath = sourceFile.Substring(sourcePath.Length + 1);
                string destinationFile = Path.Combine(destinationPath, relativePath);
                string destinationDirectory = Path.GetDirectoryName(destinationFile);
                if (!string.IsNullOrEmpty(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);
                File.Copy(sourceFile, destinationFile, true);
                return true;
            }
            catch (Exception ex)
            {
                error = "Could not copy " + Path.GetFileName(sourceFile) + ": " + ex.Message;
                return false;
            }
        }

        private bool TryValidateImportedPath(string outputPath, out string arena2Path, out string error)
        {
            arena2Path = string.Empty;
            error = string.Empty;
            try
            {
                arena2Path = DaggerfallUnity.TestArena2Exists(outputPath);
                if (string.IsNullOrEmpty(arena2Path))
                {
                    error = "Imported files did not produce an arena2 folder.";
                    return false;
                }

                DFValidator.ValidationResults validation;
                DFValidator.ValidateArena2Folder(arena2Path, out validation, true);
                if (!validation.AppearsValid)
                {
                    error = GetValidationFailureText(validation);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "Could not validate imported data: " + ex.Message;
                return false;
            }
        }

        private bool TryActivatePath(string rootPath, string arena2Path, out string error)
        {
            error = string.Empty;
            try
            {
                if (!DaggerfallUnity.HasInstance)
                {
                    error = "Daggerfall Unity is not initialized.";
                    return false;
                }

                DaggerfallUnity.Settings.MyDaggerfallPath = rootPath;
                DaggerfallUnity.Settings.SaveSettings();
                DaggerfallUnity.Instance.ChangeArena2Path(arena2Path);

                if (!DaggerfallUnity.Instance.IsPathValidated)
                {
                    error = "Daggerfall Unity rejected the imported arena2 path.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "Could not activate the imported game-data path: " + ex.Message;
                return false;
            }
        }

        private void TryWriteImportMarker(string arena2Path)
        {
            try
            {
                string markerPath = Path.Combine(Paths.PersistentDataPath, ImportedMarkerName);
                File.WriteAllText(
                    markerPath,
                    "Imported " + arena2Path + Environment.NewLine +
                    "UTC " + DateTime.UtcNow.ToString("O") + Environment.NewLine);
            }
            catch (Exception ex)
            {
                LogDiagnostic("Could not write import marker: " + ex.Message);
            }
        }

        private void CleanupCache(string cachePath)
        {
            try
            {
                if (Directory.Exists(cachePath))
                    Directory.Delete(cachePath, true);
            }
            catch (Exception ex)
            {
                LogDiagnostic("Could not clean import cache: " + ex.Message);
            }
        }

        private void LoadGameScene()
        {
            int gameSceneIndex = SceneControl.GameSceneIndex;
            if (SceneManager.GetActiveScene().buildIndex == gameSceneIndex)
            {
                LogDiagnostic("Game scene is already active.");
                return;
            }

            LogDiagnostic("Loading game scene index " + gameSceneIndex + ".");
            FlushLog();
            SceneManager.LoadScene(gameSceneIndex);
        }

        private bool TryGetValidImportSource(
            string cachePath,
            string outputPath,
            out string sourcePath,
            out string destinationPath,
            out string arena2Path,
            out DFValidator.ValidationResults bestValidationResults,
            out string error)
        {
            sourcePath = null;
            destinationPath = null;
            arena2Path = null;
            error = string.Empty;
            bestValidationResults = new DFValidator.ValidationResults();
            int bestScore = -1;

            try
            {
                foreach (string candidate in GetArena2Candidates(cachePath))
                {
                    TryUnpackPackedDat(candidate);

                    DFValidator.ValidationResults validationResults;
                    DFValidator.ValidateArena2Folder(candidate, out validationResults, true);

                    int score = GetValidationScore(validationResults);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestValidationResults = validationResults;
                    }

                    if (!validationResults.AppearsValid)
                        continue;

                    arena2Path = candidate;
                    sourcePath = GetImportSourcePath(cachePath, candidate);
                    destinationPath = string.Equals(sourcePath, candidate, StringComparison.OrdinalIgnoreCase)
                        ? Path.Combine(outputPath, "arena2")
                        : outputPath;
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = "Could not inspect the selected Daggerfall archive: " + ex.Message;
                return false;
            }

            return false;
        }

        private static IEnumerable<string> GetArena2Candidates(string cachePath)
        {
            if (!Directory.Exists(cachePath))
                yield break;

            yield return cachePath;

            foreach (string directory in Directory.EnumerateDirectories(cachePath, "*", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFileName(directory), "arena2", StringComparison.OrdinalIgnoreCase))
                    yield return directory;
            }
        }

        private static string GetImportSourcePath(string cachePath, string arena2Path)
        {
            if (string.Equals(cachePath, arena2Path, StringComparison.OrdinalIgnoreCase))
                return arena2Path;

            DirectoryInfo parent = Directory.GetParent(arena2Path);
            if (parent == null || string.Equals(parent.FullName, cachePath, StringComparison.OrdinalIgnoreCase))
                return cachePath;

            return parent.FullName;
        }

        private void TryUnpackPackedDat(string arena2Path)
        {
            string packedDatPath = GetFileIgnoreCase(arena2Path, "PACKED.DAT");
            if (string.IsNullOrEmpty(packedDatPath))
                return;

            bool arch3dExists = !string.IsNullOrEmpty(GetFileIgnoreCase(arena2Path, "ARCH3D.BSA"));
            bool daggerSndExists = !string.IsNullOrEmpty(GetFileIgnoreCase(arena2Path, "DAGGER.SND"));
            if (arch3dExists && daggerSndExists)
                return;

            DirectoryInfo arena2Parent = Directory.GetParent(arena2Path);
            string unpackOutputPath = arena2Parent != null ? arena2Parent.FullName : arena2Path;

            statusText = "Expanding PACKED.DAT...";
            LogDiagnostic("Expanding PACKED.DAT from " + packedDatPath);
            PackedDatFileUtils.UnpackFile(
                packedDatPath,
                unpackOutputPath,
                delegate(float progress)
                {
                    importProgress = 0.20f + Mathf.Clamp01(progress) * 0.05f;
                });
        }

        private static string GetFileIgnoreCase(string path, string filename)
        {
            if (!Directory.Exists(path))
                return null;

            foreach (string file in Directory.GetFiles(path))
            {
                if (string.Equals(Path.GetFileName(file), filename, StringComparison.OrdinalIgnoreCase))
                    return file;
            }

            return null;
        }

        private static int GetValidationScore(DFValidator.ValidationResults validationResults)
        {
            int score = 0;
            if (validationResults.FolderValid) score++;
            if (validationResults.TexturesValid) score++;
            if (validationResults.ModelsValid) score++;
            if (validationResults.BlocksValid) score++;
            if (validationResults.MapsValid) score++;
            if (validationResults.SoundsValid) score++;
            if (validationResults.WoodsValid) score++;
            if (validationResults.VideosValid) score++;
            return score;
        }

        private static string GetValidationFailureText(DFValidator.ValidationResults validationResults)
        {
            if (string.IsNullOrEmpty(validationResults.PathTested))
                return "The ZIP did not contain an arena2 folder.";

            List<string> missing = new List<string>();
            if (!validationResults.TexturesValid) missing.Add("textures");
            if (!validationResults.ModelsValid) missing.Add("ARCH3D.BSA");
            if (!validationResults.BlocksValid) missing.Add("BLOCKS.BSA");
            if (!validationResults.MapsValid) missing.Add("MAPS.BSA");
            if (!validationResults.SoundsValid) missing.Add("DAGGER.SND");
            if (!validationResults.WoodsValid) missing.Add("WOODS.WLD");
            if (!validationResults.VideosValid) missing.Add("videos");

            return missing.Count == 0
                ? "The ZIP did not contain a valid Daggerfall data folder."
                : "The selected data is missing: " + string.Join(", ", missing.ToArray()) + ".";
        }

        private void Fail(string summary, Exception exception)
        {
            state = RuntimeState.Error;
            importProgress = 0f;
            showOverlay = true;
            capturedError = true;
            lastError = exception == null ? summary : summary + " " + exception.Message;
            statusText = lastError;

            if (exception == null)
                Debug.LogError("[DFU iOS] " + summary);
            else
                Debug.LogError("[DFU iOS] " + summary + Environment.NewLine + exception);
        }

        private void ExportLog()
        {
            try
            {
                FlushLog();
                string exportPath = Path.Combine(Application.temporaryCachePath, LogFileName);
                File.Copy(logPath, exportPath, true);
                statusText = "Choose where to save the diagnostic log.";
                global::NativeFilePicker.ExportFile(
                    exportPath,
                    delegate(bool success)
                    {
                        statusText = success
                            ? "Diagnostic log exported."
                            : "Diagnostic log export was cancelled or failed.";
                    });
            }
            catch (Exception ex)
            {
                Fail("Could not export the diagnostic log.", ex);
            }
        }

        private void InitializeLog()
        {
            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                logPath = Path.Combine(Application.persistentDataPath, LogFileName);
                logWriter = new StreamWriter(logPath, true, new UTF8Encoding(false));
                logWriter.AutoFlush = true;
                logWriter.WriteLine();
                logWriter.WriteLine("===== iOS session " + DateTime.UtcNow.ToString("O") + " =====");
            }
            catch
            {
                logWriter = null;
                logPath = Path.Combine(Application.persistentDataPath, LogFileName);
            }
        }

        private void HandleUnityLog(string condition, string stackTrace, LogType type)
        {
            string line = DateTime.UtcNow.ToString("O") + " [" + type + "] " + condition;
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && !string.IsNullOrEmpty(stackTrace))
                line += Environment.NewLine + stackTrace;

            lock (logSync)
            {
                if (logWriter != null)
                    logWriter.WriteLine(line);

                string compact = condition == null ? string.Empty : condition.Replace('\n', ' ').Replace('\r', ' ');
                if (compact.Length > 180)
                    compact = compact.Substring(0, 180) + "...";

                recentLogLines.Enqueue(type + ": " + compact);
                while (recentLogLines.Count > MaxRecentLogLines)
                    recentLogLines.Dequeue();
            }

            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                capturedError = true;
        }

        private void LogDiagnostic(string message)
        {
            Debug.Log("[DFU iOS] " + message);
        }

        private void FlushLog()
        {
            lock (logSync)
            {
                if (logWriter != null)
                    logWriter.Flush();
            }
        }

        private void InitializeStyles()
        {
            if (titleStyle != null)
                return;

            int baseFontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height / 32f), 18, 34);

            titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = baseFontSize;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.alignment = TextAnchor.UpperCenter;
            titleStyle.wordWrap = true;
            titleStyle.normal.textColor = Color.white;

            bodyStyle = new GUIStyle(GUI.skin.label);
            bodyStyle.fontSize = Mathf.Max(16, baseFontSize - 6);
            bodyStyle.alignment = TextAnchor.UpperCenter;
            bodyStyle.wordWrap = true;
            bodyStyle.normal.textColor = Color.white;

            detailStyle = new GUIStyle(GUI.skin.label);
            detailStyle.fontSize = Mathf.Max(12, baseFontSize - 11);
            detailStyle.alignment = TextAnchor.UpperLeft;
            detailStyle.wordWrap = true;
            detailStyle.normal.textColor = new Color(0.82f, 0.9f, 1f, 1f);

            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontSize = Mathf.Max(16, baseFontSize - 5);
            buttonStyle.fontStyle = FontStyle.Bold;
        }

        private void OnGUI()
        {
            bool importerVisible = state == RuntimeState.NeedsData ||
                                   state == RuntimeState.PickingFile ||
                                   state == RuntimeState.Importing ||
                                   state == RuntimeState.Error;

            if (!showOverlay && !capturedError && !importerVisible)
                return;

            InitializeStyles();
            GUI.depth = -10000;

            float panelWidth = Mathf.Min(Screen.width - 32f, Mathf.Max(520f, Screen.width * 0.72f));
            float panelHeight = Mathf.Min(Screen.height - 32f, Mathf.Max(300f, Screen.height * 0.64f));
            Rect panel = new Rect(
                (Screen.width - panelWidth) * 0.5f,
                (Screen.height - panelHeight) * 0.5f,
                panelWidth,
                panelHeight);

            Color previousColor = GUI.color;
            GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.96f);
            GUI.Box(panel, GUIContent.none);
            GUI.color = previousColor;

            float padding = Mathf.Max(16f, panelWidth * 0.025f);
            float contentWidth = panelWidth - padding * 2f;
            float y = panel.y + padding;

            GUI.Label(new Rect(panel.x + padding, y, contentWidth, 48f), "Daggerfall Unity for iOS", titleStyle);
            y += 50f;

            GUI.Label(new Rect(panel.x + padding, y, contentWidth, 72f), statusText, bodyStyle);
            y += 76f;

            if (state == RuntimeState.Importing)
            {
                Rect progressBackground = new Rect(panel.x + padding, y, contentWidth, 24f);
                GUI.Box(progressBackground, GUIContent.none);
                Color progressColor = GUI.color;
                GUI.color = new Color(0.15f, 0.75f, 0.3f, 1f);
                GUI.DrawTexture(
                    new Rect(
                        progressBackground.x + 2f,
                        progressBackground.y + 2f,
                        (progressBackground.width - 4f) * Mathf.Clamp01(importProgress),
                        progressBackground.height - 4f),
                    Texture2D.whiteTexture);
                GUI.color = progressColor;
                y += 34f;
            }

            float buttonHeight = Mathf.Max(44f, Screen.height * 0.065f);
            float buttonWidth = Mathf.Min(330f, contentWidth * 0.48f);

            if (state == RuntimeState.NeedsData || state == RuntimeState.Error)
            {
                if (GUI.Button(
                    new Rect(panel.x + (panelWidth - buttonWidth) * 0.5f, y, buttonWidth, buttonHeight),
                    "Import Daggerfall Data ZIP",
                    buttonStyle))
                {
                    BeginFilePick();
                }
                y += buttonHeight + 10f;
            }

            if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath) && state != RuntimeState.PickingFile)
            {
                float exportWidth = Mathf.Min(220f, contentWidth * 0.38f);
                if (GUI.Button(
                    new Rect(panel.x + (panelWidth - exportWidth) * 0.5f, y, exportWidth, buttonHeight * 0.82f),
                    "Export Diagnostic Log",
                    buttonStyle))
                {
                    ExportLog();
                }
                y += buttonHeight * 0.82f + 10f;
            }

            StringBuilder details = new StringBuilder();
            details.AppendLine("Scene: " + SceneManager.GetActiveScene().name);
            details.AppendLine("Data folder: " + Path.Combine(Paths.PersistentDataPath, DataDirectoryName));
            details.AppendLine("Log: " + logPath);
            if (!string.IsNullOrEmpty(lastError))
                details.AppendLine("Error: " + lastError);

            lock (logSync)
            {
                foreach (string line in recentLogLines)
                    details.AppendLine(line);
            }

            float detailsHeight = panel.yMax - padding - y;
            if (detailsHeight > 40f)
                GUI.Label(new Rect(panel.x + padding, y, contentWidth, detailsHeight), details.ToString(), detailStyle);
        }
    }
}
#endif
