using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI.Editor
{
    public static class GeonBuildingInfoCardPolishApplier
    {
        private const string CardPath = "UI_MainCanvas/UI_BuildingInfoCard";

        [MenuItem("CityFlow/UI/Geon/Apply Building Info Card Polish")]
        public static void Apply()
        {
            Transform card = LayerLabUiAssetCatalog.FindInGeonScene(CardPath);
            if (card == null)
            {
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(
                card.gameObject,
                "Apply Geon Building Info Card Polish");

            Image background =
                LayerLabUiAssetCatalog.GetOrAddComponent<Image>(card.gameObject);
            LayerLabUiAssetCatalog.ApplyImage(
                background,
                LayerLabUiAssetCatalog.LoadSprite(
                    "Frame/Frame_ListFrame01_White1.png"),
                new Color(0.1f, 0.12f, 0.16f, 0.98f));
            background.raycastTarget = true;

            RectTransform cardRect = card as RectTransform;
            if (cardRect != null)
            {
                cardRect.sizeDelta = new Vector2(380f, 280f);
            }

            TMP_Text[] texts = card.GetComponentsInChildren<TMP_Text>(true);
            for (int index = 0; index < texts.Length; index++)
            {
                TMP_Text text = texts[index];
                if (text.name == "TxtBuildingName")
                {
                    LayerLabUiAssetCatalog.StyleText(
                        text,
                        24f,
                        new Color(1f, 0.84f, 0.3f, 1f),
                        FontStyles.Bold);
                }
                else if (text.name == "TxtStoryComment")
                {
                    LayerLabUiAssetCatalog.StyleText(
                        text,
                        16f,
                        new Color(0.86f, 0.9f, 0.96f, 1f));
                    text.textWrappingMode = TextWrappingModes.Normal;
                }
                else
                {
                    LayerLabUiAssetCatalog.StyleText(
                        text,
                        15f,
                        Color.white,
                        FontStyles.Bold);
                }
            }

            LayerLabUiAssetCatalog.CompleteSceneChange(
                card.gameObject,
                nameof(GeonBuildingInfoCardPolishApplier));
        }
    }
}
