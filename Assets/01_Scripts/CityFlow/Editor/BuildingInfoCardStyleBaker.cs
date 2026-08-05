using System;
using System.Collections.Generic;
using CityFlow.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CityFlow.Editor
{
    public static class BuildingInfoCardStyleBaker
    {
        private const string TargetScenePath =
            "Assets/00_Scenes/Debug/CityFlowIntegrated_han.unity";
        private const string CardName = "UI_BuildingInfoCard";
        private const string LegacyAnalysisCardName =
            "AnalysisCard_BottomLeft";
        private const string ThemeRootName = "VisualTheme_GreenSNS";

        private const string AssetRoot =
            "Assets/99_Download/Layer Lab/GUI-MonoRound/ResourcesData/" +
            "Sprites/Components";

        private const string PopupPath = AssetRoot + "/Popup/Popup00.png";
        private const string TopBarPath = AssetRoot + "/Popup/Popup01_TopBar.png";
        private const string RowFramePath =
            AssetRoot + "/Frame/Frame_ListFrame01_White1.png";
        private const string MessageIconPath =
            AssetRoot + "/Icon_PictoIcons/pictoicon_message.png";

        private static readonly string[] MetricIconPaths =
        {
            AssetRoot + "/Icon_PictoIcons/pictoicon_group.png",
            AssetRoot + "/Icon_PictoIcons/pictoicon_warning.png",
            AssetRoot + "/Icon_PictoIcons/pictoicon_money.png",
            AssetRoot + "/Icon_PictoIcons/pictoicon_time.png"
        };

        private static readonly Color CardColor =
            new Color(0.025f, 0.105f, 0.082f, 0.98f);
        private static readonly Color HeaderColor =
            new Color(0.055f, 0.40f, 0.31f, 1f);
        private static readonly Color AccentColor =
            new Color(0.28f, 0.92f, 0.63f, 1f);
        private static readonly Color PrimaryTextColor =
            new Color(0.91f, 0.98f, 0.94f, 1f);
        private static readonly Color SecondaryTextColor =
            new Color(0.55f, 0.73f, 0.65f, 1f);
        private static readonly Color RowColor =
            new Color(0.075f, 0.20f, 0.16f, 0.90f);

        [MenuItem("Tools/GreenLight/UI/Bake Building Info Card Style %#i")]
        public static void BakeActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!TryFindCard(scene, out BuildingInfoCardController card))
            {
                Debug.LogWarning(
                    $"[BuildingInfoCardStyleBaker] {CardName} was not found " +
                    $"in the active scene '{scene.path}'.");
                return;
            }

            ApplyStyle(card);
            SuppressLegacyAnalysisCard(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = card.gameObject;
            Debug.Log(
                $"[BuildingInfoCardStyleBaker] Applied the Green SNS style " +
                $"to {scene.path}.",
                card);
        }

        public static void BakeDebugScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                BuildPipeline.isBuildingPlayer)
            {
                return;
            }

            Scene scene = SceneManager.GetSceneByPath(TargetScenePath);
            bool openedForBake = !scene.IsValid() || !scene.isLoaded;

            if (openedForBake)
            {
                scene = EditorSceneManager.OpenScene(
                    TargetScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                if (!TryFindCard(scene, out BuildingInfoCardController card))
                {
                    Debug.LogError(
                        $"[BuildingInfoCardStyleBaker] {CardName} was not found " +
                        $"in {TargetScenePath}.");
                    return;
                }

                ApplyStyle(card);
                SuppressLegacyAnalysisCard(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log(
                    $"[BuildingInfoCardStyleBaker] Baked the Green SNS card " +
                    $"style into {TargetScenePath}.",
                    card);
            }
            finally
            {
                if (openedForBake && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static void ApplyStyle(BuildingInfoCardController card)
        {
            RectTransform cardRect = (RectTransform)card.transform;
            Undo.RecordObject(cardRect, "Style building info card");
            cardRect.sizeDelta = new Vector2(430f, 286f);

            Image cardImage = card.GetComponent<Image>();
            if (cardImage == null)
            {
                cardImage = Undo.AddComponent<Image>(card.gameObject);
            }

            Undo.RecordObject(cardImage, "Style building info card");
            cardImage.sprite = LoadSprite(PopupPath);
            cardImage.type = Image.Type.Sliced;
            cardImage.color = CardColor;
            cardImage.raycastTarget = false;

            Shadow shadow = card.GetComponent<Shadow>();
            if (shadow == null)
            {
                shadow = Undo.AddComponent<Shadow>(card.gameObject);
            }

            Undo.RecordObject(shadow, "Style building info card");
            shadow.effectColor = new Color(0f, 0.035f, 0.025f, 0.72f);
            shadow.effectDistance = new Vector2(0f, -7f);
            shadow.useGraphicAlpha = true;

            Transform existingTheme = card.transform.Find(ThemeRootName);
            if (existingTheme != null)
            {
                Undo.DestroyObjectImmediate(existingTheme.gameObject);
            }

            RectTransform themeRoot = CreateRect(ThemeRootName, card.transform);
            Stretch(themeRoot);
            themeRoot.SetSiblingIndex(0);

            AddHeader(themeRoot, card.transform);
            AddStoryPanel(themeRoot);
            AddMetricRows(card.transform);
            AddFooter(themeRoot, card.transform);
            StyleText(card.transform);

            EditorUtility.SetDirty(card);
        }

        private static void SuppressLegacyAnalysisCard(Scene scene)
        {
            AnalysisCardController legacyCard = FindComponent<AnalysisCardController>(
                scene,
                LegacyAnalysisCardName);
            if (legacyCard == null)
            {
                return;
            }

            CanvasGroup group = legacyCard.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = Undo.AddComponent<CanvasGroup>(legacyCard.gameObject);
            }

            Undo.RecordObject(group, "Suppress legacy analysis card");
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            group.ignoreParentGroups = true;
            EditorUtility.SetDirty(group);

            FloatingHudLevelController hud =
                FindComponent<FloatingHudLevelController>(scene);
            if (hud == null)
            {
                return;
            }

            Undo.RecordObject(hud, "Remove legacy analysis card from HUD level");
            SerializedObject serializedHud = new SerializedObject(hud);
            SerializedProperty levelObjects =
                serializedHud.FindProperty("mLevelObjects");
            if (levelObjects == null)
            {
                return;
            }

            List<UnityEngine.Object> retained = new List<UnityEngine.Object>();
            for (int index = 0; index < levelObjects.arraySize; index++)
            {
                UnityEngine.Object target = levelObjects
                    .GetArrayElementAtIndex(index)
                    .objectReferenceValue;
                if (target != null && target != legacyCard.gameObject)
                {
                    retained.Add(target);
                }
            }

            levelObjects.arraySize = retained.Count;
            for (int index = 0; index < retained.Count; index++)
            {
                levelObjects.GetArrayElementAtIndex(index)
                    .objectReferenceValue = retained[index];
            }

            serializedHud.ApplyModifiedProperties();
            EditorUtility.SetDirty(hud);
        }

        private static void AddHeader(
            RectTransform themeRoot,
            Transform cardTransform)
        {
            Image header = CreateImage(
                "HeaderBand",
                themeRoot,
                LoadSprite(TopBarPath),
                HeaderColor,
                Image.Type.Sliced);
            SetTopRect(header.rectTransform, 8f, -7f, -8f, 48f);

            Image messageIcon = CreateImage(
                "BuildingIcon",
                header.transform,
                LoadSprite(MessageIconPath),
                PrimaryTextColor,
                Image.Type.Simple);
            SetTopLeftRect(
                messageIcon.rectTransform,
                17f,
                -12f,
                25f,
                25f);
            messageIcon.preserveAspect = true;

            TMP_Text title = FindText(cardTransform, "TxtBuildingName");
            if (title != null)
            {
                RectTransform rect = (RectTransform)title.transform;
                Undo.RecordObject(rect, "Style building info card title");
                rect.anchoredPosition = new Vector2(18f, -10f);
                rect.sizeDelta = new Vector2(-84f, 37f);
            }

            TMP_Text tag = CreateText(
                "GreenCityTag",
                header.transform,
                title,
                "GREEN CITY");
            RectTransform tagRect = (RectTransform)tag.transform;
            tagRect.anchorMin = new Vector2(1f, 1f);
            tagRect.anchorMax = new Vector2(1f, 1f);
            tagRect.pivot = new Vector2(1f, 1f);
            tagRect.anchoredPosition = new Vector2(-16f, -14f);
            tagRect.sizeDelta = new Vector2(92f, 20f);
            tag.alignment = TextAlignmentOptions.TopRight;
            tag.fontSize = 10f;
            tag.fontStyle = FontStyles.Bold;
            tag.color = new Color(0.68f, 0.93f, 0.80f, 0.86f);
        }

        private static void AddStoryPanel(RectTransform themeRoot)
        {
            Image storyPanel = CreateImage(
                "StoryPanel",
                themeRoot,
                LoadSprite(RowFramePath),
                new Color(0.075f, 0.23f, 0.18f, 0.82f),
                Image.Type.Sliced);
            SetTopRect(storyPanel.rectTransform, 12f, -59f, -12f, 42f);

            RectTransform accent = CreateImage(
                "StoryAccent",
                storyPanel.transform,
                null,
                AccentColor,
                Image.Type.Simple).rectTransform;
            accent.anchorMin = new Vector2(0f, 0.5f);
            accent.anchorMax = new Vector2(0f, 0.5f);
            accent.pivot = new Vector2(0f, 0.5f);
            accent.anchoredPosition = new Vector2(8f, 0f);
            accent.sizeDelta = new Vector2(3f, 24f);
        }

        private static void AddMetricRows(Transform cardTransform)
        {
            List<RectTransform> rows = new List<RectTransform>();
            for (int index = 0; index < cardTransform.childCount; index++)
            {
                RectTransform child =
                    cardTransform.GetChild(index) as RectTransform;
                if (child != null && child.name.StartsWith(
                        "Line_",
                        StringComparison.Ordinal))
                {
                    rows.Add(child);
                }
            }

            rows.Sort((left, right) =>
                right.anchoredPosition.y.CompareTo(left.anchoredPosition.y));

            Sprite rowSprite = LoadSprite(RowFramePath);
            for (int index = 0; index < rows.Count; index++)
            {
                RectTransform row = rows[index];
                Undo.RecordObject(row, "Style building info card row");
                row.sizeDelta = new Vector2(-24f, 34f);

                Image background = row.GetComponent<Image>();
                if (background == null)
                {
                    background = Undo.AddComponent<Image>(row.gameObject);
                }

                Undo.RecordObject(background, "Style building info card row");
                background.sprite = rowSprite;
                background.type = Image.Type.Sliced;
                background.color = index % 2 == 0
                    ? RowColor
                    : new Color(0.055f, 0.16f, 0.13f, 0.88f);
                background.raycastTarget = false;

                for (int childIndex = row.childCount - 1;
                     childIndex >= 0;
                     childIndex--)
                {
                    Transform child = row.GetChild(childIndex);
                    if (child.name == "MetricIcon")
                    {
                        Undo.DestroyObjectImmediate(child.gameObject);
                    }
                }

                if (index < MetricIconPaths.Length)
                {
                    Image icon = CreateImage(
                        "MetricIcon",
                        row,
                        LoadSprite(MetricIconPaths[index]),
                        index == 1
                            ? new Color(1f, 0.68f, 0.36f, 1f)
                            : AccentColor,
                        Image.Type.Simple);
                    RectTransform iconRect = icon.rectTransform;
                    iconRect.anchorMin = new Vector2(0f, 0.5f);
                    iconRect.anchorMax = new Vector2(0f, 0.5f);
                    iconRect.pivot = new Vector2(0f, 0.5f);
                    iconRect.anchoredPosition = new Vector2(11f, 0f);
                    iconRect.sizeDelta = new Vector2(18f, 18f);
                    icon.preserveAspect = true;
                }

                TMP_Text[] rowTexts = row.GetComponentsInChildren<TMP_Text>(true);
                Array.Sort(rowTexts, (left, right) =>
                    ((RectTransform)left.transform).anchoredPosition.x.CompareTo(
                        ((RectTransform)right.transform).anchoredPosition.x));

                for (int textIndex = 0;
                     textIndex < rowTexts.Length;
                     textIndex++)
                {
                    TMP_Text text = rowTexts[textIndex];
                    RectTransform textRect = (RectTransform)text.transform;
                    Undo.RecordObject(text, "Style building info card metric");
                    Undo.RecordObject(textRect, "Style building info card metric");

                    if (textIndex == 0)
                    {
                        textRect.offsetMin = new Vector2(39f, textRect.offsetMin.y);
                        text.fontSize = 12f;
                        text.fontStyle = FontStyles.Normal;
                        text.color = SecondaryTextColor;
                    }
                    else
                    {
                        textRect.offsetMax = new Vector2(-12f, textRect.offsetMax.y);
                        text.fontSize = 15f;
                        text.fontStyle = FontStyles.Bold;
                        text.color = PrimaryTextColor;
                    }
                }
            }
        }

        private static void AddFooter(
            RectTransform themeRoot,
            Transform cardTransform)
        {
            TMP_Text title = FindText(cardTransform, "TxtBuildingName");
            TMP_Text footer = CreateText(
                "LiveDataLabel",
                themeRoot,
                title,
                "LIVE CITY DATA");
            RectTransform footerRect = (RectTransform)footer.transform;
            footerRect.anchorMin = new Vector2(0f, 0f);
            footerRect.anchorMax = new Vector2(1f, 0f);
            footerRect.pivot = new Vector2(0.5f, 0f);
            footerRect.anchoredPosition = new Vector2(0f, 8f);
            footerRect.sizeDelta = new Vector2(-30f, 18f);
            footer.alignment = TextAlignmentOptions.BottomRight;
            footer.fontSize = 9f;
            footer.fontStyle = FontStyles.Bold;
            footer.color = new Color(0.42f, 0.78f, 0.60f, 0.76f);

            Image footerLine = CreateImage(
                "FooterLine",
                themeRoot,
                null,
                new Color(0.20f, 0.72f, 0.49f, 0.54f),
                Image.Type.Simple);
            RectTransform lineRect = footerLine.rectTransform;
            lineRect.anchorMin = new Vector2(0f, 0f);
            lineRect.anchorMax = new Vector2(1f, 0f);
            lineRect.pivot = new Vector2(0.5f, 0f);
            lineRect.anchoredPosition = new Vector2(0f, 4f);
            lineRect.sizeDelta = new Vector2(-30f, 2f);
        }

        private static void StyleText(Transform cardTransform)
        {
            TMP_Text title = FindText(cardTransform, "TxtBuildingName");
            if (title != null)
            {
                Undo.RecordObject(title, "Style building info card title");
                title.fontSize = 20f;
                title.fontStyle = FontStyles.Bold;
                title.color = PrimaryTextColor;
                title.alignment = TextAlignmentOptions.MidlineLeft;
            }

            TMP_Text story = FindText(cardTransform, "TxtStoryComment");
            if (story != null)
            {
                Undo.RecordObject(story, "Style building info card story");
                RectTransform storyRect = (RectTransform)story.transform;
                Undo.RecordObject(storyRect, "Style building info card story");
                storyRect.anchoredPosition = new Vector2(12f, -61f);
                storyRect.sizeDelta = new Vector2(-58f, 36f);
                story.fontSize = 12.5f;
                story.fontStyle = FontStyles.Italic;
                story.color = new Color(0.76f, 0.90f, 0.82f, 1f);
                story.alignment = TextAlignmentOptions.MidlineLeft;
                story.textWrappingMode = TextWrappingModes.Normal;
            }
        }

        private static bool TryFindCard(
            Scene scene,
            out BuildingInfoCardController card)
        {
            card = null;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return false;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                BuildingInfoCardController[] cards =
                    root.GetComponentsInChildren<BuildingInfoCardController>(true);
                foreach (BuildingInfoCardController candidate in cards)
                {
                    if (candidate.name != CardName)
                    {
                        continue;
                    }

                    card = candidate;
                    return true;
                }
            }

            return false;
        }

        private static T FindComponent<T>(
            Scene scene,
            string objectName = null) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T[] components = root.GetComponentsInChildren<T>(true);
                foreach (T component in components)
                {
                    if (string.IsNullOrEmpty(objectName) ||
                        component.name == objectName)
                    {
                        return component;
                    }
                }
            }

            return null;
        }

        private static Sprite LoadSprite(string path)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                Debug.LogWarning(
                    $"[BuildingInfoCardStyleBaker] Missing Layer Lab sprite: {path}");
            }

            return sprite;
        }

        private static TMP_Text FindText(Transform root, string objectName)
        {
            TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text text in texts)
            {
                if (text.name == objectName)
                {
                    return text;
                }
            }

            return null;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject gameObject = new GameObject(
                name,
                typeof(RectTransform));
            gameObject.layer = parent.gameObject.layer;
            Undo.RegisterCreatedObjectUndo(gameObject, "Style building info card");
            RectTransform rect = (RectTransform)gameObject.transform;
            Undo.SetTransformParent(rect, parent, "Style building info card");
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            return rect;
        }

        private static Image CreateImage(
            string name,
            Transform parent,
            Sprite sprite,
            Color color,
            Image.Type type)
        {
            GameObject gameObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            gameObject.layer = parent.gameObject.layer;
            Undo.RegisterCreatedObjectUndo(gameObject, "Style building info card");
            RectTransform rect = (RectTransform)gameObject.transform;
            Undo.SetTransformParent(rect, parent, "Style building info card");
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            Image image = gameObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.type = type;
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text CreateText(
            string name,
            Transform parent,
            TMP_Text template,
            string value)
        {
            GameObject gameObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            gameObject.layer = parent.gameObject.layer;
            Undo.RegisterCreatedObjectUndo(gameObject, "Style building info card");
            RectTransform rect = (RectTransform)gameObject.transform;
            Undo.SetTransformParent(rect, parent, "Style building info card");
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            TMP_Text text = gameObject.GetComponent<TMP_Text>();
            text.text = value;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            if (template != null)
            {
                text.font = template.font;
                text.fontSharedMaterial = template.fontSharedMaterial;
            }

            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        private static void SetTopRect(
            RectTransform rect,
            float left,
            float top,
            float right,
            float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(
                (left + right) * 0.5f,
                top);
            rect.sizeDelta = new Vector2(-(left - right), height);
        }

        private static void SetTopLeftRect(
            RectTransform rect,
            float left,
            float top,
            float width,
            float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(left, top);
            rect.sizeDelta = new Vector2(width, height);
        }

        // Unity setup: open the target test scene and run
        // Tools > GreenLight > UI > Bake Building Info Card Style.
    }
}
