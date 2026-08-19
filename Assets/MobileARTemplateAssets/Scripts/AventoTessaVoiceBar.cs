using UnityEngine;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// IMGUI voice bar shown after "Ask Avento" triggers Tessa.
    /// Displays a status label and a Stop (X) button — similar to LiveGuideVoiceBar in avento-app.
    /// The bar sits above the exit chrome at the bottom of the screen.
    /// </summary>
    public class AventoTessaVoiceBar : MonoBehaviour
    {
        static AventoTessaVoiceBar s_Instance;

        bool m_Visible;
        GUIStyle m_LabelStyle;
        GUIStyle m_StopBtnStyle;

        public static bool IsVisible =>
            s_Instance != null && s_Instance.m_Visible;

        public static void Show()
        {
            EnsureInstance();
            s_Instance.m_Visible = true;
            Debug.Log("[AventoTessaVoiceBar] Show");
        }

        public static void Hide()
        {
            if (s_Instance != null)
            {
                s_Instance.m_Visible = false;
                Debug.Log("[AventoTessaVoiceBar] Hide");
            }
        }

        public static Rect BarImguiRect()
        {
            if (s_Instance == null || !s_Instance.m_Visible)
                return Rect.zero;
            var barH = Mathf.Max(48f, Screen.height / 18f);
            var barW = Mathf.Min(Screen.width * 0.7f, 360f);
            var exitBottom = AventoUnityHost.ExitChromeBottomPad + AventoUnityHost.ExitChromeSize;
            var barX = (Screen.width - barW) * 0.5f;
            var barY = Screen.height - exitBottom - barH - 12f;
            return new Rect(barX, barY, barW, barH);
        }

        public static bool IsInBarImgui(Vector2 imguiPos)
        {
            if (!IsVisible) return false;
            return BarImguiRect().Contains(imguiPos);
        }

        static void EnsureInstance()
        {
            if (s_Instance != null) return;
            var go = new GameObject("AventoTessaVoiceBar");
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<AventoTessaVoiceBar>();
        }

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            s_Instance = this;
        }

        void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;
        }

        void OnGUI()
        {
            if (!m_Visible) return;
            EnsureStyles();

            var bar = BarImguiRect();
            var prev = GUI.color;

            // Background pill
            GUI.color = new Color(0.06f, 0.06f, 0.08f, 0.88f);
            GUI.Box(bar, GUIContent.none);
            GUI.color = prev;

            // Tessa avatar placeholder + label
            var labelRect = new Rect(bar.x + 16f, bar.y, bar.width - 64f, bar.height);
            GUI.Label(labelRect, "Tessa · Listening…", m_LabelStyle);

            // Stop button (X) on the right
            var btnSize = Mathf.Max(36f, bar.height - 12f);
            var btnRect = new Rect(bar.xMax - btnSize - 8f, bar.y + (bar.height - btnSize) * 0.5f, btnSize, btnSize);
            GUI.color = new Color(0.85f, 0.2f, 0.2f, 1f);
            GUI.Box(btnRect, GUIContent.none);
            GUI.color = prev;
            GUI.Label(btnRect, "✕", m_StopBtnStyle);
        }

        /// <summary>
        /// Called from AventoInteractionDirector to check if a tap hits the stop button.
        /// </summary>
        public static bool TryConsumeTap(Vector2 unityScreenPos)
        {
            if (!IsVisible) return false;
            var imgui = new Vector2(unityScreenPos.x, Screen.height - unityScreenPos.y);
            var bar = BarImguiRect();
            if (!bar.Contains(imgui)) return false;

            // Check if hit the stop button region (right side)
            var btnSize = Mathf.Max(36f, bar.height - 12f);
            var btnRect = new Rect(bar.xMax - btnSize - 8f, bar.y + (bar.height - btnSize) * 0.5f, btnSize, btnSize);
            if (btnRect.Contains(imgui))
            {
                Debug.Log("[AventoTessaVoiceBar] Stop tapped");
                Hide();
                AventoUnityAudioGate.SetTessaVoiceActive(false);
                AventoUnityNative.NotifyStopTessa();
                return true;
            }

            // Tap on bar but not stop button — consume but don't act
            return true;
        }

        void EnsureStyles()
        {
            if (m_LabelStyle != null) return;
            m_LabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(16, Screen.height / 50),
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                normal = { textColor = new Color(0.9f, 0.9f, 0.92f) }
            };
            m_StopBtnStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(20, Screen.height / 40),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
        }
    }
}
