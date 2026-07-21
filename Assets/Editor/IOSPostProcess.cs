// Project: Daggerfall Unity iOS
// Purpose: Apply bounded native dependencies to the generated iOS Xcode project.

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace DaggerfallUnityIOS.Editor
{
    public static class IOSPostProcess
    {
        private const string MobileCoreServicesFramework = "MobileCoreServices.framework";

        [PostProcessBuild(1000)]
        public static void ConfigureGeneratedIOSProject(BuildTarget target, string buildPath)
        {
            if (target != BuildTarget.iOS)
                return;

            string projectPath = PBXProject.GetPBXProjectPath(buildPath);
            if (!File.Exists(projectPath))
                throw new FileNotFoundException("Generated iOS Xcode project was not found.", projectPath);

            var project = new PBXProject();
            project.ReadFromFile(projectPath);

            string unityFrameworkTarget = project.GetUnityFrameworkTargetGuid();
            if (string.IsNullOrEmpty(unityFrameworkTarget))
                throw new InvalidDataException("Unable to resolve the UnityFramework target in the generated Xcode project.");

            // NativeFilePicker.mm imports MobileCoreServices and calls the legacy
            // UTTypeCreatePreferredIdentifierForTag APIs. The plugin source is
            // retained unchanged; its required system framework is linked only
            // into the generated iOS UnityFramework target.
            project.AddFrameworkToProject(
                unityFrameworkTarget,
                MobileCoreServicesFramework,
                weak: false);

            project.WriteToFile(projectPath);
            Debug.Log($"iOS post-process: linked {MobileCoreServicesFramework} to UnityFramework.");
        }
    }
}
#endif
