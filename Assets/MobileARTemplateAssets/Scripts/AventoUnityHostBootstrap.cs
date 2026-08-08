using UnityEngine;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Ensures <see cref="AventoUnityHost"/> exists so Capacitor can UnitySendMessage into it.
    /// </summary>
    public static class AventoUnityHostBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void EnsureHost()
        {
            if (Object.FindFirstObjectByType<AventoUnityHost>() != null)
                return;

            var go = new GameObject(AventoUnityHost.GameObjectName);
            Object.DontDestroyOnLoad(go);
            go.AddComponent<AventoUnityHost>();
            Debug.Log("[AventoUnityHost] Bootstrap created host GameObject (BeforeSceneLoad).");
        }
    }
}
