using UnityEngine;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Tap and/or walk-up (proximity to AR camera) → native event for avento-app TTS / Tessa.
    /// Put on any collider in the placed prefab. Voice names come from Assets/voices.json.
    /// </summary>
    [DisallowMultipleComponent]
    public class AventoObjectInteract : MonoBehaviour
    {
        [SerializeField]
        string m_ObjectId = "";

        [SerializeField]
        string m_DisplayName = "";

        [TextArea(3, 8)]
        [SerializeField]
        string m_Prompt = "";

        [SerializeField]
        AventoPromptByLanguage[] m_PromptByLanguage;

        [SerializeField]
        AventoSpeechMode m_SpeechMode = AventoSpeechMode.TtsThenTessa;

        [SerializeField]
        AventoInteractTriggerMode m_TriggerMode = AventoInteractTriggerMode.Both;

        [SerializeField]
        float m_ProximityRadiusMeters = 1.8f;

        [SerializeField]
        float m_ProximityExitMeters = 2.4f;

        [SerializeField]
        bool m_FireOnce = true;

        [SerializeField]
        float m_CooldownSeconds = 30f;

        [SerializeField]
        bool m_RequireLineOfSight;

        [SerializeField]
        bool m_RequireFacingUser;

        [Tooltip("Google TTS catalog name from Assets/voices.json. Empty = app picks from user language + Settings.")]
        [SerializeField]
        string m_VoiceNameOverride = "";

        [SerializeField]
        AventoSsmlGenderHint m_SsmlGenderHint = AventoSsmlGenderHint.Unspecified;

        bool m_Inside;
        bool m_FiredOnce;
        float m_NextAllowedAt;
        float m_ProximityPollAt;

        public AventoInteractTriggerMode TriggerMode => m_TriggerMode;

        public bool AllowsTap =>
            m_TriggerMode == AventoInteractTriggerMode.Tap ||
            m_TriggerMode == AventoInteractTriggerMode.Both;

        public bool AllowsProximity =>
            m_TriggerMode == AventoInteractTriggerMode.Proximity ||
            m_TriggerMode == AventoInteractTriggerMode.Both;

        void Reset()
        {
            if (string.IsNullOrWhiteSpace(m_ObjectId))
                m_ObjectId = gameObject.name;
            if (string.IsNullOrWhiteSpace(m_DisplayName))
                m_DisplayName = gameObject.name;
            if (GetComponentInChildren<Collider>() == null)
                gameObject.AddComponent<BoxCollider>();
        }

        void OnEnable()
        {
            m_Inside = false;
        }

        void Update()
        {
            if (!AllowsProximity)
                return;
            if (Time.unscaledTime < m_ProximityPollAt)
                return;
            m_ProximityPollAt = Time.unscaledTime + 0.12f;

            var cam = Camera.main;
            if (cam == null)
                return;

            var d = Vector3.Distance(transform.position, cam.transform.position);
            var enterR = Mathf.Max(0.2f, m_ProximityRadiusMeters);
            var exitR = Mathf.Max(enterR + 0.15f, m_ProximityExitMeters);

            if (!m_Inside && d <= enterR)
            {
                if (m_RequireFacingUser && !IsFacingCamera(cam))
                    return;
                if (m_RequireLineOfSight && !HasLineOfSight(cam))
                    return;
                m_Inside = true;
                TryFire("proximity");
            }
            else if (m_Inside && d >= exitR)
            {
                m_Inside = false;
            }
        }

        public bool TryHandleTap()
        {
            if (!AllowsTap)
                return false;
            return TryFire("tap");
        }

        bool TryFire(string trigger)
        {
            if (m_FireOnce && m_FiredOnce)
                return false;
            if (Time.unscaledTime < m_NextAllowedAt)
                return false;

            var id = string.IsNullOrWhiteSpace(m_ObjectId) ? gameObject.name : m_ObjectId.Trim();
            if (!AventoInteractionDirector.Instance.TryBeginSpeech(id))
                return false;

            var json = AventoInteractJson.Build(
                trigger,
                id,
                string.IsNullOrWhiteSpace(m_DisplayName) ? gameObject.name : m_DisplayName.Trim(),
                m_Prompt,
                m_PromptByLanguage,
                m_SpeechMode,
                m_VoiceNameOverride,
                m_SsmlGenderHint);

            Debug.Log($"[AventoObjectInteract] {trigger} {id}", this);
            AventoUnityNative.NotifyObjectInteract(json);

            m_FiredOnce = true;
            m_NextAllowedAt = Time.unscaledTime + Mathf.Max(1f, m_CooldownSeconds);
            return true;
        }

        bool IsFacingCamera(Camera cam)
        {
            var toCam = cam.transform.position - transform.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude < 1e-4f)
                return true;
            var fwd = transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f)
                return true;
            return Vector3.Dot(fwd.normalized, toCam.normalized) > 0.3f;
        }

        bool HasLineOfSight(Camera cam)
        {
            var origin = cam.transform.position;
            var dest = transform.position + Vector3.up * 0.4f;
            var dir = dest - origin;
            var dist = dir.magnitude;
            if (dist < 0.05f)
                return true;
            if (!Physics.Raycast(origin, dir / dist, out var hit, dist))
                return true;
            return hit.collider != null && hit.collider.transform.IsChildOf(transform);
        }
    }
}
