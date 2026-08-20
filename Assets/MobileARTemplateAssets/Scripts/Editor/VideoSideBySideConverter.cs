#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;
using Debug = UnityEngine.Debug;

namespace UnityEngine.XR.Templates.AR.Editor
{
    /// <summary>
    /// Builds H.264 side-by-side (left RGB | right grayscale alpha) clips for device
    /// AssetBundles. Unity VideoPlayer on iOS/Android often drops HEVC-with-alpha;
    /// SBS + <see cref="PlayVideoOnPlace.AlphaLayout.SideBySide"/> is the reliable path.
    /// </summary>
    public static class VideoSideBySideConverter
    {
        public const string Suffix = "_sbs";
        const string FfmpegFilter =
            "[0:v]format=rgba,split[rgb][a];[rgb]format=yuv420p[c];[a]alphaextract,format=yuv420p[m];[c][m]hstack=inputs=2";

        /// <summary>
        /// For every <see cref="PlayVideoOnPlace"/> on the prefab: ensure an up-to-date
        /// <c>*_sbs.mp4</c> exists, assign it as <c>m_VideoClip</c>, and set
        /// <c>m_AlphaLayout = SideBySide</c>. Changes are saved on the prefab (SBS also
        /// works in Editor). Returns a short summary, or null when nothing changed.
        /// </summary>
        public static string ApplySideBySideForDeviceBuild(string prefabPath)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (root == null)
                return null;

            var videos = root.GetComponentsInChildren<PlayVideoOnPlace>(true);
            if (videos == null || videos.Length == 0)
                return null;

            if (!TryFindFfmpeg(out var ffmpegPath, out var ffmpegError))
            {
                throw new InvalidOperationException(
                    "Side-by-side video convert needs ffmpeg on PATH " +
                    $"(or /opt/homebrew/bin/ffmpeg, /usr/local/bin/ffmpeg). {ffmpegError}\n" +
                    "If Unity was opened from Finder, Homebrew may still work via absolute path — " +
                    "install with: brew install ffmpeg");
            }

            Debug.Log($"[VideoSBS] Using ffmpeg at '{ffmpegPath}' for prefab '{prefabPath}'");

            var converted = 0;
            var reused = 0;
            var sb = new StringBuilder();

