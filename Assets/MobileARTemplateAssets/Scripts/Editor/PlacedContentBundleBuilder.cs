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

            var panoramaSummary = ApplyPanoramaContentModeForBuild(prefabPath, out var restorePanorama);
            if (!string.IsNullOrEmpty(panoramaSummary))
                Debug.Log($"[PlacedContentBundle] {panoramaSummary}", AssetDatabase.LoadAssetAtPath<Object>(prefabPath));

            AssetBundleManifest manifest;
            try
            {
                var build = new AssetBundleBuild
                {
                    assetBundleName = bundleName,
                    assetNames = new[] { prefabPath }
                };

                manifest = BuildPipeline.BuildAssetBundles(
                    outputDir,
                    new[] { build },
                    BuildAssetBundleOptions.ChunkBasedCompression |
                    BuildAssetBundleOptions.StrictMode,
                    target);
            }
            finally
            {
                try
                {
                    restorePanorama?.Invoke();
                }
                catch (System.Exception ex)
                {
                    Debug.LogError(
                        $"[PlacedContentBundle] Failed to restore panorama refs on {prefabPath}: {ex.Message}");
                }
            }

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

            var extra = string.IsNullOrEmpty(panoramaSummary) ? string.Empty : $"{panoramaSummary}\n";
            Debug.Log(
                $"[PlacedContentBundle] Built for {target}:\n{Path.GetFullPath(bundlePath)}\n" +
                extra +
                $"Prefab: {prefabPath}\n" +
                $"unityAssetName (optional override): {assetName}\n" +
                $"local file: {bundleName}\n" +
                $"Size ≈ {sizeMb:F2} MB\n" +
                "Upload this file in avento-web (Unity Scene). MinIO stores a GUID; " +
                "the app caches by that GUID (local filename does not matter after upload). " +
                "Leave Asset name blank in admin to auto-load the first prefab.",
                AssetDatabase.LoadAssetAtPath<Object>(prefabPath));

            if (interactive)
            {
                EditorUtility.RevealInFinder(bundlePath);
                EditorUtility.DisplayDialog(
                    "AssetBundle",
                    $"Built OK for {target}\n\n" +
                    $"{bundlePath}\n≈ {sizeMb:F2} MB\n\n" +
                    extra +
                    $"Prefab: {prefabPath}\n" +
                    $"Suggested unityAssetName (optional): {assetName}\n" +
                    $"Local output name: {bundleName}\n\n" +
                    "Upload this one file in avento-web.\n" +
                    "After upload, identity is the MinIO GUID (not this filename).\n" +
                    "Build separate bundles per platform (iOS / Android).",
                    "OK");
            }

            return File.Exists(bundlePath) && new FileInfo(bundlePath).Length > 64 * 1024;
        }

        static PanoramaSkyboxViewer FindPanoramaViewer(string prefabPath)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (root == null)
                return null;
            return root.GetComponent<PanoramaSkyboxViewer>() ??
                   root.GetComponentInChildren<PanoramaSkyboxViewer>(true);
        }

        /// <summary>
        /// Pack only the media that Content Mode uses (Still Image PNG or Video MP4).
        /// The unused clip/texture is omitted from this bundle, then restored on the prefab.
        /// </summary>
        static string ApplyPanoramaContentModeForBuild(string prefabPath, out System.Action restore)
        {
            restore = null;
            var viewer = FindPanoramaViewer(prefabPath);
            if (viewer == null)
                return null;

            var so = new SerializedObject(viewer);
            var modeProp = so.FindProperty("m_ContentMode");
            var texProp = so.FindProperty("m_PanoramaTexture");
            var clipProp = so.FindProperty("m_VideoClip");
            if (modeProp == null)
                return null;

            var mode = (PanoramaSkyboxViewer.ContentMode)modeProp.intValue;
            var texture = texProp != null ? texProp.objectReferenceValue : null;
            var clip = clipProp != null ? clipProp.objectReferenceValue : null;
            var clipPath = clip != null ? AssetDatabase.GetAssetPath(clip) : null;
            var texturePath = texture != null ? AssetDatabase.GetAssetPath(texture) : null;
            var opacityProp = so.FindProperty("m_Opacity");
            var opacity = opacityProp != null ? Mathf.Clamp01(opacityProp.floatValue) : -1f;
            var strippedClip = false;
            var strippedTexture = false;

            string summary;
            if (mode == PanoramaSkyboxViewer.ContentMode.StillImage)
            {
                summary = texture != null
                    ? $"Panorama Content Mode = Still Image; packing '{texture.name}'"
                    : "Panorama Content Mode = Still Image (no Panorama Texture assigned)";
                if (clip != null && clipProp != null)
                {
                    clipProp.objectReferenceValue = null;
                    strippedClip = true;
                    summary += $"; omitting Video Clip '{clip.name}' from this bundle";
                }
            }
            else
            {
                summary = clip != null
                    ? $"Panorama Content Mode = Video; packing '{clip.name}'"
                    : "Panorama Content Mode = Video (no Video Clip assigned)";
                if (texture != null && texProp != null)
                {
                    texProp.objectReferenceValue = null;
                    strippedTexture = true;
                    summary += $"; omitting still '{texture.name}' from this bundle";
                }
            }

            if (opacity >= 0f)
                summary += $"; dome opacity {opacity:0.###} ({opacity * 100f:0}%)";

            if (!strippedClip && !strippedTexture)
                return summary;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(viewer);
            AssetDatabase.SaveAssets();

            restore = () =>
            {
                var restoredViewer = FindPanoramaViewer(prefabPath);
                if (restoredViewer == null)
                {
                    Debug.LogError(
                        $"[PlacedContentBundle] Could not reload {prefabPath} to restore panorama refs.");
                    return;
                }

                var restoreSo = new SerializedObject(restoredViewer);
                if (strippedClip && !string.IsNullOrEmpty(clipPath))
                    restoreSo.FindProperty("m_VideoClip").objectReferenceValue =
                        AssetDatabase.LoadAssetAtPath<Object>(clipPath);
                if (strippedTexture && !string.IsNullOrEmpty(texturePath))
                    restoreSo.FindProperty("m_PanoramaTexture").objectReferenceValue =
                        AssetDatabase.LoadAssetAtPath<Object>(texturePath);
                restoreSo.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(restoredViewer);
                AssetDatabase.SaveAssets();
            };

            return summary;
        }
    }
}
#endif
