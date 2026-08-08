using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Tap to raycast an AR plane, create an ARAnchor, and Instantiate content prefab on it.
    /// Tuned for UaaL embeds where Touchscreen / EventSystem often misbehave.
    /// </summary>
    public class TapToPlaceOnAnchor : MonoBehaviour
    {
        static readonly List<ARRaycastHit> s_Hits = new();

        const TrackableType k_PlaneHitMask =
            TrackableType.PlaneWithinPolygon |
            TrackableType.PlaneWithinBounds |
            TrackableType.PlaneEstimated |
            TrackableType.FeaturePoint;

        [SerializeField]
        ARRaycastManager m_RaycastManager;

        [SerializeField]
        ARAnchorManager m_AnchorManager;

        [SerializeField]
        ARPlaneManager m_PlaneManager;

        [Tooltip("Optional direct reference. If empty, tries AssetBundle then Resources by name.")]
        [SerializeField]
        GameObject m_ContentPrefab;

        [Tooltip("Resources / AssetBundle asset name, e.g. PlacedContent")]
        [SerializeField]
        string m_ContentPrefabName = "PlacedContent";

        [SerializeField]
        PlacedContentBundleLoader m_BundleLoader;

        [Tooltip("Try loading a self-contained AssetBundle before Resources.")]
        [SerializeField]
        bool m_PreferAssetBundle = true;

        [SerializeField]
        Vector3 m_LocalPositionOffset = Vector3.zero;

        [SerializeField]
        float m_ContentScale = 1f;

        [Tooltip("If enabled, each new tap removes previous placements.")]
        [SerializeField]
        bool m_ReplaceExistingPlacement = true;

        [Tooltip("Ignore EventSystem UI hits (recommended for UaaL fullscreen AR).")]
        [SerializeField]
        bool m_IgnoreUiBlocking = true;

        [Tooltip("Show on-screen tap/plane status (helps debug device builds).")]
        [SerializeField]
        bool m_ShowDebugHud = true;

        [Tooltip("Spawn a bright debug cube with content so placement is visible even if video fails.")]
        [SerializeField]
        bool m_SpawnDebugMarker = true;

        readonly List<Placement> m_Placements = new();
        bool m_Ready;
        bool m_HostOwnsPrefab;
        string m_Status = "init";
        int m_TapCount;
        int m_PlaneCount;
        bool m_HasQueuedTap;
        Vector2 m_QueuedTap;
        string m_LastInputSource = "-";

        struct Placement
        {
            public ARAnchor anchor;
            public GameObject instance;
        }

        public GameObject contentPrefab
        {
            get => m_ContentPrefab;
            set => m_ContentPrefab = value;
        }

        public float contentScale
        {
            get => m_ContentScale;
            set => m_ContentScale = value;
        }

        public int PlacementCount => m_Placements.Count;

        /// <summary>
        /// Inject a tap from native (UIKit) or IMGUI when Input System does not see touches in UaaL.
        /// Coordinates must be Unity screen space (origin bottom-left, pixels).
        /// </summary>
        public void InjectTap(Vector2 unityScreenPosition)
        {
            m_LastInputSource = "inject";
            m_TapCount++;
            m_Status = $"inject #{m_TapCount} @ {unityScreenPosition:F0}";
            Debug.Log($"[TapPlace] InjectTap #{m_TapCount} {unityScreenPosition}", this);

            if (!m_Ready)
            {
                ResolveManagers();
                if (m_ContentPrefab == null)
                    m_ContentPrefab = FindContentPrefabByName() ?? FindSceneTemplatePrefab();
                m_Ready = m_RaycastManager != null && m_AnchorManager != null && m_ContentPrefab != null;
                if (!m_Ready)
                {
                    m_Status =
                        $"inject NOT ready ray={(m_RaycastManager != null)} " +
                        $"anc={(m_AnchorManager != null)} prefab={(m_ContentPrefab != null)}";
                    Debug.LogWarning($"[TapPlace] {m_Status}", this);
                    return;
                }
            }

            TryPlaceAtScreenPosition(unityScreenPosition);
        }

        /// <summary>Mark ready after the UaaL host assigns a prefab from a downloaded bundle.</summary>
        public void MarkReady()
        {
            m_HostOwnsPrefab = true;
            ResolveManagers();
            m_Ready = m_RaycastManager != null && m_AnchorManager != null && m_ContentPrefab != null;
            m_Status = m_Ready
                ? $"ready prefab={m_ContentPrefab.name}"
                : $"host prefab set but managers missing ray={(m_RaycastManager != null)} anc={(m_AnchorManager != null)}";
            if (m_Ready)
                Debug.Log($"[TapPlace] Ready (host). Prefab='{m_ContentPrefab.name}'.", this);
            else
                Debug.LogWarning($"[TapPlace] {m_Status}", this);
        }

        void Awake()
        {
            ResolveManagers();
            HideSceneTemplateIfPresent();
            EnsureTapReceiverProxy();

            if (m_BundleLoader == null)
                m_BundleLoader = GetComponent<PlacedContentBundleLoader>();
            if (m_BundleLoader == null)
                m_BundleLoader = FindAnyObjectByType<PlacedContentBundleLoader>();
        }

        /// <summary>
        /// Stable GameObject name for UnitySendMessage from native (XR Origin name is awkward).
        /// </summary>
        void EnsureTapReceiverProxy()
        {
            const string receiverName = "AventoTapReceiver";
            var existing = GameObject.Find(receiverName);
            if (existing != null)
            {
                var bridge = existing.GetComponent<AventoTapReceiver>();
                if (bridge != null)
                    bridge.Bind(this);
                return;
            }

            var go = new GameObject(receiverName);
            DontDestroyOnLoad(go);
            go.AddComponent<AventoTapReceiver>().Bind(this);
        }

        /// <summary>UnitySendMessage("AventoTapReceiver", "OnNativeTap", csv)</summary>
        public void OnNativeTap(string csv)
        {
            AventoTapReceiver.ParseAndInject(this, csv);
        }

        void Start()
        {
            StartCoroutine(InitializeContentPrefab());
        }

        void OnGUI()
        {
            // UaaL often never feeds Input System / legacy Input, but IMGUI still gets MouseDown
            // from UIKit touches. Capture them here (Y is top-left in IMGUI).
            var ev = Event.current;
            if (ev != null && ev.type == EventType.MouseDown && ev.button == 0)
            {
                // Ignore Exit AR button area (top-left) so Host OnGUI can handle it.
                if (ev.mousePosition.x > 140f || ev.mousePosition.y > 60f)
                {
                    m_QueuedTap = new Vector2(ev.mousePosition.x, Screen.height - ev.mousePosition.y);
                    m_HasQueuedTap = true;
                    m_LastInputSource = "imgui";
                }
            }

            if (!m_ShowDebugHud)
                return;

            var planes = m_PlaneManager != null ? m_PlaneManager.trackables.count : m_PlaneCount;
            var label =
                $"[TapPlace] {m_Status}\n" +
                $"ready={m_Ready} prefab={(m_ContentPrefab != null ? m_ContentPrefab.name : "null")}\n" +
                $"planes={planes} taps={m_TapCount} placed={m_Placements.Count}\n" +
                $"in={m_LastInputSource} fwd=v3\n" +
                "Scan floor/table, then tap a plane.";

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(22, Screen.height / 40),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.LowerLeft,
                wordWrap = true,
                normal = { textColor = Color.white }
            };
            var pad = 16f;
            var h = Mathf.Min(Screen.height * 0.32f, style.fontSize * 9f);
            var rect = new Rect(pad, Screen.height - h - pad, Screen.width - pad * 2f, h);
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = prev;
            GUI.Label(rect, label, style);
        }

        IEnumerator InitializeContentPrefab()
        {
            ResolveManagers();

            if (m_ContentPrefab == null)
            {
                var waitHostUntil = Time.realtimeSinceStartup + 2.5f;
                while (m_ContentPrefab == null && Time.realtimeSinceStartup < waitHostUntil)
                    yield return null;
            }

            if (m_ContentPrefab == null && m_PreferAssetBundle && !m_HostOwnsPrefab)
            {
                if (m_BundleLoader == null)
                    m_BundleLoader = gameObject.AddComponent<PlacedContentBundleLoader>();

                if (!m_BundleLoader.IsLoaded && !m_BundleLoader.IsLoading)
                    m_BundleLoader.BeginLoad();

                var timeout = Time.realtimeSinceStartup + 15f;
                while (!m_BundleLoader.IsLoaded &&
                       m_BundleLoader.IsLoading &&
                       Time.realtimeSinceStartup < timeout)
                    yield return null;

                if (m_ContentPrefab == null && m_BundleLoader.LoadedPrefab != null)
                {
                    m_ContentPrefab = m_BundleLoader.LoadedPrefab;
                    Debug.Log($"[TapPlace] Using AssetBundle prefab '{m_ContentPrefab.name}'.", this);
                }
            }

            if (m_ContentPrefab == null)
                m_ContentPrefab = FindContentPrefabByName();

            if (m_ContentPrefab == null)
                m_ContentPrefab = FindSceneTemplatePrefab();

            m_Ready = m_RaycastManager != null && m_AnchorManager != null && m_ContentPrefab != null;
            m_Status = m_Ready
                ? $"ready prefab={m_ContentPrefab.name}"
                : $"NOT ready ray={(m_RaycastManager != null)} anc={(m_AnchorManager != null)} prefab={(m_ContentPrefab != null)}";

            if (!m_Ready)
                Debug.LogError($"[TapPlace] {m_Status}", this);
            else
                Debug.Log($"[TapPlace] Ready. Prefab='{m_ContentPrefab.name}'. Tap a plane.", this);
        }

        void ResolveManagers()
        {
            if (m_RaycastManager == null)
                m_RaycastManager = GetComponent<ARRaycastManager>();
            if (m_RaycastManager == null)
                m_RaycastManager = FindAnyObjectByType<ARRaycastManager>();

            if (m_AnchorManager == null)
                m_AnchorManager = GetComponent<ARAnchorManager>();
            if (m_AnchorManager == null)
                m_AnchorManager = FindAnyObjectByType<ARAnchorManager>();
            if (m_AnchorManager == null)
            {
                m_AnchorManager = gameObject.AddComponent<ARAnchorManager>();
                Debug.LogWarning("[TapPlace] ARAnchorManager was missing — added at runtime.", this);
            }

            if (m_PlaneManager == null)
                m_PlaneManager = GetComponent<ARPlaneManager>();
            if (m_PlaneManager == null)
                m_PlaneManager = FindAnyObjectByType<ARPlaneManager>();
            if (m_PlaneManager != null && !m_PlaneManager.enabled)
            {
                m_PlaneManager.enabled = true;
                Debug.LogWarning("[TapPlace] ARPlaneManager was disabled — enabled.", this);
            }
        }

        GameObject FindContentPrefabByName()
        {
            var name = string.IsNullOrWhiteSpace(m_ContentPrefabName)
                ? "PlacedContent"
                : m_ContentPrefabName.Trim();

            var fromResources = Resources.Load<GameObject>(name);
            if (fromResources != null)
            {
                Debug.Log($"[TapPlace] Loaded prefab by name from Resources: '{name}'", this);
                return fromResources;
            }

#if UNITY_EDITOR
            var guids = UnityEditor.AssetDatabase.FindAssets($"{name} t:Prefab");
            foreach (var guid in guids)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset != null && asset.name == name)
                {
                    Debug.LogWarning(
                        $"[TapPlace] Found '{name}' at '{path}'. " +
                        "For device builds use Resources or an AssetBundle.",
                        this);
                    return asset;
                }
            }
