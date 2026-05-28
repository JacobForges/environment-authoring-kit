#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    [CustomEditor(typeof(CaveBuildCursorSettings))]
    sealed class CaveBuildCursorSettingsEditor : UnityEditor.Editor
    {
        string _apiKeyEdit = "";

        public override void OnInspectorGUI()
        {
            var settings = (CaveBuildCursorSettings)target;
            settings.LoadFromPrefs();

            EditorGUILayout.LabelField("AI provider credentials (never committed)", EditorStyles.boldLabel);
            settings.aiProvider = (EnvironmentKitAiProvider)EditorGUILayout.EnumPopup("Active provider", settings.aiProvider);
            _apiKeyEdit = EditorGUILayout.PasswordField("API Key", _apiKeyEdit);
            if (GUILayout.Button("Save API Key to EditorPrefs"))
            {
                settings.SetApiKey(settings.aiProvider, _apiKeyEdit);
                _apiKeyEdit = string.Empty;
            }

            if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("CURSOR_API_KEY")))
                EditorGUILayout.HelpBox("CURSOR_API_KEY is set in environment (preferred).", MessageType.Info);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("GPU memory (laptop)", EditorStyles.boldLabel);
            if (GUILayout.Button("Apply MacBook Air M4 16GB budget"))
                EnvironmentKitHardwareBudget.ApplyMacBookAirPresetToSettings(settings);
            if (settings.hardwareBudget == EnvironmentKitHardwareBudget.Preset.MacBookAir16Gb)
            {
                EditorGUILayout.HelpBox(
                    "MacBook Air budget active: 257 terrain heightmap, 256m terrain, 512px textures, " +
                    "no reflection probes, asset unload between queue steps (more CPU, less GPU RAM).",
                    MessageType.Info);
            }

            EditorGUILayout.Space(8);
            DrawDefaultInspector();

            if (GUILayout.Button("Save Settings"))
                settings.SaveToPrefs();
        }
    }
}
#endif
