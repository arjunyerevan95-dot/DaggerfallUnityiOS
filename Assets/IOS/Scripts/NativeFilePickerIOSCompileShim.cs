// Project: Daggerfall Unity iOS
// Purpose: Compile-time compatibility shim for Android-only file picker calls.
// This is intentionally limited to iOS player builds. A real UIDocumentPicker
// implementation belongs to a later runtime milestone.

#if UNITY_IOS && !UNITY_EDITOR
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
                $"Native file picking is not implemented on iOS yet. Requested type: {mimeType}");
            callback?.Invoke(null);
        }

        public static void ExportFile(string path, Action<bool> callback)
        {
            Debug.LogWarning(
                $"Native file export is not implemented on iOS yet. Requested path: {path}");
            callback?.Invoke(false);
        }
    }
}
#endif