#endif

            Debug.LogWarning($"[TapPlace] Prefab '{name}' not found in Resources/AssetBundle.", this);
            return null;
        }

        GameObject FindSceneTemplatePrefab()
        {
            var name = string.IsNullOrWhiteSpace(m_ContentPrefabName)
                ? "PlacedContent"
                : m_ContentPrefabName.Trim();

            foreach (var candidate in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (candidate == null || candidate.name != name)
                    continue;
                if (candidate.gameObject.scene != gameObject.scene)
                    continue;
                if (candidate.parent != null)
                    continue;

                Debug.Log($"[TapPlace] Using scene template '{name}' as prefab source.", this);
                return candidate.gameObject;
            }

            return null;
        }

        void HideSceneTemplateIfPresent()
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (candidate == null || candidate.name != "PlacedContent")
                    continue;
                if (candidate.gameObject.scene != gameObject.scene)
                    continue;
                if (candidate.parent != null)
                    continue;
                if (candidate.gameObject.activeSelf)
                    candidate.gameObject.SetActive(false);
            }
        }

        void Update()
        {
            if (m_PlaneManager != null)
                m_PlaneCount = m_PlaneManager.trackables.count;

            if (!WasTapThisFrame(out var screenPosition))
                return;

            m_TapCount++;
            Debug.Log($"[TapPlace] Tap #{m_TapCount} at {screenPosition}", this);
            m_Status = $"tap #{m_TapCount} @ {screenPosition:F0}";

            if (!m_IgnoreUiBlocking && IsPointerOverUI(screenPosition))
            {
                m_Status = $"tap #{m_TapCount} ignored (UI)";
                Debug.Log("[TapPlace] Tap ignored (UI).", this);
                return;
            }

            if (!m_Ready)
            {
                ResolveManagers();
                if (m_ContentPrefab == null)
                    m_ContentPrefab = FindContentPrefabByName() ?? FindSceneTemplatePrefab();
                m_Ready = m_RaycastManager != null && m_AnchorManager != null && m_ContentPrefab != null;
                if (!m_Ready)
                {
                    m_Status =
                        $"tap while NOT ready ray={(m_RaycastManager != null)} " +
                        $"anc={(m_AnchorManager != null)} prefab={(m_ContentPrefab != null)}";
                    Debug.LogWarning($"[TapPlace] {m_Status}", this);
                    return;
                }
            }

            TryPlaceAtScreenPosition(screenPosition);
        }

        bool WasTapThisFrame(out Vector2 screenPosition)
        {
            screenPosition = default;

            // Queued from IMGUI Event or native UIKit → UnitySendMessage (UaaL primary path).
            if (m_HasQueuedTap)
            {
                screenPosition = m_QueuedTap;
                m_HasQueuedTap = false;
                return true;
            }

            // Input System touchscreen.
            var touchscreen = Touchscreen.current;
            if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame)
            {
                screenPosition = touchscreen.primaryTouch.position.ReadValue();
                m_LastInputSource = "touchscreen";
                return true;
            }

            if (touchscreen != null)
            {
                for (var i = 0; i < touchscreen.touches.Count; i++)
                {
                    var t = touchscreen.touches[i];
                    if (t.press.wasPressedThisFrame)
                    {
                        screenPosition = t.position.ReadValue();
                        m_LastInputSource = "touchscreen";
                        return true;
                    }
                }
            }

            var pointer = Pointer.current;
            if (pointer != null && pointer.press.wasPressedThisFrame)
            {
                screenPosition = pointer.position.ReadValue();
                m_LastInputSource = "pointer";
                return true;
            }

            try
            {
                if (Input.touchCount > 0)
                {
                    var t = Input.GetTouch(0);
                    if (t.phase == TouchPhase.Began)
                    {
                        screenPosition = t.position;
                        m_LastInputSource = "legacy-touch";
                        return true;
                    }
                }

                if (Input.GetMouseButtonDown(0))
                {
                    screenPosition = Input.mousePosition;
                    m_LastInputSource = "legacy-mouse";
                    return true;
                }
            }
            catch (System.InvalidOperationException)
            {
                // Legacy input disabled in Player Settings.
            }

            return false;
        }

        static bool IsPointerOverUI(Vector2 screenPosition)
        {
            if (EventSystem.current == null)
                return false;

            var eventData = new PointerEventData(EventSystem.current)
            {
                position = screenPosition
            };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);
            for (var i = 0; i < results.Count; i++)
            {
                if (results[i].gameObject != null)
                    return true;
            }

            return false;
        }

        async void TryPlaceAtScreenPosition(Vector2 screenPosition)
        {
            if (m_RaycastManager == null || m_AnchorManager == null || m_ContentPrefab == null)
            {
                m_Status = "missing managers/prefab on place";
                Debug.LogError($"[TapPlace] {m_Status}", this);
                return;
            }

            m_Ready = true;

            if (!m_RaycastManager.Raycast(screenPosition, s_Hits, k_PlaneHitMask))
            {
                var planes = m_PlaneManager != null ? m_PlaneManager.trackables.count : -1;
                m_Status = $"no plane hit (planes={planes}). Keep scanning.";
                Debug.Log($"[TapPlace] No plane hit at {screenPosition}. planes={planes}", this);
                return;
            }

            var hit = s_Hits[0];
            m_Status = $"hit {hit.hitType} @ {hit.pose.position}";
            Debug.Log($"[TapPlace] Hit {hit.hitType} at {hit.pose.position}", this);

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
                    m_Status = $"anchor failed: {result.status}";
                    Debug.LogError($"[TapPlace] TryAddAnchorAsync failed: {result.status}");
                    return;
                }

                anchor = result.value;
            }

            if (anchor == null)
            {
                m_Status = "anchor null";
                return;
            }

            if (m_ReplaceExistingPlacement)
                ClearAllPlacements();

            var instance = PlaceContentOnAnchor(anchor);
            m_Status = $"placed '{instance.name}' total={m_Placements.Count}";
            Debug.Log(
                $"[TapPlace] OK — placed '{instance.name}' at {instance.transform.position} " +
                $"(anchor {anchor.trackableId}, total={m_Placements.Count})",
                this);
        }

        GameObject PlaceContentOnAnchor(ARAnchor anchor)
        {
            var instance = Instantiate(m_ContentPrefab, anchor.transform);
            instance.name = $"{m_ContentPrefab.name}_{m_Placements.Count + 1}";
            instance.transform.localPosition = m_LocalPositionOffset;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * m_ContentScale;
            instance.SetActive(true);

            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                t.gameObject.SetActive(true);

            var animators = instance.GetComponentsInChildren<AnimatePlacedCube>(true);
            for (var i = 0; i < animators.Length; i++)
                animators[i].Play();

            var videos = instance.GetComponentsInChildren<PlayVideoOnPlace>(true);
            for (var i = 0; i < videos.Length; i++)
                videos[i].enabled = true;

            if (m_SpawnDebugMarker)
                SpawnDebugMarker(instance.transform);

            m_Placements.Add(new Placement
            {
                anchor = anchor,
                instance = instance
            });

            return instance;
        }

        static void SpawnDebugMarker(Transform parent)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "DebugPlaceMarker";
            marker.transform.SetParent(parent, false);
            marker.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            marker.transform.localScale = new Vector3(0.12f, 0.12f, 0.12f);
            var renderer = marker.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit")
                             ?? Shader.Find("Unlit/Color")
                             ?? Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    renderer.material = new Material(shader);
                    if (renderer.material.HasProperty("_BaseColor"))
                        renderer.material.SetColor("_BaseColor", Color.magenta);
                    else if (renderer.material.HasProperty("_Color"))
                        renderer.material.color = Color.magenta;
                }
            }

            var col = marker.GetComponent<Collider>();
            if (col != null)
                Destroy(col);
        }

        public void ClearAllPlacements()
        {
            for (var i = m_Placements.Count - 1; i >= 0; i--)
            {
                var placement = m_Placements[i];
                if (placement.instance != null)
                    Destroy(placement.instance);

                if (placement.anchor != null && m_AnchorManager != null)
                    m_AnchorManager.TryRemoveAnchor(placement.anchor);
            }

            m_Placements.Clear();
        }
    }
}
