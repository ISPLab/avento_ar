using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Scripting;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// UaaL entry point: receives open/dismiss commands from the Capacitor native host
    /// via UnitySendMessage, loads an AssetBundle from a local path, and wires TapToPlace.
    /// </summary>
    public class AventoUnityHost : MonoBehaviour
    {
        public const string GameObjectName = "AventoUnityHost";

        [SerializeField]
        PlacedContentBundleLoader m_BundleLoader;

        [SerializeField]
        TapToPlaceOnAnchor m_TapToPlace;

        static AventoUnityHost s_Instance;
        bool m_SessionOpen;
        string m_PendingOpenJson;
        bool m_HostAliveNotified;
        Coroutine m_OpenRoutine;
        string m_OpenBundlePath;

        public const float ExitChromeSize = 52f;
        public const float ExitChromeBottomPad = 12f;

        public static AventoUnityHost Instance => s_Instance;

        public bool AutoStartTessa { get; private set; }
        public string AutoStartTessaPrompt { get; private set; } = "";
        public string SessionTitle { get; private set; } = "Unity AR";
        public string SessionLanguage { get; private set; } = "";

        Coroutine m_SceneTessaRoutine;

        public static Rect ExitChromeImguiRect()
        {
            var x = (Screen.width - ExitChromeSize) * 0.5f;
            var y = Screen.height - ExitChromeSize - ExitChromeBottomPad;
            return new Rect(x, y, ExitChromeSize, ExitChromeSize);
        }

        public static bool IsInExitChromeImgui(Vector2 imguiPos) =>
            ExitChromeImguiRect().Contains(imguiPos);

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_Instance = this;
            DontDestroyOnLoad(gameObject);
            gameObject.name = GameObjectName;

            if (m_BundleLoader == null)
                m_BundleLoader = FindFirstObjectByType<PlacedContentBundleLoader>();
            if (m_TapToPlace == null)
                m_TapToPlace = FindFirstObjectByType<TapToPlaceOnAnchor>();
        }

        void Start()
        {
            // Prevent IL2CPP from stripping UnitySendMessage entry points.
            if (Time.frameCount < -1)
            {
                OnNativeTap(string.Empty);
                OpenFromNative(string.Empty);
                DismissFromNative(string.Empty);
            }

            NotifyHostAlive();
            if (!string.IsNullOrEmpty(m_PendingOpenJson))
            {
                var json = m_PendingOpenJson;
                m_PendingOpenJson = null;
                if (m_OpenRoutine != null)
                    StopCoroutine(m_OpenRoutine);
                m_OpenRoutine = StartCoroutine(OpenRoutine(json));
            }
        }

        void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;
        }

        /// <summary>
        /// Called from native via UnitySendMessage("AventoUnityHost", "OpenFromNative", json).
        /// JSON: { "bundlePath", "assetName", "bundleFileName", "scale", "title",
        ///         "heading", "automaticScenePlacement", "autoPlaceDistanceMeters",
        ///         "autoStartTessa", "autoStartTessaPrompt", "language" }
        /// </summary>
        [Preserve]
        public void OpenFromNative(string json)
        {
            Debug.Log($"[AventoUnityHost] OpenFromNative {json}", this);
            m_SessionOpen = true;

            // Message can arrive before Start() / scene wiring is ready.
            if (!isActiveAndEnabled)
            {
                m_PendingOpenJson = json;
                return;
            }

            var opts = ParseOpenOptions(json);
            AutoStartTessa = opts.autoStartTessa;
            AutoStartTessaPrompt = opts.autoStartTessaPrompt ?? "";
            SessionTitle = string.IsNullOrWhiteSpace(opts.title) ? "Unity AR" : opts.title;
            SessionLanguage = opts.language ?? "";
            if (!string.IsNullOrWhiteSpace(opts.bundlePath) &&
                m_OpenRoutine != null &&
                string.Equals(m_OpenBundlePath, opts.bundlePath, StringComparison.Ordinal) &&
                (m_BundleLoader != null && (m_BundleLoader.IsLoading || m_BundleLoader.IsLoaded)))
            {
                Debug.Log("[AventoUnityHost] Ignoring duplicate OpenFromNative for the same bundle path", this);
                return;
            }

            m_OpenBundlePath = opts.bundlePath;
            AventoInteractionDirector.ResetSession();
            if (m_SceneTessaRoutine != null)
            {
                StopCoroutine(m_SceneTessaRoutine);
                m_SceneTessaRoutine = null;
            }

            Debug.Log(
                $"[AventoUnityHost] Open options autoStartTessa={opts.autoStartTessa} " +
                $"autoPlace={opts.automaticScenePlacement} title='{opts.title}' " +
                $"promptChars={(opts.autoStartTessaPrompt ?? "").Length}",
                this);

            if (m_OpenRoutine != null)
                StopCoroutine(m_OpenRoutine);
            m_OpenRoutine = StartCoroutine(OpenRoutine(json));
        }

        /// <summary>Called from native to force-close the Unity overlay.</summary>
        [Preserve]
        public void DismissFromNative(string _unused = null)
        {
            Debug.Log("[AventoUnityHost] DismissFromNative", this);
            FinishSession("host_dismiss");
        }

        public void RequestExit()
        {
            FinishSession("user_exit");
        }

        /// <summary>
        /// Native UIKit tap: "x,y" pixels OR "n,nx,ny" normalized (origin bottom-left).
        /// </summary>
        [Preserve]
        public void OnNativeTap(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
                return;

            Debug.Log($"[AventoUnityHost] OnNativeTap raw='{csv}'");

            var parts = csv.Split(',');
            float x;
            float y;

            if (parts.Length >= 3 &&
                parts[0].Trim().Equals("n", StringComparison.OrdinalIgnoreCase))
            {
                if (!float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var nx))
                    return;
                if (!float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var ny))
                    return;
                x = nx * Screen.width;
                y = ny * Screen.height;
            }
            else if (parts.Length >= 2)
            {
                if (!float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out x))
                    return;
                if (!float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out y))
                    return;
            }
            else
            {
                return;
            }

            if (m_TapToPlace == null)
                m_TapToPlace = FindAnyObjectByType<TapToPlaceOnAnchor>();

            if (m_TapToPlace == null)
            {
                Debug.LogWarning("[AventoUnityHost] OnNativeTap but TapToPlace missing");
                return;
            }

            Debug.Log($"[AventoUnityHost] OnNativeTap → InjectTap ({x},{y}) screen={Screen.width}x{Screen.height}");
            m_TapToPlace.InjectTap(new Vector2(x, y));
        }

        void NotifyHostAlive()
        {
            if (m_HostAliveNotified)
                return;
            m_HostAliveNotified = true;
            AventoUnityNative.NotifyReady("{\"host\":true,\"awaitingOpen\":true}");
        }

        IEnumerator OpenRoutine(string json)
        {
            var opts = ParseOpenOptions(json);
            if (string.IsNullOrWhiteSpace(opts.bundlePath))
            {
                NotifyNativeError("Missing bundlePath");
                yield break;
            }

            // Wait briefly for XR Origin / TapToPlace to exist after scene load.
            var deadline = Time.realtimeSinceStartup + 5f;
            while (m_TapToPlace == null && Time.realtimeSinceStartup < deadline)
            {
                m_TapToPlace = FindFirstObjectByType<TapToPlaceOnAnchor>();
                if (m_TapToPlace == null)
                    yield return null;
            }

            EnsureBundleLoader();

            Debug.Log(
                $"[AventoUnityHost] Loading bundle path='{opts.bundlePath}' " +
                $"(exists={System.IO.File.Exists(opts.bundlePath)})",
                this);

            m_BundleLoader.Configure(
                string.IsNullOrWhiteSpace(opts.assetName) ? string.Empty : opts.assetName.Trim(),
                string.IsNullOrWhiteSpace(opts.bundleFileName) ? string.Empty : opts.bundleFileName.Trim());

            var done = false;
            GameObject prefab = null;
            string fail = null;

            void OnLoaded(GameObject p)
            {
                prefab = p;
                done = true;
            }

            void OnFail(string err)
            {
                fail = err;
                done = true;
            }

            m_BundleLoader.PrefabLoaded += OnLoaded;
            m_BundleLoader.LoadFailed += OnFail;
            m_BundleLoader.BeginLoadFromAbsolutePath(opts.bundlePath);

            var timeout = Time.realtimeSinceStartup + 30f;
            while (!done && Time.realtimeSinceStartup < timeout)
                yield return null;

            m_BundleLoader.PrefabLoaded -= OnLoaded;
            m_BundleLoader.LoadFailed -= OnFail;

            if (prefab == null)
            {
                NotifyNativeError(fail ?? "Bundle load timed out");
                yield break;
            }

            if (m_TapToPlace == null)
                m_TapToPlace = FindFirstObjectByType<TapToPlaceOnAnchor>();

            if (m_TapToPlace != null)
            {
                m_TapToPlace.contentPrefab = prefab;
                if (opts.scale > 0f)
                    m_TapToPlace.contentScale = opts.scale;
                m_TapToPlace.contentHeadingDegrees = opts.heading;
                m_TapToPlace.MarkReady();
                m_TapToPlace.SetAutomaticScenePlacement(
                    opts.automaticScenePlacement,
                    opts.autoPlaceDistanceMeters > 0f ? opts.autoPlaceDistanceMeters : 2f);
            }
            else
            {
                NotifyNativeError("TapToPlaceOnAnchor not found in scene");
                yield break;
            }

            if (GetComponent<AventoSceneTessa>() == null)
            {
                var fallback = gameObject.AddComponent<AventoSceneTessa>();
                fallback.SetAsHostFallback(1.1f);
            }
            else
            {
                GetComponent<AventoSceneTessa>().SetAsHostFallback(1.1f);
            }

            // Host-owned kickoff (more reliable than only AventoSceneTessa subscription).
            AventoInteractionDirector.ContentPlaced -= OnHostContentPlaced;
            AventoInteractionDirector.ContentPlaced += OnHostContentPlaced;

            NotifyNativeReady(opts);
        }

        void OnHostContentPlaced(GameObject instance)
        {
            if (instance == null)
                return;

            Debug.Log(
                $"[AventoUnityHost] ContentPlaced autoStartTessa={AutoStartTessa} " +
                $"instance={instance.name}",
                this);

            if (!AutoStartTessa)
                return;

            if (m_SceneTessaRoutine != null)
                StopCoroutine(m_SceneTessaRoutine);
            m_SceneTessaRoutine = StartCoroutine(SceneTessaAfterPlaceRoutine());
        }

        IEnumerator SceneTessaAfterPlaceRoutine()
        {
            yield return new WaitForSecondsRealtime(0.7f);
            FireSceneStartToNative();
            m_SceneTessaRoutine = null;
        }

        public void FireSceneStartToNative()
        {
            if (!AventoInteractionDirector.TryMarkSceneStartSent())
            {
                Debug.Log("[AventoUnityHost] scene_start already sent — skip", this);
                return;
            }

            var json = AventoInteractJson.Build(
                "scene_start",
                "scene",
                SessionTitle,
                AutoStartTessaPrompt,
                null,
                AventoSpeechMode.Tessa,
                "",
                AventoSsmlGenderHint.Unspecified);

            Debug.Log($"[AventoUnityHost] scene_start → native ({json.Length} chars)", this);
            AventoUnityNative.NotifyObjectInteract(json);
        }

        void EnsureBundleLoader()
        {
            if (m_BundleLoader != null)
            {
                m_BundleLoader.SetLoadOnAwake(false);
                return;
            }

            // Inactive until LoadOnAwake is cleared so Awake does not start a Resources load.
            var go = new GameObject("PlacedContentBundleLoader");
            go.SetActive(false);
            m_BundleLoader = go.AddComponent<PlacedContentBundleLoader>();
            m_BundleLoader.SetLoadOnAwake(false);
            Object.DontDestroyOnLoad(go);
            go.SetActive(true);
        }

        void FinishSession(string reason)
        {
            if (!m_SessionOpen && reason != "host_dismiss")
                return;

            m_SessionOpen = false;
            if (m_OpenRoutine != null)
            {
                StopCoroutine(m_OpenRoutine);
                m_OpenRoutine = null;
            }

            if (m_SceneTessaRoutine != null)
            {
                StopCoroutine(m_SceneTessaRoutine);
                m_SceneTessaRoutine = null;
            }

            AventoInteractionDirector.ContentPlaced -= OnHostContentPlaced;

            var count = m_TapToPlace != null ? m_TapToPlace.PlacementCount : 0;
            m_TapToPlace?.ClearAllPlacements();
            m_BundleLoader?.Unload();
            m_OpenBundlePath = null;
            AutoStartTessa = false;
            AutoStartTessaPrompt = "";
            AventoUnityAudioGate.Reset();
            AventoInteractionDirector.ResetSession();

            var payload =
                "{\"reason\":\"" + Escape(reason) + "\",\"placementsCount\":" + count + "}";
            AventoUnityNative.NotifySessionEnded(payload);
        }

        void NotifyNativeReady(OpenOptions opts)
        {
            var payload =
                "{\"ok\":true,\"contentReady\":true,\"assetName\":\"" + Escape(opts.assetName) +
                "\",\"title\":\"" + Escape(opts.title) +
                "\",\"automaticScenePlacement\":" + (opts.automaticScenePlacement ? "true" : "false") +
                ",\"autoStartTessa\":" + (opts.autoStartTessa ? "true" : "false") +
                "}";
            AventoUnityNative.NotifyReady(payload);
        }

        void NotifyNativeError(string message)
        {
            // Don't clobber a session that already placed content (duplicate OpenFromNative).
            if (m_TapToPlace != null && m_TapToPlace.contentPrefab != null && m_SessionOpen)
            {
                Debug.LogWarning(
                    $"[AventoUnityHost] Suppressing error after content already assigned: {message}",
                    this);
                return;
            }

            m_SessionOpen = false;
            var payload = "{\"error\":\"" + Escape(message) + "\"}";
            AventoUnityNative.NotifyError(payload);
        }

        static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        static OpenOptions ParseOpenOptions(string json)
        {
            var opts = new OpenOptions
            {
                assetName = string.Empty,
                bundleFileName = string.Empty,
                scale = 1f,
                title = "Unity AR",
                heading = 0f,
                automaticScenePlacement = false,
                autoPlaceDistanceMeters = 2f,
                autoStartTessa = false,
                autoStartTessaPrompt = string.Empty,
                language = string.Empty,
            };

            if (string.IsNullOrWhiteSpace(json))
                return opts;

            // Prefer JsonUtility — correctly unescapes NSJSONSerialization's \/ sequences.
            try
            {
                var dto = JsonUtility.FromJson<OpenOptionsDto>(json);
                if (dto != null)
                {
                    if (!string.IsNullOrWhiteSpace(dto.bundlePath))
                        opts.bundlePath = NormalizePath(dto.bundlePath);
                    // Blank assetName is intentional → load first GameObject in the bundle.
                    if (dto.assetName != null)
                        opts.assetName = dto.assetName.Trim();
                    if (dto.bundleFileName != null)
                        opts.bundleFileName = dto.bundleFileName.Trim();
                    if (!string.IsNullOrWhiteSpace(dto.title))
                        opts.title = dto.title;
                    if (dto.scale > 0f)
                        opts.scale = dto.scale;
                    opts.heading = dto.heading;
                    opts.automaticScenePlacement = dto.automaticScenePlacement;
                    if (ExtractJsonBool(json, "automaticScenePlacement"))
                        opts.automaticScenePlacement = true;
                    if (dto.autoPlaceDistanceMeters > 0f)
                        opts.autoPlaceDistanceMeters = dto.autoPlaceDistanceMeters;
                    opts.autoStartTessa = dto.autoStartTessa;
                    if (dto.autoStartTessaPrompt != null)
                        opts.autoStartTessaPrompt = dto.autoStartTessaPrompt;
                    if (dto.language != null)
                        opts.language = dto.language.Trim();
                    // JsonUtility can miss bools when the payload is large — overlay from raw JSON.
                    if (ExtractJsonBool(json, "autoStartTessa"))
                        opts.autoStartTessa = true;
                    var promptOverlay = ExtractJsonString(json, "autoStartTessaPrompt");
                    if (!string.IsNullOrEmpty(promptOverlay))
                        opts.autoStartTessaPrompt = promptOverlay;
                    return opts;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AventoUnityHost] JsonUtility parse failed: {ex.Message}");
            }

            opts.bundlePath = NormalizePath(ExtractJsonString(json, "bundlePath") ?? opts.bundlePath);
            opts.assetName = ExtractJsonString(json, "assetName") ?? opts.assetName;
            opts.bundleFileName = ExtractJsonString(json, "bundleFileName") ?? opts.bundleFileName;
            opts.title = ExtractJsonString(json, "title") ?? opts.title;
            var scale = ExtractJsonFloat(json, "scale");
            if (scale > 0f)
                opts.scale = scale;
            if (TryExtractJsonFloat(json, "heading", out var heading))
                opts.heading = heading;
            opts.automaticScenePlacement = ExtractJsonBool(json, "automaticScenePlacement");
            var dist = ExtractJsonFloat(json, "autoPlaceDistanceMeters");
            if (dist > 0f)
                opts.autoPlaceDistanceMeters = dist;
            opts.autoStartTessa = ExtractJsonBool(json, "autoStartTessa");
            opts.autoStartTessaPrompt = ExtractJsonString(json, "autoStartTessaPrompt") ?? opts.autoStartTessaPrompt;
            opts.language = ExtractJsonString(json, "language") ?? opts.language;
            return opts;
        }

        /// <summary>
        /// NSJSONSerialization escapes '/' as '\/'. A naive quote-slice keeps the backslashes
        /// and File.Exists fails. Also strip a leading file:// if present.
        /// </summary>
        static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            path = UnescapeJsonString(path);
            if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    path = new Uri(path).LocalPath;
                }
                catch
                {
                    path = path.Substring(path.IndexOf(':') + 1);
                    if (path.StartsWith("//", StringComparison.Ordinal))
                        path = path.Substring(1);
                }
            }

            return path;
        }

        static string UnescapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf('\\') < 0)
                return value;

            var sb = new System.Text.StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length)
                {
                    var next = value[i + 1];
                    switch (next)
                    {
                        case '/':
                        case '\\':
                        case '"':
                            sb.Append(next);
                            i++;
                            continue;
                        case 'n':
                            sb.Append('\n');
                            i++;
                            continue;
                        case 't':
                            sb.Append('\t');
                            i++;
                            continue;
                        case 'u':
                            // Keep \uXXXX as-is if truncated; otherwise decode.
                            if (i + 5 < value.Length &&
                                int.TryParse(
                                    value.Substring(i + 2, 4),
                                    System.Globalization.NumberStyles.HexNumber,
                                    null,
                                    out var code))
                            {
                                sb.Append((char)code);
                                i += 5;
                                continue;
                            }

                            break;
                    }
                }

                sb.Append(value[i]);
            }

            return sb.ToString();
        }

        static string ExtractJsonString(string json, string key)
        {
            var token = "\"" + key + "\"";
            var idx = json.IndexOf(token, StringComparison.Ordinal);
            if (idx < 0)
                return null;
            var colon = json.IndexOf(':', idx + token.Length);
            if (colon < 0)
                return null;
            var firstQuote = json.IndexOf('"', colon + 1);
            if (firstQuote < 0)
                return null;

            // Walk the string respecting JSON escapes so \" inside values doesn't truncate.
            var i = firstQuote + 1;
            var sb = new System.Text.StringBuilder();
            while (i < json.Length)
            {
                var c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    sb.Append('\\').Append(json[i + 1]);
                    i += 2;
                    continue;
                }

                if (c == '"')
                    break;
                sb.Append(c);
                i++;
            }

            return UnescapeJsonString(sb.ToString());
        }

        static float ExtractJsonFloat(string json, string key)
        {
            return TryExtractJsonFloat(json, key, out var value) ? value : -1f;
        }

        static bool TryExtractJsonFloat(string json, string key, out float value)
        {
            value = 0f;
            var token = "\"" + key + "\"";
            var idx = json.IndexOf(token, StringComparison.Ordinal);
            if (idx < 0)
                return false;
            var colon = json.IndexOf(':', idx + token.Length);
            if (colon < 0)
                return false;
            var end = colon + 1;
            while (end < json.Length && (char.IsWhiteSpace(json[end]) || json[end] == '"'))
                end++;
            var start = end;
            while (end < json.Length &&
                   (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-' || json[end] == '+'))
                end++;
            if (start == end)
                return false;
            return float.TryParse(
                json.Substring(start, end - start),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }

        static bool ExtractJsonBool(string json, string key)
        {
            var token = "\"" + key + "\"";
            var idx = json.IndexOf(token, StringComparison.Ordinal);
            if (idx < 0)
                return false;
            var colon = json.IndexOf(':', idx + token.Length);
            if (colon < 0)
                return false;
            var slice = json.Substring(colon + 1).TrimStart();
            if (slice.StartsWith("true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (slice.StartsWith("1"))
                return true;
            return false;
        }

        [Serializable]
        class OpenOptionsDto
        {
            public string bundlePath;
            public string assetName;
            public string bundleFileName;
            public string title;
            public float scale;
            public float heading;
            public bool automaticScenePlacement;
            public float autoPlaceDistanceMeters;
            public bool autoStartTessa;
            public string autoStartTessaPrompt;
            public string language;
        }

        struct OpenOptions
        {
            public string bundlePath;
            public string assetName;
            public string bundleFileName;
            public string title;
            public float scale;
            public float heading;
            public bool automaticScenePlacement;
            public float autoPlaceDistanceMeters;
            public bool autoStartTessa;
            public string autoStartTessaPrompt;
            public string language;
        }
    }
}
