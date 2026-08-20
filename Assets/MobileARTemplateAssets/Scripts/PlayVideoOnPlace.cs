using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Video;
using Unity.XR.CoreUtils;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Plays a looping alpha video on a transparent billboard.
    /// Always uses an ARGB32 RenderTexture so HEVC-with-alpha reaches the shader
    /// (MaterialOverride on iOS often writes opaque RGB and drops alpha → black box).
    /// Resizes the quad X/Y to the video pixel aspect using the authored sprite
    /// scale as the bounding box (same behaviour as <see cref="PlayImageOnPlace"/>).
    /// </summary>
    [RequireComponent(typeof(VideoPlayer))]
    [RequireComponent(typeof(MeshRenderer))]
    public class PlayVideoOnPlace : MonoBehaviour
    {
        public enum AspectFitMode
        {
            /// <summary>Fit inside authored X/Y box (can shrink a tall character when video is 16:9).</summary>
            Contain = 0,
            /// <summary>Pin authored height (Y) — best for walking-man / full-body billboards.</summary>
            FitHeight = 1,
            /// <summary>Pin authored width (X).</summary>
            FitWidth = 2,
        }

        public enum AlphaLayout
        {
            /// <summary>Alpha in the video frame (_BaseMap.a). HEVC-with-alpha / ProRes / PNG.</summary>
            Embedded = 0,
            /// <summary>
            /// Side-by-side H.264/HEVC without an alpha track: left = RGB, right = grayscale matte.
            /// Reliable on iOS where Unity VideoPlayer often drops HEVC-with-alpha.
            /// </summary>
            SideBySide = 1,
        }

        [SerializeField]
        VideoClip m_VideoClip;

        [Header("Editor / Simulator fallbacks")]
        [Tooltip(
            "Optional. Used only in Editor / macOS Simulator (RenderTexture path). " +
            "Animation Compression / QT RLE alpha that plays in Sim but is not the device clip.")]
        [SerializeField]
        VideoClip m_EditorFallbackClip;

        [Tooltip(
            "Optional opaque H.264 used in Editor / Simulator when the alpha fallback is missing. " +
            "Device always uses Video Clip (HEVC-with-alpha or side-by-side; do not ship ProRes).")]
        [SerializeField]
        VideoClip m_EditorOpaqueFallbackClip;

        [SerializeField]
        string m_TexturePropertyName = "_BaseMap";

        [Tooltip(
            "Embedded = alpha in clip (HEVC-with-alpha). " +
            "SideBySide = left RGB / right matte H.264 (recommended for iOS device).")]
        [SerializeField]
        AlphaLayout m_AlphaLayout = AlphaLayout.Embedded;

        [SerializeField]
        bool m_FlipVertical;

        [Header("Audio")]
        [Tooltip("Play the video clip's audio track through an AudioSource on this object.")]
        [SerializeField]
        bool m_PlayAudio = true;

        [Tooltip("0 = 2D (always same loudness), 1 = full 3D spatial (quieter with distance).")]
        [SerializeField]
        [Range(0f, 1f)]
        float m_SpatialBlend = 1f;

        [Tooltip(
            "Resize this object's X/Y localScale to the video pixel aspect. " +
            "Authored Transform Y is treated as world height for Fit Height mode.")]
        [SerializeField]
        bool m_FitQuadToAspect = true;

        [Tooltip("Fit Height = keep Transform Y (person height in meters), set X from video aspect.")]
        [SerializeField]
        AspectFitMode m_AspectFitMode = AspectFitMode.FitHeight;

        [Tooltip("Overall sprite opacity (1 = fully opaque, 0 = invisible). Multiplies video alpha.")]
        [Range(0f, 1f)]
        [SerializeField]
        float m_Opacity = 1f;

        [Header("Face camera")]
        [Tooltip(
            "On = sprite always turns to face the XR / main camera (billboard). " +
            "Off = keeps placement / authored rotation.")]
        [FormerlySerializedAs("m_RotateTowardCamera")]
        [SerializeField]
        bool m_FaceCamera = true;

        [Header("Move toward camera")]
        [Tooltip("Walk the video billboard toward the XR / main camera on the ground plane.")]
        [SerializeField]
        bool m_MoveTowardCamera;

        [Tooltip("Meters per second toward the camera.")]
        [SerializeField]
        float m_ApproachSpeedMetersPerSecond = 0.35f;

        [Tooltip("Stop when this close to the camera (horizontal distance, meters).")]
        [SerializeField]
        float m_StopDistanceMeters = 1.2f;

        [Tooltip("Only move while the VideoPlayer is playing.")]
        [SerializeField]
        bool m_MoveOnlyWhilePlaying = true;

        [Tooltip("Optional walk bob (meters) — subtle up/down while approaching.")]
        [SerializeField]
        float m_WalkBobAmplitudeMeters;

        [SerializeField]
        float m_WalkBobFrequency = 2f;

        VideoPlayer m_VideoPlayer;
        AudioSource m_AudioSource;
        MeshRenderer m_Renderer;
        RenderTexture m_RenderTexture;
        bool m_Configured;
        bool m_PlayWhenReady;
        Vector3 m_BaseLocalScale;
        bool m_HasBaseScale;
        Coroutine m_FitRoutine;
        float m_BaseWorldY;
        bool m_HasBaseWorldY;
        bool m_ApproachActive;

        /// <summary>
        /// Always use an ARGB32 RenderTexture so HEVC-with-alpha is preserved into
        /// the material. (iOS MaterialOverride often drops alpha → opaque black box.)
        /// </summary>
        static bool UseRenderTexturePath => true;

        /// <summary>
        /// Editor / macOS Simulator only: optional QT-RLE / opaque H.264 fallbacks.
        /// Device always uses <see cref="m_VideoClip"/> (HEVC-with-alpha; no ProRes).
        /// </summary>
        static bool UseEditorFallbackClips =>
            Application.isEditor || Application.platform == RuntimePlatform.OSXPlayer;

        /// <summary>
        /// Device → authored alpha clip. Editor/Sim → editor fallbacks when assigned.
        /// </summary>
        VideoClip ResolveActiveClip()
        {
            if (UseEditorFallbackClips)
            {
                if (m_EditorFallbackClip != null)
                    return m_EditorFallbackClip;
                if (m_EditorOpaqueFallbackClip != null)
                    return m_EditorOpaqueFallbackClip;
            }

            return m_VideoClip;
        }

        void Awake()
        {
            m_VideoPlayer = GetComponent<VideoPlayer>();
            m_Renderer = GetComponent<MeshRenderer>();
            EnsureAudioSource();
            CaptureBaseScale();
            ApplyFaceCameraOption();
            ConfigurePlayer();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // Keep BillboardSprite in sync when toggling in the Inspector.
            if (!Application.isPlaying)
                ApplyFaceCameraOption();
        }
#endif

        void EnsureAudioSource()
        {
            if (!m_PlayAudio)
                return;

            if (m_AudioSource == null)
                m_AudioSource = GetComponent<AudioSource>();
            if (m_AudioSource == null)
                m_AudioSource = gameObject.AddComponent<AudioSource>();

            m_AudioSource.playOnAwake = false;
            m_AudioSource.spatialBlend = m_SpatialBlend;
            m_AudioSource.loop = false;
        }

        void OnEnable()
        {
            if (m_VideoPlayer == null)
                return;

            m_VideoPlayer.errorReceived += OnVideoError;
            m_VideoPlayer.prepareCompleted += OnPrepareCompleted;
            m_VideoPlayer.started += OnStarted;

            m_PlayWhenReady = true;
            EnsureFitRoutine();
            BeginApproachIfEnabled();
            if (m_VideoPlayer.isPrepared)
                StartPlayback();
            else
                m_VideoPlayer.Prepare();
        }

        void OnDisable()
        {
            m_PlayWhenReady = false;
            if (m_FitRoutine != null)
            {
                StopCoroutine(m_FitRoutine);
                m_FitRoutine = null;
            }

            if (m_VideoPlayer == null)
                return;

            m_VideoPlayer.errorReceived -= OnVideoError;
            m_VideoPlayer.prepareCompleted -= OnPrepareCompleted;
            m_VideoPlayer.started -= OnStarted;

            if (m_VideoPlayer.isPlaying)
                m_VideoPlayer.Pause();
        }

        void OnDestroy()
        {
            if (m_VideoPlayer != null)
                m_VideoPlayer.targetTexture = null;

            if (m_RenderTexture != null)
            {
                m_RenderTexture.Release();
                Destroy(m_RenderTexture);
                m_RenderTexture = null;
            }
        }

        /// <summary>Re-fit / reconfigure after Instantiate or clip change.</summary>
        /// <summary>
        /// After parenting/scale, re-fit only — avoid Stop/Prepare again (breaks iOS VideoClip).
        /// </summary>
        public void RefreshFitOnly()
        {
            if (!m_HasBaseScale || m_BaseLocalScale == Vector3.zero)
                CaptureBaseScale();

            if (m_VideoPlayer != null && m_VideoPlayer.isPrepared)
                ApplyFitFromPreparedSource(m_VideoPlayer);
            else
                EnsureFitRoutine();

            ApplyFaceCameraOption();
            BeginApproachIfEnabled();
        }

        public void Refresh()
        {
            // Keep the first authored base scale — do not capture a post-fit scale.
            if (!m_HasBaseScale || m_BaseLocalScale == Vector3.zero)
                CaptureBaseScale();

            if (m_VideoPlayer != null)
            {
                if (m_VideoPlayer.isPlaying || m_VideoPlayer.isPrepared)
                    m_VideoPlayer.Stop();
                m_VideoPlayer.clip = null;
            }

            m_Configured = false;
            ConfigurePlayer();
            EnsureFitRoutine();
            ApplyFaceCameraOption();
            BeginApproachIfEnabled();

            if (isActiveAndEnabled && m_VideoPlayer != null)
            {
                m_PlayWhenReady = true;
                if (m_VideoPlayer.isPrepared)
                {
                    ApplyFitFromPreparedSource(m_VideoPlayer);
                    StartPlayback();
                }
                else
                {
                    m_VideoPlayer.Prepare();
                }
            }
        }

        /// <summary>Enable/disable automatic camera-facing rotation (billboard).</summary>
        public bool faceCamera
        {
            get => m_FaceCamera;
            set => SetFaceCamera(value);
        }

        public float opacity
        {
            get => m_Opacity;
            set
            {
                m_Opacity = Mathf.Clamp01(value);
                if (m_Renderer != null)
                    ApplyOpacity(m_Renderer.material);
            }
        }

        /// <summary>Enable/disable automatic camera-facing rotation (billboard).</summary>
        public void SetFaceCamera(bool enabled)
        {
            m_FaceCamera = enabled;
            ApplyFaceCameraOption();
        }

        /// <summary>Obsolete alias — use <see cref="SetFaceCamera"/>.</summary>
        public void SetRotateTowardCamera(bool enabled) => SetFaceCamera(enabled);

        void ApplyFaceCameraOption()
        {
            var billboard = GetComponent<BillboardSprite>();
            if (billboard == null)
            {
                if (!m_FaceCamera || !Application.isPlaying)
                    return;
                billboard = gameObject.AddComponent<BillboardSprite>();
            }

            billboard.rotateTowardCamera = m_FaceCamera;
            billboard.enabled = m_FaceCamera;
        }

        /// <summary>Enable/disable approach movement at runtime (e.g. from host JSON later).</summary>
        public void SetMoveTowardCamera(bool enabled)
        {
            m_MoveTowardCamera = enabled;
            if (enabled)
                BeginApproachIfEnabled();
            else
                m_ApproachActive = false;
        }

        void BeginApproachIfEnabled()
        {
            if (!m_MoveTowardCamera)
            {
                m_ApproachActive = false;
                return;
            }

            m_BaseWorldY = transform.position.y;
            m_HasBaseWorldY = true;
            m_ApproachActive = true;
        }

        void Update()
        {
            if (!m_ApproachActive || !m_MoveTowardCamera)
                return;

            if (m_MoveOnlyWhilePlaying &&
                (m_VideoPlayer == null || !m_VideoPlayer.isPlaying))
                return;

            var cam = ResolveCamera();
            if (cam == null)
                return;

            var pos = transform.position;
            var camPos = cam.transform.position;
            var toCam = camPos - pos;
            toCam.y = 0f;
            var distance = toCam.magnitude;
            var stop = Mathf.Max(0.05f, m_StopDistanceMeters);

            if (distance <= stop)
            {
                ApplyWalkBob(pos);
                return;
            }

            var dir = toCam / distance;
            var step = m_ApproachSpeedMetersPerSecond * Time.deltaTime;
            if (step >= distance - stop)
                step = Mathf.Max(0f, distance - stop);

            pos += dir * step;
            if (m_HasBaseWorldY)
                pos.y = m_BaseWorldY;
            ApplyWalkBob(pos);
        }

        void ApplyWalkBob(Vector3 groundPos)
        {
            if (m_WalkBobAmplitudeMeters > 0.0001f)
            {
                var bob = Mathf.Sin(Time.time * m_WalkBobFrequency * Mathf.PI * 2f) *
                          m_WalkBobAmplitudeMeters;
                groundPos.y = (m_HasBaseWorldY ? m_BaseWorldY : groundPos.y) + bob;
            }
            else if (m_HasBaseWorldY)
            {
                groundPos.y = m_BaseWorldY;
            }

            transform.position = groundPos;
        }

        Camera ResolveCamera()
        {
            var origin = FindFirstObjectByType<XROrigin>();
            if (origin != null && origin.Camera != null)
                return origin.Camera;
            return Camera.main;
        }

        void CaptureBaseScale()
        {
            // Authored Transform scale is the target billboard box (width/height in local units).
            m_BaseLocalScale = transform.localScale;
            if (m_BaseLocalScale == Vector3.zero)
                m_BaseLocalScale = Vector3.one;
            m_HasBaseScale = true;
        }

        void ConfigurePlayer()
        {
            if (m_Configured)
                return;

            if (m_VideoPlayer == null)
                m_VideoPlayer = GetComponent<VideoPlayer>();
            if (m_Renderer == null)
                m_Renderer = GetComponent<MeshRenderer>();

            var activeClip = ResolveActiveClip();
            if (activeClip == null || m_VideoPlayer == null || m_Renderer == null)
            {
                Debug.LogWarning("[VideoSprite] Missing VideoClip, VideoPlayer, or MeshRenderer.", this);
                return;
            }

            if (!m_HasBaseScale)
                CaptureBaseScale();

            // Instance material once so MaterialOverride / RT write the same material the shader uses.
            var material = m_Renderer.material;
            ApplyFlip(material);
            ApplyAlphaLayout(material);

            m_VideoPlayer.playOnAwake = false;
            m_VideoPlayer.waitForFirstFrame = true;
            m_VideoPlayer.isLooping = true;
            m_VideoPlayer.skipOnDrop = true;
            m_VideoPlayer.source = VideoSource.VideoClip;
            m_VideoPlayer.clip = activeClip;
            // Quad is resized to the pixel aspect; texture should fill that quad.
            m_VideoPlayer.aspectRatio = VideoAspectRatio.Stretch;

            if (!UseEditorFallbackClips &&
                activeClip.originalPath != null &&
                activeClip.originalPath.IndexOf("man_with_transparent.mov", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Debug.LogError(
                    "[VideoSprite] Device clip is Apple ProRes (man_with_transparent.mov). " +
                    "iOS VideoPlayer cannot decode it (white quad). Assign walking_man_device.mov " +
                    "(HEVC with Alpha, transcoding off) and rebuild the iOS AssetBundle.",
                    this);
            }

            // Match the working iPhone path: never attach audio on the video sprite.
            // Enabling a track (even when importAudio is 0 but audioTrackCount reports 1)
            // can make Prepare fail on device with "cannot play clip".
            m_VideoPlayer.audioOutputMode = VideoAudioOutputMode.None;

            // Best-effort early fit from clip metadata (often 0 until Prepare on some platforms).
            TryFitFromClipMetadata(activeClip);

            // ARGB32 RT keeps the alpha plane from HEVC-with-alpha. Do not use
            // MaterialOverride on iOS — it commonly composites onto opaque black.
            EnsureRenderTexture(activeClip);
            ClearRenderTexture(m_RenderTexture);
            m_VideoPlayer.renderMode = VideoRenderMode.RenderTexture;
            m_VideoPlayer.targetTexture = m_RenderTexture;
            m_VideoPlayer.targetMaterialRenderer = null;
            ApplyTexture(material, m_RenderTexture);
            Debug.Log(
                $"[VideoSprite] RenderTexture path clip='{activeClip.name}' " +
                $"layout={m_AlphaLayout} " +
                $"rt={(m_RenderTexture != null ? $"{m_RenderTexture.width}x{m_RenderTexture.height}" : "null")}.",
                this);

            ApplyOpacity(material);
            m_Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Renderer.receiveShadows = false;
            m_Configured = true;

            Debug.Log(
                $"[VideoSprite] configured clip='{activeClip.name}' " +
                $"{activeClip.width}x{activeClip.height} " +
                $"path='{activeClip.originalPath}' audioTracks={activeClip.audioTrackCount}",
                this);
        }

        void EnsureFitRoutine()
        {
            if (!m_FitQuadToAspect || !isActiveAndEnabled)
                return;

            if (m_FitRoutine != null)
                StopCoroutine(m_FitRoutine);
            m_FitRoutine = StartCoroutine(FitWhenDimensionsReady());
        }

        IEnumerator FitWhenDimensionsReady()
        {
            // VideoClip / VideoPlayer often report 0×0 until prepared — retry briefly.
            var deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (TryFitFromAnySource())
                {
                    m_FitRoutine = null;
                    yield break;
                }

                yield return null;
            }

            m_FitRoutine = null;
            Debug.LogWarning(
                "[VideoSprite] Timed out waiting for video width/height — quad aspect not fitted.",
                this);
        }

        bool TryFitFromClipMetadata()
        {
            return TryFitFromClipMetadata(ResolveActiveClip());
        }

        bool TryFitFromClipMetadata(VideoClip clip)
        {
            if (clip == null)
                return false;
            var w = (int)clip.width;
            var h = (int)clip.height;
            if (w <= 0 || h <= 0)
                return false;
            FitQuadToVideoAspect(w, h);
            return true;
        }

        bool TryFitFromAnySource()
        {
            if (TryFitFromClipMetadata())
                return true;

            if (m_VideoPlayer != null)
            {
                var w = (int)m_VideoPlayer.width;
                var h = (int)m_VideoPlayer.height;
                if (w > 0 && h > 0)
                {
                    FitQuadToVideoAspect(w, h);
                    return true;
                }

                if (m_VideoPlayer.texture != null &&
                    m_VideoPlayer.texture.width > 0 &&
                    m_VideoPlayer.texture.height > 0)
                {
                    FitQuadToVideoAspect(m_VideoPlayer.texture.width, m_VideoPlayer.texture.height);
                    return true;
                }
            }

            return false;
        }

        void ApplyFitFromPreparedSource(VideoPlayer source)
        {
            var w = (int)source.width;
            var h = (int)source.height;
            if (w > 0 && h > 0)
                FitQuadToVideoAspect(w, h);
            else
                TryFitFromAnySource();
        }

        /// <summary>
        /// Apply video pixel aspect to localScale.
        /// Default <see cref="AspectFitMode.FitHeight"/> keeps authored Y as world height
        /// (e.g. 1.8m walking man) and sets X = Y * (width/height).
        /// </summary>
        void FitQuadToVideoAspect(int width, int height)
        {
            if (!m_FitQuadToAspect || width <= 0 || height <= 0)
                return;

            // Full frame size for the RT (SBS is wider); billboard aspect uses color half only.
            var rtWidth = width;
            var rtHeight = height;
            var aspectWidth = m_AlphaLayout == AlphaLayout.SideBySide
                ? Mathf.Max(1, width / 2)
                : width;

            if (!m_HasBaseScale || m_BaseLocalScale == Vector3.zero)
                CaptureBaseScale();

            var videoAspect = (float)aspectWidth / height;
            var baseX = Mathf.Abs(m_BaseLocalScale.x);
            var baseY = Mathf.Abs(m_BaseLocalScale.y);
            if (baseX < 1e-5f)
                baseX = 1f;
            if (baseY < 1e-5f)
                baseY = 1f;

            var scale = m_BaseLocalScale;

            switch (m_AspectFitMode)
            {
                case AspectFitMode.FitHeight:
                    // Pin height (character size). X follows video aspect.
                    scale.y = m_BaseLocalScale.y;
                    scale.x = Mathf.Sign(m_BaseLocalScale.x) * (baseY * videoAspect);
                    break;

                case AspectFitMode.FitWidth:
                    scale.x = m_BaseLocalScale.x;
                    scale.y = Mathf.Sign(m_BaseLocalScale.y) * (baseX / videoAspect);
                    break;

                default: // Contain
                {
                    var baseAspect = baseX / baseY;
                    if (videoAspect >= baseAspect)
                    {
                        scale.x = m_BaseLocalScale.x;
                        scale.y = Mathf.Sign(m_BaseLocalScale.y) * (baseX / videoAspect);
                    }
                    else
                    {
                        scale.y = m_BaseLocalScale.y;
                        scale.x = Mathf.Sign(m_BaseLocalScale.x) * (baseY * videoAspect);
                    }

                    break;
                }
            }

            scale.z = m_BaseLocalScale.z;
            transform.localScale = scale;

            if (UseRenderTexturePath && ResolveActiveClip() != null)
                EnsureRenderTexture(ResolveActiveClip(), rtWidth, rtHeight);
        }

        void ApplyAlphaLayout(Material material)
        {
            if (material == null)
                return;

            if (material.HasProperty("_AlphaLayout"))
                material.SetFloat("_AlphaLayout", (float)m_AlphaLayout);
        }

        void EnsureRenderTexture(VideoClip clip)
        {
            var width = clip != null ? Mathf.Max(2, (int)clip.width) : 2;
            var height = clip != null ? Mathf.Max(2, (int)clip.height) : 2;
            if (width <= 2 || height <= 2)
            {
                width = 960;
                height = 540;
            }

            EnsureRenderTexture(clip, width, height);
        }

        void EnsureRenderTexture(VideoClip clip, int width, int height)
        {
            width = Mathf.Max(2, width);
            height = Mathf.Max(2, height);

            if (m_RenderTexture != null &&
                m_RenderTexture.width == width &&
                m_RenderTexture.height == height)
                return;

            if (m_RenderTexture != null)
            {
                if (m_VideoPlayer != null && m_VideoPlayer.targetTexture == m_RenderTexture)
                    m_VideoPlayer.targetTexture = null;
                m_RenderTexture.Release();
                Destroy(m_RenderTexture);
            }

            m_RenderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "VideoSpriteRT",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            m_RenderTexture.Create();

            if (m_VideoPlayer != null &&
                m_VideoPlayer.renderMode == VideoRenderMode.RenderTexture)
            {
                m_VideoPlayer.targetTexture = m_RenderTexture;
                if (m_Renderer != null)
                    ApplyTexture(m_Renderer.material, m_RenderTexture);
            }
        }

        void ApplyFlip(Material material)
        {
            if (!m_FlipVertical)
                return;

            if (material.HasProperty(m_TexturePropertyName))
            {
                material.SetTextureScale(m_TexturePropertyName, new Vector2(1f, -1f));
                material.SetTextureOffset(m_TexturePropertyName, new Vector2(0f, 1f));
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTextureScale("_MainTex", new Vector2(1f, -1f));
                material.SetTextureOffset("_MainTex", new Vector2(0f, 1f));
            }
        }

        void ApplyTexture(Material material, Texture texture)
        {
            if (material.HasProperty(m_TexturePropertyName))
                material.SetTexture(m_TexturePropertyName, texture);
            else if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
            else
                material.mainTexture = texture;
        }

        void ApplyOpacity(Material material)
        {
            if (material == null)
                return;

            var opacity = Mathf.Clamp01(m_Opacity);
            if (material.HasProperty("_Opacity"))
                material.SetFloat("_Opacity", opacity);

            if (material.HasProperty("_BaseColor"))
            {
                var c = material.GetColor("_BaseColor");
                c.a = opacity;
                material.SetColor("_BaseColor", c);
            }
            else if (material.HasProperty("_Color"))
            {
                var c = material.GetColor("_Color");
                c.a = opacity;
                material.SetColor("_Color", c);
            }
        }

        static void ClearRenderTexture(RenderTexture renderTexture)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = renderTexture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = previous;
        }

        void OnPrepareCompleted(VideoPlayer source)
        {
            ApplyFitFromPreparedSource(source);
            // RT path only: re-bind target texture after prepare. MaterialOverride must
            // own _BaseMap itself — do not ApplyTexture(source.texture) on device.
            if (UseRenderTexturePath && m_Renderer != null && m_RenderTexture != null)
            {
                if (m_VideoPlayer != null)
                    m_VideoPlayer.targetTexture = m_RenderTexture;
                ApplyTexture(m_Renderer.material, m_RenderTexture);
                ApplyAlphaLayout(m_Renderer.material);
                ApplyOpacity(m_Renderer.material);
            }
            else if (m_Renderer != null)
            {
                ApplyAlphaLayout(m_Renderer.material);
                ApplyOpacity(m_Renderer.material);
            }

            if (m_PlayWhenReady && isActiveAndEnabled)
                StartPlayback();
        }

        void OnStarted(VideoPlayer source)
        {
            // Some platforms only expose stable width/height after the first frame.
            ApplyFitFromPreparedSource(source);
            if (m_Renderer != null)
            {
                ApplyAlphaLayout(m_Renderer.material);
                ApplyOpacity(m_Renderer.material);
            }
            BeginApproachIfEnabled();
            Debug.Log(
                $"[VideoSprite] started playing={source.isPlaying} " +
                $"{source.width}x{source.height} mode={source.renderMode}",
                this);
        }

        void StartPlayback()
        {
            if (m_VideoPlayer == null || m_VideoPlayer.clip == null)
                return;

            m_VideoPlayer.Play();
        }

        /// <summary>
        /// Pause/resume for caption panels. Does not change authored autoplay flags.
        /// </summary>
        public void SetPausedForCaption(bool paused)
        {
            if (m_VideoPlayer == null || m_VideoPlayer.clip == null)
                return;

            if (paused)
            {
                if (m_VideoPlayer.isPlaying)
                    m_VideoPlayer.Pause();
                return;
            }

            if (isActiveAndEnabled && !m_VideoPlayer.isPlaying)
                m_VideoPlayer.Play();
        }

        void OnVideoError(VideoPlayer source, string message)
        {
            var clip = source != null && source.clip != null ? source.clip : ResolveActiveClip();
            var path = clip != null ? clip.originalPath : "(null clip)";
            Debug.LogError(
                $"[VideoSprite] VideoPlayer error: {message} (clipPath='{path}' " +
                $"mode={source?.renderMode} materialProp={m_TexturePropertyName}). " +
                "iOS cannot decode Apple ProRes. Device clip must be HEVC-with-alpha " +
                "(Assets/Videos/walking_man_device.mov, transcoding off) packed in the AssetBundle.",
                this);
        }
    }
}
