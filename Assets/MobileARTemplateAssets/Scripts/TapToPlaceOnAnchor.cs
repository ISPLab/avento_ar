using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
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

        /// <summary>
        /// Auto-place uses real plane hits (polygon + bounds). Feature points are excluded
        /// so content does not spawn before a floor/table is scanned.
        /// </summary>
        const TrackableType k_AutoPlaceHitMask =
            TrackableType.PlaneWithinPolygon |
            TrackableType.PlaneWithinBounds;

        const float k_AutoPlaceMinPlaneArea = 0.08f;
        const float k_AutoPlaceMinCoachingSeconds = 0.4f;
        const string k_ScanOverlayResourcePath = "overlays/scanning";
        const string k_ScanOverlayAssetPath = "Assets/Scenes/overlays/scanning.png";

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

        [Tooltip("Yaw offset in degrees (from offer Heading). Applied on top of camera-forward alignment.")]
        [SerializeField]
        float m_HeadingDegrees;

        [Tooltip("If enabled, each new tap removes previous placements.")]
        [SerializeField]
        bool m_ReplaceExistingPlacement = true;

        [Tooltip("Ignore EventSystem UI hits (recommended for UaaL fullscreen AR).")]
        [SerializeField]
        bool m_IgnoreUiBlocking = true;

        [Tooltip("Show on-screen tap/plane status (helps debug device builds).")]
        [SerializeField]
        bool m_ShowDebugHud = false;

        [Header("Editor / XR Simulation")]
        [Tooltip("If on, hide the in-scene PlacedContent preview in Play Mode (device-style). Leave off to preview the scene content.")]
        [SerializeField]
        bool m_HideSceneContentInEditor;

        [Tooltip("If on, do not move the in-scene PlacedContent in front of the XR Simulation camera.")]
        [SerializeField]
        bool m_DoNotFrameEditorCameraOnSceneContent;

        [Tooltip("How far in front of the simulation camera to put the scene content (meters). 0 = 3.5.")]
        [SerializeField]
        float m_EditorViewDistanceMeters = 3.5f;

        readonly List<Placement> m_Placements = new();
        bool m_Ready;
        bool m_HostOwnsPrefab;
        string m_Status = "init";
        int m_TapCount;
        int m_PlaneCount;
        bool m_HasQueuedTap;
        Vector2 m_QueuedTap;
        string m_LastInputSource = "-";

        /// <summary>
        /// When true, place once on the first horizontal plane ~N meters in front of the camera (no tap).
        /// </summary>
        bool m_AutomaticScenePlacement;
        float m_AutoPlaceDistanceMeters = 2f;
        bool m_AutoPlaceDone;
        bool m_AutoPlaceInFlight;
        float m_AutoPlaceArmedAt;
        bool m_AutoPlaceCoachingArmed;
        bool m_ScanCoachingDismissed;
        Texture2D m_ScanCoachingTexture;
        bool m_ScanCoachingTextureLoadAttempted;

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

        /// <summary>Offer heading (°) — yaw offset after aligning content to camera forward.</summary>
        public float contentHeadingDegrees
        {
            get => m_HeadingDegrees;
            set => m_HeadingDegrees = value;
        }

        public int PlacementCount => m_Placements.Count;

        bool HasPlacedContent() => m_Placements.Count > 0 || m_AutoPlaceDone || m_ScanCoachingDismissed;

        bool ShouldShowScanCoaching() =>
            !m_ScanCoachingDismissed && m_Placements.Count == 0 && !m_AutoPlaceDone;

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

            if (AventoInteractionDirector.TryHandleTap(unityScreenPosition))
            {
                m_Status = $"interact #{m_TapCount}";
                Debug.Log($"[TapPlace] InjectTap consumed by interactable @ {unityScreenPosition}", this);
                return;
            }

            if (m_AutomaticScenePlacement)
            {
                m_Status = m_AutoPlaceDone
                    ? "auto placed — tap-to-replace disabled"
                    : "auto: scanning — tap ignored until a surface is found";
                return;
            }

            if (!m_Ready)
            {
                ResolveManagers();
                if (m_ContentPrefab == null)
                    TryAssignEditorOrResourcesFallback();
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

        /// <summary>
        /// Enable/disable automatic placement: wait for a scanned floor/table, then put content
        /// <paramref name="distanceMeters"/> in front of the phone on that surface (no tap).
        /// </summary>
        public void SetAutomaticScenePlacement(bool enabled, float distanceMeters = 2f)
        {
            m_AutomaticScenePlacement = enabled;
            m_AutoPlaceDistanceMeters = distanceMeters > 0.1f ? distanceMeters : 2f;
            m_AutoPlaceDone = false;
            m_AutoPlaceInFlight = false;
            m_AutoPlaceArmedAt = Time.realtimeSinceStartup;
            m_AutoPlaceCoachingArmed = false;
            m_ScanCoachingDismissed = false;
            m_Status = enabled
                ? $"auto-place ON ({m_AutoPlaceDistanceMeters:0.##}m) — scan floor/table"
                : "auto-place OFF — tap to place";
            Debug.Log($"[TapPlace] AutomaticScenePlacement={enabled} distance={m_AutoPlaceDistanceMeters}", this);
            if (enabled)
                ArmScanCoaching();
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
            ArmScanCoaching();
#if UNITY_EDITOR
            StartCoroutine(PresentSceneContentInEditor());
#endif
        }

        void OnGUI()
        {
            // UaaL often never feeds Input System / legacy Input, but IMGUI still gets MouseDown
            // from UIKit touches. Capture them here (Y is top-left in IMGUI).
            var ev = Event.current;
            if (ev != null && ev.type == EventType.MouseDown && ev.button == 0)
            {
                // Ignore TabBar-style cancel chrome (bottom center).
                if (!AventoUnityHost.IsInExitChromeImgui(ev.mousePosition)
                    && !AventoTessaVoiceBar.IsInBarImgui(ev.mousePosition))
                {
                    m_QueuedTap = new Vector2(ev.mousePosition.x, Screen.height - ev.mousePosition.y);
                    m_HasQueuedTap = true;
                    m_LastInputSource = "imgui";
                }
            }

            if (ShouldShowScanCoaching())
                DrawScanSurfacesCoaching();

            if (!m_ShowDebugHud)
                return;

            var planes = m_PlaneManager != null ? m_PlaneManager.trackables.count : m_PlaneCount;
            var autoHint = m_AutomaticScenePlacement
                ? (m_AutoPlaceDone ? "auto placed" : $"auto {m_AutoPlaceDistanceMeters:0.#}m…")
                : "tap a plane";
            var label =
                $"[TapPlace] {m_Status}\n" +
                $"ready={m_Ready} prefab={(m_ContentPrefab != null ? m_ContentPrefab.name : "null")}\n" +
                $"planes={planes} taps={m_TapCount} placed={m_Placements.Count}\n" +
                $"in={m_LastInputSource} {autoHint}\n" +
                (m_AutomaticScenePlacement && !m_AutoPlaceDone
                    ? "Scan floor/table — placing automatically."
                    : "Scan floor/table, then tap a plane.");

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

#if UNITY_EDITOR
            // Editor / XR Simulation: prefer the in-scene instance, then Resources.
            if (m_ContentPrefab == null)
                m_ContentPrefab = FindSceneTemplatePrefab() ?? FindContentPrefabByName();
            m_Ready = m_RaycastManager != null && m_AnchorManager != null && m_ContentPrefab != null;
            m_Status = m_Ready
                ? $"ready prefab={m_ContentPrefab.name}"
                : $"NOT ready ray={(m_RaycastManager != null)} anc={(m_AnchorManager != null)} prefab={(m_ContentPrefab != null)}";
            if (!m_Ready)
                Debug.LogError($"[TapPlace] {m_Status}", this);
            else
                Debug.Log($"[TapPlace] Ready. Prefab='{m_ContentPrefab.name}'. Tap a plane.", this);
            yield break;
#else
            var hostPresent = AventoUnityHost.Instance != null;
            if (m_ContentPrefab == null)
            {
                // Device UaaL: wait for the native-downloaded AssetBundle, not Resources.
                var waitHostUntil = Time.realtimeSinceStartup + 45f;
                while (m_ContentPrefab == null && Time.realtimeSinceStartup < waitHostUntil)
                {
                    hostPresent = AventoUnityHost.Instance != null;
                    if (!hostPresent && Time.realtimeSinceStartup > 2.5f)
                        break;
                    yield return null;
                }
            }

            hostPresent = AventoUnityHost.Instance != null || m_HostOwnsPrefab;

            if (m_ContentPrefab == null && m_PreferAssetBundle && !m_HostOwnsPrefab && !hostPresent)
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

            if (m_ContentPrefab == null && !hostPresent && !m_HostOwnsPrefab)
                m_ContentPrefab = FindSceneTemplatePrefab() ?? FindContentPrefabByName();
#endif

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

        /// <summary>
        /// Editor Play Mode / XR Simulation: load Resources or the scene template.
        /// On device UaaL the host supplies the downloaded prefab — do not use Resources there.
        /// </summary>
        void TryAssignEditorOrResourcesFallback()
        {
#if UNITY_EDITOR
            if (m_ContentPrefab != null)
                return;
            m_ContentPrefab = FindSceneTemplatePrefab() ?? FindContentPrefabByName();
#else
            if (m_HostOwnsPrefab || AventoUnityHost.Instance != null)
                return;
            m_ContentPrefab = FindSceneTemplatePrefab() ?? FindContentPrefabByName();
#endif
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

        void HideSceneTemplateIfPresent(bool force = false)
        {
#if UNITY_EDITOR
            if (!force && !m_HideSceneContentInEditor)
                return;
#endif
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

#if UNITY_EDITOR
        IEnumerator PresentSceneContentInEditor()
        {
            // XR Simulation camera + environment scene load a few frames after Play.
            Camera cam = null;
            var until = Time.realtimeSinceStartup + 2.5f;
            while (Time.realtimeSinceStartup < until)
            {
                cam = ResolveEditorViewCamera();
                if (cam != null)
                    break;
                yield return null;
            }

            for (var i = 0; i < 8; i++)
                yield return null;

            cam = ResolveEditorViewCamera() ?? cam;

            var template = FindSceneTemplatePrefab();
            if (template == null)
            {
                var prefab = FindContentPrefabByName();
                if (prefab != null)
                {
                    template = Instantiate(prefab);
                    template.name = string.IsNullOrWhiteSpace(m_ContentPrefabName)
                        ? "PlacedContent"
                        : m_ContentPrefabName.Trim();
                    Debug.Log(
                        $"[TapPlace] Editor: instantiated '{template.name}' for XR Simulation preview.",
                        this);
                }
            }

            if (template == null)
            {
                Debug.LogWarning(
                    "[TapPlace] Editor: no PlacedContent in the scene or Resources — XR Simulation is empty.",
                    this);
                yield break;
            }

            if (!template.activeSelf)
            {
                Debug.Log(
                    $"[TapPlace] Editor: '{template.name}' is disabled in the scene, keeping it hidden.",
                    this);
                yield break;
            }

            if (m_DoNotFrameEditorCameraOnSceneContent)
            {
                Debug.Log(
                    $"[TapPlace] Editor: leaving '{template.name}' at authored pose (do-not-frame is on).",
                    this);
                yield break;
            }

            PlaceTemplateInFrontOfEditorCamera(template, cam);
        }

        void PlaceTemplateInFrontOfEditorCamera(GameObject template, Camera cam)
        {
            if (cam == null)
                cam = ResolveEditorViewCamera();
            if (cam == null)
            {
                Debug.LogWarning(
                    "[TapPlace] Editor: no XR Simulation camera yet — content stays at authored pose.",
                    this);
                return;
            }

            var bounds = EncapsulateRenderers(template);
            var forward = cam.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.forward;
            forward.Normalize();

            var distance = m_EditorViewDistanceMeters > 0.1f ? m_EditorViewDistanceMeters : 3.5f;
            // Frame the visible mesh (children can sit 10m+ off the empty root).
            var desiredCenter = cam.transform.position + forward * distance;
            desiredCenter.y = Mathf.Max(0.05f, bounds.extents.y);
            template.transform.position += desiredCenter - bounds.center;

            var after = EncapsulateRenderers(template);
            Debug.Log(
                $"[TapPlace] Editor: showing '{template.name}' in XR Simulation " +
                $"{distance:0.0}m ahead (visual {after.center}, size {after.size}).",
                this);
        }

        Camera ResolveEditorViewCamera()
        {
            var origin = FindFirstObjectByType<XROrigin>();
            if (origin != null && origin.Camera != null)
                return origin.Camera;
            return Camera.main;
        }

        static Bounds EncapsulateRenderers(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.one);

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
#endif

        void Update()
        {
            if (m_PlaneManager != null)
                m_PlaneCount = m_PlaneManager.trackables.count;

            if (ShouldShowScanCoaching() && Time.frameCount % 30 == 0)
                ArmScanCoaching(force: true);
            else if (m_ScanCoachingDismissed && Time.frameCount % 30 == 0)
                HideScanCoachingVisuals();

            if (m_AutomaticScenePlacement && !m_AutoPlaceDone && !m_AutoPlaceInFlight)
                TryAutomaticPlacement();

            if (!WasTapThisFrame(out var screenPosition))
                return;

            m_TapCount++;
            Debug.Log($"[TapPlace] Tap #{m_TapCount} at {screenPosition}", this);
            m_Status = $"tap #{m_TapCount} @ {screenPosition:F0}";

            if (AventoInteractionDirector.TryHandleTap(screenPosition))
            {
                m_Status = $"interact #{m_TapCount}";
                Debug.Log($"[TapPlace] Tap consumed by interactable @ {screenPosition}", this);
                return;
            }

            if (m_AutomaticScenePlacement)
            {
                if (!m_AutoPlaceDone)
                    m_Status = "auto: scanning — tap ignored";
                return;
            }

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
                    TryAssignEditorOrResourcesFallback();
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

        void TryAutomaticPlacement()
        {
            if (!EnsureReadyForPlace())
            {
                m_Status = "auto: waiting for prefab…";
                return;
            }

            ArmScanCoaching();

            if (ARSession.state < ARSessionState.SessionTracking)
            {
                m_Status = $"auto: waiting for tracking ({ARSession.state})";
                return;
            }

            if (Time.realtimeSinceStartup - m_AutoPlaceArmedAt < k_AutoPlaceMinCoachingSeconds)
            {
                m_Status = "auto: scan coaching…";
                return;
            }

            var cam = Camera.main;
            if (cam == null)
            {
                m_Status = "auto: no Camera.main";
                return;
            }

            if (m_PlaneManager == null || !HasUsableBottomPlane())
            {
                m_Status = $"auto: waiting for a floor/table (planes={m_PlaneCount})";
                return;
            }

            // 1) Screen center — natural “in front of the phone” look.
            var screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (TryAutoPlaceFromScreenRay(screenCenter, cam, "auto-center"))
                return;

            // 2) Slightly below center — floor is usually in the lower half of the view.
            var screenLower = new Vector2(Screen.width * 0.5f, Screen.height * 0.32f);
            if (TryAutoPlaceFromScreenRay(screenLower, cam, "auto-lower"))
                return;

            // 3) Floor exists but the view-center ray missed it (looking at the horizon).
            if (TryAutoPlaceOnFloorInFront(cam))
                return;

            m_Status = "auto: floor found — point the camera at it";
        }

        bool TryAutoPlaceFromScreenRay(Vector2 screenPosition, Camera cam, string source)
        {
            if (m_RaycastManager == null)
                return false;
            if (!m_RaycastManager.Raycast(screenPosition, s_Hits, k_AutoPlaceHitMask))
                return false;

            var hit = s_Hits[0];
            if (!IsUsableAutoPlaceHit(hit, cam))
                return false;

            m_LastInputSource = source;
            m_Status = $"auto: placing via {source}";
            PlaceAtPose(hit.pose, hit.trackable as ARPlane, markAutoDone: true);
            return true;
        }

        bool TryAutoPlaceOnFloorInFront(Camera cam)
        {
            var pose = PoseInFrontOnPlane(cam, float.NaN);
            ARPlane best = null;
            var bestScore = float.MaxValue;
            foreach (var plane in m_PlaneManager.trackables)
            {
                if (!IsUsableAutoPlacePlane(plane))
                    continue;
                if (plane.center.y >= cam.transform.position.y - 0.02f)
                    continue;
                var dx = plane.center.x - pose.position.x;
                var dz = plane.center.z - pose.position.z;
                var score = dx * dx + dz * dz;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = plane;
                }
            }

            if (best == null)
                return false;

            pose = new Pose(
                new Vector3(pose.position.x, best.center.y, pose.position.z),
                pose.rotation);

            m_LastInputSource = "auto-floor";
            m_Status = "auto: placing on scanned floor";
            PlaceAtPose(pose, best, markAutoDone: true);
            return true;
        }

        Pose PoseInFrontOnPlane(Camera cam, float planeY)
        {
            var flat = FlattenCameraForward(cam);
            var dist = m_AutoPlaceDistanceMeters > 0.1f ? m_AutoPlaceDistanceMeters : 2f;
            var aim = cam.transform.position + flat * dist;
            var y = float.IsNaN(planeY) ? aim.y : planeY;
            return new Pose(new Vector3(aim.x, y, aim.z), Quaternion.identity);
        }

        static Vector3 FlattenCameraForward(Camera cam)
        {
            var flat = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f)
                flat = Vector3.forward;
            return flat.normalized;
        }

        bool HasUsableBottomPlane()
        {
            if (m_PlaneManager == null)
                return false;

            foreach (var plane in m_PlaneManager.trackables)
            {
                if (IsUsableAutoPlacePlane(plane))
                    return true;
            }

            return false;
        }

        static bool IsUsableAutoPlacePlane(ARPlane plane)
        {
            if (plane == null)
                return false;
            if (plane.trackingState != TrackingState.Tracking &&
                plane.trackingState != TrackingState.Limited)
                return false;
            if (plane.alignment != PlaneAlignment.HorizontalUp)
                return false;
            return plane.size.x * plane.size.y >= k_AutoPlaceMinPlaneArea;
        }

        static bool IsUsableAutoPlaceHit(ARRaycastHit hit, Camera cam)
        {
            if (hit.trackable is not ARPlane plane || !IsUsableAutoPlacePlane(plane))
                return false;

            if (hit.pose.position.y >= cam.transform.position.y - 0.02f)
                return false;

            var vp = cam.WorldToViewportPoint(hit.pose.position);
            if (vp.z <= 0f)
                return false;

            var up = hit.pose.up;
            return Vector3.Dot(up, Vector3.up) > 0.55f;
        }

        void ArmScanCoaching(bool force = false)
        {
            if (!ShouldShowScanCoaching())
                return;
            if (m_AutoPlaceCoachingArmed && !force)
                return;
            m_AutoPlaceCoachingArmed = true;

            var goals = FindObjectsByType<GoalManager>(FindObjectsInactive.Include);
            for (var i = 0; i < goals.Length; i++)
            {
                if (goals[i] != null)
                    goals[i].HoldScanSurfacesCoaching(true);
            }

            var menus = FindObjectsByType<ARTemplateMenuManager>(FindObjectsInactive.Include);
            for (var i = 0; i < menus.Length; i++)
            {
                if (menus[i] != null)
                    menus[i].SetPlaneVisualizationVisible(true);
            }

            var faders = FindObjectsByType<ARPlaneMeshVisualizerFader>(FindObjectsInactive.Include);
            for (var i = 0; i < faders.Length; i++)
            {
                if (faders[i] != null)
                    faders[i].visualizeSurfaces = true;
            }

            if (m_AutomaticScenePlacement)
                HideSceneTemplateIfPresent(force: true);
        }

        void DrawScanSurfacesCoaching()
        {
            var scanTexture = ResolveScanCoachingTexture();
            if (scanTexture != null)
            {
                var maxWidth = Screen.width * 0.58f;
                var maxHeight = Screen.height * 0.34f;
                var texW = Mathf.Max(1f, scanTexture.width);
                var texH = Mathf.Max(1f, scanTexture.height);
                var scale = Mathf.Min(maxWidth / texW, maxHeight / texH);
                var drawW = texW * scale;
                var drawH = texH * scale;
                var rect = new Rect(
                    (Screen.width - drawW) * 0.5f,
                    (Screen.height - drawH) * 0.5f,
                    drawW,
                    drawH);
                GUI.DrawTexture(rect, scanTexture, ScaleMode.ScaleToFit, true);
                return;
            }

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(32, Screen.height / 28),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                normal = { textColor = Color.white }
            };
            var width = Mathf.Min(Screen.width - 48f, 640f);
            var height = titleStyle.fontSize * 2.2f + 28f;
            var rectFallback = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.Box(rectFallback, GUIContent.none);
            GUI.color = prev;
            GUI.Label(rectFallback, "scanning ...", titleStyle);
        }

        Texture2D ResolveScanCoachingTexture()
        {
            if (m_ScanCoachingTextureLoadAttempted)
                return m_ScanCoachingTexture;

            m_ScanCoachingTextureLoadAttempted = true;

            // Device builds can only load from Resources (AssetDatabase is Editor-only).
            m_ScanCoachingTexture = Resources.Load<Texture2D>(k_ScanOverlayResourcePath);
            if (m_ScanCoachingTexture != null)
            {
                Debug.Log(
                    $"[TapPlace] Scan overlay loaded from Resources/{k_ScanOverlayResourcePath} " +
                    $"({m_ScanCoachingTexture.width}x{m_ScanCoachingTexture.height})",
                    this);
                return m_ScanCoachingTexture;
            }

#if UNITY_EDITOR
            m_ScanCoachingTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(k_ScanOverlayAssetPath);
            if (m_ScanCoachingTexture != null)
            {
                Debug.Log(
                    $"[TapPlace] Scan overlay loaded from {k_ScanOverlayAssetPath} " +
                    $"({m_ScanCoachingTexture.width}x{m_ScanCoachingTexture.height})",
                    this);
                return m_ScanCoachingTexture;
            }
#endif

            Debug.LogWarning(
                $"[TapPlace] Scan overlay missing. Put PNG at Assets/Resources/{k_ScanOverlayResourcePath}.png " +
                $"(also editable at {k_ScanOverlayAssetPath}). Falling back to text.",
                this);
            return null;
        }

        static bool IsHorizontalPlaneHit(ARRaycastHit hit)
        {
            if (hit.trackable is ARPlane plane)
            {
                return plane.alignment == PlaneAlignment.HorizontalUp ||
                       plane.alignment == PlaneAlignment.HorizontalDown;
            }

            // Estimated / feature hits: accept if normal is mostly up.
            var up = hit.pose.up;
            return Vector3.Dot(up, Vector3.up) > 0.7f;
        }

        bool EnsureReadyForPlace()
        {
            if (m_Ready)
                return m_RaycastManager != null && m_AnchorManager != null && m_ContentPrefab != null;

            ResolveManagers();
            if (m_ContentPrefab == null)
                TryAssignEditorOrResourcesFallback();
            m_Ready = m_RaycastManager != null && m_AnchorManager != null && m_ContentPrefab != null;
            return m_Ready;
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
            if (!EnsureReadyForPlace())
            {
                m_Status = "missing managers/prefab on place";
                Debug.LogError($"[TapPlace] {m_Status}", this);
                return;
            }

            if (!m_RaycastManager.Raycast(screenPosition, s_Hits, k_PlaneHitMask))
            {
                var planes = m_PlaneManager != null ? m_PlaneManager.trackables.count : -1;
                m_Status = $"no plane hit (planes={planes}). Keep scanning.";
                Debug.Log($"[TapPlace] No plane hit at {screenPosition}. planes={planes}", this);
                return;
            }

            PlaceAtHit(s_Hits[0], markAutoDone: false);
        }

        void PlaceAtHit(ARRaycastHit hit, bool markAutoDone)
        {
            m_Status = $"hit {hit.hitType} @ {hit.pose.position}";
            Debug.Log($"[TapPlace] Hit {hit.hitType} at {hit.pose.position}", this);
            // Never use AR plane hit.rotation for content yaw — sim/device plane axes are
            // arbitrary and often appear ~90° off. Position from hit; rotation from camera + heading.
            var oriented = new Pose(hit.pose.position, ResolveContentRotation(hit.pose.position));
            PlaceAtPose(oriented, hit.trackable as ARPlane, markAutoDone);
        }

        async void PlaceAtPose(Pose pose, ARPlane plane, bool markAutoDone)
        {
            if (!EnsureReadyForPlace())
            {
                m_Status = "missing managers/prefab on place";
                return;
            }

            // Re-apply stable orientation even if caller passed a plane-derived pose.
            pose = new Pose(pose.position, ResolveContentRotation(pose.position));

            if (markAutoDone)
                m_AutoPlaceInFlight = true;

            ARAnchor anchor = null;

            if (plane != null)
            {
                anchor = m_AnchorManager.AttachAnchor(plane, pose);
                if (anchor == null)
                    Debug.LogWarning("[TapPlace] AttachAnchor failed, trying TryAddAnchorAsync.");
            }

            if (anchor == null)
            {
                var result = await m_AnchorManager.TryAddAnchorAsync(pose);
                if (!result.status.IsSuccess())
                {
                    m_Status = $"anchor failed: {result.status}";
                    Debug.LogError($"[TapPlace] TryAddAnchorAsync failed: {result.status}");
                    m_AutoPlaceInFlight = false;
                    return;
                }

                anchor = result.value;
            }

            if (anchor == null)
            {
                m_Status = "anchor null";
                m_AutoPlaceInFlight = false;
                return;
            }

            if (m_ReplaceExistingPlacement)
                ClearAllPlacements();

            var instance = PlaceContentOnAnchor(anchor);
            if (markAutoDone)
            {
                m_AutoPlaceDone = true;
                m_AutoPlaceInFlight = false;
            }

            DismissSurfaceCoachingAfterPlace();
            AventoInteractionDirector.NotifyContentPlaced(instance);
            AventoUnityNative.NotifyReady(
                "{\"ok\":true,\"contentReady\":true,\"scenePlaced\":true}");

            m_Status = $"placed '{instance.name}' yaw={m_HeadingDegrees:0.#}° total={m_Placements.Count}";
            Debug.Log(
                $"[TapPlace] OK — placed '{instance.name}' at {instance.transform.position} " +
                $"rot={instance.transform.rotation.eulerAngles} heading={m_HeadingDegrees} " +
                $"(anchor {anchor.trackableId}, total={m_Placements.Count}, auto={markAutoDone})",
                this);
        }

        /// <summary>
        /// Hide Scan Surfaces / coaching cards and plane overlays once content is on the floor.
        /// Does not rely on PanoramaSkyboxViewer (bundle timing / ObjectSpawner path).
        /// </summary>
        void DismissSurfaceCoachingAfterPlace()
        {
            m_ScanCoachingDismissed = true;
            m_AutoPlaceCoachingArmed = false;
            HideScanCoachingVisuals();
        }

        void HideScanCoachingVisuals()
        {
            var goals = FindObjectsByType<GoalManager>(FindObjectsInactive.Include);
            for (var i = 0; i < goals.Length; i++)
            {
                if (goals[i] != null)
                {
                    goals[i].HoldScanSurfacesCoaching(false);
                    goals[i].DismissCoaching();
                }
            }

            var menus = FindObjectsByType<ARTemplateMenuManager>(FindObjectsInactive.Include);
            for (var i = 0; i < menus.Length; i++)
            {
                if (menus[i] != null)
                    menus[i].SetPlaneVisualizationVisible(false);
            }

            var faders = FindObjectsByType<ARPlaneMeshVisualizerFader>(FindObjectsInactive.Include);
            for (var i = 0; i < faders.Length; i++)
            {
                if (faders[i] != null)
                    faders[i].visualizeSurfaces = false;
            }
        }

        /// <summary>
        /// Stable horizontal yaw for sim + device: align content +Z with camera forward on the
        /// ground plane, then apply offer <see cref="m_HeadingDegrees"/>. Ignores AR plane axes
        /// (those caused the ~90° skew in XR Simulation).
        /// </summary>
        Quaternion ResolveContentRotation(Vector3 worldPosition)
        {
            var flatForward = Vector3.forward;
            var cam = Camera.main;
            if (cam != null)
            {
                flatForward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
                if (flatForward.sqrMagnitude < 1e-4f)
                {
                    // Looking straight down/up — face toward camera instead.
                    var toCam = cam.transform.position - worldPosition;
                    toCam.y = 0f;
                    flatForward = toCam.sqrMagnitude > 1e-4f ? toCam : Vector3.forward;
                }
            }

            flatForward.Normalize();
            var rot = Quaternion.LookRotation(flatForward, Vector3.up);
            if (Mathf.Abs(m_HeadingDegrees) > 0.01f)
                rot *= Quaternion.Euler(0f, m_HeadingDegrees, 0f);
            return rot;
        }

        GameObject PlaceContentOnAnchor(ARAnchor anchor)
        {
            var instance = Instantiate(m_ContentPrefab, anchor.transform);
            instance.name = $"{m_ContentPrefab.name}_{m_Placements.Count + 1}";
            instance.transform.localPosition = m_LocalPositionOffset;
            instance.transform.localScale = Vector3.one * m_ContentScale;
            // Force world yaw after parenting: AttachAnchor may still inherit plane axes
            // (common ~90° skew in XR Simulation). Same rule on device and in Editor.
            instance.transform.rotation = ResolveContentRotation(instance.transform.position);
            instance.SetActive(true);

            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                t.gameObject.SetActive(true);

            var animators = instance.GetComponentsInChildren<AnimatePlacedCube>(true);
            for (var i = 0; i < animators.Length; i++)
                animators[i].Play();

            var videos = instance.GetComponentsInChildren<PlayVideoOnPlace>(true);
            for (var i = 0; i < videos.Length; i++)
            {
                videos[i].enabled = true;
                videos[i].Refresh();
            }

            var images = instance.GetComponentsInChildren<PlayImageOnPlace>(true);
            for (var i = 0; i < images.Length; i++)
            {
                images[i].enabled = true;
                images[i].Refresh();
            }

            // Parent scale is applied before child Refresh; run one more fit pass next
            // frame so video sprites pick up prepared width/height reliably.
            if (videos.Length > 0)
                StartCoroutine(RefreshVideosNextFrame(videos));

            m_Placements.Add(new Placement
            {
                anchor = anchor,
                instance = instance
            });

            return instance;
        }

        static IEnumerator RefreshVideosNextFrame(PlayVideoOnPlace[] videos)
        {
            yield return null;
            for (var i = 0; i < videos.Length; i++)
            {
                if (videos[i] != null)
                    videos[i].RefreshFitOnly();
            }
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
