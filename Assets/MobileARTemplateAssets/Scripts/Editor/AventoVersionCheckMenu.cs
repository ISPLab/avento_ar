#if UNITY_EDITOR
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityEngine.XR.Templates.AR.Editor
{
    /// <summary>
    /// Runs <c>scripts/check-unity-ar-versions.sh</c> and prints the report in the Console.
    /// </summary>
    public static class AventoVersionCheckMenu
    {
        const string ScriptRelative = "scripts/check-unity-ar-versions.sh";

        [MenuItem("AR Test/Check Unity AR versions (bundle vs UaaL)")]
        public static void CheckVersions()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var script = Path.Combine(projectRoot, ScriptRelative);
            if (!File.Exists(script))
            {
                EditorUtility.DisplayDialog(
                    "Version check",
                    $"Missing script:\n{script}",
                    "OK");
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"\"{script}\" --sha256",
                WorkingDirectory = projectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var proc = Process.Start(psi);
                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(60_000);
                if (!string.IsNullOrWhiteSpace(stdout))
                    UnityEngine.Debug.Log($"[Avento version check]\n{stdout}");
                if (!string.IsNullOrWhiteSpace(stderr))
                    UnityEngine.Debug.LogWarning($"[Avento version check stderr]\n{stderr}");
                EditorUtility.DisplayDialog(
                    "Version check",
                    proc.ExitCode == 0
                        ? "OK — see Console for full report."
                        : "Mismatch / missing artifacts — see Console for details.",
                    "OK");
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Version check failed", ex.Message, "OK");
            }
        }
    }
}
#endif
