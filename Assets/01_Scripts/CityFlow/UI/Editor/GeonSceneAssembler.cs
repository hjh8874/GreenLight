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

            // Create Demolish button inside Infra_Panel
            Transform demolishBtnTransform = infraPanel.transform.Find("Btn_Demolish");
            if (demolishBtnTransform == null)
            {
                GameObject demoBtnGO = new GameObject("Btn_Demolish", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                demoBtnGO.transform.SetParent(infraPanel.transform, false);
                var rect = demoBtnGO.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(1, 0.5f);
                rect.anchorMax = new Vector2(1, 0.5f);
                rect.pivot = new Vector2(1, 0.5f);
                rect.anchoredPosition = new Vector2(-60, 0); // Position it on the right side
                rect.sizeDelta = new Vector2(100, 60);

                var img = demoBtnGO.GetComponent<Image>();
                img.color = Color.red;

                GameObject textGO = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                textGO.transform.SetParent(demoBtnGO.transform, false);
                var txt = textGO.GetComponent<TextMeshProUGUI>();
                txt.text = "Demolish";
                txt.alignment = TextAlignmentOptions.Center;
                txt.color = Color.white;
                txt.fontSize = 18;

                var btn = demoBtnGO.GetComponent<Button>();
                
                var coordinator = Object.FindFirstObjectByType<InfrastructurePlacementCoordinator>(FindObjectsInactive.Include);
                if (coordinator != null)
                {
                    UnityEditor.Events.UnityEventTools.AddPersistentListener(btn.onClick, coordinator.StartDemolishMode);
                }
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
