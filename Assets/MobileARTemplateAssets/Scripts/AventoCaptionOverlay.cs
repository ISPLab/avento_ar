using UnityEngine;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Screen-space description panel for <see cref="AventoSpeechMode.Caption"/>.
    /// Drawn with IMGUI so it works in UaaL without TextMeshPro / extra fonts.
    /// </summary>
    public class AventoCaptionOverlay : MonoBehaviour
    {
        public const string GameObjectName = "AventoCaptionOverlay";
        const string MicIconResourcePath = "overlays/mic";
        const string MicIconAssetPath = "Assets/Scenes/overlays/mic.png";

        static AventoCaptionOverlay s_Instance;

        string m_Title = "";
        string m_Body = "";
        AventoObjectInteract m_Source;
        int m_ShowFrame = -10;
        Rect m_PanelRect;
        Rect m_AskIconRect;
        Rect m_AskButtonRect;
        Vector2 m_BodyScroll = Vector2.zero;
        GUIStyle m_TitleStyle;
        GUIStyle m_BodyStyle;
        GUIStyle m_HintStyle;
        Texture2D m_MicIconTexture;
        bool m_MicIconLoadAttempted;

        public static bool IsVisible =>
            s_Instance != null && s_Instance.isActiveAndEnabled && s_Instance.m_Source != null;

        public static AventoObjectInteract CurrentSource =>
            s_Instance != null ? s_Instance.m_Source : null;

        static AventoCaptionOverlay Instance
        {
            get
            {
                if (s_Instance != null)
                    return s_Instance;
                var existing = FindAnyObjectByType<AventoCaptionOverlay>();
                if (existing != null)
                {
                    s_Instance = existing;
                    return s_Instance;
                }

                var go = new GameObject(GameObjectName);
                DontDestroyOnLoad(go);
                s_Instance = go.AddComponent<AventoCaptionOverlay>();
                return s_Instance;
            }
        }

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_Instance = this;
            gameObject.name = GameObjectName;
            ResolveMicIconTexture();
        }

        void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;
        }

        public static void Show(AventoObjectInteract source, string title, string body)
        {
            if (source == null)
                return;

            var inst = Instance;
            if (inst.m_Source != null && inst.m_Source != source)
                inst.m_Source.NotifyCaptionClosed();

            inst.m_Source = source;
            inst.m_Title = string.IsNullOrWhiteSpace(title) ? "" : title.Trim();
            inst.m_Body = body ?? "";
            inst.m_ShowFrame = Time.frameCount;
            inst.m_BodyScroll = Vector2.zero;
            // Reload each open so edited mic.png is picked up (DontDestroyOnLoad otherwise caches forever).
            inst.m_MicIconLoadAttempted = false;
            inst.m_MicIconTexture = null;
            inst.ResolveMicIconTexture();
            inst.enabled = true;
            inst.gameObject.SetActive(true);
        }

        public static void Hide()
        {
            if (s_Instance == null)
                return;
            var source = s_Instance.m_Source;
            s_Instance.m_Source = null;
            s_Instance.m_Title = "";
            s_Instance.m_Body = "";
            s_Instance.m_BodyScroll = Vector2.zero;
            if (source != null)
                source.NotifyCaptionClosed();
        }

        public static bool IsShowing(AventoObjectInteract source) =>
            IsVisible && CurrentSource == source;

        public static bool IsShowFrame =>
            s_Instance != null && Time.frameCount - s_Instance.m_ShowFrame <= 1;

        /// <summary>
        /// <paramref name="unityScreenPosition"/> is Unity screen space (origin bottom-left).
        /// </summary>
        public static bool TryConsumeTap(Vector2 unityScreenPosition)
        {
            if (!IsVisible)
                return false;

            // Ignore duplicate taps arriving on the same or next frame (IMGUI queue + native inject).
            if (Time.frameCount - s_Instance.m_ShowFrame <= 1)
                return true;

            var imgui = new Vector2(unityScreenPosition.x, Screen.height - unityScreenPosition.y);

            var hitAskIcon = s_Instance.m_AskIconRect.width > 1f && s_Instance.m_AskIconRect.Contains(imgui);
            var hitAskButton = s_Instance.m_AskButtonRect.width > 1f && s_Instance.m_AskButtonRect.Contains(imgui);
            if (hitAskIcon || hitAskButton)
            {
                var source = s_Instance.m_Source;
                Hide();
                if (source != null)
                    source.AskTessaFromCaption();
                return true;
            }

            // Consume taps on the panel so AR behind it does not fire, but keep overlay open for scrolling.
            if (s_Instance.m_PanelRect.Contains(imgui))
                return true;

            // Tap dim background dismisses.
            Hide();
            return true;
        }

        void OnGUI()
        {
            if (m_Source == null)
                return;

            EnsureStyles();

            var pad = Mathf.Max(8f, Screen.width * 0.015f);
            var width = Screen.width - pad * 2f;
            var height = Screen.height * 0.4f;
            var innerW = width - 32f;
            var iconSize = Mathf.Max(48f, Screen.height / 15f);
            var micPad = 8f;
            var titleH = string.IsNullOrEmpty(m_Title)
                ? 0f
                : m_TitleStyle.CalcHeight(new GUIContent(m_Title), innerW);
            var bodyText = string.IsNullOrWhiteSpace(m_Body) ? " " : m_Body;
            var bodyH = m_BodyStyle.CalcHeight(new GUIContent(bodyText), innerW);
            var hintH = m_HintStyle.lineHeight + 6f;
            var headerH = (titleH > 0f ? titleH : 0f) + 8f;
            var panelX = (Screen.width - width) * 0.5f;
            var panelY = (Screen.height - height) * 0.5f;
            m_PanelRect = new Rect(panelX, panelY, width, height);

            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none);

            GUI.color = new Color(0.08f, 0.08f, 0.10f, 0.92f);
            GUI.Box(m_PanelRect, GUIContent.none);
            GUI.color = prev;

            var inner = new Rect(m_PanelRect.x + 16f, m_PanelRect.y + 16f, m_PanelRect.width - 32f, m_PanelRect.height - 20f);

            m_AskIconRect = new Rect(m_PanelRect.xMax - iconSize - micPad, m_PanelRect.y + micPad, iconSize, iconSize);
            m_AskButtonRect = new Rect(0f, 0f, 0f, 0f);
            if (!string.IsNullOrEmpty(m_Title))
            {
                GUI.Label(new Rect(inner.x, inner.y, inner.width, headerH), m_Title, m_TitleStyle);
            }

            inner.y += headerH;
            inner.height -= headerH;

            var bodyAreaH = inner.height - hintH - 12f;
            var bodyAreaRect = new Rect(inner.x, inner.y, inner.width, bodyAreaH);
            var contentH = Mathf.Max(bodyAreaH, bodyH + 8f);
            var bodyViewRect = new Rect(0f, 0f, inner.width - 18f, contentH);
            m_BodyScroll = GUI.BeginScrollView(
                bodyAreaRect,
                m_BodyScroll,
                bodyViewRect,
                false,
                true);
            GUI.Label(new Rect(0f, 0f, bodyViewRect.width, contentH), bodyText, m_BodyStyle);
            GUI.EndScrollView();

            GUI.Label(
                new Rect(inner.x, m_PanelRect.yMax - hintH - 6f, inner.width, hintH),
                "Tap outside to close",
                m_HintStyle);

            // Draw mic action last so it is always visible above text.
            var micTexture = ResolveMicIconTexture();
            if (micTexture != null)
            {
                GUI.DrawTexture(m_AskIconRect, micTexture, ScaleMode.ScaleToFit, true);
            }
            else
            {
                GUI.color = new Color(0.92f, 0.18f, 0.20f, 1f);
                GUI.DrawTexture(m_AskIconRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true);
                GUI.color = prev;
                DrawMicIcon(m_AskIconRect);
            }
        }

        static void DrawMicIcon(Rect rect)
        {
            var prev = GUI.color;
            GUI.color = Color.white;

            var cx = rect.x + rect.width * 0.5f;
            var headW = rect.width * 0.34f;
            var headH = rect.height * 0.40f;
            var head = new Rect(cx - headW * 0.5f, rect.y + rect.height * 0.14f, headW, headH);
            GUI.Box(head, GUIContent.none);

            var stemW = rect.width * 0.10f;
            var stemH = rect.height * 0.18f;
            GUI.Box(new Rect(cx - stemW * 0.5f, head.yMax - 1f, stemW, stemH), GUIContent.none);

            var baseW = rect.width * 0.46f;
            var baseH = Mathf.Max(3f, rect.height * 0.07f);
            GUI.Box(new Rect(cx - baseW * 0.5f, head.yMax + stemH + 1f, baseW, baseH), GUIContent.none);

            GUI.color = prev;
        }

        Texture2D ResolveMicIconTexture()
        {
            if (m_MicIconLoadAttempted)
                return m_MicIconTexture;

            m_MicIconLoadAttempted = true;

#if UNITY_EDITOR
            // Prefer the editable Scenes asset in Editor so PNG swaps show immediately.
            m_MicIconTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(MicIconAssetPath);
            if (m_MicIconTexture != null)
            {
                Debug.Log(
                    $"[AventoCaptionOverlay] Mic icon loaded from {MicIconAssetPath} ({m_MicIconTexture.width}x{m_MicIconTexture.height})");
                return m_MicIconTexture;
            }
#endif

            m_MicIconTexture = Resources.Load<Texture2D>(MicIconResourcePath);
            if (m_MicIconTexture != null)
            {
                Debug.Log(
                    $"[AventoCaptionOverlay] Mic icon loaded from Resources/{MicIconResourcePath} ({m_MicIconTexture.width}x{m_MicIconTexture.height})");
                return m_MicIconTexture;
            }

            Debug.LogWarning(
                $"[AventoCaptionOverlay] Mic icon not found. Checked {MicIconAssetPath} and Resources/{MicIconResourcePath}. Falling back to built-in mic.");

            return m_MicIconTexture;
        }

        void EnsureStyles()
        {
            if (m_TitleStyle != null)
                return;

            m_TitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(26, Screen.height / 36),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                normal = { textColor = Color.white }
            };
            m_BodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(20, Screen.height / 46),
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                normal = { textColor = new Color(0.92f, 0.92f, 0.94f) }
            };
            m_HintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(14, Screen.height / 64),
                fontStyle = FontStyle.Italic,
                alignment = TextAnchor.LowerRight,
                normal = { textColor = new Color(1f, 1f, 1f, 0.55f) }
            };
        }
    }
}
