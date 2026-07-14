using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor.SceneManagement;
using CityFlow.UI;

namespace CityFlow.UI.Editor
{
    public static class GeonSignalUIAssembler
    {
        [MenuItem("CityFlow/UI/Assemble Signal Control UI")]
        public static void Assemble()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != "CityFlowIntegrated_Geon")
            {
                Debug.LogError("Error: Please open the CityFlowIntegrated_Geon scene first.");
                return;
            }

            GameObject analysisCardObj = FindObjectIncludingInactive("AnalysisCard_BottomLeft");
            if (analysisCardObj == null)
            {
                Debug.LogError("Error: AnalysisCard_BottomLeft not found in scene.");
                return;
            }

            AnalysisCardController controller = analysisCardObj.GetComponent<AnalysisCardController>();
            if (controller == null)
            {
                Debug.LogError("Error: AnalysisCardController not found on AnalysisCard_BottomLeft.");
                return;
            }

            Transform normalContainer = analysisCardObj.transform.Find("NormalInfoContainer");
            if (normalContainer == null)
            {
                GameObject normalObj = new GameObject("NormalInfoContainer", typeof(RectTransform));
                normalObj.transform.SetParent(analysisCardObj.transform, false);
                var rect = normalObj.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.sizeDelta = Vector2.zero;
                rect.anchoredPosition = Vector2.zero;
                normalContainer = normalObj.transform;

                // Move existing children to NormalInfoContainer
                int childCount = analysisCardObj.transform.childCount;
                for (int i = childCount - 1; i >= 0; i--)
                {
                    Transform child = analysisCardObj.transform.GetChild(i);
                    if (child != normalContainer)
                    {
                        child.SetParent(normalContainer, false);
                    }
                }
            }

            Transform signalContainer = analysisCardObj.transform.Find("SignalControlContainer");
            if (signalContainer != null)
            {
                Object.DestroyImmediate(signalContainer.gameObject);
            }

            GameObject signalObj = new GameObject("SignalControlContainer", typeof(RectTransform));
            signalObj.transform.SetParent(analysisCardObj.transform, false);
            var signalRect = signalObj.GetComponent<RectTransform>();
            signalRect.anchorMin = Vector2.zero;
            signalRect.anchorMax = Vector2.one;
            signalRect.sizeDelta = Vector2.zero;
            signalRect.anchoredPosition = Vector2.zero;
            signalContainer = signalObj.transform;

            // Extract Font from existing UI
            TMP_FontAsset fontToUse = null;
            var existingText = normalContainer.GetComponentInChildren<TextMeshProUGUI>(true);
            if (existingText != null) fontToUse = existingText.font;

            // Create Sliders and Buttons inside SignalControlContainer
            CreateLabel(signalContainer, "LblOffset", "Offset", new Vector2(-120, 40), fontToUse);
            Slider sliderOffset = CreateSlider(signalContainer, "SliderOffset", new Vector2(20, 40));
            
            CreateLabel(signalContainer, "LblGreen", "Green", new Vector2(-120, -10), fontToUse);
            Slider sliderGreen = CreateSlider(signalContainer, "SliderGreen", new Vector2(20, -10));
            
            Button btnH = CreateButton(signalContainer, "BtnOverrideH", "가로 ➔", new Vector2(-60, -70), fontToUse);
            Button btnV = CreateButton(signalContainer, "BtnOverrideV", "세로 ⬆", new Vector2(60, -70), fontToUse);

            var serializedObject = new SerializedObject(controller);
            
