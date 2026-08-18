using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Video;
using UnityEngine.XR.ARFoundation;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Immersive equirectangular PNG or MP4 on an inverted dome.
    /// Pick Content Mode: Still Image (PNG) or Video (MP4). Look around via
    /// touch drag and/or device orientation.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class PanoramaSkyboxViewer : MonoBehaviour
    {
        public enum ContentMode
        {
            StillImage = 0,
            Video = 1,
        }

        public enum StereoLayout
        {
            Mono = 0,
            SideBySide = 1,
            TopBottom = 2,
        }

        public enum LookControlMode
        {
            /// <summary>Finger drag only — disables AR pose tracking on the camera.</summary>
            TouchDrag = 0,
            /// <summary>Move the phone only — keeps TrackedPoseDriver active.</summary>
            DeviceOrientation = 1,
            /// <summary>Phone orientation plus finger drag offset.</summary>
            Both = 2,
        }

        const string k_DomeShaderName = "AR/EquirectangularDome";
        const string k_DomeTextureProperty = "_BaseMap";
        const float k_TapMoveThresholdPixels = 12f;

        /// <summary>
        /// RenderTexture is more reliable in Editor / macOS; MaterialOverride on iOS/Android
        /// (same split as <see cref="PlayVideoOnPlace"/>).
        /// </summary>
        static bool UseRenderTexturePath =>
            Application.isEditor || Application.platform == RuntimePlatform.OSXPlayer;

        [Tooltip("Still Image = PNG panorama. Video = MP4 360 clip. Only one is used.")]
        [SerializeField]
        ContentMode m_ContentMode = ContentMode.StillImage;

        [SerializeField]
        Texture2D m_PanoramaTexture;

        [SerializeField]
        VideoClip m_VideoClip;

        [SerializeField]
        bool m_Loop = true;

        [SerializeField]
        bool m_PlayAudio;

        [Tooltip("If off, a short tap/click starts playback (drag still looks around).")]
        [SerializeField]
        bool m_AutoPlay = true;

        [SerializeField]
        StereoLayout m_StereoLayout = StereoLayout.Mono;

        [SerializeField]
        Material m_SkyboxMaterial;

        [Tooltip("URP dome material (AR/EquirectangularDome). Required so the shader ships in player/AssetBundle builds.")]
        [SerializeField]
        Material m_DomeMaterialTemplate;

        [Tooltip("When immersive panorama/video starts, hide Scan Surfaces coaching and plane overlays.")]
        [SerializeField]
        bool m_HideSurfaceCoachingWhenActive = true;

        [Header("Dome / look")]
        [Tooltip("TouchDrag = mouse/finger only. DeviceOrientation = move phone (default). Both = phone + drag.")]
        [SerializeField]
        LookControlMode m_LookControlMode = LookControlMode.DeviceOrientation;

        [Tooltip("Disable AR camera feed only when Opacity is fully opaque (1). Ignored while blending.")]
        [SerializeField]
        bool m_HideArCameraBackground = true;

        [Tooltip("Hide XR Simulation environment meshes so they do not poke through the dome.")]
        [SerializeField]
        bool m_HideSimulationEnvironment = true;

        [Tooltip("Dome radius in meters. Keep larger than scene content so 360 renders as background.")]
        [SerializeField]
        float m_DomeRadius = 50f;

        [Tooltip("Drag look sensitivity in degrees per pixel.")]
        [SerializeField]
        float m_LookSensitivity = 0.15f;

        [SerializeField]
        float m_MinPitch = -89f;

        [SerializeField]
        float m_MaxPitch = 89f;

        [Tooltip("Optional. Defaults to Camera Offset (XR rig) or Main Camera.")]
        [SerializeField]
        Transform m_LookTarget;

        [SerializeField]
        float m_Exposure = 1f;

        [Tooltip("Dome opacity over the live camera (1 = fully opaque, 0 = invisible). Default 0.95.")]
        [Range(0f, 1f)]
        [SerializeField]
        float m_Opacity = 0.95f;

        [SerializeField]
        float m_YawOffset;

        Material m_RuntimeSkybox;
        Material m_DomeMaterial;
        Material m_PreviousSkybox;
        GameObject m_Dome;
        Camera m_Camera;
        ARCameraBackground m_ArBackground;
        bool m_HadArBackground;
        bool m_ArBackgroundWasEnabled;
        CameraClearFlags m_PreviousClearFlags;
        float m_PreviousFarClipPlane;
        bool m_HasPreviousFarClip;
        readonly List<(Behaviour driver, bool wasEnabled)> m_PoseDrivers = new();
        readonly List<(Renderer renderer, bool wasEnabled)> m_HiddenSimRenderers = new();

        VideoPlayer m_VideoPlayer;
        AudioSource m_AudioSource;
        RenderTexture m_VideoRT;
        bool m_VideoConfigured;
        bool m_ShowingVideo;

        bool m_PointerDown;
        bool m_PointerMoved;
        Vector2 m_PointerDownPos;
        Vector2 m_LastPointer;
        float m_Yaw;
        float m_Pitch;

        // UaaL / iOS often only delivers touches through IMGUI (see TapToPlaceOnAnchor).
        bool m_ImguiPointerDown;
        bool m_ImguiPointerMoved;

        bool UsesTouchLook =>
            m_LookControlMode == LookControlMode.TouchDrag || m_LookControlMode == LookControlMode.Both;

        bool UsesDeviceLook =>
            m_LookControlMode == LookControlMode.DeviceOrientation || m_LookControlMode == LookControlMode.Both;

        bool ShouldDisablePoseDrivers => m_LookControlMode == LookControlMode.TouchDrag;

        bool ShouldHideArCameraBackground =>
            m_HideArCameraBackground && m_Opacity >= 0.999f;

        bool IsVideoMode => m_ContentMode == ContentMode.Video;

        public ContentMode contentMode
        {
            get => m_ContentMode;
            set
            {
                if (m_ContentMode == value)
                    return;
                m_ContentMode = value;
                if (isActiveAndEnabled)
                    RestartContent();
            }
        }

        public Texture2D panoramaTexture
        {
            get => m_PanoramaTexture;
            set
            {
                m_PanoramaTexture = value;
                if (isActiveAndEnabled && !IsVideoMode)
                    ApplyPanorama();
            }
        }

        public VideoClip videoClip
        {
            get => m_VideoClip;
            set
            {
                m_VideoClip = value;
                m_VideoConfigured = false;
                if (isActiveAndEnabled && IsVideoMode)
                    SetupVideo();
            }
        }

        public float opacity
        {
            get => m_Opacity;
            set
            {
                m_Opacity = Mathf.Clamp01(value);
                if (isActiveAndEnabled)
                    ApplyLiveDomeParams();
            }
        }

        void OnEnable()
        {
            ResolveLookTarget();
            CaptureLookAngles();
            ConfigureCamera();
            HideSimulationEnvironment();
            RestartContent();
            if (m_HideSurfaceCoachingWhenActive)
                StartCoroutine(DismissSurfaceFindingUiRoutine());
        }

        void OnDisable()
        {
            TeardownVideo();
            RestoreCamera();
            RestoreSimulationEnvironment();
            RestoreSkybox();
            DestroyDome();
        }

        System.Collections.IEnumerator DismissSurfaceFindingUiRoutine()
        {
            DismissSurfaceFindingUi();
            // Coaching / plane visuals may enable the same frame as placement.
            yield return null;
            DismissSurfaceFindingUi();
        }

        void DismissSurfaceFindingUi()
        {
            // Do not hide Scan Surfaces until PlacedContent is actually on a plane.
            var placer = FindAnyObjectByType<TapToPlaceOnAnchor>();
            if (placer == null || placer.PlacementCount <= 0)
                return;

            var goals = FindObjectsByType<GoalManager>(FindObjectsInactive.Include);
            for (var i = 0; i < goals.Length; i++)
            {
                if (goals[i] != null)
                    goals[i].DismissCoaching();
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

        void RestartContent()
        {
            TeardownVideo();
            m_ShowingVideo = false;

            Debug.Log(
                $"[PanoramaSkybox] Applying Content Mode={m_ContentMode} opacity={m_Opacity:0.###}",
                this);

            if (IsVideoMode)
            {
                EnsureDome();
                ApplyDomeMaterialParams();
                SetupVideo();
            }
            else
            {
                ApplyPanorama();
            }
        }

        /// <summary>Called from the custom inspector after Content Mode / clip / texture changes.</summary>
        public void RestartContentFromInspector()
        {
            if (!isActiveAndEnabled)
                return;
            RestartContent();
        }

        /// <summary>Called from the custom inspector for opacity / exposure / look without restarting media.</summary>
        public void ApplyLiveDomeParamsFromInspector()
        {
            if (!isActiveAndEnabled)
                return;
            ApplyLiveDomeParams();
        }

        void ApplyLiveDomeParams()
        {
            ApplyDomeMaterialParams();
            KeepDomeOnCamera();
            UpdateArCameraBackgroundVisibility();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (Application.isPlaying)
                return;
            // Keep VideoPlayer from previewing when Content Mode is Still Image.
            var vp = GetComponent<VideoPlayer>();
            if (vp != null && m_ContentMode != ContentMode.Video && vp.isPlaying)
                vp.Stop();
        }
#endif

        void LateUpdate()
        {
            UpdateArCameraBackgroundVisibility();

            if (m_HideSimulationEnvironment && Time.frameCount % 30 == 0)
                HideSimulationEnvironment();

            if (m_Camera != null && m_Camera.clearFlags != CameraClearFlags.SolidColor &&
                m_Camera.clearFlags != CameraClearFlags.Skybox)
                m_Camera.clearFlags = CameraClearFlags.SolidColor;

            UpdatePoseDriversForLookMode();
            KeepDomeOnCamera();
            ApplyDomeMaterialParams();

            if (m_HideSurfaceCoachingWhenActive && Time.frameCount % 30 == 0)
                DismissSurfaceFindingUi();

            if (UsesTouchLook)
                HandlePointerInput();
        }

        void OnGUI()
        {
            if (!UsesTouchLook || !Application.isPlaying)
                return;

            // UaaL on iOS often never feeds Input System / legacy Input for drags.
            var ev = Event.current;
            if (ev == null)
                return;

            switch (ev.type)
            {
                case EventType.MouseDown when ev.button == 0:
                    m_ImguiPointerDown = true;
                    m_ImguiPointerMoved = false;
                    m_PointerDown = true;
                    m_PointerMoved = false;
                    m_PointerDownPos = ImguiToScreen(ev.mousePosition);
                    m_LastPointer = m_PointerDownPos;
                    break;

                case EventType.MouseDrag when ev.button == 0 && m_ImguiPointerDown:
                {
                    var pos = ImguiToScreen(ev.mousePosition);
                    ApplyLookDelta(pos - m_LastPointer);
                    m_LastPointer = pos;

                    if ((pos - m_PointerDownPos).sqrMagnitude >
                        k_TapMoveThresholdPixels * k_TapMoveThresholdPixels)
                    {
                        m_ImguiPointerMoved = true;
                        m_PointerMoved = true;
                    }

                    break;
                }

                case EventType.MouseUp when ev.button == 0 && m_ImguiPointerDown:
                    if (!m_ImguiPointerMoved && !m_PointerMoved)
                        TryTapToPlay();
                    m_ImguiPointerDown = false;
                    m_ImguiPointerMoved = false;
                    m_PointerDown = false;
                    m_PointerMoved = false;
                    break;
            }
        }

        static Vector2 ImguiToScreen(Vector2 imguiPosition) =>
            new Vector2(imguiPosition.x, Screen.height - imguiPosition.y);

        void ApplyPanorama()
        {
            if (m_PanoramaTexture == null)
            {
                Debug.LogWarning("[PanoramaSkybox] Still Image mode needs a Panorama Texture (PNG).", this);
                return;
            }

            ApplySkyboxFallback();
            EnsureDome();
            ApplyStillToDome();

            Debug.Log(
                $"[PanoramaSkybox] Dome still '{m_PanoramaTexture.name}' {m_PanoramaTexture.width}x{m_PanoramaTexture.height}",
                this);
        }

        void ApplySkyboxFallback()
        {
            if (m_PanoramaTexture == null)
                return;

            if (m_PreviousSkybox == null)
                m_PreviousSkybox = RenderSettings.skybox;

            var mat = m_SkyboxMaterial != null ? m_SkyboxMaterial : EnsureRuntimeSkybox();
            if (mat == null)
                return;

            var panoramic = Shader.Find("Skybox/Panoramic");
            if (panoramic != null && mat.shader != panoramic)
                mat.shader = panoramic;

            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", m_PanoramaTexture);
            if (mat.HasProperty("_Tex"))
                mat.SetTexture("_Tex", m_PanoramaTexture);
            if (mat.HasProperty("_Exposure"))
                mat.SetFloat("_Exposure", m_Exposure);
            if (mat.HasProperty("_Rotation"))
                mat.SetFloat("_Rotation", m_YawOffset);
            if (mat.HasProperty("_Mapping"))
                mat.SetFloat("_Mapping", 1f);

            RenderSettings.skybox = mat;
            DynamicGI.UpdateEnvironment();
        }

        Material EnsureRuntimeSkybox()
        {
            if (m_RuntimeSkybox != null)
                return m_RuntimeSkybox;

            var shader = Shader.Find("Skybox/Panoramic");
            if (shader == null)
                return null;

            m_RuntimeSkybox = new Material(shader) { name = "PanoramaSkybox (Runtime)" };
            return m_RuntimeSkybox;
        }

        void EnsureDome()
        {
            if (m_Dome != null)
                return;

            m_Dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m_Dome.name = "PanoramaDome";
            m_Dome.hideFlags = HideFlags.DontSave;

            var collider = m_Dome.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            if (m_DomeMaterialTemplate != null)
            {
                m_DomeMaterial = new Material(m_DomeMaterialTemplate) { name = "PanoramaDome (Runtime)" };
            }
            else
            {
                var shader = Shader.Find(k_DomeShaderName);
                if (shader == null)
                {
                    Debug.LogError(
                        $"[PanoramaSkybox] Shader '{k_DomeShaderName}' not found. Assign Dome Material Template (and rebuild player).",
                        this);
                    return;
                }

                m_DomeMaterial = new Material(shader) { name = "PanoramaDome (Runtime)" };
            }

            // Draw as sky background so sprites/boxes appear in front.
            m_DomeMaterial.renderQueue = 1010;

            var renderer = m_Dome.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = m_DomeMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            KeepDomeOnCamera();
            ApplyDomeMaterialParams();
        }

        void ApplyStillToDome()
        {
            if (m_DomeMaterial == null)
                return;

            if (m_PanoramaTexture != null && m_DomeMaterial.HasProperty(k_DomeTextureProperty))
                m_DomeMaterial.SetTexture(k_DomeTextureProperty, m_PanoramaTexture);

            ApplyDomeMaterialParams();
            m_ShowingVideo = false;
        }

        void ApplyVideoToDome()
        {
            if (m_DomeMaterial == null || !IsVideoMode)
                return;

            if (UseRenderTexturePath && m_VideoRT != null &&
                m_DomeMaterial.HasProperty(k_DomeTextureProperty))
            {
                m_DomeMaterial.SetTexture(k_DomeTextureProperty, m_VideoRT);
            }
            else if (!UseRenderTexturePath && m_VideoPlayer != null &&
                     m_VideoPlayer.texture != null &&
                     m_DomeMaterial.HasProperty(k_DomeTextureProperty))
            {
                // Fallback if MaterialOverride has not written yet.
                m_DomeMaterial.SetTexture(k_DomeTextureProperty, m_VideoPlayer.texture);
            }

            ApplyDomeMaterialParams();
            m_ShowingVideo = true;
        }

        void ApplyDomeMaterialParams()
        {
            if (m_DomeMaterial == null)
                return;

            if (m_DomeMaterial.HasProperty("_BaseColor"))
            {
                var c = Color.white * m_Exposure;
                c.a = 1f;
                m_DomeMaterial.SetColor("_BaseColor", c);
            }

            m_DomeMaterial.SetFloat("_Opacity", Mathf.Clamp01(m_Opacity));
            if (m_DomeMaterial.HasProperty("_RotationY"))
                m_DomeMaterial.SetFloat("_RotationY", m_YawOffset);
            if (m_DomeMaterial.HasProperty("_StereoMode"))
                m_DomeMaterial.SetFloat("_StereoMode", (float)m_StereoLayout);
        }

        void KeepDomeOnCamera()
        {
            if (m_Dome == null)
                return;

            var cam = m_Camera != null ? m_Camera : ResolveCamera();
            if (cam == null)
                return;

            m_Camera = cam;
            EnsureCameraFarClipForDome(cam);

            // Large world-fixed dome so placed content sits inside it and draws in front.
            var t = m_Dome.transform;
            if (t.parent != null)
                t.SetParent(null, true);
            t.position = cam.transform.position;
            t.rotation = Quaternion.identity;
            t.localScale = Vector3.one * (m_DomeRadius * 2f);
        }

        void EnsureCameraFarClipForDome(Camera cam)
        {
            var needed = m_DomeRadius + 5f;
            if (!m_HasPreviousFarClip)
            {
                m_PreviousFarClipPlane = cam.farClipPlane;
                m_HasPreviousFarClip = true;
            }

            if (cam.farClipPlane < needed)
                cam.farClipPlane = needed;
        }

        void DestroyDome()
        {
            if (m_DomeMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(m_DomeMaterial);
                else
                    DestroyImmediate(m_DomeMaterial);
                m_DomeMaterial = null;
            }

            if (m_Dome != null)
            {
                if (Application.isPlaying)
                    Destroy(m_Dome);
                else
                    DestroyImmediate(m_Dome);
                m_Dome = null;
            }

            m_ShowingVideo = false;
        }

        #region Video

        void SetupVideo()
        {
            if (!IsVideoMode)
                return;

            if (m_VideoClip == null)
            {
                Debug.LogWarning("[PanoramaSkybox] Video mode needs a Video Clip (MP4).", this);
                return;
            }

            EnsureVideoPlayer();
            ConfigureVideoPlayer();

            m_VideoPlayer.errorReceived += OnVideoError;
            m_VideoPlayer.prepareCompleted += OnVideoPrepareCompleted;
            m_VideoPlayer.started += OnVideoStarted;

            if (m_AutoPlay)
            {
                if (m_VideoPlayer.isPrepared)
                    StartVideoPlayback();
                else
                    m_VideoPlayer.Prepare();
            }
            else
            {
                if (!m_VideoPlayer.isPrepared)
                    m_VideoPlayer.Prepare();
            }
        }

        void TeardownVideo()
        {
            if (m_VideoPlayer != null)
            {
                m_VideoPlayer.errorReceived -= OnVideoError;
                m_VideoPlayer.prepareCompleted -= OnVideoPrepareCompleted;
                m_VideoPlayer.started -= OnVideoStarted;

                if (m_VideoPlayer.isPlaying)
                    m_VideoPlayer.Stop();
                m_VideoPlayer.targetTexture = null;
                m_VideoPlayer.targetMaterialRenderer = null;
                m_VideoPlayer.clip = null;
            }

            m_ShowingVideo = false;
            ReleaseVideoRT();
            m_VideoConfigured = false;
        }

        void EnsureVideoPlayer()
        {
            if (m_VideoPlayer == null)
                m_VideoPlayer = GetComponent<VideoPlayer>();
            if (m_VideoPlayer == null)
                m_VideoPlayer = gameObject.AddComponent<VideoPlayer>();

            if (m_PlayAudio)
            {
                if (m_AudioSource == null)
                    m_AudioSource = GetComponent<AudioSource>();
                if (m_AudioSource == null)
                    m_AudioSource = gameObject.AddComponent<AudioSource>();
                m_AudioSource.playOnAwake = false;
                m_AudioSource.spatialBlend = 0f;
            }
        }

        void ConfigureVideoPlayer()
        {
            if (m_VideoConfigured || m_VideoPlayer == null || m_VideoClip == null)
                return;

            EnsureDome();
            if (m_Dome == null || m_DomeMaterial == null)
            {
                Debug.LogError("[PanoramaSkybox] Dome not ready — cannot bind VideoPlayer.", this);
                return;
            }

            m_VideoPlayer.playOnAwake = false;
            m_VideoPlayer.waitForFirstFrame = true;
            m_VideoPlayer.isLooping = m_Loop;
            m_VideoPlayer.skipOnDrop = true;
            m_VideoPlayer.source = VideoSource.VideoClip;
            m_VideoPlayer.clip = m_VideoClip;
            m_VideoPlayer.aspectRatio = VideoAspectRatio.Stretch;

            if (m_PlayAudio && m_AudioSource != null)
            {
                m_VideoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
                m_VideoPlayer.SetTargetAudioSource(0, m_AudioSource);
                m_VideoPlayer.EnableAudioTrack(0, true);
                m_VideoPlayer.controlledAudioTrackCount = 1;
            }
            else
            {
                m_VideoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            }

            var renderer = m_Dome.GetComponent<MeshRenderer>();
            if (UseRenderTexturePath)
            {
                EnsureVideoRT();
                m_VideoPlayer.renderMode = VideoRenderMode.RenderTexture;
                m_VideoPlayer.targetTexture = m_VideoRT;
                if (m_DomeMaterial.HasProperty(k_DomeTextureProperty))
                    m_DomeMaterial.SetTexture(k_DomeTextureProperty, m_VideoRT);
            }
            else
            {
                // iOS / Android: MaterialOverride is the reliable path (see PlayVideoOnPlace).
                m_VideoPlayer.renderMode = VideoRenderMode.MaterialOverride;
                m_VideoPlayer.targetMaterialRenderer = renderer;
                m_VideoPlayer.targetMaterialProperty = k_DomeTextureProperty;
                m_VideoPlayer.targetTexture = null;
            }

            m_VideoConfigured = true;
            Debug.Log(
                $"[PanoramaSkybox] Video configured clip='{m_VideoClip.name}' " +
                $"mode={(UseRenderTexturePath ? "RenderTexture" : "MaterialOverride")} " +
                $"{m_VideoClip.width}x{m_VideoClip.height}",
                this);
        }

        void EnsureVideoRT()
        {
            var width = m_VideoClip != null ? Mathf.Max(2, (int)m_VideoClip.width) : 2;
            var height = m_VideoClip != null ? Mathf.Max(2, (int)m_VideoClip.height) : 2;
            if (width <= 2 || height <= 2)
            {
                width = 720;
                height = 352;
            }

            if (m_VideoRT != null && m_VideoRT.width == width && m_VideoRT.height == height)
                return;

            ReleaseVideoRT();

            m_VideoRT = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "PanoramaVideoRT",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
            };
            m_VideoRT.Create();
            ClearRenderTexture(m_VideoRT);
        }

        void ReleaseVideoRT()
        {
            if (m_VideoRT == null)
                return;

            if (m_VideoPlayer != null && m_VideoPlayer.targetTexture == m_VideoRT)
                m_VideoPlayer.targetTexture = null;

            m_VideoRT.Release();
            if (Application.isPlaying)
                Destroy(m_VideoRT);
            else
                DestroyImmediate(m_VideoRT);
            m_VideoRT = null;
        }

        void StartVideoPlayback()
        {
            if (m_VideoPlayer == null || m_VideoClip == null)
                return;

            if (UseRenderTexturePath && m_VideoPlayer.width > 0 && m_VideoPlayer.height > 0)
            {
                var w = (int)m_VideoPlayer.width;
                var h = (int)m_VideoPlayer.height;
                if (m_VideoRT == null || m_VideoRT.width != w || m_VideoRT.height != h)
                {
                    ReleaseVideoRT();
                    m_VideoRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
                    {
                        name = "PanoramaVideoRT",
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                    };
                    m_VideoRT.Create();
                    m_VideoPlayer.targetTexture = m_VideoRT;
                    if (m_DomeMaterial != null && m_DomeMaterial.HasProperty(k_DomeTextureProperty))
                        m_DomeMaterial.SetTexture(k_DomeTextureProperty, m_VideoRT);
                }
            }

            m_VideoPlayer.isLooping = m_Loop;
            m_VideoPlayer.Play();
            Debug.Log($"[PanoramaSkybox] Playing 360 video '{m_VideoClip.name}'", this);
        }

        void OnVideoPrepareCompleted(VideoPlayer source)
        {
            if (!isActiveAndEnabled)
                return;

            if (m_AutoPlay || (m_VideoPlayer != null && m_VideoPlayer.isPlaying))
                StartVideoPlayback();
        }

        void OnVideoStarted(VideoPlayer source)
        {
            ApplyVideoToDome();
        }

        void OnVideoError(VideoPlayer source, string message)
        {
            Debug.LogError($"[PanoramaSkybox] VideoPlayer error: {message}", this);
        }

        static void ClearRenderTexture(RenderTexture renderTexture)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = renderTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = previous;
        }

        #endregion

        #region Camera / sim

        void ConfigureCamera()
        {
            m_Camera = ResolveCamera();
            if (m_Camera == null)
                return;

            m_PreviousClearFlags = m_Camera.clearFlags;
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = Color.black;

            m_ArBackground = m_Camera.GetComponent<ARCameraBackground>();
            m_HadArBackground = m_ArBackground != null;
            if (m_HadArBackground)
                m_ArBackgroundWasEnabled = m_ArBackground.enabled;

            UpdateArCameraBackgroundVisibility();
            CachePoseDrivers();
            UpdatePoseDriversForLookMode();
        }

        void UpdateArCameraBackgroundVisibility()
        {
            if (m_Camera == null)
                m_Camera = ResolveCamera();
            if (m_Camera == null)
                return;

            if (m_ArBackground == null)
                m_ArBackground = m_Camera.GetComponent<ARCameraBackground>();
            if (m_ArBackground == null)
                return;

            // Blend needs the live camera feed behind the translucent dome.
            m_ArBackground.enabled = !ShouldHideArCameraBackground;
        }

        void CachePoseDrivers()
        {
            if (m_PoseDrivers.Count > 0 || m_Camera == null)
                return;

            foreach (var behaviour in m_Camera.GetComponentsInParent<Behaviour>(true))
            {
                if (behaviour == null)
                    continue;

                if (!IsPoseDriver(behaviour))
                    continue;

                m_PoseDrivers.Add((behaviour, behaviour.enabled));
            }

            foreach (var behaviour in m_Camera.GetComponents<Behaviour>())
            {
                if (behaviour == null || !IsPoseDriver(behaviour))
                    continue;

                var alreadyTracked = false;
                for (var i = 0; i < m_PoseDrivers.Count; i++)
                {
                    if (m_PoseDrivers[i].driver == behaviour)
                    {
                        alreadyTracked = true;
                        break;
                    }
                }

                if (!alreadyTracked)
                    m_PoseDrivers.Add((behaviour, behaviour.enabled));
            }
        }

        void UpdatePoseDriversForLookMode()
        {
            if (m_Camera == null)
                return;

            if (m_PoseDrivers.Count == 0)
                CachePoseDrivers();

            var disable = ShouldDisablePoseDrivers;
            for (var i = 0; i < m_PoseDrivers.Count; i++)
            {
                var (driver, wasEnabled) = m_PoseDrivers[i];
                if (driver == null)
                    continue;

                if (disable)
                    driver.enabled = false;
                else
                    driver.enabled = wasEnabled;
            }
        }

        static bool IsPoseDriver(Behaviour behaviour)
        {
            var typeName = behaviour.GetType().Name;
            return typeName == "TrackedPoseDriver" || typeName == "ARPoseDriver";
        }

        void HideSimulationEnvironment()
        {
            if (!m_HideSimulationEnvironment)
                return;

            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (renderer == null || !renderer.enabled)
                    continue;

                if (!IsSimulationEnvironmentObject(renderer.gameObject))
                    continue;

                var alreadyTracked = false;
                for (var i = 0; i < m_HiddenSimRenderers.Count; i++)
                {
                    if (m_HiddenSimRenderers[i].renderer == renderer)
                    {
                        alreadyTracked = true;
                        break;
                    }
                }

                if (alreadyTracked)
                    continue;

                m_HiddenSimRenderers.Add((renderer, true));
                renderer.enabled = false;
            }
        }

        static bool IsSimulationEnvironmentObject(GameObject go)
        {
            var t = go.transform;
            while (t != null)
            {
                var n = t.name;
                if (n.Contains("SimulationEnvironment") ||
                    n.Contains("Avento1Simulation") ||
                    n.Contains("DefaultSimulationEnvironment"))
                    return true;
                t = t.parent;
            }

            return false;
        }

        void RestoreSimulationEnvironment()
        {
            foreach (var (renderer, wasEnabled) in m_HiddenSimRenderers)
            {
                if (renderer != null)
                    renderer.enabled = wasEnabled;
            }

            m_HiddenSimRenderers.Clear();
        }

        void RestoreCamera()
        {
            if (m_Camera != null)
            {
                m_Camera.clearFlags = m_PreviousClearFlags;
                if (m_HasPreviousFarClip)
                    m_Camera.farClipPlane = m_PreviousFarClipPlane;
            }

            m_HasPreviousFarClip = false;

            if (m_HadArBackground && m_ArBackground != null)
                m_ArBackground.enabled = m_ArBackgroundWasEnabled;

            foreach (var (driver, wasEnabled) in m_PoseDrivers)
            {
                if (driver != null)
                    driver.enabled = wasEnabled;
            }

            m_PoseDrivers.Clear();
        }

        void RestoreSkybox()
        {
            if (m_PreviousSkybox != null)
                RenderSettings.skybox = m_PreviousSkybox;

            if (m_RuntimeSkybox != null)
            {
                if (Application.isPlaying)
                    Destroy(m_RuntimeSkybox);
                else
                    DestroyImmediate(m_RuntimeSkybox);
                m_RuntimeSkybox = null;
            }
        }

        #endregion

        #region Input

        void HandlePointerInput()
        {
            if (m_LookTarget == null)
                ResolveLookTarget();

            if (TryGetPointer(out var pos, out var pressed, out var held, out var released))
            {
                if (pressed)
                {
                    m_PointerDown = true;
                    m_PointerMoved = false;
                    m_PointerDownPos = pos;
                    m_LastPointer = pos;
                }

                if (held && m_PointerDown)
                {
                    var delta = pos - m_LastPointer;
                    m_LastPointer = pos;

                    if ((pos - m_PointerDownPos).sqrMagnitude >
                        k_TapMoveThresholdPixels * k_TapMoveThresholdPixels)
                        m_PointerMoved = true;

                    if (m_PointerMoved)
                        ApplyLookDelta(delta);
                }

                if (released && m_PointerDown)
                {
                    if (!m_PointerMoved)
                        TryTapToPlay();
                    m_PointerDown = false;
                    m_PointerMoved = false;
                }
            }
            else if (m_PointerDown && !m_ImguiPointerDown)
            {
                if (!m_PointerMoved)
                    TryTapToPlay();
                m_PointerDown = false;
                m_PointerMoved = false;
            }
        }

        void ApplyLookDelta(Vector2 delta)
        {
            if (m_LookTarget == null)
                return;

            m_Yaw += delta.x * m_LookSensitivity;
            m_Pitch -= delta.y * m_LookSensitivity;
            m_Pitch = Mathf.Clamp(m_Pitch, m_MinPitch, m_MaxPitch);
            m_LookTarget.rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
        }

        void TryTapToPlay()
        {
            if (!IsVideoMode || m_AutoPlay || m_VideoClip == null || m_VideoPlayer == null)
                return;

            if (m_VideoPlayer.isPlaying)
                return;

            if (m_VideoPlayer.isPrepared)
                StartVideoPlayback();
            else
                m_VideoPlayer.Prepare();
        }

        static bool TryGetPointer(
            out Vector2 position,
            out bool pressedThisFrame,
            out bool held,
            out bool releasedThisFrame)
        {
            position = default;
            pressedThisFrame = false;
            held = false;
            releasedThisFrame = false;

            var touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                for (var i = 0; i < touchscreen.touches.Count; i++)
                {
                    var touch = touchscreen.touches[i];
                    if (touch.press.isPressed || touch.press.wasReleasedThisFrame)
                    {
                        position = touch.position.ReadValue();
                        pressedThisFrame = touch.press.wasPressedThisFrame;
                        held = touch.press.isPressed;
                        releasedThisFrame = touch.press.wasReleasedThisFrame;
                        return true;
                    }
                }

                var primary = touchscreen.primaryTouch;
                if (primary.press.isPressed || primary.press.wasReleasedThisFrame)
                {
                    position = primary.position.ReadValue();
                    pressedThisFrame = primary.press.wasPressedThisFrame;
                    held = primary.press.isPressed;
                    releasedThisFrame = primary.press.wasReleasedThisFrame;
                    return true;
                }
            }

            var pointer = Pointer.current;
            if (pointer != null &&
                (pointer.press.isPressed || pointer.press.wasReleasedThisFrame))
            {
                position = pointer.position.ReadValue();
                pressedThisFrame = pointer.press.wasPressedThisFrame;
                held = pointer.press.isPressed;
                releasedThisFrame = pointer.press.wasReleasedThisFrame;
                return true;
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.leftButton.isPressed || mouse.leftButton.wasReleasedThisFrame)
                {
                    position = mouse.position.ReadValue();
                    pressedThisFrame = mouse.leftButton.wasPressedThisFrame;
                    held = mouse.leftButton.isPressed;
                    releasedThisFrame = mouse.leftButton.wasReleasedThisFrame;
                    return true;
                }
            }

            try
            {
                if (UnityEngine.Input.touchCount > 0)
                {
                    var t = UnityEngine.Input.GetTouch(0);
                    position = t.position;
                    pressedThisFrame = t.phase == UnityEngine.TouchPhase.Began;
                    held = t.phase == UnityEngine.TouchPhase.Began
                        || t.phase == UnityEngine.TouchPhase.Moved
                        || t.phase == UnityEngine.TouchPhase.Stationary;
                    releasedThisFrame = t.phase == UnityEngine.TouchPhase.Ended
                        || t.phase == UnityEngine.TouchPhase.Canceled;
                    return true;
                }

                if (UnityEngine.Input.GetMouseButton(0) || UnityEngine.Input.GetMouseButtonUp(0))
                {
                    position = UnityEngine.Input.mousePosition;
                    pressedThisFrame = UnityEngine.Input.GetMouseButtonDown(0);
                    held = UnityEngine.Input.GetMouseButton(0);
                    releasedThisFrame = UnityEngine.Input.GetMouseButtonUp(0);
                    return true;
                }
            }
            catch (System.InvalidOperationException)
            {
                // Legacy input disabled in Player Settings.
            }

            return false;
        }

        void ResolveLookTarget()
        {
            if (m_LookTarget != null)
                return;

            var cam = ResolveCamera();
            if (cam == null)
                return;

            // Prefer Camera Offset so device tracking on Main Camera stacks with touch yaw.
            var parent = cam.transform.parent;
            if (parent != null &&
                (parent.name.Contains("Camera Offset") || parent.childCount > 0))
            {
                m_LookTarget = parent;
                return;
            }

            m_LookTarget = cam.transform;
        }

        void CaptureLookAngles()
        {
            if (m_LookTarget == null)
                return;

            var euler = m_LookTarget.rotation.eulerAngles;
            m_Yaw = euler.y;
            m_Pitch = euler.x > 180f ? euler.x - 360f : euler.x;
        }

        static Camera ResolveCamera()
        {
            if (Camera.main != null)
                return Camera.main;

            return FindAnyObjectByType<Camera>();
        }

        #endregion
    }
}
