using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
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

            // Find the Dock Right panel to add our button to
            GameObject dockRight = FindObjectIncludingInactive("Dock_Right");
            if (dockRight == null)
            {
                Debug.LogError("Error: Dock_Right not found.");
                return;
            }

            // Create btnInfra by duplicating btnBuild if btnInfra doesn't exist yet
            Transform btnInfraTransform = dockRight.transform.Find("Btn_Infra");
            Button btnInfra = null;
            if (btnInfraTransform == null)
            {
                // Try finding any button inside dockRight
                Button existingBtn = dockRight.GetComponentInChildren<Button>();
                if (existingBtn == null)
                {
                    Debug.LogError("Error: No existing buttons found in Dock_Right to duplicate.");
                    return;
                }
                
                GameObject btnInfraGO = Object.Instantiate(existingBtn.gameObject, dockRight.transform);
                btnInfraGO.name = "Btn_Infra";
                btnInfra = btnInfraGO.GetComponent<Button>();
                
                var text = btnInfraGO.GetComponentInChildren<TMP_Text>();
                if (text != null) text.text = "Infra";
            }
            else
            {
                btnInfra = btnInfraTransform.GetComponent<Button>();
            }

            // Add btnInfra and panelInfra to UIDockController
            var serializedObject = new SerializedObject(uidock);
            serializedObject.FindProperty("btnInfra").objectReferenceValue = btnInfra;
            serializedObject.FindProperty("panelInfra").objectReferenceValue = infraPanel;
            serializedObject.ApplyModifiedProperties();

            // Cleanup: Remove legacy Btn_Demolish if it exists in scene
            Transform demolishBtnTransform = infraPanel.transform.Find("Btn_Demolish");
            if (demolishBtnTransform != null)
            {
                Object.DestroyImmediate(demolishBtnTransform.gameObject);
                Debug.Log("[CityFlow] Legacy Btn_Demolish removed from Infra_Panel.");
            }

            // Make sure Infra_Panel starts inactive so toggle works correctly
            infraPanel.SetActive(false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            
            Debug.Log("[CityFlow] Infra_Panel toggle and Demolish button perfectly assembled into the scene!");
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
