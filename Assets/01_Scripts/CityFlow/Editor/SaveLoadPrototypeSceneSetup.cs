using CityFlow.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CityFlow.EditorTools
{
    public static class SaveLoadPrototypeSceneSetup
    {
        private const string ScenePath = "Assets/00_Scenes/UI_Proto_SaveTest.unity";
        private const string PanelName = "SaveLoad_TestPanel";

        [MenuItem("CityFlow/Setup SaveLoad Panel")]
        public static void Setup()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject parent = FindByName("Setting_Panel ") ?? FindByName("Setting_Panel") ?? FindByName("Setting");

            if (parent == null)
            {
                Debug.LogWarning("SaveLoad_TestPanel setup skipped because Setting parent was not found.");
                return;
            }

            GameObject panel = FindChild(parent.transform, PanelName) ?? CreatePanel(parent.transform);
            SaveLoadPrototypePanel controller = panel.GetComponent<SaveLoadPrototypePanel>()
                ?? panel.AddComponent<SaveLoadPrototypePanel>();

            Button saveButton = FindChild(panel.transform, "SaveButton")?.GetComponent<Button>()
                ?? CreateButton(panel.transform, "SaveButton", "Save");

            Button loadButton = FindChild(panel.transform, "LoadButton")?.GetComponent<Button>()
                ?? CreateButton(panel.transform, "LoadButton", "Load");

            TMP_Text statusText = FindChild(panel.transform, "StatusText")?.GetComponent<TMP_Text>()
                ?? CreateStatusText(panel.transform);

            SerializedObject serializedController = new SerializedObject(controller);
            serializedController.FindProperty("saveButton").objectReferenceValue = saveButton;
            serializedController.FindProperty("loadButton").objectReferenceValue = loadButton;
            serializedController.FindProperty("statusText").objectReferenceValue = statusText;
            serializedController.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("SaveLoad_TestPanel setup completed.");
        }

        private static GameObject CreatePanel(Transform parent)
        {
            GameObject panel = new GameObject(PanelName, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(parent, false);

            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -120f);
            rect.sizeDelta = new Vector2(260f, 150f);

            Image image = panel.GetComponent<Image>();
            image.color = new Color(0.08f, 0.09f, 0.1f, 0.92f);

            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 14, 14);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            return panel;
        }

        private static Button CreateButton(Transform parent, string name, string label)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(220f, 36f);

            LayoutElement layout = buttonObject.GetComponent<LayoutElement>();
            layout.preferredHeight = 36f;

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.18f, 0.28f, 0.32f, 1f);

            GameObject textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(buttonObject.transform, false);

            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 18f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;

            return buttonObject.GetComponent<Button>();
        }

        private static TMP_Text CreateStatusText(Transform parent)
        {
            GameObject textObject = new GameObject("StatusText", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            textObject.transform.SetParent(parent, false);

            LayoutElement layout = textObject.GetComponent<LayoutElement>();
            layout.preferredHeight = 38f;

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = "Waiting for SimEngine : ISimSaveSource.";
            text.fontSize = 13f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.82f, 0.88f, 0.9f, 1f);

            return text;
        }

        private static GameObject FindByName(string name)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                GameObject found = FindChild(root.transform, name);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static GameObject FindChild(Transform parent, string name)
        {
            if (parent.name == name)
            {
                return parent.gameObject;
            }

            foreach (Transform child in parent)
            {
                GameObject found = FindChild(child, name);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
