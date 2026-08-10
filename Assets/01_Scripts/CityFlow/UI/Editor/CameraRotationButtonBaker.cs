using CityFlow.UI.Controllers;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI.Editor
{
    public static class CameraRotationButtonBaker
    {
        [MenuItem("CityFlow/UI/Bake Camera Rotation Button")]
        public static void Bake()
        {
            EnsurePrefabFolder();

            GameObject root = new GameObject(
                "UI_CameraRotationButton",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(VerticalLayoutGroup),
                typeof(LayoutElement),
                typeof(CameraRotationButtonController));
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(104f, 108f);

            Image panelBackground = root.GetComponent<Image>();
            LayerLabUiAssetCatalog.ApplyImage(
                panelBackground,
                LayerLabUiAssetCatalog.LoadSprite(
                    "Button/Btn_Rectangle01_n_Green.png"),
                new Color(1f, 1f, 1f, 0.62f));
            panelBackground.raycastTarget = true;

            VerticalLayoutGroup layout =
                root.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(7, 7, 3, 3);
            layout.spacing = 2f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            LayoutElement rootLayout = root.GetComponent<LayoutElement>();
            rootLayout.minWidth = 104f;
            rootLayout.preferredWidth = 104f;
            rootLayout.minHeight = 108f;
            rootLayout.preferredHeight = 108f;

            CreateCameraIcon(root.transform);
            CreateTitle(root.transform);

            var buttonRow = new GameObject(
                "DirectionButtons",
                typeof(RectTransform),
                typeof(HorizontalLayoutGroup),
                typeof(LayoutElement));
            buttonRow.transform.SetParent(root.transform, false);
            RectTransform rowRect = buttonRow.GetComponent<RectTransform>();
            rowRect.sizeDelta = new Vector2(88f, 40f);

            HorizontalLayoutGroup rowLayout =
                buttonRow.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 4f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = false;
            rowLayout.childControlHeight = false;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            LayoutElement rowElement = buttonRow.GetComponent<LayoutElement>();
            rowElement.minWidth = 88f;
            rowElement.preferredWidth = 88f;
            rowElement.minHeight = 40f;
            rowElement.preferredHeight = 40f;

            Button leftButton = CreateRotationButton(
                buttonRow.transform,
                "RotateLeftButton",
                "ButtonIcons/ButtonIcon_Line_White/" +
                "btn_line_white_arrow_prev.png");
            Button rightButton = CreateRotationButton(
                buttonRow.transform,
                "RotateRightButton",
                "ButtonIcons/ButtonIcon_Line_White/" +
                "btn_line_white_arrow_next.png");

            CameraRotationButtonController controller =
                root.GetComponent<CameraRotationButtonController>();
            controller.Configure(leftButton, rightButton);

            PrefabUtility.SaveAsPrefabAsset(
                root,
                LayerLabUiAssetCatalog.CameraButtonPrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"[CameraRotationButtonBaker] Baked " +
                LayerLabUiAssetCatalog.CameraButtonPrefabPath);
        }

        private static void CreateCameraIcon(Transform parent)
        {
            var iconObject = new GameObject(
                "CameraIcon",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(LayoutElement));
            iconObject.transform.SetParent(parent, false);

            RectTransform rect = iconObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(34f, 34f);

            Image image = iconObject.GetComponent<Image>();
            image.sprite = LayerLabUiAssetCatalog.LoadSprite(
                "ButtonIcons/ButtonIcon_Hole/btn_white_dig_camera.png");
            image.preserveAspect = true;
            image.raycastTarget = false;

            LayoutElement layout = iconObject.GetComponent<LayoutElement>();
            layout.minWidth = 34f;
            layout.preferredWidth = 34f;
            layout.minHeight = 34f;
            layout.preferredHeight = 34f;
        }

        private static void CreateTitle(Transform parent)
        {
            var titleObject = new GameObject(
                "Title",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI),
                typeof(LayoutElement));
            titleObject.transform.SetParent(parent, false);

            RectTransform rect = titleObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(90f, 20f);

            TextMeshProUGUI title = titleObject.GetComponent<TextMeshProUGUI>();
            title.text = "카메라 회전";
            title.alignment = TextAlignmentOptions.Center;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.raycastTarget = false;
            LayerLabUiAssetCatalog.StyleText(
                title,
                13f,
                Color.white,
                FontStyles.Bold);

            LayoutElement layout = titleObject.GetComponent<LayoutElement>();
            layout.minWidth = 90f;
            layout.preferredWidth = 90f;
            layout.minHeight = 20f;
            layout.preferredHeight = 20f;
        }

        private static Button CreateRotationButton(
            Transform parent,
            string name,
            string iconPath)
        {
            var buttonObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button),
                typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);

            RectTransform rect =
                buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(42f, 40f);

            Image background = buttonObject.GetComponent<Image>();
            LayerLabUiAssetCatalog.ApplyImage(
                background,
                LayerLabUiAssetCatalog.LoadSprite(
                    "Button/Btn_Rectangle01_n_Green.png"),
                Color.white);

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = background;

            LayoutElement layout = buttonObject.GetComponent<LayoutElement>();
            layout.minWidth = 42f;
            layout.preferredWidth = 42f;
            layout.minHeight = 40f;
            layout.preferredHeight = 40f;

            var iconObject = new GameObject(
                "Icon",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            iconObject.transform.SetParent(buttonObject.transform, false);
            RectTransform iconRect =
                iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(28f, 28f);

            Image icon = iconObject.GetComponent<Image>();
            icon.sprite = LayerLabUiAssetCatalog.LoadSprite(iconPath);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            return button;
        }

        private static void EnsurePrefabFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/02_Prefabs"))
            {
                AssetDatabase.CreateFolder("Assets", "02_Prefabs");
            }

            if (!AssetDatabase.IsValidFolder("Assets/02_Prefabs/UI"))
            {
                AssetDatabase.CreateFolder("Assets/02_Prefabs", "UI");
            }
        }
    }
}
