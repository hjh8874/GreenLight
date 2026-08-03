using CityFlow.Content;
using UnityEditor;
using UnityEngine;

namespace CityFlow.EditorTools
{
    [CustomEditor(typeof(EmergencyIncidentSystem))]
    public sealed class EmergencyIncidentSystemEditor :
        UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(
                       !Application.isPlaying))
            {
                if (GUILayout.Button(
                        "Create Test Incident Now"))
                {
                    ((EmergencyIncidentSystem)target)
                        .TryCreateTestIncidentNow();
                }
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play Mode to create an immediate test incident.",
                    MessageType.Info);
            }
        }
    }
}
