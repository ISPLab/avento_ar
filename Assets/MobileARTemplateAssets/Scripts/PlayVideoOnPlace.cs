using UnityEngine;
using UnityEngine.Video;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Plays a looping alpha video on a transparent billboard.
    /// Uses RenderTexture in the Editor/XR Simulation (more reliable) and
    /// MaterialOverride on device.
    /// Optionally resizes the quad to the video aspect so the clip fits without stretch distortion.
    /// </summary>
    [RequireComponent(typeof(VideoPlayer))]
    [RequireComponent(typeof(MeshRenderer))]
    public class PlayVideoOnPlace : MonoBehaviour
    {
        [SerializeField]
        VideoClip m_VideoClip;

        [SerializeField]
        string m_TexturePropertyName = "_BaseMap";

        [SerializeField]
        bool m_FlipVertical;

        [Tooltip("Resize this object's X/Y scale to the video aspect (keeps the larger axis). Applies to every Video Sprite using this component.")]
        [SerializeField]
        bool m_FitQuadToAspect = true;

        VideoPlayer m_VideoPlayer;
        MeshRenderer m_Renderer;
        RenderTexture m_RenderTexture;
        bool m_Configured;
        bool m_PlayWhenReady;
        Vector3 m_BaseLocalScale;

        static bool UseRenderTexturePath =>
            Application.isEditor || Application.platform == RuntimePlatform.OSXPlayer;

        void Awake()
        {
            m_VideoPlayer = GetComponent<VideoPlayer>();
            m_Renderer = GetComponent<MeshRenderer>();
            m_BaseLocalScale = transform.localScale;
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
            if (m_VideoPlayer.isPrepared)
                StartPlayback();
            else
                m_VideoPlayer.Prepare();
        }

        void OnDisable()
        {
            m_PlayWhenReady = false;

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
            if (m_BaseLocalScale == Vector3.zero)
                m_BaseLocalScale = transform.localScale;
            ConfigurePlayer();
            if (isActiveAndEnabled && m_VideoPlayer != null)
            {
                m_PlayWhenReady = true;
                if (m_VideoPlayer.isPrepared)
                    StartPlayback();
                else
                    m_VideoPlayer.Prepare();
            }
        }

        void ConfigurePlayer()
        {
            if (m_Configured)
                return;

            if (m_VideoClip == null || m_VideoPlayer == null || m_Renderer == null)
            {
                Debug.LogWarning("[VideoSprite] Missing VideoClip, VideoPlayer, or MeshRenderer.", this);
                return;
            }

            var material = m_Renderer.material;
            ApplyFlip(material);

            m_VideoPlayer.playOnAwake = false;
            m_VideoPlayer.waitForFirstFrame = true;
            m_VideoPlayer.isLooping = true;
            m_VideoPlayer.skipOnDrop = true;
            m_VideoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            m_VideoPlayer.clip = m_VideoClip;
            // Texture fills the fitted quad; we change transform scale instead of letterboxing.
            m_VideoPlayer.aspectRatio = VideoAspectRatio.Stretch;

            FitQuadToVideoAspect(
                (int)m_VideoClip.width,
                (int)m_VideoClip.height);

            if (UseRenderTexturePath)
            {
                EnsureRenderTexture();
                ClearRenderTexture(m_RenderTexture);
                m_VideoPlayer.renderMode = VideoRenderMode.RenderTexture;
                m_VideoPlayer.targetTexture = m_RenderTexture;
                ApplyTexture(material, m_RenderTexture);
                Debug.Log("[VideoSprite] Using RenderTexture path (Editor/Sim).", this);
            }
            else
            {
                m_VideoPlayer.renderMode = VideoRenderMode.MaterialOverride;
                m_VideoPlayer.targetMaterialRenderer = m_Renderer;
                m_VideoPlayer.targetMaterialProperty = m_TexturePropertyName;
                Debug.Log("[VideoSprite] Using MaterialOverride path (Device).", this);
            }

            m_Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Renderer.receiveShadows = false;
            m_Configured = true;

            Debug.Log(
                $"[VideoSprite] configured clip='{m_VideoClip.name}' " +
                $"{m_VideoClip.width}x{m_VideoClip.height} fit={m_FitQuadToAspect}",
                this);
        }

        void FitQuadToVideoAspect(int width, int height)
        {
            if (!m_FitQuadToAspect || width <= 0 || height <= 0)
                return;

            if (m_BaseLocalScale == Vector3.zero)
                m_BaseLocalScale = transform.localScale;

            var videoAspect = (float)width / height;
            var baseAspect = Mathf.Abs(m_BaseLocalScale.y) > 0.0001f
                ? Mathf.Abs(m_BaseLocalScale.x / m_BaseLocalScale.y)
                : 1f;

            var scale = m_BaseLocalScale;
            if (videoAspect >= baseAspect)
                scale.y = scale.x / videoAspect;
            else
                scale.x = scale.y * videoAspect;

            transform.localScale = scale;
        }

        void EnsureRenderTexture()
        {
            var width = Mathf.Max(2, (int)m_VideoClip.width);
            var height = Mathf.Max(2, (int)m_VideoClip.height);

            if (m_RenderTexture != null &&
                m_RenderTexture.width == width &&
                m_RenderTexture.height == height)
                return;

            if (m_RenderTexture != null)
            {
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
            var w = (int)source.width;
            var h = (int)source.height;
            if (w > 0 && h > 0)
                FitQuadToVideoAspect(w, h);

            Debug.Log($"[VideoSprite] prepared {w}x{h}", this);
            if (m_PlayWhenReady && isActiveAndEnabled)
                StartPlayback();
        }

        void OnStarted(VideoPlayer source)
        {
            Debug.Log($"[VideoSprite] started playing time={source.time:F2}", this);
        }

        void StartPlayback()
        {
            if (m_VideoPlayer == null || m_VideoPlayer.clip == null)
                return;

            m_VideoPlayer.Play();
            Debug.Log($"[VideoSprite] Play() isPlaying={m_VideoPlayer.isPlaying}", this);
        }

        void OnVideoError(VideoPlayer source, string message)
        {
            Debug.LogError($"[VideoSprite] VideoPlayer error: {message}", this);
        }
    }
}
