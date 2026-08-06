using CityFlow.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CityFlow.EditorTools
{
    public static class ManualCoinHarvestUiBaker
    {
        private const string TopBarName = "HUD_TopBar";
        private const string ButtonName = "CoinHarvestButton";

        [MenuItem("Tools/GreenLight/UI/Bake Manual Coin Harvest UI")]
        public static void Bake()
        {
            Scene activeScene = SceneManager.GetActiveScene();

            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                Debug.LogWarning("[ManualCoinHarvestUiBaker] Open a target scene before baking the UI.");
                return;
            }

            Canvas canvas = WeeklySettlementPopupBaker.FindTargetCanvas(activeScene);
            if (canvas == null)
            {
                Debug.LogError("[ManualCoinHarvestUiBaker] No canvas found in the active scene.");
                return;
            }
            BakeIntoCanvas(canvas);
        }

        public static void BakeIntoCanvas(Canvas canvas)
        {
            if (canvas == null)
                throw new System.ArgumentNullException(nameof(canvas));

            WeeklySettlementPopupBaker.BakeIntoCanvas(canvas);
            BakeButton(canvas);
        }

        private static void BakeButton(Canvas canvas)
        {
            CoinHarvestButton existingButton = canvas.GetComponentInChildren<CoinHarvestButton>(true);

            if (existingButton != null)
            {
                UpdateExistingButton(canvas, existingButton);
                return;
            }

            Transform topBar = FindTransform(canvas.transform, TopBarName);

            if (topBar == null)
            {
                throw new System.InvalidOperationException(
                    $"[ManualCoinHarvestUiBaker] '{TopBarName}' was not found in canvas '{canvas.name}'.");
            }

            GameObject root = CreateUiObject(
                ButtonName,
                topBar,
                typeof(Image),
                typeof(Button),
                typeof(Shadow));
            Undo.RegisterCreatedObjectUndo(root, "Bake Manual Coin Harvest Button");
            root.layer = topBar.gameObject.layer;

            RectTransform rect = root.GetComponent<RectTransform>();
            ApplyButtonRect(rect);

            Image image = root.GetComponent<Image>();
            image.color = new Color(0.12f, 0.55f, 0.48f, 0.98f);

            Shadow shadow = root.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
            shadow.effectDistance = new Vector2(0f, -3f);
            shadow.useGraphicAlpha = true;

            Button button = root.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.78f, 0.86f, 0.84f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.42f, 0.45f, 0.46f, 0.72f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            TextMeshProUGUI label = CreateLabel(root.transform);
            TextMeshProUGUI receipt = CreateReceipt(topBar);
            Undo.RegisterCreatedObjectUndo(
                receipt.gameObject,
                "Create Coin Harvest Receipt");
            CoinHarvestButton controller = root.AddComponent<CoinHarvestButton>();
            controller.Configure(button, label, receipt);

            EditorUtility.SetDirty(root);
            if (canvas.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log(
                $"[ManualCoinHarvestUiBaker] Coin harvest UI baked below " +
                $"'{TopBarName}' in canvas '{canvas.name}'.");
        }

        private static void UpdateExistingButton(
            Canvas canvas,
            CoinHarvestButton existingButton)
        {
            RectTransform existingRect =
                existingButton.GetComponent<RectTransform>();
            Undo.RecordObject(
                existingRect,
                "Update Manual Coin Harvest Button Layout");
            ApplyButtonRect(existingRect);

            Transform receiptParent =
                FindTransform(canvas.transform, TopBarName) ??
                existingButton.transform.parent;
            TextMeshProUGUI receipt = FindReceipt(receiptParent);
            if (receipt == null && receiptParent != null)
            {
                receipt = CreateReceipt(receiptParent);
                Undo.RegisterCreatedObjectUndo(
                    receipt.gameObject,
                    "Create Coin Harvest Receipt");
            }

            Button button = existingButton.GetComponent<Button>();
            TextMeshProUGUI label =
                existingButton.GetComponentInChildren<TextMeshProUGUI>(true);
            Undo.RecordObject(existingButton, "Update Manual Coin Harvest UI");
            existingButton.Configure(button, label, receipt);
            EditorUtility.SetDirty(existingButton);
            EditorUtility.SetDirty(existingRect);
            if (receipt != null)
            {
                EditorUtility.SetDirty(receipt);
            }
            if (canvas.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            Selection.activeGameObject = existingButton.gameObject;
            EditorGUIUtility.PingObject(existingButton.gameObject);
            Debug.Log(
                $"[ManualCoinHarvestUiBaker] Updated existing coin harvest UI " +
                $"in canvas '{canvas.name}'.");
        }

        private static TextMeshProUGUI CreateLabel(Transform parent)
        {
            GameObject labelObject = CreateUiObject("Label", parent, typeof(TextMeshProUGUI));
            labelObject.layer = parent.gameObject.layer;

            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(-16f, -4f);

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = "HARVEST  0";
            label.fontSize = 15f;
            label.fontStyle = FontStyles.Bold;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            label.characterSpacing = 0f;
            return label;
        }

        private static void ApplyButtonRect(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -18f);
            rect.sizeDelta = new Vector2(184f, 40f);
        }

        private static TextMeshProUGUI FindReceipt(Transform parent)
        {
            if (parent == null)
            {
                return null;
            }

            Transform receiptTransform = parent.Find("CoinHarvestReceipt");
            return receiptTransform == null
                ? null
                : receiptTransform.GetComponent<TextMeshProUGUI>();
        }

        private static TextMeshProUGUI CreateReceipt(Transform parent)
        {
            GameObject receiptObject = CreateUiObject(
                "CoinHarvestReceipt",
                parent,
                typeof(TextMeshProUGUI));
            receiptObject.layer = parent.gameObject.layer;

            RectTransform rect = receiptObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -64f);
            rect.sizeDelta = new Vector2(260f, 86f);

            TextMeshProUGUI receipt = receiptObject.GetComponent<TextMeshProUGUI>();
            receipt.fontSize = 12f;
            receipt.color = new Color(1f, 0.92f, 0.62f, 1f);
            receipt.alignment = TextAlignmentOptions.Top;
            receipt.textWrappingMode = TextWrappingModes.Normal;
            receipt.raycastTarget = false;
            receipt.enabled = false;
            return receipt;
        }

        private static GameObject CreateUiObject(
            string name,
            Transform parent,
            params System.Type[] components)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);

            foreach (System.Type component in components)
            {
                gameObject.AddComponent(component);
            }

            return gameObject;
        }

        private static Transform FindTransform(Transform root, string targetName)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform candidate in transforms)
            {
                if (candidate.name == targetName)
                {
                    return candidate;
                }
            }
            return null;
        }

        // Unity setup: Open the target scene, then use Tools > GreenLight > UI > Bake Manual Coin Harvest UI.
    }
}
