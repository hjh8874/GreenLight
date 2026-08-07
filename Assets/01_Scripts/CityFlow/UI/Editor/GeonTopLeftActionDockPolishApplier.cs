using TMPro;
using CityFlow.UI.Controllers;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI.Editor
{
    public static class GeonTopLeftActionDockPolishApplier
    {
        private const string ContentRootPath =
            "UI_MainCanvas/FloatingWindowContentRoot";
        private const string ActionDockName = "TopLeftActionDock";

        [MenuItem("CityFlow/UI/Geon/Apply Top Left Action Dock Polish")]
        public static void Apply()
        {
            Transform contentRoot =
                LayerLabUiAssetCatalog.FindInGeonScene(ContentRootPath);
            if (contentRoot == null)
            {
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(
                contentRoot.gameObject,
                "Apply Geon Top Left Action Dock Polish");

            Transform topBar = contentRoot.Find("HUD_TopBar");
            Transform actionDock = EnsureActionDock(
                topBar != null ? topBar : contentRoot,
                contentRoot);
            Transform floatingButton = FindFloatingButton(contentRoot, actionDock);
            Transform cameraButton = FindCameraButton(contentRoot, actionDock);
            if (floatingButton == null || cameraButton == null)
            {
                return;
            }

            MoveToActionDock(floatingButton, actionDock);
            MoveToActionDock(cameraButton, actionDock);
            floatingButton.SetSiblingIndex(0);
            cameraButton.SetSiblingIndex(1);
            LayerLabUiAssetCatalog.GetOrAddComponent<TopBarActionDockController>(
                actionDock.gameObject);

            StyleActionButton(
                floatingButton,
                "플로팅",
                "Button/Btn_Rectangle02_Dark.png");
            StyleActionButton(
                cameraButton,
                "카메라 회전",
                "Button/Btn_Rectangle01_n_Green.png");

            actionDock.SetAsLastSibling();
            LayerLabUiAssetCatalog.CompleteSceneChange(
                actionDock.gameObject,
                nameof(GeonTopLeftActionDockPolishApplier));
        }

        private static Transform EnsureActionDock(
            Transform parent,
            Transform contentRoot)
        {
            Transform existing = parent.Find(ActionDockName)
                ?? contentRoot.Find(ActionDockName);
            GameObject dockObject;
            if (existing != null)
            {
                dockObject = existing.gameObject;
                if (existing.parent != parent)
                {
                    Undo.SetTransformParent(
                        existing,
                        parent,
                        "Parent Top Left Action Dock");
                }
            }
            else
            {
                dockObject = new GameObject(
                    ActionDockName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(HorizontalLayoutGroup));
                Undo.RegisterCreatedObjectUndo(
                    dockObject,
                    "Create Top Left Action Dock");
                Undo.SetTransformParent(
                    dockObject.transform,
                    parent,
                    "Parent Top Left Action Dock");
            }

            RectTransform rect = dockObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-8f, 0f);
            rect.sizeDelta = new Vector2(196f, 52f);

            Image background =
                LayerLabUiAssetCatalog.GetOrAddComponent<Image>(dockObject);
            LayerLabUiAssetCatalog.ApplyImage(
                background,
                LayerLabUiAssetCatalog.LoadSprite(
                    "Frame/Frame_ListFrame01_White1.png"),
                new Color(0.1f, 0.11f, 0.13f, 0.42f));
            background.raycastTarget = true;

            HorizontalLayoutGroup layout =
                LayerLabUiAssetCatalog.GetOrAddComponent<HorizontalLayoutGroup>(
                    dockObject);
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return dockObject.transform;
        }

        private static Transform FindFloatingButton(
            Transform contentRoot,
            Transform actionDock)
        {
            Transform button = actionDock.Find("Btn_Floating");
            if (button == null)
            {
                button = contentRoot.Find("Dock_Right/Btn_Floating");
            }

            if (button == null)
            {
                Debug.LogError(
                    "[GeonTopLeftActionDockPolishApplier] " +
                    "Btn_Floating was not found.");
            }

            return button;
        }

        private static Transform FindCameraButton(
            Transform contentRoot,
            Transform actionDock)
        {
            Transform button = actionDock.Find("CameraRotateButton");
            if (button == null)
            {
                button = contentRoot.Find("HUD_TopBar/CameraRotateButton");
            }

            if (button != null)
            {
                return button;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                LayerLabUiAssetCatalog.CameraButtonPrefabPath);
            if (prefab == null)
            {
                Debug.LogError(
                    "[GeonTopLeftActionDockPolishApplier] Camera button " +
                    "prefab is missing. Run its baker first.");
                return null;
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(
                prefab,
                actionDock) as GameObject;
            if (instance == null)
            {
                return null;
            }

            instance.name = "CameraRotateButton";
            Undo.RegisterCreatedObjectUndo(
                instance,
                "Create Camera Rotation Button");
            return instance.transform;
        }

        private static void MoveToActionDock(
            Transform button,
            Transform actionDock)
        {
            if (button.parent != actionDock)
            {
                Undo.SetTransformParent(
                    button,
                    actionDock,
                    "Move Button To Top Left Action Dock");
            }

            button.gameObject.SetActive(true);
            RectTransform rect = button as RectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(86f, 40f);
        }

        private static void StyleActionButton(
            Transform target,
            string label,
            string spritePath)
        {
            Button button = target.GetComponent<Button>();
            LayerLabUiAssetCatalog.StyleButton(
                button,
                LayerLabUiAssetCatalog.LoadSprite(spritePath),
                label);
            Image image = button.targetGraphic as Image;
            if (image != null)
            {
                Color color = image.color;
                color.a = 0.62f;
                image.color = color;
            }

            LayoutElement layout =
                LayerLabUiAssetCatalog.GetOrAddComponent<LayoutElement>(
                    target.gameObject);
            layout.minWidth = 86f;
            layout.preferredWidth = 86f;
            layout.flexibleWidth = 0f;
            layout.minHeight = 40f;
            layout.preferredHeight = 40f;
            layout.flexibleHeight = 0f;

            TMP_Text text = target.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                LayerLabUiAssetCatalog.StyleText(
                    text,
                    13f,
                    Color.white,
                    FontStyles.Bold);
                text.alignment = TextAlignmentOptions.Center;
                text.textWrappingMode = TextWrappingModes.NoWrap;
            }
        }
    }
}
