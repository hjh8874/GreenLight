using UnityEditor;
using UnityEngine;

namespace CityFlow.Gameplay.Progression.Editor
{
    [CustomEditor(typeof(GameTimeSettingsSO))]
    public sealed class GameTimeSettingsSOEditor : UnityEditor.Editor
    {
        private SerializedProperty realMinutesPerGameDay;

        private void OnEnable()
        {
            realMinutesPerGameDay = serializedObject.FindProperty(
                "realMinutesPerGameDay");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(realMinutesPerGameDay);
            serializedObject.ApplyModifiedProperties();

            GameTimeSettingsSO settings =
                (GameTimeSettingsSO)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Calculated Pace At x1",
                EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.FloatField(
                    "Seconds Per Game Hour",
                    settings.RealSecondsPerGameHour);
                EditorGUILayout.FloatField(
                    "Minutes Per Game Day",
                    settings.RealMinutesPerGameDay);
                EditorGUILayout.FloatField(
                    "Minutes Per Game Week",
                    settings.RealMinutesPerGameDay * 7f);
                EditorGUILayout.FloatField(
                    "Hours Per Game Month",
                    settings.RealMinutesPerGameDay * 30f / 60f);
                EditorGUILayout.FloatField(
                    "Hours Per Game Year",
                    settings.RealMinutesPerGameDay * 360f / 60f);
            }

            EditorGUILayout.HelpBox(
                "Restart Play Mode after changing this asset so every " +
                "calendar consumer starts with the same pace.",
                MessageType.Info);
        }

        // Unity setup: This Inspector is used automatically for GameTimeSettingsSO assets.
    }
}
