using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using CityFlow.UI;
using CityFlow.UI.Controllers;

namespace CityFlow.UI.Editor
{
    public static class GeonSceneAssembler
    {
        [MenuItem("CityFlow/UI/Assemble Infra Shop")]
        public static void Assemble()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != "CityFlowIntegrated_Geon")
            {
                Debug.LogError("Error: Please open the CityFlowIntegrated_Geon scene first.");
                return;
            }

            var uidock = Object.FindFirstObjectByType<UIDockController>();
            if (uidock == null)
            {
                Debug.LogError("Error: UIDockController not found.");
                return;
            }

            // Find Infra_Panel (works even if inactive)
            GameObject infraPanel = FindObjectIncludingInactive("Infra_Panel");
            if (infraPanel == null)
            {
                Debug.LogError("Error: Infra_Panel not found in scene (even among inactive objects). Check the exact name.");
                return;
            }

            // Cleanup: Remove legacy Btn_Demolish if it exists in scene
            Transform demolishBtnTransform = infraPanel.transform.Find("Btn_Demolish");
            if (demolishBtnTransform != null)
            {
                Object.DestroyImmediate(demolishBtnTransform.gameObject);
                Debug.Log("[CityFlow] Legacy Btn_Demolish removed from Infra_Panel.");
            }

            // Infra_Panel is controlled by the first category tab inside Build_Panel.
            infraPanel.SetActive(false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            
            Debug.Log("[CityFlow] Infra_Panel assembled as a Build category and legacy Demolish button removed.");
        }

        private static GameObject FindObjectIncludingInactive(string name)
        {
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                var children = root.GetComponentsInChildren<Transform>(true);
                foreach (var child in children)
                {
                    if (child.name == name) return child.gameObject;
                }
            }
            return null;
        }
    }
}
