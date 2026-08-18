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
    /// Prepares and exports Unity-as-a-Library iOS for Avento Capacitor.
    /// </summary>
    public static class AventoUaalIosExporter
    {
        public const string DefaultExportPath = "Builds/iOS_UaaL";

        [MenuItem("AR Test/UaaL/Prepare iOS Player Settings")]
        public static void PreparePlayerSettings()
        {
            ApplyPlayerSettings();
            EditorUtility.DisplayDialog(
                "UaaL iOS settings",
                "Applied IL2CPP + ARM64 + iOS 16.0.\n\n" +
                "Next: AR Test → UaaL → Export iOS Library Project",
                "OK");
        }

        [MenuItem("AR Test/UaaL/Export iOS Library Project")]
        public static void ExportIosLibrary()
        {
            var absOut = Path.GetFullPath(DefaultExportPath);
            var proceed = EditorUtility.DisplayDialog(
                "Export Unity as a Library (iOS)",
                "Export a fresh iOS Xcode project to:\n\n" + absOut +
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
                        "UaaL iOS export ready",
                        "Exported to:\n" + absOut +
                        "\n\nNext: open Unity-iPhone.xcodeproj, build UnityFramework, then run integrate-unity-ios.sh.",
                        "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog(
                    "UaaL export failed",
                    ex.Message + "\n\nSee Console for details.",
                    "OK");
            }
        }

        /// <summary>
        /// Batch/CI: -executeMethod UnityEngine.XR.Templates.AR.Editor.AventoUaalIosExporter.ExportIosLibraryBatch
        /// Optional: -aventoUaalOut=/abs/path
        /// </summary>
        public static void ExportIosLibraryBatch()
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

        [MenuItem("AR Test/UaaL/Show iOS Integration Checklist")]
        public static void ShowChecklist()
        {
            var text = BuildChecklistMarkdown(DefaultExportPath);
            var outPath = Path.Combine("Builds", "AVENTO_UAAL_IOS_CHECKLIST.md");
            Directory.CreateDirectory("Builds");
            File.WriteAllText(outPath, text, Encoding.UTF8);
            EditorUtility.RevealInFinder(outPath);
            Debug.Log($"[Avento UaaL] Checklist written to {Path.GetFullPath(outPath)}");
        }

        static void ApplyPlayerSettings()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetArchitecture(NamedBuildTarget.iOS, 1); // ARM64
            PlayerSettings.iOS.targetOSVersionString = "16.0";
            PlayerSettings.stripEngineCode = true;
            // Skip Made with Unity splash when licensing allows (Plus/Pro / UaaL host).
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
        }

        static bool ExportToPath(string path, bool interactive)
        {
            ApplyPlayerSettings();

            // Fresh export — do NOT use AcceptExternalModificationsToPlayer (append).
            // Append throws "The build cannot be appended" on empty/non-iOS folders.
            if (Directory.Exists(path))
            {
                Debug.Log($"[Avento UaaL] Clearing previous export at {path}");
                Directory.Delete(path, recursive: true);
            }

            Directory.CreateDirectory(path);

            var scenePaths = CollectExistingEnabledScenes();

            if (scenePaths.Count == 0)
            {
                var msg = "No enabled scenes in Build Settings.";
                Debug.LogError("[Avento UaaL] " + msg);
                if (interactive)
                    EditorUtility.DisplayDialog("UaaL export", msg, "OK");
                return false;
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenePaths.ToArray(),
                locationPathName = path,
                target = BuildTarget.iOS,
                options = BuildOptions.None,
            };

            Debug.Log($"[Avento UaaL] Exporting iOS library to {path} …");
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                var msg = $"Build result: {report.summary.result}";
                Debug.LogError("[Avento UaaL] " + msg);
                if (interactive)
                    EditorUtility.DisplayDialog("UaaL export failed", msg + "\n\nSee Console.", "OK");
                return false;
            }

            WriteChecklistBesideExport(path);
            Debug.Log($"[Avento UaaL] Export succeeded: {path}");
            return true;
        }

        static List<string> CollectExistingEnabledScenes()
        {
            var scenePaths = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (!scene.enabled || string.IsNullOrEmpty(scene.path))
                    continue;
                if (!File.Exists(scene.path))
                {
                    Debug.LogWarning($"[Avento UaaL] Skipping missing scene: {scene.path}");
                    continue;
                }
                scenePaths.Add(scene.path);
            }
            return scenePaths;
        }

        static void WriteChecklistBesideExport(string exportPath)
        {
            File.WriteAllText(
                Path.Combine(exportPath, "AVENTO_UAAL_IOS_CHECKLIST.md"),
                BuildChecklistMarkdown(exportPath),
                Encoding.UTF8);
        }

        static string BuildChecklistMarkdown(string exportPath)
        {
            return
                "# Avento — Unity as a Library (iOS) checklist\n\n" +
                $"Export path: `{exportPath}`\n\n" +
                "## Integrate into avento-app\n\n" +
                "```bash\n" +
                "cd /path/to/avento-app\n" +
                "./scripts/integrate-unity-ios.sh /path/to/avento-ar/Builds/iOS_UaaL\n" +
                "```\n\n" +
                "Then open Xcode, build, and confirm `isAvailable.unityEmbedded === true`.\n";
        }
    }
}
#endif
