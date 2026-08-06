using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI.Editor
{
    public static class GeonDockRightPolishApplier
    {
        private const string DockPath =
            "UI_MainCanvas/FloatingWindowContentRoot/Dock_Right";
        private const string SubPanelsPath =
            "UI_MainCanvas/FloatingWindowContentRoot/SubPanels_Right";

        [MenuItem("CityFlow/UI/Geon/Apply Dock Right Polish")]
        public static void Apply()
        {
            Transform dock = LayerLabUiAssetCatalog.FindInGeonScene(DockPath);
            if (dock == null)
            {
                return;
            }

            Transform subPanels =
                LayerLabUiAssetCatalog.FindInGeonScene(SubPanelsPath);
            if (subPanels == null)
            {
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(
                dock.gameObject,
                "Apply Geon Dock Right Polish");
            Undo.RecordObject(
                subPanels,
                "Move Geon Sub Panels");

            StyleDockFrame(dock);
            StyleSubPanels(subPanels);
            StyleDockButton(
                dock.Find("Build"),
                "건설",
                "Button/Btn_Rectangle01_n_Green.png");
            StyleDockButton(
                dock.Find("Research"),
                "연구",
                "Button/Btn_Rectangle01_n_Blue.png");
            StyleDockButton(
                dock.Find("Statistics"),
                "통계",
                "Button/Btn_Rectangle01_n_Orange.png");
            StyleDockButton(
                dock.Find("Setting"),
                "설정",
                "Button/Btn_Rectangle01_n_Blue.png");

            UIDockController controller = dock.GetComponent<UIDockController>();
            if (controller != null)
            {
                Undo.RecordObject(controller, "Keep Dock Layout Style");
                SerializedObject serialized = new SerializedObject(controller);
                SerializedProperty normalize =
                    serialized.FindProperty("normalizeLayoutOnStart");
                if (normalize != null)
                {
                    normalize.boolValue = false;
                    serialized.ApplyModifiedProperties();
                }
            }

            LayerLabUiAssetCatalog.CompleteSceneChange(
                dock.gameObject,
                nameof(GeonDockRightPolishApplier));
        }

        private static void StyleSubPanels(Transform subPanels)
        {
            RectTransform rect = subPanels as RectTransform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-130f, 440f);
            rect.sizeDelta = new Vector2(200f, 240f);
        }

        private static void StyleDockFrame(Transform dock)
        {
            RectTransform rect = dock as RectTransform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(92f, 240f);
            rect.anchoredPosition = new Vector2(-12f, 0f);

            Image background =
                LayerLabUiAssetCatalog.GetOrAddComponent<Image>(dock.gameObject);
            LayerLabUiAssetCatalog.ApplyImage(
                background,
                LayerLabUiAssetCatalog.LoadSprite(
                    "Frame/Frame_ListFrame01_White1.png"),
                new Color(0.1f, 0.11f, 0.13f, 0.97f));
            background.raycastTarget = true;

            VerticalLayoutGroup layout =
                LayerLabUiAssetCatalog.GetOrAddComponent<VerticalLayoutGroup>(
                    dock.gameObject);
            layout.padding = new RectOffset(8, 8, 12, 12);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        private static void StyleDockButton(
            Transform target,
            string label,
            string spritePath)
        {
            if (target == null)
            {
                Debug.LogError(
                    $"[GeonDockRightPolishApplier] Dock button missing: " +
                    label);
                return;
            }

            target.gameObject.SetActive(true);
            Button button = target.GetComponent<Button>();
            LayerLabUiAssetCatalog.StyleButton(
                button,
                LayerLabUiAssetCatalog.LoadSprite(spritePath),
                label);

            LayoutElement layout =
                LayerLabUiAssetCatalog.GetOrAddComponent<LayoutElement>(
                    target.gameObject);
            layout.minHeight = 48f;
            layout.preferredHeight = 48f;
            layout.flexibleHeight = 0f;

            TMP_Text text = target.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                LayerLabUiAssetCatalog.StyleText(
                    text,
                    14f,
                    Color.white,
                    FontStyles.Bold);
                text.alignment = TextAlignmentOptions.Center;
                text.textWrappingMode = TextWrappingModes.NoWrap;
            }
        }
    }
}
