using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Native callbacks from the Unity player into the Capacitor host (iOS/Android UaaL).
    /// </summary>
    public static class AventoUnityNative
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void AventoUnity_OnReady(string json);

        [DllImport("__Internal")]
        static extern void AventoUnity_OnSessionEnded(string json);

        [DllImport("__Internal")]
        static extern void AventoUnity_OnError(string json);

        [DllImport("__Internal")]
        static extern void AventoUnity_OnObjectInteract(string json);
#endif

        public static void NotifyReady(string json)
        {
            Debug.Log($"[AventoUnityNative] Ready {json}");
#if UNITY_IOS && !UNITY_EDITOR
            AventoUnity_OnReady(json ?? "{}");
#elif UNITY_ANDROID && !UNITY_EDITOR
            CallAndroid("onUnityReady", json ?? "{}");
#endif
        }

        public static void NotifySessionEnded(string json)
        {
            Debug.Log($"[AventoUnityNative] SessionEnded {json}");
#if UNITY_IOS && !UNITY_EDITOR
            AventoUnity_OnSessionEnded(json ?? "{}");
#elif UNITY_ANDROID && !UNITY_EDITOR
            CallAndroid("onUnitySessionEnded", json ?? "{}");
#endif
        }

        public static void NotifyError(string json)
        {
            Debug.LogError($"[AventoUnityNative] Error {json}");
#if UNITY_IOS && !UNITY_EDITOR
            AventoUnity_OnError(json ?? "{}");
#elif UNITY_ANDROID && !UNITY_EDITOR
            CallAndroid("onUnityError", json ?? "{}");
#endif
        }

        public static void NotifyObjectInteract(string json)
        {
            Debug.Log($"[AventoUnityNative] ObjectInteract {json}");
#if UNITY_IOS && !UNITY_EDITOR
            AventoUnity_OnObjectInteract(json ?? "{}");
#elif UNITY_ANDROID && !UNITY_EDITOR
            CallAndroid("onUnityObjectInteract", json ?? "{}");
#elif UNITY_EDITOR
            Debug.Log($"[AventoUnityNative] (editor) object interact {json}");
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static void CallAndroid(string method, string json)
        {
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                activity?.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    using var bridge = new AndroidJavaClass("club.avento.app.UnityArNativeBridge");
                    bridge.CallStatic(method, json);
                }));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AventoUnityNative] Android bridge missing: {ex.Message}");
            }
        }
#endif
    }
}
