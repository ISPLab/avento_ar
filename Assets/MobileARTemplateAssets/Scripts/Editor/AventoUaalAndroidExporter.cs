#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UnityEngine.XR.Templates.AR.Editor
{
    /// <summary>
    /// Prepares and exports Unity-as-a-Library Android (Google Gradle project / unityLibrary)
    /// for Avento Capacitor.
    /// </summary>
    public static class AventoUaalAndroidExporter
    {
        public const string DefaultExportPath = "Builds/Android_UaaL";

        [MenuItem("AR Test/UaaL/Prepare Android Player Settings")]
        public static void PreparePlayerSettings()
        {
            ApplyPlayerSettings();
            EditorUtility.DisplayDialog(
                "UaaL Android settings",
                "Applied IL2CPP + ARM64 + minSdk 26 + Export Project.\n\n" +
                "Next: AR Test → UaaL → Export Android Library Project",
                "OK");
        }

        [MenuItem("AR Test/UaaL/Export Android Library Project")]
        public static void ExportAndroidLibrary()
        {
            var absOut = Path.GetFullPath(DefaultExportPath);
            var proceed = EditorUtility.DisplayDialog(
                "Export Unity as a Library (Android)",
                "Export a fresh Android Gradle project to:\n\n" + absOut +
                "\n\nExisting contents of that folder will be replaced.\n" +
                "This can take several minutes — watch the Console.",
                "Export",
                "Cancel");
            if (!proceed)
                return;

            try
            {
                var ok = ExportToPath(absOut, interactive: true);
                if (ok)
                {
                    EditorUtility.RevealInFinder(absOut);
                    EditorUtility.DisplayDialog(
                        "UaaL Android export ready",
                        "Exported to:\n" + absOut +
                        "\n\nNext:\n" +
                        "cd avento-app && ./scripts/integrate-unity-android.sh " + absOut,
                        "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog(
                    "UaaL Android export failed",
                    ex.Message + "\n\nSee Console for details.",
                    "OK");
            }
        }

        /// <summary>
        /// Batch/CI: -executeMethod UnityEngine.XR.Templates.AR.Editor.AventoUaalAndroidExporter.ExportAndroidLibraryBatch
        /// Optional: -aventoUaalOut=/abs/path
        /// </summary>
        public static void ExportAndroidLibraryBatch()
        {
            var outPath = DefaultExportPath;
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (arg.StartsWith("-aventoUaalOut=", StringComparison.Ordinal))
                    outPath = arg.Substring("-aventoUaalOut=".Length).Trim();
            }

            if (!Path.IsPathRooted(outPath))
                outPath = Path.GetFullPath(outPath);

            try
            {
                var ok = ExportToPath(outPath, interactive: false);
                EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("AR Test/UaaL/Show Android Integration Checklist")]
        public static void ShowChecklist()
        {
            var text = BuildChecklistMarkdown(DefaultExportPath);
            var outPath = Path.Combine("Builds", "AVENTO_UAAL_ANDROID_CHECKLIST.md");
            Directory.CreateDirectory("Builds");
            File.WriteAllText(outPath, text, Encoding.UTF8);
            EditorUtility.RevealInFinder(outPath);
            Debug.Log($"[Avento UaaL] Checklist written to {Path.GetFullPath(outPath)}");
        }

        static void ApplyPlayerSettings()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.stripEngineCode = true;
            // Skip Made with Unity splash when licensing allows (Plus/Pro / UaaL host).
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = true;
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            EditorUserBuildSettings.buildAppBundle = false;
        }

        static bool ExportToPath(string path, bool interactive)
        {
            ApplyPlayerSettings();

            if (Directory.Exists(path))
            {
                Debug.Log($"[Avento UaaL] Clearing previous Android export at {path}");
                Directory.Delete(path, recursive: true);
            }

            Directory.CreateDirectory(path);

            var scenePaths = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                    scenePaths.Add(scene.path);
            }

            if (scenePaths.Count == 0)
            {
                var msg = "No enabled scenes in Build Settings.";
                Debug.LogError("[Avento UaaL] " + msg);
                if (interactive)
                    EditorUtility.DisplayDialog("UaaL Android export", msg, "OK");
                return false;
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenePaths.ToArray(),
                locationPathName = path,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };

            Debug.Log($"[Avento UaaL] Exporting Android library to {path} …");
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                var msg = $"Build result: {report.summary.result}";
                Debug.LogError("[Avento UaaL] " + msg);
                if (interactive)
                    EditorUtility.DisplayDialog("UaaL Android export failed", msg + "\n\nSee Console.", "OK");
                return false;
            }

            var unityLib = FindUnityLibraryDir(path);
            if (string.IsNullOrEmpty(unityLib))
            {
                var msg = "Export finished but unityLibrary/ was not found under " + path;
                Debug.LogError("[Avento UaaL] " + msg);
                if (interactive)
                    EditorUtility.DisplayDialog("UaaL Android export", msg, "OK");
                return false;
            }

            WriteChecklistBesideExport(path);
            Debug.Log($"[Avento UaaL] Android export succeeded: {path}\nunityLibrary: {unityLib}");
            return true;
        }

        static string FindUnityLibraryDir(string exportRoot)
        {
            var direct = Path.Combine(exportRoot, "unityLibrary");
            if (Directory.Exists(direct) && File.Exists(Path.Combine(direct, "build.gradle")))
                return direct;

            foreach (var dir in Directory.GetDirectories(exportRoot, "unityLibrary", SearchOption.AllDirectories))
            {
                if (File.Exists(Path.Combine(dir, "build.gradle")))
                    return dir;
            }

            return null;
        }

        static void WriteChecklistBesideExport(string exportPath)
        {
            File.WriteAllText(
                Path.Combine(exportPath, "AVENTO_UAAL_ANDROID_CHECKLIST.md"),
                BuildChecklistMarkdown(exportPath),
                Encoding.UTF8);
        }

        static string BuildChecklistMarkdown(string exportPath)
        {
            return
                "# Avento — Unity as a Library (Android) checklist\n\n" +
                $"Export path: `{exportPath}`\n\n" +
                "## Integrate into avento-app\n\n" +
                "```bash\n" +
                "cd /path/to/avento-app\n" +
                "./scripts/integrate-unity-android.sh /path/to/avento-ar/Builds/Android_UaaL\n" +
                "```\n\n" +
                "Or rebuild both platforms from avento-ar:\n\n" +
                "```bash\n" +
                "./scripts/rebuild-ios-uaal.sh --skip-bundle --skip-upload-hint\n" +
                "```\n\n" +
                "Then Android Studio → Run on an ARCore device.\n" +
                "Confirm `isAvailable.unityEmbedded === true`.\n";
        }
    }
}
#endif
