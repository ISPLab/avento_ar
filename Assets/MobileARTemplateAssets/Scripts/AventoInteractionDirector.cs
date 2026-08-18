using UnityEngine;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Single-speaker lock + tap routing for AR interactables.
    /// </summary>
    public class AventoInteractionDirector : MonoBehaviour
    {
        public const string GameObjectName = "AventoInteractionDirector";

        static AventoInteractionDirector s_Instance;
        float m_LockUntil;
        string m_LockedId;
        bool m_SceneStartSent;

        public static AventoInteractionDirector Instance
        {
            get
            {
                if (s_Instance != null)
                    return s_Instance;
                var existing = FindAnyObjectByType<AventoInteractionDirector>();
                if (existing != null)
                {
                    s_Instance = existing;
                    return s_Instance;
                }

                var go = new GameObject(GameObjectName);
                DontDestroyOnLoad(go);
                s_Instance = go.AddComponent<AventoInteractionDirector>();
                return s_Instance;
            }
        }

        public static event System.Action<GameObject> ContentPlaced;

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_Instance = this;
            gameObject.name = GameObjectName;
        }

        void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;
        }

        public static void NotifyContentPlaced(GameObject instance)
        {
            _ = Instance;
            ContentPlaced?.Invoke(instance);
        }

        public static void ResetSession()
        {
            if (s_Instance == null)
                return;
            s_Instance.m_LockUntil = 0f;
            s_Instance.m_LockedId = null;
            s_Instance.m_SceneStartSent = false;
        }

        public static bool TryMarkSceneStartSent()
        {
            var inst = Instance;
            if (inst.m_SceneStartSent)
                return false;
            inst.m_SceneStartSent = true;
            return true;
        }

        public bool IsBusy => Time.unscaledTime < m_LockUntil;

        public bool TryBeginSpeech(string objectId, float lockSeconds = 2f)
        {
            if (IsBusy && !string.Equals(m_LockedId, objectId, System.StringComparison.Ordinal))
                return false;
            m_LockedId = objectId;
            m_LockUntil = Time.unscaledTime + Mathf.Max(0.4f, lockSeconds);
            return true;
        }

        public static bool TryHandleTap(Vector2 unityScreenPosition)
        {
            var cam = Camera.main;
            if (cam == null)
                return false;

            var ray = cam.ScreenPointToRay(unityScreenPosition);
            if (!Physics.Raycast(ray, out var hit, 80f))
                return false;

            var interact = hit.collider.GetComponentInParent<AventoObjectInteract>();
            if (interact == null)
                return false;

            return interact.TryHandleTap();
        }
    }
}
