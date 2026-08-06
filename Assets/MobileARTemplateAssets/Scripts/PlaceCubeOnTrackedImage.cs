using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Places content on a tracked reference image and shows debug UI/logs while searching.
    /// </summary>
    public class PlaceCubeOnTrackedImage : MonoBehaviour
    {
        [SerializeField]
        ARTrackedImageManager m_TrackedImageManager;

        [SerializeField]
        Transform m_Content;

        [SerializeField]
        string m_TargetImageName = "target";

        [SerializeField]
        Vector3 m_LocalPositionOffset = new Vector3(0f, 0.05f, 0f);

        [SerializeField]
        bool m_HideWhenNotTracking = true;

        [SerializeField]
        bool m_ShowDebugOverlay = true;

        [SerializeField]
        Texture2D m_DebugPreviewTexture;

        Text m_StatusText;
        RawImage m_PreviewImage;
        Transform m_DebugFrame;
        MeshRenderer m_DebugFrameRenderer;
        string m_LastStatus;
        float m_NextHeartbeatLog;

        public ARTrackedImageManager trackedImageManager
        {
            get => m_TrackedImageManager;
            set => m_TrackedImageManager = value;
        }

        public Transform content
        {
            get => m_Content;
            set => m_Content = value;
        }

        public string targetImageName
        {
            get => m_TargetImageName;
            set => m_TargetImageName = value;
        }

        void Awake()
        {
            if (m_TrackedImageManager == null)
                m_TrackedImageManager = GetComponent<ARTrackedImageManager>();

            if (m_Content != null)
                m_Content.gameObject.SetActive(false);

            if (m_ShowDebugOverlay)
                CreateDebugOverlay();
        }

        void OnEnable()
        {
            if (m_TrackedImageManager != null)
                m_TrackedImageManager.trackablesChanged.AddListener(OnTrackablesChanged);

            LogLibraryState("OnEnable");
            SetStatus($"Searching for image '{m_TargetImageName}'...");
        }

        void OnDisable()
        {
            if (m_TrackedImageManager != null)
                m_TrackedImageManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
        }

        void Update()
        {
            // Poll every frame in case the trackablesChanged UnityEvent was not serialized.
            if (m_TrackedImageManager == null)
                return;

            ARTrackedImage best = null;
            foreach (var image in m_TrackedImageManager.trackables)
            {
                if (!IsTargetImage(image))
                    continue;

                UpdateContentForImage(image);
                best = image;
            }

            if (best == null)
            {
                if (m_HideWhenNotTracking && m_Content != null && m_Content.gameObject.activeSelf)
                    m_Content.gameObject.SetActive(false);

                if (m_DebugFrame != null)
                    m_DebugFrame.gameObject.SetActive(false);

                if (Time.unscaledTime >= m_NextHeartbeatLog)
                {
                    m_NextHeartbeatLog = Time.unscaledTime + 2f;
                    var libraryCount = m_TrackedImageManager.referenceLibrary?.count ?? 0;
                    Debug.Log(
                        $"[AR Image] Still searching for '{m_TargetImageName}'. " +
                        $"Manager enabled={m_TrackedImageManager.enabled}, libraryCount={libraryCount}, trackables={m_TrackedImageManager.trackables.count}");
                    SetStatus($"Searching '{m_TargetImageName}'...\nLibrary images: {libraryCount}\nTrackables: {m_TrackedImageManager.trackables.count}");
                }
            }
        }

        void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> eventArgs)
        {
            foreach (var image in eventArgs.added)
            {
                Debug.Log(
                    $"[AR Image] ADDED '{image.referenceImage.name}' state={image.trackingState} " +
                    $"size={image.referenceImage.size} pos={image.transform.position}");
                UpdateContentForImage(image);
            }

            foreach (var image in eventArgs.updated)
                UpdateContentForImage(image);

            foreach (var removed in eventArgs.removed)
            {
                var image = removed.Value;
                var name = image != null ? image.referenceImage.name : removed.Key.ToString();
                Debug.Log($"[AR Image] REMOVED '{name}'");
                if (image != null && IsTargetImage(image) && m_HideWhenNotTracking && m_Content != null)
                    m_Content.gameObject.SetActive(false);
            }
        }

        void UpdateContentForImage(ARTrackedImage image)
        {
            if (!IsTargetImage(image))
            {
                Debug.Log($"[AR Image] Ignoring '{image.referenceImage.name}' (want '{m_TargetImageName}')");
                return;
            }

            var tracking = image.trackingState;
            SetStatus(
                $"FOUND '{image.referenceImage.name}'\n" +
                $"State: {tracking}\n" +
                $"Size: {image.referenceImage.size.x:F3} x {image.referenceImage.size.y:F3} m");

            UpdateDebugFrame(image);

            var canPlace = tracking == TrackingState.Tracking || tracking == TrackingState.Limited;
            if (!canPlace)
            {
                if (m_HideWhenNotTracking && m_Content != null)
                    m_Content.gameObject.SetActive(false);
                return;
            }

            if (m_Content == null)
                return;

            m_Content.SetParent(image.transform, false);
            m_Content.localPosition = m_LocalPositionOffset;
            m_Content.localRotation = Quaternion.identity;
            m_Content.localScale = Vector3.one * 0.1f;
            m_Content.gameObject.SetActive(true);
        }

        bool IsTargetImage(ARTrackedImage image)
        {
            return image != null && image.referenceImage.name == m_TargetImageName;
        }

        void LogLibraryState(string context)
        {
            if (m_TrackedImageManager == null)
            {
                Debug.LogError($"[AR Image] {context}: ARTrackedImageManager is missing.");
                return;
            }

            var library = m_TrackedImageManager.referenceLibrary;
            if (library == null)
            {
                Debug.LogError(
                    $"[AR Image] {context}: referenceLibrary is NULL. " +
                    "Assign ReferenceImageLibrary on AR Tracked Image Manager and rebuild the app.");
                SetStatus("ERROR: no ReferenceImageLibrary");
                return;
            }

            Debug.Log($"[AR Image] {context}: library has {library.count} image(s). Looking for '{m_TargetImageName}'.");
            for (var i = 0; i < library.count; i++)
            {
                var entry = library[i];
                Debug.Log(
                    $"[AR Image]   [{i}] name='{entry.name}' size={entry.size} " +
                    $"specifySize={entry.specifySize} texture={(entry.texture != null ? entry.texture.name : "null")}");
            }
        }

        void CreateDebugOverlay()
        {
            var canvasGo = new GameObject("AR Image Debug Canvas");
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
            panelRt.sizeDelta = new Vector2(280f, 220f);
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

            var previewGo = new GameObject("Preview", typeof(RectTransform), typeof(RawImage));
            previewGo.transform.SetParent(panel.transform, false);
            var previewRt = previewGo.GetComponent<RectTransform>();
            previewRt.anchorMin = new Vector2(0f, 1f);
            previewRt.anchorMax = new Vector2(0f, 1f);
            previewRt.pivot = new Vector2(0f, 1f);
            previewRt.anchoredPosition = new Vector2(16f, -16f);
            previewRt.sizeDelta = new Vector2(120f, 120f);
            m_PreviewImage = previewGo.GetComponent<RawImage>();

            var texture = m_DebugPreviewTexture;
            if (texture == null && m_TrackedImageManager != null &&
                m_TrackedImageManager.referenceLibrary is XRReferenceImageLibrary serializedLibrary)
            {
                for (var i = 0; i < serializedLibrary.count; i++)
                {
                    if (serializedLibrary[i].name == m_TargetImageName && serializedLibrary[i].texture != null)
                    {
                        texture = serializedLibrary[i].texture;
                        break;
                    }
                }
            }

            if (texture != null)
                m_PreviewImage.texture = texture;

            var labelGo = new GameObject("Status", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(panel.transform, false);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0f, 0f);
            labelRt.anchorMax = new Vector2(1f, 1f);
            labelRt.offsetMin = new Vector2(150f, 12f);
            labelRt.offsetMax = new Vector2(-12f, -12f);
            m_StatusText = labelGo.GetComponent<Text>();
            m_StatusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (m_StatusText.font == null)
                m_StatusText.font = Font.CreateDynamicFontFromOSFont(new[] { "Helvetica", "Arial", "sans-serif" }, 16);
            m_StatusText.fontSize = 16;
            m_StatusText.color = Color.white;
            m_StatusText.alignment = TextAnchor.UpperLeft;
            m_StatusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            m_StatusText.verticalOverflow = VerticalWrapMode.Overflow;

            // World-space green square that appears on the marker when detected.
            var frameGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            frameGo.name = "Tracked Image Debug Frame";
            Object.Destroy(frameGo.GetComponent<Collider>());
            m_DebugFrame = frameGo.transform;
            m_DebugFrameRenderer = frameGo.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            mat.color = new Color(0.1f, 1f, 0.2f, 0.35f);
            m_DebugFrameRenderer.material = mat;
            frameGo.SetActive(false);
        }

        void UpdateDebugFrame(ARTrackedImage image)
        {
            if (m_DebugFrame == null)
                return;

            var size = image.size;
            if (size.x <= 0f || size.y <= 0f)
                size = image.referenceImage.size;

            m_DebugFrame.SetParent(image.transform, false);
            m_DebugFrame.localPosition = new Vector3(0f, 0.001f, 0f);
            m_DebugFrame.localRotation = Quaternion.Euler(90f, 0f, 0f);
            m_DebugFrame.localScale = new Vector3(size.x, size.y, 1f);
            m_DebugFrame.gameObject.SetActive(true);

            if (m_DebugFrameRenderer != null)
            {
                var tracking = image.trackingState;
                m_DebugFrameRenderer.material.color = tracking == TrackingState.Tracking
                    ? new Color(0.1f, 1f, 0.2f, 0.4f)
                    : new Color(1f, 0.85f, 0.1f, 0.4f);
            }
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
