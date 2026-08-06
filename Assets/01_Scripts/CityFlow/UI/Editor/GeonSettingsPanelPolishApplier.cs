using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI.Editor
{
    public static class GeonSettingsPanelPolishApplier
    {
        private const string PanelPath =
            "UI_MainCanvas/FloatingWindowContentRoot/SubPanels_Right/Setting_Panel ";

        [MenuItem("CityFlow/UI/Geon/Apply Settings Panel Polish")]
        public static void Apply()
        {
            Transform panel = LayerLabUiAssetCatalog.FindInGeonScene(PanelPath);
            if (panel == null)
            {
                return;
            }

            SettingsPanelController controller =
                panel.GetComponent<SettingsPanelController>();
            if (controller == null)
            {
                Debug.LogError(
                    "[GeonSettingsPanelPolishApplier] " +
                    "SettingsPanelController is missing.");
                return;
            }

            GameObject audioSettingsPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    LayerLabUiAssetCatalog.AudioSettingsPrefabPath);
            if (audioSettingsPrefab == null)
            {
                Debug.LogError(
                    "[GeonSettingsPanelPolishApplier] Audio settings prefab " +
                    "is missing. Run its baker first.");
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(
                panel.gameObject,
                "Apply Geon Settings Panel Polish");

            StylePanel(panel);
            EnsureTitle(panel);
            RemoveLegacyAudioControls(panel);
            EnsureAudioSettings(panel, audioSettingsPrefab);
            EnsureCongestionToggle(panel);
            Button quitButton = StyleQuitButton(panel);
            BindController(controller, quitButton);

            LayerLabUiAssetCatalog.CompleteSceneChange(
                panel.gameObject,
                nameof(GeonSettingsPanelPolishApplier));
        }

        private static void StylePanel(Transform panel)
        {
            RectTransform rect = panel as RectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-180f, 0f);
            rect.offsetMax = Vector2.zero;

            Image background =
                LayerLabUiAssetCatalog.GetOrAddComponent<Image>(panel.gameObject);
            LayerLabUiAssetCatalog.ApplyImage(
                background,
                LayerLabUiAssetCatalog.LoadSprite(
                    "Frame/Frame_ListFrame01_White1.png"),
                new Color(0.1f, 0.12f, 0.16f, 0.98f));
            background.raycastTarget = true;
        }

        private static void EnsureTitle(Transform panel)
        {
            TMP_Text title = EnsureText(panel, "Title", "설정");
            SetTopRect(title.rectTransform, new Vector2(0f, -19f),
                new Vector2(330f, 30f));
            title.alignment = TextAlignmentOptions.Center;
            LayerLabUiAssetCatalog.StyleText(
                title,
                22f,
                new Color(1f, 0.84f, 0.3f, 1f),
                FontStyles.Bold);
        }

        private static void RemoveLegacyAudioControls(Transform panel)
        {
            RemoveChild(panel, "Sound");
            RemoveChild(panel, "BGM_Group");
            RemoveChild(panel, "SFX_Group");
        }

        private static void RemoveChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null)
            {
                Undo.DestroyObjectImmediate(child.gameObject);
            }
        }

        private static void EnsureAudioSettings(
            Transform panel,
            GameObject prefab)
        {
            Transform existing = panel.Find("UI_AudioSettings");
            GameObject instance = existing != null
                ? existing.gameObject
                : null;
            if (instance != null &&
                PrefabUtility.GetCorrespondingObjectFromSource(instance) !=
                prefab)
            {
                Undo.DestroyObjectImmediate(instance);
                instance = null;
            }

            if (instance == null)
            {
                instance = PrefabUtility.InstantiatePrefab(
                    prefab,
                    panel) as GameObject;
                if (instance == null)
                {
                    Debug.LogError(
                        "[GeonSettingsPanelPolishApplier] Failed to " +
                        "instantiate UI_AudioSettings.");
                    return;
                }

                Undo.RegisterCreatedObjectUndo(
                    instance,
                    "Create Audio Settings Panel");
            }

            SetTopRect(
                instance.transform as RectTransform,
                new Vector2(0f, -39f),
                new Vector2(330f, 126f));
        }

        private static void EnsureCongestionToggle(Transform panel)
        {
            Transform existing = panel.Find("CongestionViewToggle");
            GameObject instance = existing != null
                ? existing.gameObject
                : InstantiateCongestionToggle(panel);
            if (instance == null)
            {
                return;
            }

            instance.name = "CongestionViewToggle";
            SetTopRect(
                instance.transform as RectTransform,
                new Vector2(0f, -181f),
                new Vector2(320f, 34f));
            LayerLabUiAssetCatalog.StyleToggle(
                instance.GetComponent<Toggle>(),
                "실시간 정체");
        }

        private static GameObject InstantiateCongestionToggle(Transform parent)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                LayerLabUiAssetCatalog.CongestionTogglePrefabPath);
            if (prefab == null)
            {
                Debug.LogError(
                    "[GeonSettingsPanelPolishApplier] Congestion toggle prefab " +
                    "is missing. Run its baker first.");
                return null;
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(
                prefab,
                parent) as GameObject;
            if (instance != null)
            {
                Undo.RegisterCreatedObjectUndo(
                    instance,
                    "Create Congestion View Toggle");
            }

            return instance;
        }

        private static Button StyleQuitButton(Transform panel)
        {
            Transform target = panel.Find("EndButton");
            Button button = target != null ? target.GetComponent<Button>() : null;
            if (button == null)
            {
                Debug.LogError(
                    "[GeonSettingsPanelPolishApplier] Legacy EndButton is missing.");
                return null;
            }

            SetTopRect(
                button.transform as RectTransform,
                new Vector2(0f, -224f),
                new Vector2(180f, 30f));
            LayerLabUiAssetCatalog.StyleButton(
                button,
                LayerLabUiAssetCatalog.LoadSprite(
                    "Button/Btn_Rectangle02_Dark.png"),
                "게임 종료");
            return button;
        }

        private static void BindController(
            SettingsPanelController controller,
            Button quitButton)
        {
            Undo.RecordObject(controller, "Bind Settings Panel Controls");
            SerializedObject serialized = new SerializedObject(controller);
            SerializedProperty property =
                serialized.FindProperty("btnQuitGame");
            if (property == null)
            {
                Debug.LogError(
                    "[GeonSettingsPanelPolishApplier] Missing property: " +
                    "btnQuitGame");
                return;
            }

            property.objectReferenceValue = quitButton;
            serialized.ApplyModifiedProperties();
        }

        private static TMP_Text EnsureText(
            Transform parent,
            string name,
            string initialText)
        {
            Transform existing = parent.Find(name);
            GameObject target = existing != null
                ? existing.gameObject
                : CreateRectObject(parent, name);
            TextMeshProUGUI text =
                LayerLabUiAssetCatalog.GetOrAddComponent<TextMeshProUGUI>(target);
            if (existing == null)
            {
                text.text = initialText;
            }

            return text;
        }

        private static GameObject CreateRectObject(Transform parent, string name)
        {
            GameObject target = new GameObject(name, typeof(RectTransform));
            target.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(target, $"Create {name}");
            return target;
        }

        private static void SetTopRect(
            RectTransform rect,
            Vector2 position,
            Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
