using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI.Editor
{
    public static class GeonFloatingPanelPolishApplier
    {
        private const string PanelPath =
            "UI_MainCanvas/FloatingWindowContentRoot/SubPanels_Right/Floating_Panel";

        [MenuItem("CityFlow/UI/Geon/Apply Floating Panel Polish")]
        public static void Apply()
        {
            Transform panel = LayerLabUiAssetCatalog.FindInGeonScene(PanelPath);
            if (panel == null)
            {
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(
                panel.gameObject,
                "Apply Geon Floating Panel Polish");

            Image background =
                LayerLabUiAssetCatalog.GetOrAddComponent<Image>(panel.gameObject);
            LayerLabUiAssetCatalog.ApplyImage(
                background,
                LayerLabUiAssetCatalog.LoadSprite(
                    "Frame/Frame_ListFrame01_White1.png"),
                new Color(0.12f, 0.14f, 0.18f, 0.97f));
            background.raycastTarget = true;

            Transform toggleTransform = panel.Find("Floating") ??
                                        panel.Find("Floating Toggle");
            if (toggleTransform != null)
            {
                LayerLabUiAssetCatalog.StyleToggle(
                    toggleTransform.GetComponent<Toggle>(),
                    "플로팅 모드");
            }

            StylePreset(panel.Find("S_Button"), "S");
            StylePreset(panel.Find("M_Button "), "M");
            StylePreset(panel.Find("L_Button "), "L");

            TMP_Text[] texts = panel.GetComponentsInChildren<TMP_Text>(true);
            for (int index = 0; index < texts.Length; index++)
            {
                if (texts[index].text != "플로팅 모드")
                {
                    LayerLabUiAssetCatalog.StyleText(
                        texts[index],
                        16f,
                        Color.white,
                        FontStyles.Bold);
                }
            }

            LayerLabUiAssetCatalog.CompleteSceneChange(
                panel.gameObject,
                nameof(GeonFloatingPanelPolishApplier));
        }

        private static void StylePreset(Transform target, string label)
        {
            if (target == null)
            {
                return;
            }

            LayerLabUiAssetCatalog.StyleButton(
                target.GetComponent<Button>(),
                LayerLabUiAssetCatalog.LoadSprite(
                    "Button/Btn_Rectangle02_Dark.png"),
                label);
        }
    }
}
