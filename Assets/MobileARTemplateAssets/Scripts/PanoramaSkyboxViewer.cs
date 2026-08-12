using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Shows an equirectangular panorama in XR Simulation / AR by parenting an inverted
    /// dome to the camera (skybox alone is hidden behind the simulation room mesh).
    /// Mouse / touch drag looks around.
    /// </summary>
    public class PanoramaSkyboxViewer : MonoBehaviour
    {
        const string k_DomeShaderName = "AR/EquirectangularDome";

        [SerializeField]
        Texture2D m_PanoramaTexture;

        [SerializeField]
        Material m_SkyboxMaterial;

        [Tooltip("Disable ARCameraBackground so the live/sim camera feed does not cover the dome.")]
        [SerializeField]
        bool m_HideArCameraBackground = true;

        [Tooltip("Hide XR Simulation environment meshes so they do not poke through the dome.")]
        [SerializeField]
        bool m_HideSimulationEnvironment = true;

        [Tooltip("Disable TrackedPoseDriver so mouse/touch look is not overwritten by XR tracking.")]
        [SerializeField]
        bool m_DisableTrackedPoseWhileActive = true;

        [Tooltip("Dome radius in meters. Keep small so it sits between the camera and sim room walls.")]
        [SerializeField]
        float m_DomeRadius = 0.75f;

        [Tooltip("Drag look sensitivity in degrees per pixel.")]
        [SerializeField]
        float m_LookSensitivity = 0.15f;

        [SerializeField]
        float m_MinPitch = -89f;

        [SerializeField]
        float m_MaxPitch = 89f;

        [Tooltip("Optional. Defaults to Camera.main / XR Origin camera.")]
        [SerializeField]
        Transform m_LookTarget;

        [SerializeField]
        float m_Exposure = 1f;

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
        readonly List<(Behaviour driver, bool wasEnabled)> m_PoseDrivers = new();
        readonly List<(Renderer renderer, bool wasEnabled)> m_HiddenSimRenderers = new();
        bool m_Dragging;
        Vector2 m_LastPointer;
        float m_Yaw;
        float m_Pitch;

        public Texture2D panoramaTexture
        {
            get => m_PanoramaTexture;
            set
            {
                m_PanoramaTexture = value;
                if (isActiveAndEnabled)
                    ApplyPanorama();
            }
        }

        void OnEnable()
        {
            ResolveLookTarget();
            CaptureLookAngles();
            ApplyPanorama();
            ConfigureCamera();
            HideSimulationEnvironment();
        }

        void OnDisable()
        {
            RestoreCamera();
            RestoreSimulationEnvironment();
            RestoreSkybox();
            DestroyDome();
        }

        void LateUpdate()
        {
            // AR / simulation systems may re-enable background or spawn the room after us.
            if (m_HideArCameraBackground && m_ArBackground != null && m_ArBackground.enabled)
                m_ArBackground.enabled = false;

            if (m_HideSimulationEnvironment && Time.frameCount % 30 == 0)
                HideSimulationEnvironment();

            if (m_Camera != null && m_Camera.clearFlags != CameraClearFlags.SolidColor &&
                m_Camera.clearFlags != CameraClearFlags.Skybox)
                m_Camera.clearFlags = CameraClearFlags.SolidColor;

            KeepDomeOnCamera();
            HandleLookInput();
        }

        void ApplyPanorama()
        {
            if (m_PanoramaTexture == null)
            {
                Debug.LogWarning("[PanoramaSkybox] No panorama texture assigned.", this);
                return;
            }

            ApplySkyboxFallback();
            EnsureDome();
            ApplyDomeTexture();

            Debug.Log(
                $"[PanoramaSkybox] Dome + skybox '{m_PanoramaTexture.name}' {m_PanoramaTexture.width}x{m_PanoramaTexture.height}",
                this);
        }

        void ApplySkyboxFallback()
        {
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

            var shader = Shader.Find(k_DomeShaderName);
            if (shader == null)
            {
                Debug.LogError($"[PanoramaSkybox] Shader '{k_DomeShaderName}' not found.", this);
                return;
            }

            m_DomeMaterial = new Material(shader) { name = "PanoramaDome (Runtime)" };
            var renderer = m_Dome.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = m_DomeMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            KeepDomeOnCamera();
        }

        void ApplyDomeTexture()
        {
            if (m_DomeMaterial == null || m_PanoramaTexture == null)
                return;

            if (m_DomeMaterial.HasProperty("_BaseMap"))
                m_DomeMaterial.SetTexture("_BaseMap", m_PanoramaTexture);
            if (m_DomeMaterial.HasProperty("_BaseColor"))
                m_DomeMaterial.SetColor("_BaseColor", Color.white * m_Exposure);
            if (m_DomeMaterial.HasProperty("_RotationY"))
                m_DomeMaterial.SetFloat("_RotationY", m_YawOffset);
        }

        void KeepDomeOnCamera()
        {
            if (m_Dome == null)
                return;

            var cam = m_Camera != null ? m_Camera : ResolveCamera();
            if (cam == null)
                return;

            m_Camera = cam;
            // Keep the dome world-fixed around the camera so looking around reveals the panorama.
            // Parenting with camera rotation would pin the image to the view.
            var t = m_Dome.transform;
            if (t.parent != null)
                t.SetParent(null, true);
            t.position = cam.transform.position;
            t.rotation = Quaternion.identity;
            t.localScale = Vector3.one * (m_DomeRadius * 2f);
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
        }

        void ConfigureCamera()
        {
            m_Camera = ResolveCamera();
            if (m_Camera == null)
                return;

            m_PreviousClearFlags = m_Camera.clearFlags;
            // SolidColor avoids fighting AR systems that reset Skybox clear flags.
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = Color.black;

            if (m_HideArCameraBackground)
            {
                m_ArBackground = m_Camera.GetComponent<ARCameraBackground>();
                m_HadArBackground = m_ArBackground != null;
                if (m_HadArBackground)
                {
                    m_ArBackgroundWasEnabled = m_ArBackground.enabled;
                    m_ArBackground.enabled = false;
                }
            }

            if (!m_DisableTrackedPoseWhileActive)
                return;

            m_PoseDrivers.Clear();
            foreach (var behaviour in m_Camera.GetComponents<Behaviour>())
            {
                if (behaviour == null)
                    continue;

                var typeName = behaviour.GetType().Name;
                if (typeName != "TrackedPoseDriver" && typeName != "ARPoseDriver")
                    continue;

                m_PoseDrivers.Add((behaviour, behaviour.enabled));
                behaviour.enabled = false;
            }
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

                // Avoid duplicate entries when called repeatedly from LateUpdate.
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
                m_Camera.clearFlags = m_PreviousClearFlags;

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

        void HandleLookInput()
        {
            if (m_LookTarget == null)
                ResolveLookTarget();
            if (m_LookTarget == null)
                return;

            if (TryGetPointer(out var pos, out var pressed, out var held))
            {
                if (pressed)
                {
                    m_Dragging = true;
                    m_LastPointer = pos;
                }
                else if (held && m_Dragging)
                {
                    var delta = pos - m_LastPointer;
                    m_LastPointer = pos;
                    m_Yaw += delta.x * m_LookSensitivity;
                    m_Pitch -= delta.y * m_LookSensitivity;
                    m_Pitch = Mathf.Clamp(m_Pitch, m_MinPitch, m_MaxPitch);
                    m_LookTarget.rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
                }
                else if (!held)
                {
                    m_Dragging = false;
                }
            }
            else
            {
                m_Dragging = false;
            }
        }

        static bool TryGetPointer(out Vector2 position, out bool pressedThisFrame, out bool held)
        {
            position = default;
            pressedThisFrame = false;
            held = false;

            var touchscreen = Touchscreen.current;
            if (touchscreen != null && touchscreen.primaryTouch.press.isPressed)
            {
                position = touchscreen.primaryTouch.position.ReadValue();
                pressedThisFrame = touchscreen.primaryTouch.press.wasPressedThisFrame;
                held = true;
                return true;
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed)
            {
                position = mouse.position.ReadValue();
                pressedThisFrame = mouse.leftButton.wasPressedThisFrame;
                held = true;
                return true;
            }

            if (UnityEngine.Input.touchCount > 0)
            {
                var t = UnityEngine.Input.GetTouch(0);
                position = t.position;
                pressedThisFrame = t.phase == UnityEngine.TouchPhase.Began;
                held = t.phase == UnityEngine.TouchPhase.Began
                    || t.phase == UnityEngine.TouchPhase.Moved
                    || t.phase == UnityEngine.TouchPhase.Stationary;
                return held;
            }

            if (UnityEngine.Input.GetMouseButton(0))
            {
                position = UnityEngine.Input.mousePosition;
                pressedThisFrame = UnityEngine.Input.GetMouseButtonDown(0);
                held = true;
                return true;
            }

            return false;
        }

        void ResolveLookTarget()
        {
            if (m_LookTarget != null)
                return;

            var cam = ResolveCamera();
            if (cam != null)
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
    }
}