            try
            {
                for (var i = 0; i < videos.Length; i++)
                {
                    var video = videos[i];
                    if (video == null)
                        continue;

                    EditorUtility.DisplayProgressBar(
                        "Side-by-side video",
                        $"Preparing clip {i + 1}/{videos.Length}…",
                        (float)i / Math.Max(1, videos.Length));

                    var so = new SerializedObject(video);
                    var clipProp = so.FindProperty("m_VideoClip");
                    var layoutProp = so.FindProperty("m_AlphaLayout");
                    if (clipProp == null)
                        continue;

                    if (!TryResolveSourceForComponent(video, clipProp, prefabPath, out var sourcePath, out var resolveNote))
                    {
                        sb.AppendLine($"- {video.name}: skipped ({resolveNote})");
                        continue;
                    }

                    var sbsPath = GetSideBySideAssetPath(sourcePath);
                    var didConvert = EnsureSideBySideAsset(ffmpegPath, sourcePath, sbsPath);
                    AssetDatabase.Refresh();
                    var sbsClip = AssetDatabase.LoadAssetAtPath<VideoClip>(sbsPath);
                    if (sbsClip == null)
                        throw new InvalidOperationException(
                            $"SBS clip failed to import: {sbsPath}");

                    var changed = false;
                    if (clipProp.objectReferenceValue != sbsClip)
                    {
                        clipProp.objectReferenceValue = sbsClip;
                        changed = true;
                    }

                    if (layoutProp != null &&
                        layoutProp.intValue != (int)PlayVideoOnPlace.AlphaLayout.SideBySide)
                    {
                        layoutProp.intValue = (int)PlayVideoOnPlace.AlphaLayout.SideBySide;
                        changed = true;
                    }

                    // Keep VideoPlayer component clip in sync with PlayVideoOnPlace.
                    var player = video.GetComponent<VideoPlayer>();
                    if (player != null && player.clip != sbsClip)
                    {
                        player.clip = sbsClip;
                        EditorUtility.SetDirty(player);
                        changed = true;
                    }

                    if (changed)
                    {
                        so.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(video);
                    }

                    if (didConvert)
                    {
                        converted++;
                        sb.AppendLine(
                            $"- {video.name}: converted '{Path.GetFileName(sourcePath)}' → '{Path.GetFileName(sbsPath)}'");
                    }
                    else
                    {
                        reused++;
                        sb.AppendLine(
                            $"- {video.name}: up-to-date SBS '{Path.GetFileName(sbsPath)}'");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (converted == 0 && reused == 0)
                return null;

            AssetDatabase.SaveAssets();
            return
                $"Side-by-side for device bundle: converted={converted}, reused={reused}\n{sb}";
        }

        [MenuItem("AR Test/Convert selected prefab videos to side-by-side")]
        public static void ConvertSelectedPrefabMenu()
        {
            if (!TryGetSelectedPrefabPathForMenu(out var prefabPath))
            {
                EditorUtility.DisplayDialog(
                    "Side-by-side video",
                    "Select a PlacedContent prefab in the Project window (or a prefab instance in the Hierarchy).",
                    "OK");
                return;
            }

            try
            {
                var summary = ApplySideBySideForDeviceBuild(prefabPath);
                if (string.IsNullOrEmpty(summary))
                {
                    EditorUtility.DisplayDialog(
                        "Side-by-side video",
                        $"No PlayVideoOnPlace clips found on:\n{prefabPath}",
                        "OK");
                    return;
                }

                Debug.Log($"[VideoSBS] {summary}", AssetDatabase.LoadAssetAtPath<Object>(prefabPath));
                EditorUtility.DisplayDialog(
                    "Side-by-side video",
                    summary + "\n\nPrefab:\n" + prefabPath,
                    "OK");
                EditorUtility.RevealInFinder(Path.GetDirectoryName(Path.GetFullPath(prefabPath)));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VideoSBS] {ex.Message}");
                EditorUtility.DisplayDialog("Side-by-side video", ex.Message, "OK");
            }
        }

        [MenuItem("AR Test/Convert selected prefab videos to side-by-side", true)]
        public static bool ValidateConvertSelectedPrefabMenu()
        {
            return TryGetSelectedPrefabPathForMenu(out _);
        }

        static bool TryGetSelectedPrefabPathForMenu(out string prefabPath)
        {
            prefabPath = null;
            var go = Selection.activeGameObject;
            if (go != null)
            {
                var instancePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                if (!string.IsNullOrEmpty(instancePath) &&
                    instancePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    prefabPath = instancePath;
                    return true;
                }
            }

            var guids = Selection.assetGUIDs;
            if (guids == null || guids.Length == 0)
                return false;
            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            if (string.IsNullOrEmpty(path) ||
                !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                return false;
            prefabPath = path;
            return AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
        }

        /// <summary>
        /// Resolves ProRes/alpha source even when the prefab's VideoClip is Missing
        /// (deleted *_sbs.mp4) by looking next to the prefab.
        /// </summary>
        static bool TryResolveSourceForComponent(
            PlayVideoOnPlace video,
            SerializedProperty clipProp,
            string prefabPath,
            out string sourcePath,
            out string note)
        {
            sourcePath = null;
            note = null;

            VideoClip clip = null;
            if (clipProp != null)
                clip = clipProp.objectReferenceValue as VideoClip;
            if (clip == null)
            {
                var player = video != null ? video.GetComponent<VideoPlayer>() : null;
                if (player != null)
                    clip = player.clip;
            }

            if (clip != null)
            {
                var clipPath = AssetDatabase.GetAssetPath(clip);
                if (!string.IsNullOrEmpty(clipPath) &&
                    TryResolveSourceAssetPath(clipPath, out sourcePath))
                {
                    note = "from assigned clip";
                    return true;
                }

                // Assigned clip path exists in DB but file was deleted (Missing SBS).
                if (!string.IsNullOrEmpty(clipPath) &&
                    clipPath.IndexOf(Suffix, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    TryResolveSourceAssetPath(clipPath, out sourcePath))
                {
                    note = "from missing SBS sibling";
                    return true;
                }
            }

            if (TryFindAlphaSourceNearPrefab(prefabPath, out sourcePath))
            {
                note = "from prefab folder";
                return true;
            }

            note = "no VideoClip and no ProRes/MOV source next to prefab";
            return false;
        }

        /// <summary>
        /// Prefers a non-_sbs .mov/.mp4 in the same folder as the prefab (DaVinci ProRes source).
        /// </summary>
        public static bool TryFindAlphaSourceNearPrefab(string prefabPath, out string sourcePath)
        {
            sourcePath = null;
            var dir = Path.GetDirectoryName(prefabPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return false;

            string[] exts = { "*.mov", "*.mp4", "*.m4v", "*.webm" };
            var candidates = new System.Collections.Generic.List<string>();
            for (var i = 0; i < exts.Length; i++)
            {
                var files = Directory.GetFiles(dir, exts[i]);
                for (var f = 0; f < files.Length; f++)
                {
                    var name = Path.GetFileNameWithoutExtension(files[f]);
                    if (name.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (name.StartsWith(".", StringComparison.Ordinal))
                        continue;
                    candidates.Add(files[f].Replace('\\', '/'));
                }
            }

            if (candidates.Count == 0)
                return false;

            // Prefer ProRes-sized / .mov first (typical DaVinci alpha master).
            candidates.Sort((a, b) =>
            {
                var am = a.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                var bm = b.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                if (am != bm)
                    return am.CompareTo(bm);
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });

            sourcePath = candidates[0];
            return File.Exists(sourcePath);
        }

        /// <summary>
        /// If <paramref name="clipPath"/> is already <c>*_sbs.*</c>, find the alpha
        /// source sibling; otherwise the clip itself is the source.
        /// </summary>
        public static bool TryResolveSourceAssetPath(string clipPath, out string sourcePath)
        {
            sourcePath = null;
            if (string.IsNullOrEmpty(clipPath))
                return false;

            var fileName = Path.GetFileNameWithoutExtension(clipPath);
            if (!fileName.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
            {
                sourcePath = clipPath.Replace('\\', '/');
                return File.Exists(sourcePath);
            }

            var dir = Path.GetDirectoryName(clipPath)?.Replace('\\', '/') ?? string.Empty;
            var stem = fileName.Substring(0, fileName.Length - Suffix.Length);
            string[] exts = { ".mov", ".mp4", ".webm", ".m4v" };
            for (var i = 0; i < exts.Length; i++)
            {
                var candidate = $"{dir}/{stem}{exts[i]}";
                if (File.Exists(candidate))
                {
                    sourcePath = candidate;
                    return true;
                }
            }

            // No master — caller can keep the existing SBS clip.
            return false;
        }
        public static string GetSideBySideAssetPath(string sourceAssetPath)
        {
            var dir = Path.GetDirectoryName(sourceAssetPath)?.Replace('\\', '/') ?? "Assets";
            var stem = Path.GetFileNameWithoutExtension(sourceAssetPath);
            if (stem.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
                stem = stem.Substring(0, stem.Length - Suffix.Length);
            return $"{dir}/{stem}{Suffix}.mp4";
        }

        /// <returns>True when ffmpeg ran; false when existing SBS is newer than source.</returns>
        public static bool EnsureSideBySideAsset(string ffmpegPath, string sourceAssetPath, string sbsAssetPath)
        {
            var sourceFull = Path.GetFullPath(sourceAssetPath);
            var sbsFull = Path.GetFullPath(sbsAssetPath);
            if (!File.Exists(sourceFull))
                throw new FileNotFoundException("Alpha source video missing.", sourceFull);

            RejectBadAlphaSource(sourceFull, sourceAssetPath);

            if (File.Exists(sbsFull) &&
                File.GetLastWriteTimeUtc(sbsFull) >= File.GetLastWriteTimeUtc(sourceFull) &&
                new FileInfo(sbsFull).Length > 1024 &&
                SideBySideHasAlphaMatte(ffmpegPath, sbsFull))
            {
                ConfigureImporter(sbsAssetPath);
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(sbsFull) ?? ".");
            var tmpFull = sbsFull + ".tmp.mp4";
            if (File.Exists(tmpFull))
                File.Delete(tmpFull);

            var args =
                $"-y -i \"{sourceFull}\" " +
                $"-filter_complex \"{FfmpegFilter}\" " +
                "-c:v libx264 -pix_fmt yuv420p -profile:v high -level 4.1 -crf 18 " +
                "-movflags +faststart -an " +
                $"\"{tmpFull}\"";

            Debug.Log($"[VideoSBS] ffmpeg {args}");
            var exit = RunProcess(ffmpegPath, args, out var stdout, out var stderr);
            if (exit != 0 || !File.Exists(tmpFull) || new FileInfo(tmpFull).Length < 1024)
            {
                if (File.Exists(tmpFull))
                    File.Delete(tmpFull);
                throw new InvalidOperationException(
                    $"ffmpeg failed (exit {exit}) converting '{sourceAssetPath}' to SBS.\n{stderr}\n{stdout}");
            }

            if (!SideBySideHasAlphaMatte(ffmpegPath, tmpFull))
            {
                File.Delete(tmpFull);
                throw new InvalidOperationException(
                    $"SBS convert produced an empty alpha matte (right half all black) for '{sourceAssetPath}'.\n" +
                    "Export Apple ProRes 4444 (with alpha) from DaVinci Resolve and use that .mov as the source, " +
                    "then rebuild the AssetBundle. Do not use Resolve HEVC multilayer as the SBS source.");
            }

            if (File.Exists(sbsFull))
                File.Delete(sbsFull);
            File.Move(tmpFull, sbsFull);

            AssetDatabase.ImportAsset(sbsAssetPath, ImportAssetOptions.ForceUpdate);
            ConfigureImporter(sbsAssetPath);
            AssetDatabase.ImportAsset(sbsAssetPath, ImportAssetOptions.ForceUpdate);
            return true;
        }

        /// <summary>
        /// Resolve HEVC multilayer often yields an empty matte in ffmpeg. Require ProRes (or
        /// another non-multilayer alpha source) from DaVinci.
        /// </summary>
        static void RejectBadAlphaSource(string sourceFullPath, string assetPathForMessage)
        {
            try
            {
                var bytes = File.ReadAllBytes(sourceFullPath);
                // 'lhvC' = layered HEVC config (Resolve HEVC-with-alpha multilayer).
                if (IndexOfBytes(bytes, System.Text.Encoding.ASCII.GetBytes("lhvC")) >= 0)
                {
                    throw new InvalidOperationException(
                        $"Alpha source '{assetPathForMessage}' is Resolve HEVC multilayer (lhvC). " +
                        "ffmpeg cannot extract a reliable alpha matte from it (SBS right half goes black).\n" +
                        "In DaVinci Resolve: Deliver → Apple ProRes 4444 (with alpha) → replace this .mov, then rebuild.");
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VideoSBS] Could not inspect source codec markers: {ex.Message}");
            }
        }

        /// <summary>
        /// Samples one frame of the right half: mean luma must not be ~0 (empty matte).
        /// </summary>
        static bool SideBySideHasAlphaMatte(string ffmpegPath, string sbsFullPath)
        {
            var args =
                $"-i \"{sbsFullPath}\" -frames:v 1 " +
                "-vf \"crop=iw/2:ih:iw/2:0,signalstats,metadata=print:file=-\" " +
                "-f null -";
            var exit = RunProcess(ffmpegPath, args, out var stdout, out var stderr, timeoutMs: 120000);
            if (exit != 0)
            {
                Debug.LogWarning($"[VideoSBS] Matte check failed (exit {exit}): {stderr}");
                return false;
            }

            var text = stdout + "\n" + stderr;
            const string key = "YAVG=";
            var idx = text.LastIndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                Debug.LogWarning("[VideoSBS] Matte check: YAVG not found in ffmpeg output.");
                return false;
            }

            var start = idx + key.Length;
            var end = start;
            while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.' || text[end] == '-'))
                end++;
            if (!double.TryParse(
                    text.Substring(start, end - start),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var yAvg))
                return false;

            // Empty matte ≈ all black → YAVG near 0. Real masks have opaque whites.
            return yAvg > 5.0;
        }

        static int IndexOfBytes(byte[] haystack, byte[] needle)
        {
            for (var i = 0; i <= haystack.Length - needle.Length; i++)
            {
                var ok = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                    return i;
            }

            return -1;
        }

        static void ConfigureImporter(string sbsAssetPath)
        {
            var importer = AssetImporter.GetAtPath(sbsAssetPath) as VideoClipImporter;
            if (importer == null)
                return;

            // Side-by-side packs alpha in RGB of the right half — no Unity alpha encode.
            var so = new SerializedObject(importer);
            var encodeAlpha = so.FindProperty("encodeAlpha");
            var importAudio = so.FindProperty("importAudio");
            if (encodeAlpha != null)
                encodeAlpha.boolValue = false;
            if (importAudio != null)
                importAudio.boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();

            try
            {
                var settings = importer.defaultTargetSettings;
                settings.enableTranscoding = false;
                importer.defaultTargetSettings = settings;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VideoSBS] Could not disable transcoding on {sbsAssetPath}: {ex.Message}");
            }

            importer.SaveAndReimport();
        }

        public static bool TryFindFfmpeg(out string path, out string error)
        {
            path = null;
            error = null;

            string[] candidates =
            {
                "/opt/homebrew/bin/ffmpeg",
                "/usr/local/bin/ffmpeg",
                "/usr/bin/ffmpeg",
                "ffmpeg"
            };

            for (var i = 0; i < candidates.Length; i++)
            {
                var c = candidates[i];
                if (c != "ffmpeg" && !File.Exists(c))
                    continue;

                try
                {
                    var exit = RunProcess(c, "-version", out _, out var err, timeoutMs: 5000);
                    if (exit == 0)
                    {
                        path = c;
                        return true;
                    }

                    error = err;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }

            if (string.IsNullOrEmpty(error))
                error = "ffmpeg executable not found.";
            return false;
        }

        static int RunProcess(string fileName, string arguments, out string stdout, out string stderr, int timeoutMs = 600000)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var p = Process.Start(psi))
            {
                if (p == null)
                {
                    stdout = string.Empty;
                    stderr = "Failed to start process.";
                    return -1;
                }

                stdout = p.StandardOutput.ReadToEnd();
                stderr = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { /* ignore */ }
                    stderr += "\n(process timed out)";
                    return -1;
                }

                return p.ExitCode;
            }
        }
    }
}
#endif
