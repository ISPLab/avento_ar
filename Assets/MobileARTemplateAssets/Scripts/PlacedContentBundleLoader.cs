using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Loads a self-contained PlacedContent AssetBundle from StreamingAssets,
    /// persistent cache, a remote HTTPS URL, or an absolute path from the native host.
    /// </summary>
    public class PlacedContentBundleLoader : MonoBehaviour
    {
        public const string DefaultBundleFileName = "placedcontent";
        public const string DefaultAssetName = "PlacedContent";

        /// <summary>Catalog files like AssetBundles/iOS/iOS are ~1–2 KB; real content is multi‑MB.</summary>
        public const long MinBundleBytes = 64 * 1024;

        [SerializeField]
        string m_BundleFileName = DefaultBundleFileName;

        [SerializeField]
        string m_AssetName = DefaultAssetName;

        [Tooltip("Optional HTTPS URL to the platform AssetBundle (cloud). Leave empty to use local files only.")]
        [SerializeField]
        string m_RemoteBundleUrl;

        [SerializeField]
        bool m_LoadOnAwake = true;

        AssetBundle m_Bundle;
        GameObject m_Prefab;
        bool m_Loading;
        Coroutine m_LoadRoutine;
        int m_LoadGeneration;
        bool m_LoadFailedNotified;
        string m_AbsoluteLoadPath;

        public GameObject LoadedPrefab => m_Prefab;
        public bool IsLoaded => m_Prefab != null;
        public bool IsLoading => m_Loading;

        public event Action<GameObject> PrefabLoaded;
        public event Action<string> LoadFailed;

        /// <summary>Override asset name before loading (UaaL host). Blank → first GameObject.</summary>
        public void Configure(string assetName, string bundleFileName = null)
        {
            m_AssetName = string.IsNullOrWhiteSpace(assetName) ? string.Empty : assetName.Trim();
            if (!string.IsNullOrWhiteSpace(bundleFileName))
                m_BundleFileName = bundleFileName.Trim();
        }

        public void SetLoadOnAwake(bool enabled) => m_LoadOnAwake = enabled;

        /// <summary>Load a bundle already downloaded by the native host (absolute path).</summary>
        public void BeginLoadFromAbsolutePath(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                NotifyFailed("Empty bundle path.");
                return;
            }

            absolutePath = SanitizeAbsolutePath(absolutePath);

            // Duplicate OpenFromNative (present + awaitingOpen) must not cancel an in-flight
            // LoadFromFileAsync — that leaves the bundle loaded in the player with m_Bundle=null,
            // so the retry's LoadFromFile/Memory returns null and looks like a version mismatch.
            if (string.Equals(m_AbsoluteLoadPath, absolutePath, StringComparison.Ordinal) &&
                (m_Prefab != null || m_Loading))
            {
                if (m_Prefab != null)
                    PrefabLoaded?.Invoke(m_Prefab);
                Debug.Log(
                    $"[PlacedContentBundle] Skipping duplicate load of {absolutePath} " +
                    $"(loaded={m_Prefab != null} loading={m_Loading})",
                    this);
                return;
            }

            // Native path always wins — cancel StreamingAssets / cache probes started by TapToPlace.
            CancelInFlightLoad();
            m_LoadFailedNotified = false;
            m_AbsoluteLoadPath = absolutePath;
            m_LoadRoutine = StartCoroutine(LoadAbsolutePathCoroutine(absolutePath));
        }

        static string SanitizeAbsolutePath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
                return absolutePath;

            absolutePath = absolutePath.Replace("\\/", "/");
            if (absolutePath.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    absolutePath = new Uri(absolutePath).LocalPath;
                }
                catch
                {
                    // keep as-is
                }
            }

            return absolutePath;
        }

        IEnumerator LoadAbsolutePathCoroutine(string absolutePath)
        {
            var gen = ++m_LoadGeneration;
            m_Loading = true;

            if (!File.Exists(absolutePath))
            {
                if (gen == m_LoadGeneration)
                {
                    m_Loading = false;
                    m_LoadRoutine = null;
                    NotifyFailed(
                        $"[PlacedContentBundle] File not found: {absolutePath}");
                }

                yield break;
            }

            var info = new FileInfo(absolutePath);
            var header = ReadBundleHeader(absolutePath);
            Debug.Log(
                $"[PlacedContentBundle] Opening {absolutePath} size={info.Length} " +
                $"magic={header.magic} format={header.unityVersion} engine={header.engineRevision} " +
                $"iosMetal={header.looksLikeIos}",
                this);

            if (info.Length < MinBundleBytes)
            {
                if (gen == m_LoadGeneration)
                {
                    m_Loading = false;
                    m_LoadRoutine = null;
                    NotifyFailed(
                        $"[PlacedContentBundle] File too small ({info.Length} bytes) — " +
                        "likely uploaded the AssetBundles/iOS/iOS catalog instead of the content pack (~20MB). " +
                        $"path={absolutePath}");
                }

                yield break;
            }

            if (!string.Equals(header.magic, "UnityFS", StringComparison.Ordinal))
            {
                if (gen == m_LoadGeneration)
                {
                    m_Loading = false;
                    m_LoadRoutine = null;
                    NotifyFailed(
                        $"[PlacedContentBundle] Not a Unity AssetBundle (magic={header.magic}). " +
                        "Re-upload the iOS UnityFS file from AssetBundles/iOS/.");
                }

                yield break;
            }

            UnloadBundleOnly();
            yield return null;
            yield return LoadFromFile(absolutePath, gen, header);

            if (gen != m_LoadGeneration)
                yield break;

            m_Loading = false;
            m_LoadRoutine = null;
            if (m_Prefab == null && !m_LoadFailedNotified)
            {
                NotifyFailed(
                    $"Failed to load AssetBundle at {absolutePath} " +
                    $"(size={info.Length}, magic={header.magic}, unity={header.unityVersion}). " +
                    "Need iOS-built placedcontent matching this player (not Android, not the iOS catalog file).");
            }
        }

        void Awake()
        {
            if (m_LoadOnAwake)
                BeginLoad();
        }

        void OnDestroy()
        {
            CancelInFlightLoad();
            Unload();
        }

        public void BeginLoad()
        {
            if (m_Prefab != null || m_Loading)
                return;

            CancelInFlightLoad();
            m_LoadFailedNotified = false;
            m_LoadRoutine = StartCoroutine(LoadCoroutine());
        }

        public IEnumerator LoadCoroutine()
        {
            var gen = ++m_LoadGeneration;
            m_Loading = true;

            var cachedPath = Path.Combine(Application.persistentDataPath, m_BundleFileName);
            if (File.Exists(cachedPath))
            {
                yield return LoadFromFile(cachedPath, gen, ReadBundleHeader(cachedPath));
                if (gen != m_LoadGeneration)
                    yield break;
                if (m_Prefab != null)
                {
                    m_Loading = false;
                    m_LoadRoutine = null;
                    yield break;
                }
            }

            var streamingPath = Path.Combine(Application.streamingAssetsPath, m_BundleFileName);
#if UNITY_ANDROID && !UNITY_EDITOR
            yield return LoadFromUrl(streamingPath, gen);
#else
            if (File.Exists(streamingPath))
                yield return LoadFromFile(streamingPath, gen, ReadBundleHeader(streamingPath));
#endif
            if (gen != m_LoadGeneration)
                yield break;
            if (m_Prefab != null)
            {
                m_Loading = false;
                m_LoadRoutine = null;
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(m_RemoteBundleUrl))
            {
                yield return DownloadAndCache(m_RemoteBundleUrl, cachedPath, gen);
                if (gen != m_LoadGeneration)
                    yield break;
                if (m_Prefab != null)
                {
                    m_Loading = false;
                    m_LoadRoutine = null;
                    yield break;
                }
            }

            if (gen != m_LoadGeneration)
                yield break;

            m_Loading = false;
            m_LoadRoutine = null;
            NotifyFailed(
                $"[PlacedContentBundle] Failed to load '{m_BundleFileName}'. " +
                "Build via menu AR Test → Build PlacedContent AssetBundle.");
        }

        IEnumerator DownloadAndCache(string url, string cachePath, int gen)
        {
            Debug.Log($"[PlacedContentBundle] Downloading {url}", this);
            using var request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();

            if (gen != m_LoadGeneration)
                yield break;

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[PlacedContentBundle] Download failed: {request.error}", this);
                yield break;
            }

            try
            {
                File.WriteAllBytes(cachePath, request.downloadHandler.data);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlacedContentBundle] Cache write failed: {ex.Message}", this);
                yield break;
            }

            yield return LoadFromFile(cachePath, gen, ReadBundleHeader(cachePath));
        }

        IEnumerator LoadFromUrl(string url, int gen)
        {
            using var request = UnityWebRequestAssetBundle.GetAssetBundle(url);
            yield return request.SendWebRequest();

            if (gen != m_LoadGeneration)
                yield break;

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[PlacedContentBundle] URL load failed: {request.error}", this);
                yield break;
            }

            m_Bundle = DownloadHandlerAssetBundle.GetContent(request);
            ExtractPrefab();
        }

        IEnumerator LoadFromFile(string path, int gen, BundleHeader header)
        {
            m_Bundle = FindAlreadyLoadedContentBundle();
            if (m_Bundle != null)
            {
                Debug.Log(
                    $"[PlacedContentBundle] Reusing already-loaded AssetBundle '{m_Bundle.name}'",
                    this);
                ExtractPrefab();
                yield break;
            }

            var request = AssetBundle.LoadFromFileAsync(path);
            yield return request;

            if (gen != m_LoadGeneration)
                yield break;

            m_Bundle = request.assetBundle;
            if (m_Bundle == null)
                m_Bundle = FindAlreadyLoadedContentBundle();

            if (m_Bundle == null)
            {
                // Fallback: some iOS paths fail LoadFromFile but succeed from memory.
                Debug.LogWarning(
                    $"[PlacedContentBundle] LoadFromFile returned null — trying LoadFromMemory " +
                    $"(unity={header.unityVersion})",
                    this);
                byte[] bytes = null;
                try
                {
                    bytes = File.ReadAllBytes(path);
                }
                catch (Exception ex)
                {
                    NotifyFailed($"Cannot read bundle bytes: {ex.Message}");
                    yield break;
                }

                var memReq = AssetBundle.LoadFromMemoryAsync(bytes);
                yield return memReq;
                if (gen != m_LoadGeneration)
                    yield break;
                m_Bundle = memReq.assetBundle;
                if (m_Bundle == null)
                    m_Bundle = FindAlreadyLoadedContentBundle();
            }

            if (m_Bundle == null)
            {
                var sizeMb = 0f;
                try
                {
                    sizeMb = new FileInfo(path).Length / (1024f * 1024f);
                }
                catch
                {
                    // ignore
                }
                var playerVer = Application.unityVersion;
                var loaded = ListLoadedBundleNames();
#if UNITY_IOS || UNITY_IPHONE
                var platformHint = header.looksLikeIos
                    ? $"iOS Metal UnityFS rejected by player {playerVer} (bundle engine={header.engineRevision}). " +
                      "Often a duplicate OpenFromNative left the bundle already loaded, or Resources " +
                      "fallback raced the download. Rebuild UaaL after this player fix; Try again without " +
                      "relying on Resources/PlacedContent. Dev: ./scripts/check-unity-ar-versions.sh --sha256 --url <iosBundleUrl>"
                    : "This file looks like an Android AssetBundle (no Metal). Upload the iOS build from AssetBundles/iOS/.";
#else
                var platformHint = header.looksLikeIos
                    ? "This file looks like an iOS AssetBundle (Metal). Upload the Android build from AssetBundles/Android/."
                    : $"Wrong platform or Unity version mismatch (player={playerVer}, bundle={header.engineRevision}).";
#endif
                NotifyFailed(
                    $"[PlacedContentBundle] LoadFromFile/Memory failed: {path} " +
                    $"(magic={header.magic}, format={header.unityVersion}, engine={header.engineRevision}, " +
                    $"player={playerVer}, size≈{sizeMb:F1}MB, alreadyLoaded=[{loaded}]). " +
                    platformHint);
                yield break;
            }

            ExtractPrefab();
        }

        static AssetBundle FindAlreadyLoadedContentBundle()
        {
            var loaded = AssetBundle.GetAllLoadedAssetBundles();
            if (loaded == null)
                return null;
            foreach (var bundle in loaded)
            {
                if (bundle == null)
                    continue;
                var n = bundle.name ?? "";
                if (n.StartsWith("unitydefault", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("unity_builtin"))
                    continue;
                return bundle;
            }

            return null;
        }

        static string ListLoadedBundleNames()
        {
            try
            {
                var loaded = AssetBundle.GetAllLoadedAssetBundles();
                if (loaded == null)
                    return "";
                var sb = new StringBuilder();
                foreach (var bundle in loaded)
                {
                    if (bundle == null)
                        continue;
                    if (sb.Length > 0)
                        sb.Append(',');
                    sb.Append(bundle.name);
                }

                return sb.ToString();
            }
            catch
            {
                return "?";
            }
        }

        void ExtractPrefab()
        {
            if (m_Bundle == null)
                return;

            m_Prefab = ResolvePrefab(m_Bundle, m_AssetName);
            if (m_Prefab == null)
            {
                var names = ListAssetNames(m_Bundle);
                NotifyFailed(
                    $"Asset '{m_AssetName}' missing in bundle. Assets=[{names}]. " +
                    "Leave Asset name blank in admin to auto-load the first prefab.");
                return;
            }

            Debug.Log($"[PlacedContentBundle] Ready: '{m_Prefab.name}'", this);
            PrefabLoaded?.Invoke(m_Prefab);
        }

        static GameObject ResolvePrefab(AssetBundle bundle, string assetName)
        {
            if (bundle == null)
                return null;

            if (!string.IsNullOrWhiteSpace(assetName))
            {
                var direct = bundle.LoadAsset<GameObject>(assetName);
                if (direct != null)
                    return direct;

                // Manifest stores full path; some loaders need it.
                var byPath = bundle.LoadAsset<GameObject>("Assets/" + assetName + ".prefab");
                if (byPath != null)
                    return byPath;
                byPath = bundle.LoadAsset<GameObject>("Assets/Resources/" + assetName + ".prefab");
                if (byPath != null)
                    return byPath;
                byPath = bundle.LoadAsset<GameObject>("Assets/Resources/PlacedContent.prefab");
                if (byPath != null)
                    return byPath;
                byPath = bundle.LoadAsset<GameObject>("Assets/PlacedContent.prefab");
                if (byPath != null)
                    return byPath;

                // Prefabs under subfolders: match GetAllAssetNames by file stem.
                var names = bundle.GetAllAssetNames();
                if (names != null)
                {
                    var needle = "/" + assetName.Trim().ToLowerInvariant() + ".prefab";
                    for (var i = 0; i < names.Length; i++)
                    {
                        var n = names[i];
                        if (string.IsNullOrEmpty(n))
                            continue;
                        if (n.EndsWith(needle, System.StringComparison.OrdinalIgnoreCase) ||
                            n.Equals(assetName, System.StringComparison.OrdinalIgnoreCase))
                        {
                            var fromList = bundle.LoadAsset<GameObject>(n);
                            if (fromList != null)
                                return fromList;
                        }
                    }
                }
            }

            var all = bundle.LoadAllAssets<GameObject>();
            if (all == null || all.Length == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(assetName))
            {
                for (var i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].name == assetName)
                        return all[i];
                }
            }

            for (var i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == "PlacedContent")
                    return all[i];
            }

            return all[0];
        }

        static string ListAssetNames(AssetBundle bundle)
        {
            try
            {
                var names = bundle.GetAllAssetNames();
                if (names == null || names.Length == 0)
                    return "(none)";
                var n = Math.Min(names.Length, 12);
                var sb = new StringBuilder();
                for (var i = 0; i < n; i++)
                {
                    if (i > 0)
                        sb.Append(", ");
                    sb.Append(names[i]);
                }

                if (names.Length > n)
                    sb.Append(", …");
                return sb.ToString();
            }
            catch
            {
                return "(unavailable)";
            }
        }

        struct BundleHeader
        {
            public string magic;
            /// Format generation (often "5.x.x") — not the editor version.
            public string unityVersion;
            /// Editor / player revision (e.g. "6000.5.6f1").
            public string engineRevision;
            public bool looksLikeIos;
        }

        static BundleHeader ReadBundleHeader(string path)
        {
            var header = new BundleHeader
            {
                magic = "(unreadable)",
                unityVersion = "?",
                engineRevision = "?",
                looksLikeIos = false
            };
            try
            {
                using var fs = File.OpenRead(path);
                var buf = new byte[64];
                var n = fs.Read(buf, 0, buf.Length);
                if (n < 7)
                {
                    header.magic = n <= 0 ? "(empty)" : "(short)";
                    return header;
                }

                header.magic = Encoding.ASCII.GetString(buf, 0, 7);
                // UnityFS\0 + int32 format + cstring formatVer + cstring engineRevision
                if (n > 12 && header.magic == "UnityFS")
                {
                    var verStart = 12;
                    var verEnd = verStart;
                    while (verEnd < n && buf[verEnd] != 0)
                        verEnd++;
                    if (verEnd > verStart)
                        header.unityVersion = Encoding.ASCII.GetString(buf, verStart, verEnd - verStart);
                    if (verEnd + 1 < n)
                    {
                        var revStart = verEnd + 1;
                        var revEnd = revStart;
                        while (revEnd < n && buf[revEnd] != 0)
                            revEnd++;
                        if (revEnd > revStart)
                            header.engineRevision =
                                Encoding.ASCII.GetString(buf, revStart, revEnd - revStart);
                    }
                }

                // iOS PlacedContent embeds Metal shader source; Android builds do not.
                try
                {
                    var marker = Encoding.ASCII.GetBytes("metal_stdlib");
                    fs.Position = 0;
                    var window = new byte[Math.Min(fs.Length, 4 * 1024 * 1024)];
                    var read = fs.Read(window, 0, window.Length);
                    header.looksLikeIos = IndexOfBytes(window, read, marker) >= 0;
                }
                catch
                {
                    // keep default
                }
            }
            catch (Exception ex)
            {
                header.magic = $"(read-error:{ex.Message})";
            }

            return header;
        }

        static int IndexOfBytes(byte[] haystack, int hayLength, byte[] needle)
        {
            if (needle == null || needle.Length == 0 || hayLength < needle.Length)
                return -1;
            for (var i = 0; i <= hayLength - needle.Length; i++)
            {
                var match = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                    return i;
            }

            return -1;
        }

        void NotifyFailed(string message)
        {
            if (m_LoadFailedNotified)
                return;
            m_LoadFailedNotified = true;
            Debug.LogError(message, this);
            LoadFailed?.Invoke(message);
        }

        void CancelInFlightLoad()
        {
            m_LoadGeneration++;
            if (m_LoadRoutine != null)
            {
                StopCoroutine(m_LoadRoutine);
                m_LoadRoutine = null;
            }

            m_Loading = false;
        }

        void UnloadBundleOnly()
        {
            m_Prefab = null;
            if (m_Bundle != null)
            {
                m_Bundle.Unload(false);
                m_Bundle = null;
            }
        }

        public void Unload()
        {
            CancelInFlightLoad();
            UnloadBundleOnly();
            m_AbsoluteLoadPath = null;
            m_LoadFailedNotified = false;
        }
    }
}
