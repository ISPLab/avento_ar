#if UNITY_EDITOR
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UnityEngine.XR.Templates.AR.Editor
{
    /// <summary>
    /// Builds a self-contained AssetBundle (prefab + dependencies) for cloud / remote download.
    /// Default menus pack <c>Assets/Resources/PlacedContent.prefab</c> (fallback:
    /// <c>Assets/PlacedContent.prefab</c>); selected-prefab menus pack any prefab
    /// (bundle file name = lowercased prefab name).
    /// </summary>
    public static class PlacedContentBundleBuilder
    {
        const string ResourcesPrefabPath = "Assets/Resources/PlacedContent.prefab";
        const string RootPrefabPath = "Assets/PlacedContent.prefab";
        const string BundleName = "placedcontent";
        const string OutputRoot = "AssetBundles";

        /// <summary>Prefer Resources copy (runtime fallback path), then root Assets/.</summary>
        public static string DefaultPrefabPath =>
            File.Exists(ResourcesPrefabPath) ? ResourcesPrefabPath :
            File.Exists(RootPrefabPath) ? RootPrefabPath :
            ResourcesPrefabPath;

        [MenuItem("AR Test/Build PlacedContent AssetBundle (active platform)")]
        public static void BuildForActivePlatform()
        {
            Build(DefaultPrefabPath, BundleName, EditorUserBuildSettings.activeBuildTarget, interactive: true);
        }

        [MenuItem("AR Test/Build PlacedContent AssetBundle (iOS)")]
        public static void BuildForIos()
        {
            Build(DefaultPrefabPath, BundleName, BuildTarget.iOS, interactive: true);
        }

        [MenuItem("AR Test/Build PlacedContent AssetBundle (Android)")]
        public static void BuildForAndroid()
        {
            Build(DefaultPrefabPath, BundleName, BuildTarget.Android, interactive: true);
        }

        [MenuItem("AR Test/Build AssetBundle from selected prefab (active platform)")]
        public static void BuildSelectedForActivePlatform()
        {
            BuildSelected(EditorUserBuildSettings.activeBuildTarget);
        }

        [MenuItem("AR Test/Build AssetBundle from selected prefab (iOS)")]
        public static void BuildSelectedForIos()
        {
            BuildSelected(BuildTarget.iOS);
        }

        [MenuItem("AR Test/Build AssetBundle from selected prefab (Android)")]
        public static void BuildSelectedForAndroid()
        {
            BuildSelected(BuildTarget.Android);
        }

        [MenuItem("AR Test/Build AssetBundle from selected prefab (active platform)", true)]
        [MenuItem("AR Test/Build AssetBundle from selected prefab (iOS)", true)]
        [MenuItem("AR Test/Build AssetBundle from selected prefab (Android)", true)]
        public static bool ValidateBuildSelected()
        {
            return TryGetSelectedPrefabPath(out _);
        }

        /// <summary>
        /// Batch/CI: Unity -batchmode -executeMethod
        /// UnityEngine.XR.Templates.AR.Editor.PlacedContentBundleBuilder.BuildForIosBatch
        /// </summary>
        public static void BuildForIosBatch()
        {
            var ok = Build(DefaultPrefabPath, BundleName, BuildTarget.iOS, interactive: false);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>
        /// Batch/CI: Unity -batchmode -executeMethod
        /// UnityEngine.XR.Templates.AR.Editor.PlacedContentBundleBuilder.BuildForAndroidBatch
        /// </summary>
        public static void BuildForAndroidBatch()
        {
            var ok = Build(DefaultPrefabPath, BundleName, BuildTarget.Android, interactive: false);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static void BuildSelected(BuildTarget target)
        {
            if (!TryGetSelectedPrefabPath(out var prefabPath))
            {
                EditorUtility.DisplayDialog(
                    "AssetBundle",
                    "Select a prefab in the Project window first.",
                    "OK");
                return;
            }

            var assetName = Path.GetFileNameWithoutExtension(prefabPath);
            var bundleName = SanitizeBundleName(assetName);
            Build(prefabPath, bundleName, target, interactive: true, displayAssetName: assetName);
        }

        static bool TryGetSelectedPrefabPath(out string prefabPath)
        {
            prefabPath = null;
            var guids = Selection.assetGUIDs;
            if (guids == null || guids.Length == 0)
                return false;

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                return false;

            prefabPath = path;
            return true;
        }

        /// <summary>
        /// Unity AssetBundle names must be lowercase; keep alphanumerics + underscore/dash.
        /// </summary>
        public static string SanitizeBundleName(string assetOrFileName)
        {
            if (string.IsNullOrWhiteSpace(assetOrFileName))
                return "content";

            var lower = assetOrFileName.Trim().ToLowerInvariant();
            lower = Regex.Replace(lower, @"[^a-z0-9_\-]+", "");
            return string.IsNullOrEmpty(lower) ? "content" : lower;
        }

        static bool Build(
            string prefabPath,
            string bundleName,
            BuildTarget target,
            bool interactive,
            string displayAssetName = null)
        {
            if (!File.Exists(prefabPath))
            {
                var msg = $"Missing prefab at:\n{prefabPath}";
                Debug.LogError($"[PlacedContentBundle] {msg}");
                if (interactive)
                    EditorUtility.DisplayDialog("AssetBundle", msg, "OK");
                return false;
            }

            var assetName = string.IsNullOrEmpty(displayAssetName)
                ? Path.GetFileNameWithoutExtension(prefabPath)
                : displayAssetName;

            var platformFolder = target.ToString();
            var outputDir = Path.Combine(OutputRoot, platformFolder);
            Directory.CreateDirectory(outputDir);

            var build = new AssetBundleBuild
            {
                assetBundleName = bundleName,
                assetNames = new[] { prefabPath }
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
                        "AssetBundle",
                        "Build failed. See Console for details.",
                        "OK");
                }

                return false;
            }

            var bundlePath = Path.Combine(outputDir, bundleName);
            var sizeMb = File.Exists(bundlePath)
                ? new FileInfo(bundlePath).Length / (1024f * 1024f)
                : 0f;

            Debug.Log(
                $"[PlacedContentBundle] Built for {target}:\n{Path.GetFullPath(bundlePath)}\n" +
                $"Prefab: {prefabPath}\n" +
                $"unityAssetName: {assetName}\n" +
                $"unityBundleFileName: {bundleName}\n" +
                $"Size ≈ {sizeMb:F2} MB\n" +
                "Upload this file in avento-web (Unity Scene). Set asset name if not PlacedContent.",
                AssetDatabase.LoadAssetAtPath<Object>(prefabPath));

            if (interactive)
            {
                EditorUtility.RevealInFinder(bundlePath);
                EditorUtility.DisplayDialog(
                    "AssetBundle",
                    $"Built OK for {target}\n\n" +
                    $"{bundlePath}\n≈ {sizeMb:F2} MB\n\n" +
                    $"Prefab: {prefabPath}\n" +
                    $"avento-web → unityAssetName: {assetName}\n" +
                    $"avento-web → unityBundleFileName: {bundleName}\n\n" +
                    "Upload this one file (prefab + materials/shaders/videos).\n" +
                    "Build separate bundles per platform (iOS / Android).",
                    "OK");
            }

            return File.Exists(bundlePath) && new FileInfo(bundlePath).Length > 64 * 1024;
        }
    }
}
#endif
