using UnityEngine;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Tap and/or walk-up (proximity to AR camera) → native TTS / Tessa, or an in-AR caption panel.
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

        [Tooltip("When Speech Mode is Caption, pause PlayVideoOnPlace on this object while the panel is open.")]
        [SerializeField]
        bool m_PauseVideoWhenCaptionShown = true;

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

        void OnDisable()
        {
            if (AventoCaptionOverlay.IsShowing(this))
                AventoCaptionOverlay.Hide();
        }

        void OnDestroy()
        {
            if (AventoCaptionOverlay.IsShowing(this))
                AventoCaptionOverlay.Hide();
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
            if (m_SpeechMode == AventoSpeechMode.Caption)
                return TryShowCaption();

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
            if (m_SpeechMode == AventoSpeechMode.Tessa || m_SpeechMode == AventoSpeechMode.TtsThenTessa)
                AventoUnityAudioGate.SetTessaVoiceActive(true);
            AventoUnityNative.NotifyObjectInteract(json);

            m_FiredOnce = true;
            m_NextAllowedAt = Time.unscaledTime + Mathf.Max(1f, m_CooldownSeconds);
            return true;
        }

        bool TryShowCaption()
        {
            if (AventoCaptionOverlay.IsShowing(this))
            {
                if (!AventoCaptionOverlay.IsShowFrame)
                    AventoCaptionOverlay.Hide();
                return true;
            }

            var title = string.IsNullOrWhiteSpace(m_DisplayName) ? gameObject.name : m_DisplayName.Trim();
            AventoCaptionOverlay.Show(this, title, ResolveCaptionBody());
            if (m_PauseVideoWhenCaptionShown)
                SetVideoPausedForCaption(true);
            Debug.Log($"[AventoObjectInteract] caption {gameObject.name}", this);
            return true;
        }

        public void NotifyCaptionClosed()
        {
            if (m_PauseVideoWhenCaptionShown)
                SetVideoPausedForCaption(false);
        }

        /// <summary>
        /// Caption panel "Ask Avento" → Tessa in avento-app about this painting.
        /// </summary>
        public void AskTessaFromCaption()
        {
            var id = string.IsNullOrWhiteSpace(m_ObjectId) ? gameObject.name : m_ObjectId.Trim();
            var title = string.IsNullOrWhiteSpace(m_DisplayName) ? gameObject.name : m_DisplayName.Trim();
            var notes = ResolveCaptionBody();
            var kickoff =
                "You are Tessa, Avento's live museum guide. The traveler is standing in AR " +
                "in front of the painting \"" + title + "\". Use these notes about the work:\n" +
                notes +
                "\nSpeak in the traveler's app language. Greet them briefly, describe the painting " +
                "in a few sentences, then invite questions. Stay in a live voice conversation. " +
                "Do not invent undocumented historical facts.";

            if (!AventoInteractionDirector.Instance.TryBeginSpeech(id, 1.2f))
                return;

            var json = AventoInteractJson.Build(
                "tap",
                id,
                title,
                kickoff,
                null,
                AventoSpeechMode.Tessa,
                m_VoiceNameOverride,
                m_SsmlGenderHint);

            Debug.Log($"[AventoObjectInteract] AskTessaFromCaption id={id} title={title} jsonLen={json.Length}", this);
            AventoUnityAudioGate.SetTessaVoiceActive(true);
            AventoUnityNative.NotifyObjectInteract(json);
            AventoTessaVoiceBar.Show();
        }

        void SetVideoPausedForCaption(bool paused)
        {
            var video = GetComponentInChildren<PlayVideoOnPlace>(true);
            if (video != null)
                video.SetPausedForCaption(paused);
        }

        string ResolveCaptionBody()
        {
            var lang = AventoUnityHost.Instance != null
                ? AventoUnityHost.Instance.SessionLanguage
                : "";
            lang = (lang ?? "").Trim().ToLowerInvariant();
            if (lang.Length >= 2 && m_PromptByLanguage != null)
            {
                var prefix = lang.Length > 2 ? lang.Substring(0, 2) : lang;
                for (var i = 0; i < m_PromptByLanguage.Length; i++)
                {
                    var row = m_PromptByLanguage[i];
                    if (row == null || string.IsNullOrWhiteSpace(row.lang))
                        continue;
                    var key = row.lang.Trim().ToLowerInvariant();
                    if (key == lang || key == prefix || lang.StartsWith(key))
                        return row.text ?? "";
                }
            }

            return m_Prompt ?? "";
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