            // Try to auto-link the Title Text component if missing
            TextMeshProUGUI titleText = null;
            foreach (var txt in analysisCardObj.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (txt.name.IndexOf("Title", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    txt.name.IndexOf("Header", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    titleText = txt;
                    break;
                }
            }
            if (titleText != null)
            {
                serializedObject.FindProperty("txtTitle").objectReferenceValue = titleText;
            }

            serializedObject.FindProperty("normalInfoContainer").objectReferenceValue = normalContainer.gameObject;
            serializedObject.FindProperty("signalControlContainer").objectReferenceValue = signalObj;
            serializedObject.FindProperty("sliderOffset").objectReferenceValue = sliderOffset;
            serializedObject.FindProperty("sliderGreen").objectReferenceValue = sliderGreen;
            serializedObject.FindProperty("btnOverrideH").objectReferenceValue = btnH;
            serializedObject.FindProperty("btnOverrideV").objectReferenceValue = btnV;
            serializedObject.ApplyModifiedProperties();
            
            // Set inactive AT THE END so meshes build properly
            signalObj.SetActive(false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[CityFlow] Signal Control UI assembled successfully!");
        }

        private static Slider CreateSlider(Transform parent, string name, Vector2 pos)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Slider));
            obj.transform.SetParent(parent, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = new Vector2(200, 20);
            
            // Basic UI Setup for Slider
            Sprite bgSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            Sprite handleSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            GameObject bgObj = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bgObj.transform.SetParent(obj.transform, false);
            bgObj.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0.25f);
            bgObj.GetComponent<RectTransform>().anchorMax = new Vector2(1, 0.75f);
            var bgImg = bgObj.GetComponent<Image>();
            bgImg.sprite = bgSprite;
            bgImg.type = Image.Type.Sliced;
            bgImg.color = Color.gray;

            GameObject fillAreaObj = new GameObject("Fill Area", typeof(RectTransform));
            fillAreaObj.transform.SetParent(obj.transform, false);
            var fillAreaRect = fillAreaObj.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0, 0.25f);
            fillAreaRect.anchorMax = new Vector2(1, 0.75f);
            fillAreaRect.sizeDelta = new Vector2(-20, 0);

            GameObject fillObj = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fillObj.transform.SetParent(fillAreaObj.transform, false);
            var fillRect = fillObj.GetComponent<RectTransform>();
            fillRect.sizeDelta = Vector2.zero;
            var fillImg = fillObj.GetComponent<Image>();
            fillImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            fillImg.type = Image.Type.Sliced;
            fillImg.color = Color.green;

            GameObject handleAreaObj = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleAreaObj.transform.SetParent(obj.transform, false);
            var handleAreaRect = handleAreaObj.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.sizeDelta = new Vector2(-20, 0);

            GameObject handleObj = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            handleObj.transform.SetParent(handleAreaObj.transform, false);
            handleObj.GetComponent<RectTransform>().sizeDelta = new Vector2(20, 0);
            var handleImg = handleObj.GetComponent<Image>();
            handleImg.sprite = handleSprite;
            handleImg.color = Color.white;

            var slider = obj.GetComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleObj.GetComponent<RectTransform>();
            slider.targetGraphic = handleObj.GetComponent<Image>();
            slider.wholeNumbers = true;
            
            return slider;
        }

        private static void CreateLabel(Transform parent, string name, string text, Vector2 pos, TMP_FontAsset font)
        {
            GameObject textObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(parent, false);
            var textRect = textObj.GetComponent<RectTransform>();
            textRect.anchoredPosition = pos;
            textRect.sizeDelta = new Vector2(80, 20);
            
            var txt = textObj.GetComponent<TextMeshProUGUI>();
            txt.text = text;
            txt.color = Color.white;
            txt.alignment = TextAlignmentOptions.Right;
            txt.fontSize = 16;
            if (font != null) txt.font = font;
        }

        private static Button CreateButton(Transform parent, string name, string text, Vector2 pos, TMP_FontAsset font)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            obj.transform.SetParent(parent, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = new Vector2(100, 40);
            
            var img = obj.GetComponent<Image>();
            img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            img.type = Image.Type.Sliced;
            img.color = new Color(0.2f, 0.6f, 0.8f);
            
            GameObject textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(obj.transform, false);
            var textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            
            var txt = textObj.GetComponent<TextMeshProUGUI>();
            txt.text = text;
            txt.color = Color.white;
            txt.alignment = TextAlignmentOptions.Center;
            txt.fontSize = 18;
            if (font != null) txt.font = font;
            
            return obj.GetComponent<Button>();
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
