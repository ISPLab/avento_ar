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

        static AventoCaptionOverlay s_Instance;

        string m_Title = "";
        string m_Body = "";
        AventoObjectInteract m_Source;
        int m_ShowFrame = -10;
        Rect m_PanelRect;
        Rect m_AskButtonRect;
        GUIStyle m_TitleStyle;
        GUIStyle m_BodyStyle;
        GUIStyle m_HintStyle;
        GUIStyle m_AskButtonStyle;

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

            // "Ask Avento" button inside the panel.
            if (s_Instance.m_AskButtonRect.width > 1f && s_Instance.m_AskButtonRect.Contains(imgui))
            {
                var source = s_Instance.m_Source;
                Hide();
                if (source != null)
                    source.AskTessaFromCaption();
                return true;
            }

            // Tap anywhere (panel body or dim background) dismisses.
            Hide();
            return true;
        }

        void OnGUI()
        {
            if (m_Source == null)
                return;

            EnsureStyles();

            var pad = Mathf.Max(24f, Screen.width * 0.06f);
            var width = Mathf.Min(Screen.width - pad * 2f, 560f);
            var maxH = Screen.height * 0.55f;
            var innerW = width - 32f;
            var titleH = string.IsNullOrEmpty(m_Title) ? 0f : m_TitleStyle.CalcHeight(new GUIContent(m_Title), innerW);
            var bodyH = m_BodyStyle.CalcHeight(new GUIContent(string.IsNullOrWhiteSpace(m_Body) ? " " : m_Body), innerW);
            var buttonH = Mathf.Max(48f, Screen.height / 22f);
            var hintH = m_HintStyle.lineHeight + 6f;
            var height = Mathf.Min(maxH, 28f + titleH + bodyH + buttonH + hintH + 28f);
            var panelX = (Screen.width - width) * 0.5f;
            var panelY = (Screen.height - height) * 0.5f;
            m_PanelRect = new Rect(panelX, panelY, width, height);

            var prev = GUI.color;
            // Dim the AR background behind the centered panel.
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none);

            GUI.color = new Color(0.08f, 0.08f, 0.10f, 0.92f);
            GUI.Box(m_PanelRect, GUIContent.none);
            GUI.color = prev;

            var inner = new Rect(m_PanelRect.x + 16f, m_PanelRect.y + 16f, m_PanelRect.width - 32f, m_PanelRect.height - 20f);
            if (!string.IsNullOrEmpty(m_Title))
            {
                GUI.Label(new Rect(inner.x, inner.y, inner.width, titleH), m_Title, m_TitleStyle);
                inner.y += titleH + 6f;
                inner.height -= titleH + 6f;
            }

            var bodyAreaH = inner.height - buttonH - hintH - 12f;
            GUI.Label(new Rect(inner.x, inner.y, inner.width, bodyAreaH), m_Body, m_BodyStyle);

            m_AskButtonRect = new Rect(inner.x, m_PanelRect.yMax - hintH - buttonH - 10f, inner.width, buttonH);
            GUI.color = new Color(0.22f, 0.48f, 0.95f, 1f);
            GUI.Box(m_AskButtonRect, GUIContent.none);
            GUI.color = prev;
            GUI.Label(m_AskButtonRect, "Ask Avento", m_AskButtonStyle);

            GUI.Label(
                new Rect(inner.x, m_PanelRect.yMax - hintH - 6f, inner.width, hintH),
                "Tap anywhere to close",
                m_HintStyle);
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
            m_AskButtonStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(20, Screen.height / 42),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                normal = { textColor = Color.white }
            };
        }
    }
}
