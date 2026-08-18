#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace UnityEngine.XR.Templates.AR.Editor
{
    [CustomEditor(typeof(AventoObjectInteract))]
    public class AventoObjectInteractEditor : UnityEditor.Editor
    {
        SerializedProperty m_ObjectId;
        SerializedProperty m_DisplayName;
        SerializedProperty m_Prompt;
        SerializedProperty m_PromptByLanguage;
        SerializedProperty m_SpeechMode;
        SerializedProperty m_TriggerMode;
        SerializedProperty m_ProximityRadiusMeters;
        SerializedProperty m_ProximityExitMeters;
        SerializedProperty m_FireOnce;
        SerializedProperty m_CooldownSeconds;
        SerializedProperty m_RequireLineOfSight;
        SerializedProperty m_RequireFacingUser;
        SerializedProperty m_VoiceNameOverride;
        SerializedProperty m_SsmlGenderHint;

        string m_LangFilter = "en-US";

        void OnEnable()
        {
            m_ObjectId = serializedObject.FindProperty("m_ObjectId");
            m_DisplayName = serializedObject.FindProperty("m_DisplayName");
            m_Prompt = serializedObject.FindProperty("m_Prompt");
            m_PromptByLanguage = serializedObject.FindProperty("m_PromptByLanguage");
            m_SpeechMode = serializedObject.FindProperty("m_SpeechMode");
            m_TriggerMode = serializedObject.FindProperty("m_TriggerMode");
            m_ProximityRadiusMeters = serializedObject.FindProperty("m_ProximityRadiusMeters");
            m_ProximityExitMeters = serializedObject.FindProperty("m_ProximityExitMeters");
            m_FireOnce = serializedObject.FindProperty("m_FireOnce");
            m_CooldownSeconds = serializedObject.FindProperty("m_CooldownSeconds");
            m_RequireLineOfSight = serializedObject.FindProperty("m_RequireLineOfSight");
            m_RequireFacingUser = serializedObject.FindProperty("m_RequireFacingUser");
            m_VoiceNameOverride = serializedObject.FindProperty("m_VoiceNameOverride");
            m_SsmlGenderHint = serializedObject.FindProperty("m_SsmlGenderHint");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(m_ObjectId);
            EditorGUILayout.PropertyField(m_DisplayName);
            EditorGUILayout.PropertyField(m_Prompt);
            EditorGUILayout.PropertyField(m_PromptByLanguage, true);
            EditorGUILayout.PropertyField(m_SpeechMode);
            EditorGUILayout.PropertyField(m_TriggerMode);
            EditorGUILayout.PropertyField(m_ProximityRadiusMeters);
            EditorGUILayout.PropertyField(m_ProximityExitMeters);
            EditorGUILayout.PropertyField(m_FireOnce);
            EditorGUILayout.PropertyField(m_CooldownSeconds);
            EditorGUILayout.PropertyField(m_RequireLineOfSight);
            EditorGUILayout.PropertyField(m_RequireFacingUser);
            EditorGUILayout.PropertyField(m_SsmlGenderHint);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Google TTS voice (voices.json)", EditorStyles.boldLabel);
            var langs = AventoVoiceCatalog.DistinctLanguageCodes();
            if (langs.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    $"Add {AventoVoiceCatalog.AssetPath} (Google TTS catalog). Empty voice = app picks from user language.",
                    MessageType.Info);
                EditorGUILayout.PropertyField(m_VoiceNameOverride);
            }
            else
            {
                var langIndex = System.Array.FindIndex(langs, l => l == m_LangFilter);
                if (langIndex < 0)
                    langIndex = 0;
                langIndex = EditorGUILayout.Popup("Catalog language", langIndex, langs);
                m_LangFilter = langs[langIndex];

                var voices = AventoVoiceCatalog.VoicesForLanguage(m_LangFilter);
                var names = new string[voices.Count + 1];
                names[0] = "(App default)";
                for (var i = 0; i < voices.Count; i++)
                {
                    var g = string.IsNullOrEmpty(voices[i].ssmlGender) ? "" : $" [{voices[i].ssmlGender}]";
                    names[i + 1] = voices[i].name + g;
                }

                var current = m_VoiceNameOverride.stringValue ?? "";
                var selected = 0;
                for (var i = 0; i < voices.Count; i++)
                {
                    if (voices[i].name == current)
                    {
                        selected = i + 1;
                        break;
                    }
                }

                var next = EditorGUILayout.Popup("Voice", selected, names);
                m_VoiceNameOverride.stringValue = next <= 0 ? "" : voices[next - 1].name;

                if (!string.IsNullOrEmpty(current) && selected == 0)
                {
                    EditorGUILayout.HelpBox(
                        $"Current voice '{current}' is not in {m_LangFilter}. Pick one above or leave default.",
                        MessageType.Warning);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
