using System.Collections;
using UnityEngine;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Optional scene-level Tessa kickoff after content is placed.
    /// Offer flag <c>autoStartTessa</c> (OpenFromNative) is the on/off switch;
    /// this component supplies title/prompt when present on the prefab.
    /// </summary>
    public class AventoSceneTessa : MonoBehaviour
    {
        [SerializeField]
        string m_ObjectId = "scene";

        [SerializeField]
        string m_DisplayName = "";

        [TextArea(3, 8)]
        [SerializeField]
        string m_Prompt = "";

        [SerializeField]
        AventoPromptByLanguage[] m_PromptByLanguage;

        [SerializeField]
        float m_DelaySeconds = 0.6f;

        bool m_Fired;
        bool m_Waiting;

        public void SetAsHostFallback(float delaySeconds)
        {
            m_DelaySeconds = delaySeconds;
            m_ObjectId = "scene";
            m_Fired = false;
            m_Waiting = false;
        }

        void OnEnable()
        {
            m_Fired = false;
            m_Waiting = false;
            AventoInteractionDirector.ContentPlaced += OnContentPlaced;
        }

        void OnDisable()
        {
            AventoInteractionDirector.ContentPlaced -= OnContentPlaced;
        }

        void OnContentPlaced(GameObject instance)
        {
            if (m_Fired || m_Waiting || instance == null)
                return;

            var onPlacedTree =
                transform == instance.transform || transform.IsChildOf(instance.transform);
            var hostLevel = GetComponent<AventoUnityHost>() != null;
            if (!onPlacedTree && !hostLevel)
                return;

            var host = AventoUnityHost.Instance;
            if (host != null && !host.AutoStartTessa)
            {
                Debug.Log("[AventoSceneTessa] skip — host.AutoStartTessa=false", this);
                return;
            }

            m_Waiting = true;
            Debug.Log($"[AventoSceneTessa] scheduling scene_start in {m_DelaySeconds:0.##}s", this);
            StartCoroutine(FireAfterDelay());
        }

        IEnumerator FireAfterDelay()
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0.2f, m_DelaySeconds));
            FireNow();
        }

        public void FireNow()
        {
            if (m_Fired)
                return;
            if (!AventoInteractionDirector.TryMarkSceneStartSent())
            {
                m_Fired = true;
                return;
            }

            m_Fired = true;
            var host = AventoUnityHost.Instance;
            var title = !string.IsNullOrWhiteSpace(m_DisplayName)
                ? m_DisplayName.Trim()
                : (host != null ? host.SessionTitle : "Unity AR");
            var prompt = !string.IsNullOrWhiteSpace(m_Prompt)
                ? m_Prompt
                : (host != null ? host.AutoStartTessaPrompt : "");

            var json = AventoInteractJson.Build(
                "scene_start",
                string.IsNullOrWhiteSpace(m_ObjectId) ? "scene" : m_ObjectId.Trim(),
                title,
                prompt,
                m_PromptByLanguage,
                AventoSpeechMode.Tessa,
                "",
                AventoSsmlGenderHint.Unspecified);

            Debug.Log("[AventoSceneTessa] scene_start", this);
            AventoUnityNative.NotifyObjectInteract(json);
        }
    }
}
