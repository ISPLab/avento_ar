#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Video;

namespace UnityEngine.XR.Templates.AR.Editor
{
    [CustomEditor(typeof(PanoramaSkyboxViewer))]
    public class PanoramaSkyboxViewerEditor : UnityEditor.Editor
    {
        SerializedProperty m_ContentMode;
        SerializedProperty m_PanoramaTexture;
        SerializedProperty m_VideoClip;
        SerializedProperty m_Loop;
        SerializedProperty m_PlayAudio;
        SerializedProperty m_AutoPlay;
        SerializedProperty m_StereoLayout;
        SerializedProperty m_SkyboxMaterial;
        SerializedProperty m_DomeMaterialTemplate;
        SerializedProperty m_HideSurfaceCoachingWhenActive;
        SerializedProperty m_LookControlMode;
        SerializedProperty m_HideArCameraBackground;
        SerializedProperty m_HideSimulationEnvironment;
        SerializedProperty m_DomeRadius;
        SerializedProperty m_LookSensitivity;
        SerializedProperty m_MinPitch;
        SerializedProperty m_MaxPitch;
        SerializedProperty m_LookTarget;
        SerializedProperty m_Exposure;
        SerializedProperty m_Opacity;
        SerializedProperty m_YawOffset;

        void OnEnable()
        {
            m_ContentMode = serializedObject.FindProperty("m_ContentMode");
            m_PanoramaTexture = serializedObject.FindProperty("m_PanoramaTexture");
            m_VideoClip = serializedObject.FindProperty("m_VideoClip");
            m_Loop = serializedObject.FindProperty("m_Loop");
            m_PlayAudio = serializedObject.FindProperty("m_PlayAudio");
            m_AutoPlay = serializedObject.FindProperty("m_AutoPlay");
            m_StereoLayout = serializedObject.FindProperty("m_StereoLayout");
            m_SkyboxMaterial = serializedObject.FindProperty("m_SkyboxMaterial");
            m_DomeMaterialTemplate = serializedObject.FindProperty("m_DomeMaterialTemplate");
            m_HideSurfaceCoachingWhenActive = serializedObject.FindProperty("m_HideSurfaceCoachingWhenActive");
            m_LookControlMode = serializedObject.FindProperty("m_LookControlMode");
            m_HideArCameraBackground = serializedObject.FindProperty("m_HideArCameraBackground");
            m_HideSimulationEnvironment = serializedObject.FindProperty("m_HideSimulationEnvironment");
            m_DomeRadius = serializedObject.FindProperty("m_DomeRadius");
            m_LookSensitivity = serializedObject.FindProperty("m_LookSensitivity");
            m_MinPitch = serializedObject.FindProperty("m_MinPitch");
            m_MaxPitch = serializedObject.FindProperty("m_MaxPitch");
            m_LookTarget = serializedObject.FindProperty("m_LookTarget");
            m_Exposure = serializedObject.FindProperty("m_Exposure");
            m_Opacity = serializedObject.FindProperty("m_Opacity");
            m_YawOffset = serializedObject.FindProperty("m_YawOffset");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var viewer = (PanoramaSkyboxViewer)target;
            DrawPrefabTargetHelp(viewer);

            if (!viewer.enabled)
            {
                EditorGUILayout.HelpBox(
                    "Panorama Skybox Viewer is disabled. Content Mode (Still Image / Video) is saved but will not show in Play Mode or on device until you enable this component.",
                    MessageType.Warning);
                if (GUILayout.Button("Enable component"))
                {
                    Undo.RecordObject(viewer, "Enable Panorama Skybox Viewer");
                    viewer.enabled = true;
                    EditorUtility.SetDirty(viewer);
                }

                EditorGUILayout.Space(6);
            }

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_ContentMode, new GUIContent("Content Mode", "Still Image (PNG) or Video (MP4) — only one is used."));

