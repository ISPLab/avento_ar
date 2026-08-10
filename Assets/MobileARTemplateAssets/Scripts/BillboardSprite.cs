using UnityEngine;
using Unity.XR.CoreUtils;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Keeps the transform facing the XR / main camera like a 2D sprite / billboard.
    /// Can be disabled so the sprite keeps its authored / placement rotation.
    /// </summary>
    public class BillboardSprite : MonoBehaviour
    {
        [SerializeField]
        Camera m_Camera;

        [Tooltip("When off, the sprite does not auto-rotate toward the camera.")]
        [SerializeField]
        bool m_RotateTowardCamera = true;

        [SerializeField]
        bool m_LockYAxis = true;

        [SerializeField]
        bool m_FlipToFaceCamera = true;

        /// <summary>Toggle automatic camera-facing rotation.</summary>
        public bool rotateTowardCamera
        {
            get => m_RotateTowardCamera;
            set => m_RotateTowardCamera = value;
        }

        void LateUpdate()
        {
            if (!m_RotateTowardCamera)
                return;

            var cam = ResolveCamera();
            if (cam == null)
                return;

            var lookDirection = cam.transform.position - transform.position;
            if (m_LockYAxis)
                lookDirection.y = 0f;

            if (lookDirection.sqrMagnitude < 0.0001f)
                return;

            var rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            if (m_FlipToFaceCamera)
                rotation *= Quaternion.Euler(0f, 180f, 0f);

            transform.rotation = rotation;
        }

        Camera ResolveCamera()
        {
            if (m_Camera != null)
                return m_Camera;

            var origin = FindFirstObjectByType<XROrigin>();
            if (origin != null && origin.Camera != null)
                return origin.Camera;

            return Camera.main;
        }
    }
}
