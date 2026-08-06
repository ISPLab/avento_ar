using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Tap the screen to raycast an AR plane, create an ARAnchor at the hit, and parent content to it.
    /// </summary>
    public class TapToPlaceOnAnchor : MonoBehaviour
    {
        static readonly List<ARRaycastHit> s_Hits = new();

        [SerializeField]
        ARRaycastManager m_RaycastManager;

        [SerializeField]
        ARAnchorManager m_AnchorManager;

        [SerializeField]
        Transform m_Content;

        [SerializeField]
        Vector3 m_LocalPositionOffset = new Vector3(0f, 0.05f, 0f);

        [SerializeField]
        float m_ContentScale = 0.1f;

        [SerializeField]
        bool m_ReplaceExistingPlacement = true;

        [SerializeField]
        bool m_ShowDebugOverlay = true;

        ARAnchor m_CurrentAnchor;
        Text m_StatusText;
        string m_LastStatus;

        public Transform content
        {
            get => m_Content;
            set => m_Content = value;
        }

        void Awake()
        {
            if (m_RaycastManager == null)
                m_RaycastManager = GetComponent<ARRaycastManager>();

            if (m_AnchorManager == null)
                m_AnchorManager = GetComponent<ARAnchorManager>();

            if (m_Content != null)
                m_Content.gameObject.SetActive(false);

            if (m_ShowDebugOverlay)
                CreateDebugOverlay();

            SetStatus("Scan a surface, then tap to place.");
        }

        void Update()
        {
            if (!WasTapThisFrame(out var screenPosition))
                return;

            if (IsPointerOverUI())
                return;

            TryPlaceAtScreenPosition(screenPosition);
        }

        bool WasTapThisFrame(out Vector2 screenPosition)
        {
            screenPosition = default;

            var pointer = Pointer.current;
            if (pointer != null && pointer.press.wasPressedThisFrame)
            {
                screenPosition = pointer.position.ReadValue();
                return true;
            }

            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
            {
                screenPosition = touch.primaryTouch.position.ReadValue();
                return true;
            }

            return false;
        }

        static bool IsPointerOverUI()
        {
            // -1 = current/left mouse / primary touch for the new Input System on mobile.
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(-1);
        }

        async void TryPlaceAtScreenPosition(Vector2 screenPosition)
        {
            if (m_RaycastManager == null || m_AnchorManager == null || m_Content == null)
            {
                Debug.LogError("[TapPlace] Missing RaycastManager, AnchorManager, or Content.");
                SetStatus("ERROR: missing managers/content");
                return;
            }

            if (!m_RaycastManager.Raycast(screenPosition, s_Hits, TrackableType.PlaneWithinPolygon))
            {
                Debug.Log("[TapPlace] No plane hit. Keep scanning surfaces.");
                SetStatus("No plane hit — scan floor/table, then tap.");
                return;
            }

            var hit = s_Hits[0];
            Debug.Log($"[TapPlace] Hit {hit.hitType} at {hit.pose.position}, trackable={hit.trackableId}");

            ARAnchor anchor = null;

            if (hit.trackable is ARPlane plane)
            {
                anchor = m_AnchorManager.AttachAnchor(plane, hit.pose);
                if (anchor == null)
                    Debug.LogWarning("[TapPlace] AttachAnchor failed, trying TryAddAnchorAsync.");
            }

            if (anchor == null)
            {
                var result = await m_AnchorManager.TryAddAnchorAsync(hit.pose);
                if (!result.status.IsSuccess())
                {
                    Debug.LogError($"[TapPlace] TryAddAnchorAsync failed: {result.status}");
                    SetStatus($"Anchor failed: {result.status}");
                    return;
                }

                anchor = result.value;
            }

            if (anchor == null)
            {
                SetStatus("Could not create anchor.");
                return;
            }

            if (m_ReplaceExistingPlacement && m_CurrentAnchor != null && m_CurrentAnchor != anchor)
                m_AnchorManager.TryRemoveAnchor(m_CurrentAnchor);

            m_CurrentAnchor = anchor;
            PlaceContentOnAnchor(anchor);
            SetStatus($"Placed on anchor\n{anchor.transform.position}");
            Debug.Log($"[TapPlace] Content parented to anchor {anchor.trackableId}");
        }

        void PlaceContentOnAnchor(ARAnchor anchor)
        {
            m_Content.SetParent(anchor.transform, false);
            m_Content.localPosition = m_LocalPositionOffset;
            m_Content.localRotation = Quaternion.identity;
            m_Content.localScale = Vector3.one * m_ContentScale;
            m_Content.gameObject.SetActive(true);
        }

        void CreateDebugOverlay()
        {
            var canvasGo = new GameObject("Tap Place Debug Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGo.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasGo.transform, false);
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0f, 1f);
            panelRt.anchorMax = new Vector2(0f, 1f);
            panelRt.pivot = new Vector2(0f, 1f);
            panelRt.anchoredPosition = new Vector2(16f, -16f);
            panelRt.sizeDelta = new Vector2(320f, 90f);
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

            var labelGo = new GameObject("Status", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(panel.transform, false);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(12f, 8f);
            labelRt.offsetMax = new Vector2(-12f, -8f);

            m_StatusText = labelGo.GetComponent<Text>();
            m_StatusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (m_StatusText.font == null)
                m_StatusText.font = Font.CreateDynamicFontFromOSFont(new[] { "Helvetica", "Arial", "sans-serif" }, 16);
            m_StatusText.fontSize = 18;
            m_StatusText.color = Color.white;
            m_StatusText.alignment = TextAnchor.UpperLeft;
            m_StatusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            m_StatusText.verticalOverflow = VerticalWrapMode.Overflow;
        }

        void SetStatus(string status)
        {
            if (status == m_LastStatus)
                return;

            m_LastStatus = status;
            if (m_StatusText != null)
                m_StatusText.text = status;
        }
    }
}
