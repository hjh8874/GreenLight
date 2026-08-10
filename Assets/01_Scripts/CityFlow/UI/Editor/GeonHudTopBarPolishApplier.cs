using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI.Editor
{
    public static class GeonHudTopBarPolishApplier
    {
        private const string HudPath =
            "UI_MainCanvas/FloatingWindowContentRoot/HUD_TopBar";

        [MenuItem("CityFlow/UI/Geon/Apply HUD Top Bar Polish")]
        public static void Apply()
        {
            Transform hud = LayerLabUiAssetCatalog.FindInGeonScene(HudPath);
            if (hud == null)
            {
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(
                hud.gameObject,
                "Apply Geon HUD Top Bar Polish");

            Image background =
                LayerLabUiAssetCatalog.GetOrAddComponent<Image>(hud.gameObject);
            LayerLabUiAssetCatalog.ApplyImage(
                background,
                null,
                new Color(0.12f, 0.14f, 0.18f, 0.97f),
                false);
            background.raycastTarget = true;

            ConfigureHudText(
                hud.Find("TimeText"),
                new Vector2(16f, -14f),
                new Vector2(210f, 30f),
                "--월 --일 --:--",
                Color.white);
            ConfigureHudText(
                hud.Find("VehicleCountText"),
                new Vector2(240f, -14f),
                new Vector2(220f, 30f),
                "차량: 0대  인구: 0명",
                Color.white);
            ConfigureHudText(
                hud.Find("CoinText"),
                new Vector2(480f, -14f),
                new Vector2(280f, 30f),
                "재화: 0",
                new Color(1f, 0.84f, 0.3f, 1f));

            DisableUnusedLegacyHudElement(hud.Find("StabilityText"));
            DisableUnusedLegacyHudElement(hud.Find("StabilityBar"));

            HUDDashboard dashboard = hud.GetComponent<HUDDashboard>();
            if (dashboard != null)
            {
                Undo.RecordObject(dashboard, "Keep Geon HUD Layout");
                SerializedObject serialized = new SerializedObject(dashboard);
                SerializedProperty normalize =
                    serialized.FindProperty("normalizeLayoutOnStart");
                if (normalize != null)
                {
                    normalize.boolValue = false;
                    serialized.ApplyModifiedProperties();
                }
            }

            Transform harvestTransform = hud.Find("CoinHarvestButton");
            if (harvestTransform != null)
            {
                LayerLabUiAssetCatalog.StyleButton(
                    harvestTransform.GetComponent<Button>(),
                    LayerLabUiAssetCatalog.LoadSprite(
                        "Button/Btn_Rectangle02_Dark.png"),
                    null,
                    new Color(1f, 0.86f, 0.35f, 1f));
            }

            LayerLabUiAssetCatalog.CompleteSceneChange(
                hud.gameObject,
                nameof(GeonHudTopBarPolishApplier));
        }

        private static void ConfigureHudText(
            Transform target,
            Vector2 anchoredPosition,
            Vector2 size,
            string placeholder,
            Color color)
        {
            if (target == null)
            {
                return;
            }

            RectTransform rect = target as RectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            TMP_Text text = target.GetComponent<TMP_Text>();
            LayerLabUiAssetCatalog.StyleText(
                text,
                18f,
                color,
                FontStyles.Bold);
            text.alignment = TextAlignmentOptions.Left;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.text = placeholder;
        }

        private static void DisableUnusedLegacyHudElement(Transform target)
        {
            if (target != null && target.gameObject.activeSelf)
            {
                Undo.RecordObject(target.gameObject, "Disable Unused HUD Element");
                target.gameObject.SetActive(false);
            }
        }
    }
}

