using UnityEngine;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Temporarily suppress Unity scene audio while native Tessa voice is speaking.
    /// This prevents Unity output from masking/owning the active device route.
    /// </summary>
    public static class AventoUnityAudioGate
    {
        static int s_TessaLocks;
        static bool s_SavedPause;
        static float s_SavedVolume = 1f;

        public static void SetTessaVoiceActive(bool active)
        {
            if (active)
            {
                s_TessaLocks += 1;
                if (s_TessaLocks != 1) return;
                s_SavedPause = AudioListener.pause;
                s_SavedVolume = AudioListener.volume;
                AudioListener.pause = true;
                AudioListener.volume = 0f;
                Debug.Log("[AventoUnityAudioGate] Tessa active -> Unity audio paused");
                return;
            }

            if (s_TessaLocks > 0)
                s_TessaLocks -= 1;
            if (s_TessaLocks != 0) return;
            AudioListener.pause = s_SavedPause;
            AudioListener.volume = s_SavedVolume > 0f ? s_SavedVolume : 1f;
            Debug.Log("[AventoUnityAudioGate] Tessa inactive -> Unity audio restored");
        }

        public static void Reset()
        {
            s_TessaLocks = 0;
            AudioListener.pause = s_SavedPause;
            AudioListener.volume = s_SavedVolume > 0f ? s_SavedVolume : 1f;
        }
    }
}
