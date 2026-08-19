using System;
using System.Text;
using UnityEngine;

namespace UnityEngine.XR.Templates.AR
{
    public enum AventoSpeechMode
    {
        Tts = 0,
        Tessa = 1,
        TtsThenTessa = 2,
        /// <summary>Show title + prompt in Unity. Does not start TTS or Tessa.</summary>
        Caption = 3,
    }

    public enum AventoInteractTriggerMode
    {
        Tap = 0,
        Proximity = 1,
        Both = 2,
    }

    public enum AventoSsmlGenderHint
    {
        Unspecified = 0,
        Female = 1,
        Male = 2,
        Neutral = 3,
    }

    [Serializable]
    public class AventoPromptByLanguage
    {
        public string lang;
        [TextArea(2, 6)]
        public string text;
    }

    public static class AventoInteractJson
    {
        public static string Build(
            string trigger,
            string objectId,
            string title,
            string prompt,
            AventoPromptByLanguage[] promptByLanguage,
            AventoSpeechMode speechMode,
            string voiceName,
            AventoSsmlGenderHint gender)
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"type\":\"object_interact\"");
            Append(sb, "trigger", trigger);
            Append(sb, "objectId", objectId ?? "");
            Append(sb, "title", title ?? "");
            Append(sb, "prompt", prompt ?? "");
            sb.Append(",\"promptByLanguage\":{");
            var first = true;
            if (promptByLanguage != null)
            {
                for (var i = 0; i < promptByLanguage.Length; i++)
                {
                    var row = promptByLanguage[i];
                    if (row == null || string.IsNullOrWhiteSpace(row.lang))
                        continue;
                    if (!first)
                        sb.Append(',');
                    first = false;
                    sb.Append('"').Append(Escape(row.lang.Trim().ToLowerInvariant())).Append("\":\"");
                    sb.Append(Escape(row.text ?? "")).Append('"');
                }
            }

            sb.Append('}');
            Append(sb, "speechMode", SpeechModeWire(speechMode));
            Append(sb, "voiceName", voiceName ?? "");
            if (gender != AventoSsmlGenderHint.Unspecified)
                Append(sb, "ssmlGender", gender.ToString().ToUpperInvariant());
            sb.Append('}');
            return sb.ToString();
        }

        static void Append(StringBuilder sb, string key, string value)
        {
            sb.Append(",\"").Append(key).Append("\":\"").Append(Escape(value ?? "")).Append('"');
        }

        public static string SpeechModeWire(AventoSpeechMode mode)
        {
            switch (mode)
            {
                case AventoSpeechMode.Tessa:
                    return "tessa";
                case AventoSpeechMode.TtsThenTessa:
                    return "tts_then_tessa";
                case AventoSpeechMode.Caption:
                    return "caption";
                default:
                    return "tts";
            }
        }

        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }
}
