// Project: Daggerfall Unity iOS
// Purpose: Compile-time compatibility shim for Android-only file picker calls.
//
// The Android fork's real picker assembly is only imported for Android. Unity
// still compiles TouchscreenLayoutsManager while importing the project and while
// building the temporary macOS carrier target, so every non-Android target needs
// this no-op API surface. A real UIDocumentPicker implementation belongs to a
// later runtime milestone.

#if !UNITY_ANDROID
using System;
using UnityEngine;

namespace NativeFilePickerNamespace
{
    public static class NativeFilePicker
    {
        public delegate void FilePickedCallback(string path);

        public static void PickFile(FilePickedCallback callback, string mimeType)
        {
            Debug.LogWarning(
                $"Native file picking is not implemented on this target yet. Requested type: {mimeType}");
            callback?.Invoke(null);
        }

        public static void ExportFile(string path, Action<bool> callback)
        {
            Debug.LogWarning(
                $"Native file export is not implemented on this target yet. Requested path: {path}");
            callback?.Invoke(false);
        }
    }
}
#endif
