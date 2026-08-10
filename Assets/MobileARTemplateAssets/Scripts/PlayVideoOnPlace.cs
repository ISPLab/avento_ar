using System.Collections;
using UnityEngine;
using UnityEngine.Video;
using Unity.XR.CoreUtils;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Plays a looping alpha video on a transparent billboard.
    /// Uses RenderTexture in the Editor/XR Simulation (more reliable) and
    /// MaterialOverride on device.
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

        [SerializeField]
        VideoClip m_VideoClip;

        [SerializeField]
        string m_TexturePropertyName = "_BaseMap";

        [SerializeField]
        bool m_FlipVertical;

        [Tooltip(
            "Resize this object's X/Y localScale to the video pixel aspect. " +
            "Authored Transform Y is treated as world height for Fit Height mode.")]
        [SerializeField]
        bool m_FitQuadToAspect = true;

        [Tooltip("Fit Height = keep Transform Y (person height in meters), set X from video aspect.")]
        [SerializeField]
        AspectFitMode m_AspectFitMode = AspectFitMode.FitHeight;

        [Header("Billboard / rotation")]
        [Tooltip(
            "When on, the video sprite automatically rotates to face the camera (BillboardSprite). " +
            "When off, it keeps the placement / authored rotation.")]
        [SerializeField]
        bool m_RotateTowardCamera = true;

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

        static bool UseRenderTexturePath =>
            Application.isEditor || Application.platform == RuntimePlatform.OSXPlayer;

        void Awake()
        {
            m_VideoPlayer = GetComponent<VideoPlayer>();
            m_Renderer = GetComponent<MeshRenderer>();
            CaptureBaseScale();
            ApplyBillboardOption();
            ConfigurePlayer();
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
        public void Refresh()
        {
            m_Configured = false;
            // Keep the first authored base scale — do not capture a post-fit scale.
            if (!m_HasBaseScale || m_BaseLocalScale == Vector3.zero)
                CaptureBaseScale();

            ConfigurePlayer();
            EnsureFitRoutine();
            ApplyBillboardOption();
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
        public void SetRotateTowardCamera(bool enabled)
        {
            m_RotateTowardCamera = enabled;
            ApplyBillboardOption();
        }

        void ApplyBillboardOption()
        {
            var billboard = GetComponent<BillboardSprite>();
            if (billboard == null)
            {
                if (!m_RotateTowardCamera)
                    return;
                billboard = gameObject.AddComponent<BillboardSprite>();
            }

            billboard.rotateTowardCamera = m_RotateTowardCamera;
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

            if (m_VideoClip == null || m_VideoPlayer == null || m_Renderer == null)
            {
                Debug.LogWarning("[VideoSprite] Missing VideoClip, VideoPlayer, or MeshRenderer.", this);
                return;
            }

            if (!m_HasBaseScale)
                CaptureBaseScale();

            var material = m_Renderer.material;
            ApplyFlip(material);

            m_VideoPlayer.playOnAwake = false;
            m_VideoPlayer.waitForFirstFrame = true;
            m_VideoPlayer.isLooping = true;
            m_VideoPlayer.skipOnDrop = true;
            m_VideoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            m_VideoPlayer.clip = m_VideoClip;
            // Quad is resized to the pixel aspect; texture should fill that quad.
            m_VideoPlayer.aspectRatio = VideoAspectRatio.Stretch;

            // Best-effort early fit from clip metadata (often 0 until Prepare on some platforms).
            TryFitFromClipMetadata();

            if (UseRenderTexturePath)
            {
                EnsureRenderTexture();
                ClearRenderTexture(m_RenderTexture);
                m_VideoPlayer.renderMode = VideoRenderMode.RenderTexture;
                m_VideoPlayer.targetTexture = m_RenderTexture;
                ApplyTexture(material, m_RenderTexture);
            }
            else
            {
                m_VideoPlayer.renderMode = VideoRenderMode.MaterialOverride;
                m_VideoPlayer.targetMaterialRenderer = m_Renderer;
                m_VideoPlayer.targetMaterialProperty = m_TexturePropertyName;
            }

            m_Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Renderer.receiveShadows = false;
            m_Configured = true;
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
            if (m_VideoClip == null)
                return false;
            var w = (int)m_VideoClip.width;
            var h = (int)m_VideoClip.height;
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

            if (!m_HasBaseScale || m_BaseLocalScale == Vector3.zero)
                CaptureBaseScale();

            var videoAspect = (float)width / height;
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

            if (UseRenderTexturePath && m_VideoClip != null)
                EnsureRenderTexture(width, height);
        }

        void EnsureRenderTexture()
        {
            var width = m_VideoClip != null ? Mathf.Max(2, (int)m_VideoClip.width) : 2;
            var height = m_VideoClip != null ? Mathf.Max(2, (int)m_VideoClip.height) : 2;
            if (width <= 2 || height <= 2)
            {
                width = 960;
                height = 540;
            }

            EnsureRenderTexture(width, height);
        }

        void EnsureRenderTexture(int width, int height)
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
            if (m_PlayWhenReady && isActiveAndEnabled)
                StartPlayback();
        }

        void OnStarted(VideoPlayer source)
        {
            // Some platforms only expose stable width/height after the first frame.
            ApplyFitFromPreparedSource(source);
            BeginApproachIfEnabled();
        }

        void StartPlayback()
        {
            if (m_VideoPlayer == null || m_VideoPlayer.clip == null)
                return;

            m_VideoPlayer.Play();
        }

        void OnVideoError(VideoPlayer source, string message)
        {
            Debug.LogError($"[VideoSprite] VideoPlayer error: {message}", this);
        }
    }
}
