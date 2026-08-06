using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
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
        AnimatePlacedCube m_ContentAnimator;

        [SerializeField]
        Vector3 m_LocalPositionOffset = Vector3.zero;

        [SerializeField]
        float m_ContentScale = 1f;

        [SerializeField]
        bool m_ReplaceExistingPlacement = true;

        ARAnchor m_CurrentAnchor;
        bool m_Ready;
        bool m_HasPlaced;

        public Transform content
        {
            get => m_Content;
            set => m_Content = value;
        }

        void Awake()
        {
            ResolveReferences();
        }

        void Start()
        {
            ResolveReferences();
            m_Ready = m_RaycastManager != null && m_AnchorManager != null && m_Content != null;

            if (!m_Ready)
            {
                Debug.LogError(
                    "[TapPlace] Still missing references after Start. " +
                    $"raycast={(m_RaycastManager != null)} " +
                    $"anchor={(m_AnchorManager != null)} " +
                    $"content={(m_Content != null)}",
                    this);
            }
            else
            {
                Debug.Log("[TapPlace] Ready. Tap a detected plane to place content.", this);
            }
        }

        void ResolveReferences()
        {
            if (m_RaycastManager == null)
                m_RaycastManager = GetComponent<ARRaycastManager>();
            if (m_RaycastManager == null)
                m_RaycastManager = FindFirstObjectByType<ARRaycastManager>();

            if (m_AnchorManager == null)
                m_AnchorManager = GetComponent<ARAnchorManager>();
            if (m_AnchorManager == null)
                m_AnchorManager = FindFirstObjectByType<ARAnchorManager>();
            if (m_AnchorManager == null)
            {
                m_AnchorManager = gameObject.AddComponent<ARAnchorManager>();
                Debug.LogWarning("[TapPlace] ARAnchorManager was missing — added at runtime.", this);
            }

            if (m_Content == null)
            {
                // GameObject.Find skips inactive objects — search scene transforms instead.
                foreach (var candidate in Resources.FindObjectsOfTypeAll<Transform>())
                {
                    if (candidate == null || candidate.name != "PlacedContent")
                        continue;
                    if (candidate.gameObject.scene != gameObject.scene)
                        continue;
                    m_Content = candidate;
                    break;
                }
            }

            if (m_Content != null)
            {
                if (m_ContentAnimator == null)
                    m_ContentAnimator = m_Content.GetComponentInChildren<AnimatePlacedCube>(true);

                // Hide only before the first successful placement.
                if (!m_HasPlaced && m_Content.gameObject.activeSelf)
                    m_Content.gameObject.SetActive(false);
            }
        }

        void Update()
        {
            if (!WasTapThisFrame(out var screenPosition))
                return;

            if (IsPointerOverUI())
                return;

            if (!m_Ready)
                ResolveReferences();

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
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(-1);
        }

        async void TryPlaceAtScreenPosition(Vector2 screenPosition)
        {
            if (m_RaycastManager == null || m_AnchorManager == null || m_Content == null)
            {
                Debug.LogError(
                    "[TapPlace] Missing RaycastManager, AnchorManager, or Content. " +
                    $"raycast={(m_RaycastManager != null)} " +
                    $"anchor={(m_AnchorManager != null)} " +
                    $"content={(m_Content != null)}",
                    this);
                return;
            }

            m_Ready = true;

            if (!m_RaycastManager.Raycast(screenPosition, s_Hits, TrackableType.PlaneWithinPolygon))
            {
                Debug.Log("[TapPlace] No plane hit. Keep scanning surfaces.");
                return;
            }

            var hit = s_Hits[0];
            Debug.Log($"[TapPlace] Hit {hit.hitType} at {hit.pose.position}");

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
                    return;
                }

                anchor = result.value;
            }

            if (anchor == null)
                return;

            if (m_ReplaceExistingPlacement && m_CurrentAnchor != null && m_CurrentAnchor != anchor)
                m_AnchorManager.TryRemoveAnchor(m_CurrentAnchor);

            m_CurrentAnchor = anchor;
            PlaceContentOnAnchor(anchor);
            Debug.Log(
                $"[TapPlace] OK — content placed at {m_Content.position} (anchor {anchor.trackableId})",
                this);
        }

        void PlaceContentOnAnchor(ARAnchor anchor)
        {
            m_HasPlaced = true;
            m_Content.SetParent(anchor.transform, false);
            m_Content.localPosition = m_LocalPositionOffset;
            m_Content.localRotation = Quaternion.identity;
            m_Content.localScale = Vector3.one * m_ContentScale;
            m_Content.gameObject.SetActive(true);

            if (m_ContentAnimator != null)
                m_ContentAnimator.Play();
        }
    }
}
