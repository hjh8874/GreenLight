using CityFlow.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CityFlow.EditorTools
{
    public static class OfflineSettlementPopupBaker
    {
        private const string TargetCanvasName = "UI_MainCanvas";
        private const string PopupName = "OfflineSettlementPopup";

        [MenuItem("Tools/GreenLight/UI/Bake Offline Settlement Popup")]
        public static void Bake()
        {
            Scene scene = SceneManager.GetActiveScene();

            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning(
                    "[OfflineSettlementPopupBaker] Open a target scene before baking.");
                return;
            }

            Canvas canvas = FindCanvas(scene);

            if (canvas == null)
            {
                Debug.LogError(
                    "[OfflineSettlementPopupBaker] No Canvas was found in the active scene.");
                return;
            }

            OfflineSettlementPopup popup =
                FindInScene<OfflineSettlementPopup>(scene);

            if (popup == null)
            {
                popup = CreatePopup(canvas.transform);
                Undo.RegisterCreatedObjectUndo(
                    popup.gameObject,
                    "Bake Offline Settlement Popup");
            }

            WirePopup(popup);
            popup.transform.SetAsLastSibling();
            EditorUtility.SetDirty(popup);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = popup.gameObject;
            EditorGUIUtility.PingObject(popup.gameObject);

            Debug.Log(
                $"[OfflineSettlementPopupBaker] Offline settlement popup baked and wired in '{scene.name}'.");
        }

        private static OfflineSettlementPopup CreatePopup(
            Transform parent)
        {
            GameObject root = CreateUiObject(
                PopupName,
                parent,
                typeof(CanvasGroup));
            SetStretch(root.GetComponent<RectTransform>());

            Image backdrop = CreateUiObject(
                "ModalBackdrop",
                root.transform,
                typeof(Image)).GetComponent<Image>();
            SetStretch(backdrop.rectTransform);
            backdrop.color =
                new Color(0.025f, 0.035f, 0.04f, 0.58f);

            RectTransform card = CreateUiObject(
                "OfflineReportCard",
                root.transform,
                typeof(Image),
                typeof(Shadow)).GetComponent<RectTransform>();
            card.anchorMin = card.anchorMax = card.pivot =
                new Vector2(0.5f, 0.5f);
            card.anchoredPosition = Vector2.zero;
            card.sizeDelta = new Vector2(540f, 390f);

            Image cardImage = card.GetComponent<Image>();
            cardImage.color =
                new Color(0.075f, 0.09f, 0.105f, 0.99f);
            Shadow shadow = card.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(0f, -8f);

            Image accent = CreateUiObject(
                "TopAccent",
                card,
                typeof(Image)).GetComponent<Image>();
            RectTransform accentRect = accent.rectTransform;
            accentRect.anchorMin = new Vector2(0f, 1f);
            accentRect.anchorMax = new Vector2(1f, 1f);
            accentRect.pivot = new Vector2(0.5f, 1f);
            accentRect.anchoredPosition = Vector2.zero;
            accentRect.sizeDelta = new Vector2(0f, 6f);
            accent.color = new Color(0.22f, 0.7f, 0.78f, 1f);

            CreateText(
                card,
                "TitleText",
                "OFFLINE REPORT",
                new Vector2(0f, 147f),
                new Vector2(450f, 34f),
                23f,
                TextAlignmentOptions.Center,
                Color.white);

            CreateRow(
                card,
                "InitialCoins",
                "INITIAL COINS",
                "0",
                69f,
                new Color(0.8f, 0.84f, 0.87f, 1f));
            CreateRow(
                card,
                "EarnedCoins",
                "OFFLINE EARNINGS",
                "+0",
                9f,
                new Color(0.35f, 0.88f, 0.58f, 1f));
            CreateRow(
                card,
                "CurrentCoins",
                "CURRENT COINS",
                "0",
                -51f,
                new Color(0.35f, 0.72f, 0.94f, 1f));

            Button closeButton = CreateUiObject(
                "CloseButton",
                card,
                typeof(Image),
                typeof(Button)).GetComponent<Button>();
            RectTransform buttonRect =
                closeButton.GetComponent<RectTransform>();
            buttonRect.anchorMin = buttonRect.anchorMax =
                buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.anchoredPosition =
                new Vector2(0f, -137f);
            buttonRect.sizeDelta = new Vector2(220f, 48f);
            closeButton.GetComponent<Image>().color =
                new Color(0.18f, 0.57f, 0.66f, 1f);

            TextMeshProUGUI label = CreateText(
                closeButton.transform,
                "Label",
                "CLOSE",
                Vector2.zero,
                Vector2.zero,
                16f,
                TextAlignmentOptions.Center,
                Color.white);
            SetStretch(label.rectTransform);

            return root.AddComponent<OfflineSettlementPopup>();
        }

        private static void CreateRow(
            RectTransform parent,
            string name,
            string label,
            string value,
            float y,
            Color valueColor)
        {
            CreateText(
                parent,
                $"{name}Label",
                label,
                new Vector2(-182f, y),
                new Vector2(210f, 30f),
                15f,
                TextAlignmentOptions.MidlineLeft,
                new Color(0.62f, 0.68f, 0.72f, 1f));
            CreateText(
                parent,
                $"{name}Value",
                value,
                new Vector2(148f, y),
                new Vector2(250f, 36f),
                24f,
                TextAlignmentOptions.MidlineRight,
                valueColor);
        }

        private static TextMeshProUGUI CreateText(
            Transform parent,
            string name,
            string value,
            Vector2 position,
            Vector2 size,
            float fontSize,
            TextAlignmentOptions alignment,
            Color color)
        {
            TextMeshProUGUI text = CreateUiObject(
                name,
                parent,
                typeof(TextMeshProUGUI))
                .GetComponent<TextMeshProUGUI>();
            RectTransform rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot =
                new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = FontStyles.Bold;
            text.alignment = alignment;
            text.color = color;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.characterSpacing = 0f;
            return text;
        }

        private static void WirePopup(
            OfflineSettlementPopup popup)
        {
            CanvasGroup group =
                popup.GetComponent<CanvasGroup>() ??
                popup.gameObject.AddComponent<CanvasGroup>();
            RectTransform card = FindChild<RectTransform>(
                popup.transform,
                "OfflineReportCard");
            Button close = FindChild<Button>(
                popup.transform,
                "CloseButton");
            TextMeshProUGUI initial =
                FindChild<TextMeshProUGUI>(
                    popup.transform,
                    "InitialCoinsValue");
            TextMeshProUGUI earned =
                FindChild<TextMeshProUGUI>(
                    popup.transform,
                    "EarnedCoinsValue");
            TextMeshProUGUI current =
                FindChild<TextMeshProUGUI>(
                    popup.transform,
                    "CurrentCoinsValue");

            popup.Configure(
                group,
                card,
                close,
                initial,
                earned,
                current);
        }

        private static GameObject CreateUiObject(
            string name,
            Transform parent,
            params System.Type[] components)
        {
            GameObject gameObject =
                new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);

            foreach (System.Type component in components)
            {
                gameObject.AddComponent(component);
            }

            return gameObject;
        }

        private static void SetStretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        private static Canvas FindCanvas(Scene scene)
        {
            Canvas fallback = null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Canvas canvas in
                    root.GetComponentsInChildren<Canvas>(true))
                {
                    if (canvas.name == TargetCanvasName)
                    {
                        return canvas;
                    }

                    fallback ??= canvas;
                }
            }

            return fallback;
        }

        private static T FindInScene<T>(Scene scene)
            where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component =
                    root.GetComponentInChildren<T>(true);

                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private static T FindChild<T>(
            Transform root,
            string name)
            where T : Component
        {
            foreach (T component in
                root.GetComponentsInChildren<T>(true))
            {
                if (component.name == name)
                {
                    return component;
                }
            }

            return null;
        }
    }
}
