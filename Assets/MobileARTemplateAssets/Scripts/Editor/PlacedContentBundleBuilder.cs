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
    /// <c>Assets/PlacedContent.prefab</c>) as local file <c>placedcontent</c>.
    /// Selected-prefab menus pack the Project prefab (or Hierarchy prefab instance)
    /// and write a unique local file from the parent folder when the prefab is
    /// named <c>PlacedContent</c>, with a platform suffix and <c>.bundle</c> extension
    /// matching avento-web <c>generateUnityBundleFileName</c>
    /// (e.g. <c>demos/art-galary/PlacedContent.prefab</c> → <c>art-galary.ios.bundle</c> / <c>art-galary.android.bundle</c>).
    /// iOS/Android builds auto-convert <see cref="PlayVideoOnPlace"/> clips to side-by-side
    /// H.264 (<c>*_sbs.mp4</c>) via ffmpeg before packing.
    /// </summary>
    public static class PlacedContentBundleBuilder
    {
        const string ResourcesPrefabPath = "Assets/Resources/PlacedContent.prefab";
        const string RootPrefabPath = "Assets/PlacedContent.prefab";
        const string BundleName = "placedcontent";
        const string OutputRoot = "AssetBundles";
        static readonly string[] GenericFolderNames =
        {
            "assets", "scenes", "resources", "prefabs", "prefab"
        };

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
                    "Select a prefab in the Project window, or a prefab instance in the Hierarchy.",
                    "OK");
                return;
            }

            if (!ConfirmUnappliedOverridesIfNeeded(prefabPath))
                return;

            var assetName = Path.GetFileNameWithoutExtension(prefabPath);
            var bundleName = UniqueBundleNameForPrefab(prefabPath);
            Build(prefabPath, bundleName, target, interactive: true, displayAssetName: assetName);
        }

        static bool TryGetSelectedPrefabPath(out string prefabPath)
        {
            prefabPath = null;

            // Prefer Hierarchy: Project can still have Resources/PlacedContent highlighted
            // while the user is looking at a demo instance in the scene.
            var go = Selection.activeGameObject;
            if (go != null)
            {
                var instancePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                if (IsPrefabAssetPath(instancePath))
                {
                    prefabPath = instancePath;
                    return true;
                }
            }

            var guids = Selection.assetGUIDs;
            if (guids == null || guids.Length == 0)
                return false;

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            if (!IsPrefabAssetPath(path))
                return false;

            prefabPath = path;
            return true;
        }

        static bool IsPrefabAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path) ||
                !path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                return false;
            return AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
        }

        static bool ConfirmUnappliedOverridesIfNeeded(string prefabPath)
        {
            var go = Selection.activeGameObject;
            if (go == null)
                return true;

            var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(go);
            if (instanceRoot == null)
                return true;

            var instanceAsset = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
            if (!string.Equals(instanceAsset, prefabPath, System.StringComparison.OrdinalIgnoreCase))
                return true;

            if (!PrefabUtility.HasPrefabInstanceAnyOverrides(instanceRoot, false))
                return true;

            return EditorUtility.DisplayDialog(
                "Unapplied prefab overrides",
                "This Hierarchy object has unapplied prefab overrides.\n\n" +
                "The bundle packs the prefab on disk, not unsaved scene edits.\n\n" +
                $"Prefab:\n{prefabPath}\n\n" +
                "Apply overrides in the Inspector first if you meant to ship the scene version.",
                "Pack prefab anyway",
                "Cancel");
        }

        /// <summary>
        /// Content base name (without platform). Default Resources/root PlacedContent stays
        /// <c>placedcontent</c>. Demo prefabs all named PlacedContent use the parent folder
        /// (<c>Assets/Scenes/demos/portal/PlacedContent.prefab</c> → <c>portal</c>).
        /// <see cref="OutputBundleFileName"/> appends <c>-ios</c> / <c>-android</c>.
        /// </summary>
        public static string UniqueBundleNameForPrefab(string prefabPath)
        {
            if (string.IsNullOrWhiteSpace(prefabPath))
                return BundleName;

            var normalized = prefabPath.Replace('\\', '/');
            if (string.Equals(normalized, ResourcesPrefabPath, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, RootPrefabPath, System.StringComparison.OrdinalIgnoreCase))
                return BundleName;

            var stem = SanitizeBundleName(Path.GetFileNameWithoutExtension(prefabPath));
            var folder = MeaningfulFolderName(prefabPath);

            if (string.Equals(stem, BundleName, System.StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrEmpty(folder) ? BundleName : folder;

            if (!string.IsNullOrEmpty(folder) &&
                !string.Equals(folder, stem, System.StringComparison.OrdinalIgnoreCase))
                return folder + "_" + stem;

            return string.IsNullOrEmpty(stem) ? "content" : stem;
        }

        static string MeaningfulFolderName(string prefabPath)
        {
            var dir = Path.GetDirectoryName(prefabPath);
            while (!string.IsNullOrEmpty(dir))
            {
                var sanitized = SanitizeBundleName(Path.GetFileName(dir));
                if (!string.IsNullOrEmpty(sanitized) && !IsGenericFolderName(sanitized))
                    return sanitized;
                dir = Path.GetDirectoryName(dir);
            }

            return null;
        }

        static bool IsGenericFolderName(string name)
        {
            for (var i = 0; i < GenericFolderNames.Length; i++)
            {
                if (string.Equals(name, GenericFolderNames[i], System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
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

        /// <summary>
        /// Local output file name: <c>{contentBase}.ios.bundle</c> / <c>{contentBase}.android.bundle</c>.
        /// Matches avento-web <c>generateUnityBundleFileName</c> in <c>vr-upload.ts</c>.
        /// </summary>
        public static string OutputBundleFileName(string contentBaseName, BuildTarget target)
        {
            var baseName = SanitizeBundleName(contentBaseName);
            var platform = PlatformSuffix(target);
            var dotPlatform = "." + platform;
            if (baseName.EndsWith(dotPlatform, System.StringComparison.OrdinalIgnoreCase))
                baseName = baseName.Substring(0, baseName.Length - dotPlatform.Length);
            return baseName + "." + platform + ".bundle";
        }

        static string PlatformSuffix(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.iOS:
                    return "ios";
                case BuildTarget.Android:
                    return "android";
                default:
                    return SanitizeBundleName(target.ToString());
            }
        }

        static bool Build(
            string prefabPath,
            string contentBaseName,
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

            var bundleName = OutputBundleFileName(contentBaseName, target);
            var outputDir = OutputRoot;
            Directory.CreateDirectory(outputDir);

            var panoramaSummary = ApplyPanoramaContentModeForBuild(prefabPath, out var restorePanorama);
            if (!string.IsNullOrEmpty(panoramaSummary))
                Debug.Log($"[PlacedContentBundle] {panoramaSummary}", AssetDatabase.LoadAssetAtPath<Object>(prefabPath));

            var videoSummary = ApplyVideoEditorFallbacksForBuild(prefabPath, out var restoreVideo);
            if (!string.IsNullOrEmpty(videoSummary))
                Debug.Log($"[PlacedContentBundle] {videoSummary}", AssetDatabase.LoadAssetAtPath<Object>(prefabPath));

            string sbsSummary = null;
            if (target == BuildTarget.iOS || target == BuildTarget.Android)
            {
                try
                {
                    sbsSummary = VideoSideBySideConverter.ApplySideBySideForDeviceBuild(prefabPath);
                    if (!string.IsNullOrEmpty(sbsSummary))
                        Debug.Log($"[PlacedContentBundle] {sbsSummary}", AssetDatabase.LoadAssetAtPath<Object>(prefabPath));
                }
                catch (System.Exception ex)
                {
                    restorePanorama?.Invoke();
                    restoreVideo?.Invoke();
                    Debug.LogError(
                        $"[PlacedContentBundle] Side-by-side convert failed for {prefabPath}: {ex.Message}");
                    if (interactive)
                    {
                        EditorUtility.DisplayDialog(
                            "AssetBundle",
                            "Side-by-side video convert failed.\n\n" +
                            ex.Message +
                            "\n\nInstall ffmpeg (brew install ffmpeg) and retry.",
                            "OK");
                    }

                    return false;
                }
            }

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

                try
                {
                    restoreVideo?.Invoke();
                }
                catch (System.Exception ex)
                {
                    Debug.LogError(
                        $"[PlacedContentBundle] Failed to restore video editor fallbacks on {prefabPath}: {ex.Message}");
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

            var extra = string.Empty;
            if (!string.IsNullOrEmpty(panoramaSummary))
                extra += panoramaSummary + "\n";
            if (!string.IsNullOrEmpty(sbsSummary))
                extra += sbsSummary + "\n";
            Debug.Log(
                $"[PlacedContentBundle] Built for {target}:\n{Path.GetFullPath(bundlePath)}\n" +
                extra +
                $"Prefab: {prefabPath}\n" +
                $"unityAssetName (optional override): {assetName}\n" +
                $"content base: {contentBaseName}\n" +
                $"local file: {bundleName}\n" +
                $"Size ≈ {sizeMb:F2} MB\n" +
                "Upload this file in avento-web (Unity Scene). " +
                "The file name ({content}-{ios|android}.bundle) is used as the MinIO key. " +
                "Leave Asset name blank in admin to auto-load the first prefab.",
                AssetDatabase.LoadAssetAtPath<Object>(prefabPath));

            if (interactive)
            {
                EditorUtility.RevealInFinder(bundlePath);
                EditorUtility.DisplayDialog(
                    "AssetBundle",
                    $"Built OK for {target}\n\n" +
                    $"Upload this file:\n{bundlePath}\n≈ {sizeMb:F2} MB\n\n" +
                    extra +
                    $"Prefab: {prefabPath}\n" +
                    $"Local file name: {bundleName}\n" +
                    $"(content: {contentBaseName}, platform: {PlatformSuffix(target)})\n\n" +
                    $"Suggested unityAssetName (optional): {assetName}\n\n" +
                    "File name is used as MinIO key (synced with avento-web naming).\n" +
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

        /// <summary>
        /// Device AssetBundles only need the device <c>m_VideoClip</c> (HEVC-with-alpha).
        /// Editor/Sim fallbacks (QT RLE / opaque H.264) are stripped so they are not packed.
        /// </summary>
        static string ApplyVideoEditorFallbacksForBuild(string prefabPath, out System.Action restore)
        {
            restore = null;
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (root == null)
                return null;

            var videos = root.GetComponentsInChildren<PlayVideoOnPlace>(true);
            if (videos == null || videos.Length == 0)
                return null;

            var restores = new System.Collections.Generic.List<System.Action>();
            var stripped = 0;

            for (var i = 0; i < videos.Length; i++)
            {
                var video = videos[i];
                if (video == null)
                    continue;

                var so = new SerializedObject(video);
                var fallbackProp = so.FindProperty("m_EditorFallbackClip");
                var opaqueProp = so.FindProperty("m_EditorOpaqueFallbackClip");
                if (fallbackProp == null && opaqueProp == null)
                    continue;

                var fallback = fallbackProp != null ? fallbackProp.objectReferenceValue : null;
                var opaque = opaqueProp != null ? opaqueProp.objectReferenceValue : null;
                if (fallback == null && opaque == null)
                    continue;

                var fallbackPath = fallback != null ? AssetDatabase.GetAssetPath(fallback) : null;
                var opaquePath = opaque != null ? AssetDatabase.GetAssetPath(opaque) : null;
                var videoIndex = i;

                if (fallbackProp != null)
                    fallbackProp.objectReferenceValue = null;
                if (opaqueProp != null)
                    opaqueProp.objectReferenceValue = null;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(video);
                stripped++;

                restores.Add(() =>
                {
                    var restoredRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (restoredRoot == null)
                        return;
                    var restoredVideos = restoredRoot.GetComponentsInChildren<PlayVideoOnPlace>(true);
                    if (restoredVideos == null || videoIndex >= restoredVideos.Length)
                        return;
                    var restored = restoredVideos[videoIndex];
                    if (restored == null)
                        return;

                    var restoreSo = new SerializedObject(restored);
                    if (!string.IsNullOrEmpty(fallbackPath))
                    {
                        var prop = restoreSo.FindProperty("m_EditorFallbackClip");
                        if (prop != null)
                            prop.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Object>(fallbackPath);
                    }

                    if (!string.IsNullOrEmpty(opaquePath))
                    {
                        var prop = restoreSo.FindProperty("m_EditorOpaqueFallbackClip");
                        if (prop != null)
                            prop.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Object>(opaquePath);
                    }

                    restoreSo.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(restored);
                });
            }

            if (stripped == 0)
                return null;

            AssetDatabase.SaveAssets();
            restore = () =>
            {
                for (var i = 0; i < restores.Count; i++)
                    restores[i]?.Invoke();
                AssetDatabase.SaveAssets();
            };

            return $"Stripped editor video fallbacks on {stripped} PlayVideoOnPlace (device bundle keeps HEVC-with-alpha clip only)";
        }
    }
}
#endif
