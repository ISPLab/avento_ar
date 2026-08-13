using UnityEngine;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Shows a static image (including PNG alpha) on a transparent billboard.
    /// Uses the MeshRenderer material texture first (what you assign in the Editor),
    /// then falls back to Texture / Sprite fields.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class PlayImageOnPlace : MonoBehaviour
    {
        [Tooltip("Optional fallback if the material has no Base Map. Prefer assigning the image on the MeshRenderer material.")]
        [SerializeField]
        Texture2D m_Texture;

        [SerializeField]
        Sprite m_Sprite;

        [SerializeField]
        string m_TexturePropertyName = "_BaseMap";

        [SerializeField]
        bool m_FlipVertical;

        [Tooltip("Resize this object's X/Y scale to the image aspect (keeps the larger axis). Same as Video Sprite.")]
        [SerializeField]
        bool m_FitQuadToAspect = true;

        [Tooltip("Overall sprite opacity (1 = fully opaque, 0 = invisible). Multiplies texture alpha.")]
        [Range(0f, 1f)]
        [SerializeField]
        float m_Opacity = 1f;

        [Header("Face camera")]
        [Tooltip(
            "On = sprite always turns to face the XR / main camera (billboard). " +
            "Off = keeps placement / authored rotation.")]
        [SerializeField]
        bool m_FaceCamera = true;

        MeshRenderer m_Renderer;
        Vector3 m_BaseLocalScale;
        bool m_HasBaseScale;

        public Texture2D texture
        {
            get => m_Texture;
            set
            {
                m_Texture = value;
                if (isActiveAndEnabled)
                    ApplyImage();
            }
        }

        public Sprite sprite
        {
            get => m_Sprite;
            set
            {
                m_Sprite = value;
                if (isActiveAndEnabled)
                    ApplyImage();
            }
        }

        public float opacity
        {
            get => m_Opacity;
            set
            {
                m_Opacity = Mathf.Clamp01(value);
                if (isActiveAndEnabled)
                    ApplyOpacity(m_Renderer != null ? m_Renderer.material : null);
            }
        }

        public bool faceCamera
        {
            get => m_FaceCamera;
            set
            {
                m_FaceCamera = value;
                ApplyFaceCameraOption();
            }
        }

        void Awake()
        {
            m_Renderer = GetComponent<MeshRenderer>();
            CaptureBaseScale();
            ApplyFaceCameraOption();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (!Application.isPlaying)
                ApplyFaceCameraOption();
        }
#endif

        void OnEnable()
        {
            ApplyFaceCameraOption();
            ApplyImage();
        }

        /// <summary>Re-apply texture and re-fit after Instantiate / host assignment.</summary>
        public void Refresh()
        {
            if (!m_HasBaseScale)
                CaptureBaseScale();
            ApplyFaceCameraOption();
            ApplyImage();
        }

        void CaptureBaseScale()
        {
            m_BaseLocalScale = transform.localScale;
            m_HasBaseScale = true;
        }

        void ApplyFaceCameraOption()
        {
            var billboard = GetComponent<BillboardSprite>();
            if (billboard == null)
            {
                if (!m_FaceCamera)
                    return;
                billboard = gameObject.AddComponent<BillboardSprite>();
            }

            billboard.rotateTowardCamera = m_FaceCamera;
        }

        void ApplyImage()
        {
            if (m_Renderer == null)
                m_Renderer = GetComponent<MeshRenderer>();
            if (m_Renderer == null)
                return;

            if (!ResolveImage(out var width, out var height, out var tex))
            {
                Debug.LogWarning(
                    "[ImageSprite] No image found. Assign Base Map on the material, or Texture/Sprite on PlayImageOnPlace.",
                    this);
                return;
            }

            var material = m_Renderer.material;
            ApplyTexture(material, tex);
            ApplyFlip(material);
            ApplyOpacity(material);
            material.renderQueue = 3000;

            FitQuadToImageAspect(width, height);

            m_Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Renderer.receiveShadows = false;

            Debug.Log(
                $"[ImageSprite] Applied '{tex.name}' {width}x{height} fit={m_FitQuadToAspect} opacity={m_Opacity:0.##}",
                this);
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

        bool ResolveImage(out int width, out int height, out Texture tex)
        {
            width = 0;
            height = 0;
            tex = null;

            // 1) MeshRenderer material — what you change in the Prefab/Inspector.
            tex = GetMaterialTexture();
            if (tex != null)
            {
                width = Mathf.Max(1, tex.width);
                height = Mathf.Max(1, tex.height);
                return true;
            }

            // 2) Explicit Texture2D field
            if (m_Texture != null)
            {
                tex = m_Texture;
                width = m_Texture.width;
                height = m_Texture.height;
                return width > 0 && height > 0;
            }

            // 3) Sprite (use rect size, not full atlas)
            if (m_Sprite != null)
            {
                tex = m_Sprite.texture;
                var rect = m_Sprite.rect;
                width = Mathf.Max(1, Mathf.RoundToInt(rect.width));
                height = Mathf.Max(1, Mathf.RoundToInt(rect.height));
                return tex != null;
            }

            return false;
        }

        Texture GetMaterialTexture()
        {
            var shared = m_Renderer != null ? m_Renderer.sharedMaterial : null;
            if (shared == null)
                return null;

            Texture matTex = null;
            if (!string.IsNullOrEmpty(m_TexturePropertyName) &&
                shared.HasProperty(m_TexturePropertyName))
                matTex = shared.GetTexture(m_TexturePropertyName);

            if (matTex == null && shared.HasProperty("_BaseMap"))
                matTex = shared.GetTexture("_BaseMap");

            if (matTex == null && shared.HasProperty("_MainTex"))
                matTex = shared.GetTexture("_MainTex");

            if (matTex == null)
                matTex = shared.mainTexture;

            return matTex;
        }

        void FitQuadToImageAspect(int width, int height)
        {
            if (!m_FitQuadToAspect || width <= 0 || height <= 0)
                return;

            if (!m_HasBaseScale || m_BaseLocalScale == Vector3.zero)
                CaptureBaseScale();

            var imageAspect = (float)width / height;
            var baseAspect = Mathf.Abs(m_BaseLocalScale.y) > 0.0001f
                ? Mathf.Abs(m_BaseLocalScale.x / m_BaseLocalScale.y)
                : 1f;

            var scale = m_BaseLocalScale;
            if (imageAspect >= baseAspect)
                scale.y = scale.x / imageAspect;
            else
                scale.x = scale.y * imageAspect;

            transform.localScale = scale;
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
    }
}