            var mode = (PanoramaSkyboxViewer.ContentMode)m_ContentMode.intValue;
            if (mode == PanoramaSkyboxViewer.ContentMode.StillImage)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Still Image (PNG)", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(m_PanoramaTexture, new GUIContent("Panorama Texture"));
                var tex = m_PanoramaTexture.objectReferenceValue as Texture2D;
                EditorGUILayout.HelpBox(
                    tex != null
                        ? $"Active: Still Image — {tex.name} ({tex.width}×{tex.height}). Rebuild AssetBundle to pack this PNG (video is omitted from the bundle)."
                        : "Assign a Panorama Texture. Video Clip is ignored in Still Image mode.",
                    tex != null ? MessageType.Info : MessageType.Warning);
            }
            else
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Video (MP4)", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(m_VideoClip, new GUIContent("Video Clip"));
                EditorGUILayout.PropertyField(m_Loop);
                EditorGUILayout.PropertyField(m_PlayAudio);
                EditorGUILayout.PropertyField(m_AutoPlay);
                EditorGUILayout.PropertyField(m_StereoLayout);
                var clip = m_VideoClip.objectReferenceValue as VideoClip;
                EditorGUILayout.HelpBox(
                    clip != null
                        ? $"Active: Video — {clip.name}. Rebuild AssetBundle to pack this MP4 (still PNG is omitted from the bundle)."
                        : "Assign a Video Clip. Panorama Texture is ignored in Video mode.",
                    clip != null ? MessageType.Info : MessageType.Warning);
            }

            var contentChanged = EditorGUI.EndChangeCheck();

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Shared", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_SkyboxMaterial);
            EditorGUILayout.PropertyField(m_DomeMaterialTemplate, new GUIContent("Dome Material"));
            EditorGUILayout.PropertyField(m_HideSurfaceCoachingWhenActive, new GUIContent("Hide Surface Coaching"));
            EditorGUILayout.PropertyField(m_Exposure);
            EditorGUILayout.Slider(
                m_Opacity,
                0f,
                1f,
                new GUIContent(
                    "Opacity",
                    "How solid the 360 dome is over the live camera. Default 0.95. Saved on the prefab and packed into the AssetBundle (0 = camera only, 1 = fully opaque)."));
            if (m_Opacity != null)
            {
                EditorGUILayout.HelpBox(
                    $"Dome opacity {m_Opacity.floatValue:0.###} ({m_Opacity.floatValue * 100f:0}%) is saved on this prefab and packed into the AssetBundle.",
                    MessageType.Info);
            }

            EditorGUILayout.PropertyField(m_YawOffset);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Dome / look", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_LookControlMode);
            EditorGUILayout.PropertyField(m_HideArCameraBackground);
            EditorGUILayout.PropertyField(m_HideSimulationEnvironment);
            EditorGUILayout.PropertyField(m_DomeRadius, new GUIContent("Dome Radius", "Keep larger than placed sprites so 360 stays in the background."));
            EditorGUILayout.PropertyField(m_LookSensitivity);
            EditorGUILayout.PropertyField(m_MinPitch);
            EditorGUILayout.PropertyField(m_MaxPitch);
            EditorGUILayout.PropertyField(m_LookTarget);
            var lookChanged = EditorGUI.EndChangeCheck();

            if (contentChanged || lookChanged)
            {
                serializedObject.ApplyModifiedProperties();
                MarkPanoramaTargetsDirty();

                if (Application.isPlaying)
                {
                    foreach (var t in targets)
                    {
                        var v = (PanoramaSkyboxViewer)t;
                        if (v == null || !v.enabled)
                            continue;
                        if (contentChanged)
                            v.RestartContentFromInspector();
                        else
                            v.ApplyLiveDomeParamsFromInspector();
                    }
                }
            }
            else
            {
                serializedObject.ApplyModifiedProperties();
            }
        }

        static void DrawPrefabTargetHelp(PanoramaSkyboxViewer viewer)
        {
            var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(viewer.gameObject);
            if (instanceRoot == null)
                return;

            var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(viewer);
            if (string.IsNullOrEmpty(prefabPath))
                return;

            EditorGUILayout.HelpBox(
                $"This is a scene/prefab instance. AssetBundle build packs the prefab asset:\n{prefabPath}\n" +
                "Apply overrides to that prefab or edit it directly, then rebuild the bundle.",
                MessageType.Info);
            if (GUILayout.Button("Apply instance overrides to prefab"))
            {
                PrefabUtility.ApplyPrefabInstance(instanceRoot, InteractionMode.UserAction);
                EditorUtility.SetDirty(AssetDatabase.LoadAssetAtPath<Object>(prefabPath));
            }

            EditorGUILayout.Space(6);
        }

        void MarkPanoramaTargetsDirty()
        {
            foreach (var t in targets)
            {
                if (t == null)
                    continue;
                EditorUtility.SetDirty(t);
                PrefabUtility.RecordPrefabInstancePropertyModifications(t);

                var component = t as Component;
                if (component == null)
                    continue;

                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null && stage.IsPartOfPrefabContents(component.gameObject))
                    EditorSceneManager.MarkSceneDirty(stage.scene);
            }
        }
    }
}
#endif
