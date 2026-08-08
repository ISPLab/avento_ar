#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityEngine.XR.Templates.AR.Editor
{
    /// <summary>
    /// Builds a single AssetBundle that contains PlacedContent + all dependencies
    /// (materials, shaders, video clips, etc.) for cloud / remote download.
    /// Prefab files alone are not self-contained — this is the "one file" packaging step.
    /// </summary>
    public static class PlacedContentBundleBuilder
    {
        const string PrefabPath = "Assets/PlacedContent.prefab";
        const string BundleName = "placedcontent";
        const string OutputRoot = "AssetBundles";

        [MenuItem("AR Test/Build PlacedContent AssetBundle (active platform)")]
        public static void BuildForActivePlatform()
        {
            Build(EditorUserBuildSettings.activeBuildTarget, interactive: true);
        }

        [MenuItem("AR Test/Build PlacedContent AssetBundle (iOS)")]
        public static void BuildForIos()
        {
            Build(BuildTarget.iOS, interactive: true);
        }

        [MenuItem("AR Test/Build PlacedContent AssetBundle (Android)")]
        public static void BuildForAndroid()
        {
            Build(BuildTarget.Android, interactive: true);
        }

        /// <summary>
        /// Batch/CI: Unity -batchmode -executeMethod
        /// UnityEngine.XR.Templates.AR.Editor.PlacedContentBundleBuilder.BuildForIosBatch
        /// </summary>
        public static void BuildForIosBatch()
        {
            var ok = Build(BuildTarget.iOS, interactive: false);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>
        /// Batch/CI: Unity -batchmode -executeMethod
        /// UnityEngine.XR.Templates.AR.Editor.PlacedContentBundleBuilder.BuildForAndroidBatch
        /// </summary>
        public static void BuildForAndroidBatch()
        {
            var ok = Build(BuildTarget.Android, interactive: false);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool Build(BuildTarget target, bool interactive)
        {
            if (!File.Exists(PrefabPath))
            {
                var msg = $"Missing prefab at:\n{PrefabPath}";
                Debug.LogError($"[PlacedContentBundle] {msg}");
                if (interactive)
                    EditorUtility.DisplayDialog("PlacedContent Bundle", msg, "OK");
                return false;
            }

            var platformFolder = target.ToString();
            var outputDir = Path.Combine(OutputRoot, platformFolder);
            Directory.CreateDirectory(outputDir);

            var build = new AssetBundleBuild
            {
                assetBundleName = BundleName,
                assetNames = new[] { PrefabPath }
            };

            var manifest = BuildPipeline.BuildAssetBundles(
                outputDir,
                new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression |
                BuildAssetBundleOptions.StrictMode,
                target);

            if (manifest == null)
            {
                Debug.LogError("[PlacedContentBundle] Build failed. See Console for details.");
                if (interactive)
                {
                    EditorUtility.DisplayDialog(
                        "PlacedContent Bundle",
                        "Build failed. See Console for details.",
                        "OK");
                }

                return false;
            }

            var bundlePath = Path.Combine(outputDir, BundleName);
            var sizeMb = File.Exists(bundlePath)
                ? new FileInfo(bundlePath).Length / (1024f * 1024f)
                : 0f;

            Debug.Log(
                $"[PlacedContentBundle] Built for {target}:\n{Path.GetFullPath(bundlePath)}\n" +
                $"Size ≈ {sizeMb:F2} MB\n" +
                "Upload this file to cloud CDN. App downloads it, then Instantiate from the bundle.",
                AssetDatabase.LoadAssetAtPath<Object>(PrefabPath));

            if (interactive)
            {
                EditorUtility.RevealInFinder(bundlePath);
                EditorUtility.DisplayDialog(
                    "PlacedContent Bundle",
                    $"Built OK for {target}\n\n{bundlePath}\n≈ {sizeMb:F2} MB\n\n" +
                    "This one file includes the prefab + materials/shaders/videos it references.\n" +
                    "Build separate bundles per platform (iOS / Android).",
                    "OK");
            }

            return File.Exists(bundlePath) && new FileInfo(bundlePath).Length > 64 * 1024;
        }
    }
}
#endif
