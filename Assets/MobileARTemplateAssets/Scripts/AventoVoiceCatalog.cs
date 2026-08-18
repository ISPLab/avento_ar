using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Google Cloud TTS catalog from <c>Assets/voices.json</c> (same shape as
    /// avento-app/public/voices/voices.json). Used by the Inspector picker;
    /// runtime speech still happens in avento-app.
    /// </summary>
    public static class AventoVoiceCatalog
    {
        public const string AssetPath = "Assets/voices.json";

        [Serializable]
        public class VoiceEntry
        {
            public string[] languageCodes;
            public string name;
            public string ssmlGender;
            public int naturalSampleRateHertz;
        }

        [Serializable]
        class VoicesFile
        {
            public VoiceEntry[] voices;
        }

        static VoiceEntry[] s_Voices;
        static bool s_Loaded;

        public static VoiceEntry[] Voices
        {
            get
            {
                EnsureLoaded();
                return s_Voices ?? Array.Empty<VoiceEntry>();
            }
        }

        public static void EnsureLoaded()
        {
            if (s_Loaded)
                return;
            s_Loaded = true;
            s_Voices = LoadFromDisk();
        }

        public static bool ContainsName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            var voices = Voices;
            for (var i = 0; i < voices.Length; i++)
            {
                if (string.Equals(voices[i].name, name, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        public static string[] DistinctLanguageCodes()
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var voices = Voices;
            for (var i = 0; i < voices.Length; i++)
            {
                var codes = voices[i].languageCodes;
                if (codes == null)
                    continue;
                for (var c = 0; c < codes.Length; c++)
                {
                    if (!string.IsNullOrWhiteSpace(codes[c]))
                        set.Add(codes[c]);
                }
            }

            var arr = new string[set.Count];
            set.CopyTo(arr);
            return arr;
        }

        public static List<VoiceEntry> VoicesForLanguage(string languageCode)
        {
            var list = new List<VoiceEntry>();
            if (string.IsNullOrWhiteSpace(languageCode))
                return list;
            var want = languageCode.Trim();
            var shortWant = want.Split('-')[0];
            var voices = Voices;
            for (var i = 0; i < voices.Length; i++)
            {
                var v = voices[i];
                if (v.languageCodes == null)
                    continue;
                for (var c = 0; c < v.languageCodes.Length; c++)
                {
                    var lc = v.languageCodes[c];
                    if (string.IsNullOrEmpty(lc))
                        continue;
                    if (lc.Equals(want, StringComparison.OrdinalIgnoreCase) ||
                        lc.StartsWith(shortWant + "-", StringComparison.OrdinalIgnoreCase) ||
                        lc.Equals(shortWant, StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add(v);
                        break;
                    }
                }
            }

            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list;
        }

        static VoiceEntry[] LoadFromDisk()
        {
            string json = null;
#if UNITY_EDITOR
            var diskPath = System.IO.Path.Combine(Application.dataPath, "voices.json");
            if (System.IO.File.Exists(diskPath))
            {
                try
                {
                    json = System.IO.File.ReadAllText(diskPath);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AventoVoiceCatalog] Read failed: {ex.Message}");
                }
            }
#endif
            if (string.IsNullOrEmpty(json))
            {
                var fromResources = Resources.Load<TextAsset>("voices");
                if (fromResources != null)
                    json = fromResources.text;
            }

            if (string.IsNullOrEmpty(json))
            {
                Debug.LogWarning($"[AventoVoiceCatalog] Missing {AssetPath}");
                return Array.Empty<VoiceEntry>();
            }

            try
            {
                var file = JsonUtility.FromJson<VoicesFile>(json);
                return file?.voices ?? Array.Empty<VoiceEntry>();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AventoVoiceCatalog] Parse failed: {ex.Message}");
                return Array.Empty<VoiceEntry>();
            }
        }
    }
}
