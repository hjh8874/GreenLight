using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI.Editor
{
    public static class CongestionToggleBaker
    {
        [MenuItem("CityFlow/Bake UI/UI_CongestionToggle")]
        public static void Bake()
        {
            EnsurePrefabFolder();

            GameObject root = new GameObject(
                "UI_CongestionToggle",
                typeof(RectTransform),
                typeof(Toggle),
                typeof(CongestionTogglePanelController));
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(320f, 36f);

            GameObject backgroundObject = CreateGraphic(
                "Background",
                root.transform,
                LayerLabUiAssetCatalog.LoadSprite(
                    "UI_Etc/Toggle_Square_l_off.png"));
            RectTransform backgroundRect =
                backgroundObject.GetComponent<RectTransform>();
            SetFixedRect(
                backgroundRect,
                new Vector2(0f, 0.5f),
                new Vector2(18f, 0f),
                new Vector2(30f, 30f));

            GameObject checkmarkObject = CreateGraphic(
                "Checkmark",
                backgroundObject.transform,
                LayerLabUiAssetCatalog.LoadSprite(
                    "UI_Etc/Toggle_Square_l_on.png"));
            RectTransform checkmarkRect =
                checkmarkObject.GetComponent<RectTransform>();
            checkmarkRect.anchorMin = Vector2.zero;
            checkmarkRect.anchorMax = Vector2.one;
            checkmarkRect.offsetMin = Vector2.zero;
            checkmarkRect.offsetMax = Vector2.zero;

            GameObject labelObject = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(root.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(42f, 0f);
            labelRect.offsetMax = Vector2.zero;

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = "정체 뷰";
            label.alignment = TextAlignmentOptions.MidlineLeft;
            LayerLabUiAssetCatalog.StyleText(
                label,
                16f,
                Color.white,
                FontStyles.Bold);

            Toggle toggle = root.GetComponent<Toggle>();
            toggle.targetGraphic = backgroundObject.GetComponent<Image>();
            toggle.graphic = checkmarkObject.GetComponent<Image>();

            CongestionTogglePanelController controller =
                root.GetComponent<CongestionTogglePanelController>();
            controller.Configure(toggle);

            PrefabUtility.SaveAsPrefabAsset(
                root,
                LayerLabUiAssetCatalog.CongestionTogglePrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"[CongestionToggleBaker] Baked " +
                LayerLabUiAssetCatalog.CongestionTogglePrefabPath);
        }

        private static GameObject CreateGraphic(
            string name,
            Transform parent,
            Sprite sprite)
        {
            GameObject target = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            target.transform.SetParent(parent, false);
            Image image = target.GetComponent<Image>();
            LayerLabUiAssetCatalog.ApplyImage(
                image,
                sprite,
                Color.white,
                false);
            image.preserveAspect = true;
            return target;
        }

        private static void SetFixedRect(
            RectTransform rect,
            Vector2 anchor,
            Vector2 position,
            Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
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
