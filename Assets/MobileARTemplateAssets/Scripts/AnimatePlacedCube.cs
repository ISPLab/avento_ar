using UnityEngine;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Spins and bobs a placed object. Call <see cref="Play"/> after anchoring.
    /// </summary>
    public class AnimatePlacedCube : MonoBehaviour
    {
        [SerializeField]
        Vector3 m_RotationSpeedDegrees = new Vector3(25f, 90f, 15f);

        [SerializeField]
        float m_BobAmplitude = 0.03f;

        [SerializeField]
        float m_BobFrequency = 1.25f;

        [SerializeField]
        Vector3 m_MoveAmplitude = new Vector3(0.02f, 0f, 0.02f);

        Vector3 m_BaseLocalPosition;
        bool m_Playing;

        void Awake()
        {
            enabled = false;
        }

        public void Play()
        {
            m_BaseLocalPosition = transform.localPosition;
            m_Playing = true;
            enabled = true;
        }

        public void Stop()
        {
            m_Playing = false;
            enabled = false;
            transform.localPosition = m_BaseLocalPosition;
        }

        void Update()
        {
            if (!m_Playing)
                return;

            transform.Rotate(m_RotationSpeedDegrees * Time.deltaTime, Space.Self);

            var t = Time.time * m_BobFrequency * Mathf.PI * 2f;
            var offset = new Vector3(
                Mathf.Sin(t) * m_MoveAmplitude.x,
                Mathf.Sin(t) * m_BobAmplitude + m_MoveAmplitude.y,
                Mathf.Cos(t) * m_MoveAmplitude.z);

            transform.localPosition = m_BaseLocalPosition + offset;
        }
    }
}
