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
                typeof(Button),
                typeof(CameraRotationButtonController));
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(84f, 40f);

            Image image = root.GetComponent<Image>();
            LayerLabUiAssetCatalog.ApplyImage(
                image,
                LayerLabUiAssetCatalog.LoadSprite(
                    "Button/Btn_Rectangle01_n_Green.png"),
                Color.white);

            Button button = root.GetComponent<Button>();
            button.targetGraphic = image;

            GameObject labelObject = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(root.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = "회전";
            label.alignment = TextAlignmentOptions.Center;
            LayerLabUiAssetCatalog.StyleText(
                label,
                15f,
                Color.white,
                FontStyles.Bold);

            CameraRotationButtonController controller =
                root.GetComponent<CameraRotationButtonController>();
            controller.Configure(button, 1);

            PrefabUtility.SaveAsPrefabAsset(
                root,
                LayerLabUiAssetCatalog.CameraButtonPrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"[CameraRotationButtonBaker] Baked " +
                LayerLabUiAssetCatalog.CameraButtonPrefabPath);
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
