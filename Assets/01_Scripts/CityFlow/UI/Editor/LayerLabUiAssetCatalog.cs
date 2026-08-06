using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CityFlow.UI.Editor
{
    internal static class LayerLabUiAssetCatalog
    {
        internal const string GeonScenePath =
            "Assets/00_Scenes/CityFlowIntegrated_Geon.unity";
        internal const string CameraButtonPrefabPath =
            "Assets/02_Prefabs/UI/UI_CameraRotationButton.prefab";
        internal const string CongestionTogglePrefabPath =
            "Assets/02_Prefabs/UI/UI_CongestionToggle.prefab";
        internal const string AudioMixerPath =
            "Assets/04_Audio/Mixers/CityAudioMixer.mixer";

        private const string SpriteRoot =
            "Assets/99_Download/Layer Lab/GUI-MonoRound/ResourcesData/Sprites/Components/";
        private const string FontPath =
            "Assets/99_Download/Fonts/NanumGothic SDF.asset";

        internal static TMP_FontAsset Font =>
            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

        internal static Sprite LoadSprite(string relativePath)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                SpriteRoot + relativePath);
            if (sprite == null)
            {
                Debug.LogError(
                    $"[LayerLabUiAssetCatalog] Layer Lab sprite not found: " +
                    $"{SpriteRoot}{relativePath}");
            }

            return sprite;
        }

        internal static bool TryGetGeonScene(out Scene scene)
        {
            scene = EditorSceneManager.GetActiveScene();
            if (scene.IsValid() &&
                string.Equals(
                    scene.path,
                    GeonScenePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            Debug.LogError(
                $"[LayerLabUiAssetCatalog] Active scene must be " +
                $"'{GeonScenePath}'. Current: '{scene.path}'.");
            return false;
        }

        internal static Transform FindInGeonScene(string hierarchyPath)
        {
            if (!TryGetGeonScene(out Scene scene))
            {
                return null;
            }

            string[] segments = hierarchyPath.Split('/');
            GameObject[] roots = scene.GetRootGameObjects();
            Transform current = null;
            for (int index = 0; index < roots.Length; index++)
            {
                if (roots[index].name == segments[0])
                {
                    current = roots[index].transform;
                    break;
                }
            }

            for (int index = 1; current != null && index < segments.Length; index++)
            {
                current = current.Find(segments[index]);
            }

            if (current == null)
            {
                Debug.LogError(
                    $"[LayerLabUiAssetCatalog] Object not found in Geon scene: " +
                    hierarchyPath);
            }

            return current;
        }

        internal static T GetOrAddComponent<T>(GameObject target)
            where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(target);
        }

        internal static void ApplyImage(
            Image image,
            Sprite sprite,
            Color color,
            bool sliced = true)
        {
            if (image == null)
            {
                return;
            }

            image.sprite = sprite;
            image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
        }

        internal static void StyleText(
            TMP_Text text,
            float fontSize,
            Color color,
            FontStyles style = FontStyles.Normal)
        {
            if (text == null)
            {
                return;
            }

            if (Font != null)
            {
                text.font = Font;
            }

            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.raycastTarget = false;
        }

        internal static void StyleButton(
            Button button,
            Sprite sprite,
            string label = null,
            Color? labelColor = null)
        {
            if (button == null)
            {
                return;
            }

            Image image = GetOrAddComponent<Image>(button.gameObject);
            ApplyImage(image, sprite, Color.white);
            image.raycastTarget = true;
            button.targetGraphic = image;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.88f, 0.96f, 1f, 1f);
            colors.pressedColor = new Color(0.7f, 0.8f, 0.88f, 1f);
            colors.disabledColor = new Color(0.45f, 0.48f, 0.52f, 0.75f);
            button.colors = colors;

            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                if (label != null)
                {
                    text.text = label;
                }

                StyleText(
                    text,
                    16f,
                    labelColor ?? Color.white,
                    FontStyles.Bold);
                text.alignment = TextAlignmentOptions.Center;
            }
        }

        internal static void StyleToggle(
            Toggle toggle,
            string label = null)
        {
            if (toggle == null)
            {
                return;
            }

            Sprite offSprite = LoadSprite("UI_Etc/Toggle_Square_l_off.png");
            Sprite onSprite = LoadSprite("UI_Etc/Toggle_Square_l_on.png");
            Transform backgroundTransform = toggle.transform.Find("Background");
            Image background = backgroundTransform != null
                ? GetOrAddComponent<Image>(backgroundTransform.gameObject)
                : toggle.targetGraphic as Image;
            if (background == null)
            {
                GameObject backgroundObject = new GameObject(
                    "Background",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                backgroundObject.transform.SetParent(toggle.transform, false);
                Undo.RegisterCreatedObjectUndo(
                    backgroundObject,
                    "Create Toggle Background");
                background = backgroundObject.GetComponent<Image>();
            }

            toggle.targetGraphic = background;
            ApplyImage(
                background,
                offSprite,
                Color.white,
                false);
            RectTransform backgroundRect = background.rectTransform;
            backgroundRect.anchorMin = new Vector2(0f, 0.5f);
            backgroundRect.anchorMax = new Vector2(0f, 0.5f);
            backgroundRect.pivot = new Vector2(0.5f, 0.5f);
            backgroundRect.anchoredPosition = new Vector2(18f, 0f);
            backgroundRect.sizeDelta = new Vector2(32f, 32f);
            background.preserveAspect = true;
            background.raycastTarget = true;

            Outline outline = GetOrAddComponent<Outline>(
                background.gameObject);
            outline.effectColor = new Color(0.45f, 1f, 0.86f, 1f);
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = false;
            StyleBrightToggleFrame(background.transform);

            Image checkmark = toggle.graphic as Image;
            if (checkmark == null)
            {
                Transform checkmarkTransform =
                    background.transform.Find("Checkmark");
                GameObject checkmarkObject = checkmarkTransform != null
                    ? checkmarkTransform.gameObject
                    : new GameObject(
                        "Checkmark",
                        typeof(RectTransform),
                        typeof(CanvasRenderer),
                        typeof(Image));
                if (checkmarkTransform == null)
                {
                    checkmarkObject.transform.SetParent(
                        background.transform,
                        false);
                    Undo.RegisterCreatedObjectUndo(
                        checkmarkObject,
                        "Create Toggle Checkmark");
                }

                checkmark = GetOrAddComponent<Image>(checkmarkObject);
            }

            if (checkmark != null)
            {
                ApplyImage(checkmark, onSprite, Color.white, false);
                RectTransform checkmarkRect = checkmark.rectTransform;
                checkmarkRect.anchorMin = Vector2.zero;
                checkmarkRect.anchorMax = Vector2.one;
                checkmarkRect.offsetMin = Vector2.zero;
                checkmarkRect.offsetMax = Vector2.zero;
                checkmark.preserveAspect = true;
                checkmark.raycastTarget = false;
                toggle.graphic = checkmark;
            }

            TMP_Text text = toggle.GetComponentInChildren<TMP_Text>(true);
            if (text == null)
            {
                Text legacyText = toggle.GetComponentInChildren<Text>(true);
                GameObject labelObject;
                if (legacyText != null)
                {
                    labelObject = legacyText.gameObject;
                    Undo.DestroyObjectImmediate(legacyText);
                }
                else
                {
                    labelObject = new GameObject(
                        "Label",
                        typeof(RectTransform),
                        typeof(CanvasRenderer));
                    labelObject.transform.SetParent(toggle.transform, false);
                    Undo.RegisterCreatedObjectUndo(
                        labelObject,
                        "Create Toggle Label");
                }

                text = GetOrAddComponent<TextMeshProUGUI>(labelObject);
            }

            if (text != null)
            {
                if (label != null)
                {
                    text.text = label;
                }

                StyleText(text, 16f, Color.white, FontStyles.Bold);
                text.alignment = TextAlignmentOptions.MidlineLeft;
                text.raycastTarget = false;
                RectTransform labelRect = text.rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(42f, 0f);
                labelRect.offsetMax = new Vector2(-4f, 0f);
            }
        }

        private static void StyleBrightToggleFrame(Transform parent)
        {
            Transform frameTransform = parent.Find("BrightFrame");
            GameObject frameObject = frameTransform != null
                ? frameTransform.gameObject
                : new GameObject("BrightFrame", typeof(RectTransform));
            if (frameTransform == null)
            {
                frameObject.transform.SetParent(parent, false);
                Undo.RegisterCreatedObjectUndo(
                    frameObject,
                    "Create Bright Toggle Frame");
            }

            RectTransform frameRect =
                frameObject.GetComponent<RectTransform>();
            frameRect.anchorMin = Vector2.zero;
            frameRect.anchorMax = Vector2.one;
            frameRect.offsetMin = new Vector2(2f, 2f);
            frameRect.offsetMax = new Vector2(-2f, -2f);
            frameObject.transform.SetAsLastSibling();

            Color frameColor = new Color(0.45f, 1f, 0.86f, 1f);
            StyleToggleFrameEdge(
                frameObject.transform,
                "Top",
                new Vector2(0f, 1f),
                Vector2.one,
                new Vector2(0f, 3f),
                frameColor);
            StyleToggleFrameEdge(
                frameObject.transform,
                "Bottom",
                Vector2.zero,
                new Vector2(1f, 0f),
                new Vector2(0f, 3f),
                frameColor);
            StyleToggleFrameEdge(
                frameObject.transform,
                "Left",
                Vector2.zero,
                new Vector2(0f, 1f),
                new Vector2(3f, 0f),
                frameColor);
            StyleToggleFrameEdge(
                frameObject.transform,
                "Right",
                new Vector2(1f, 0f),
                Vector2.one,
                new Vector2(3f, 0f),
                frameColor);
        }

        private static void StyleToggleFrameEdge(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 sizeDelta,
            Color color)
        {
            Transform edgeTransform = parent.Find(name);
            GameObject edgeObject = edgeTransform != null
                ? edgeTransform.gameObject
                : new GameObject(
                    name,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
            if (edgeTransform == null)
            {
                edgeObject.transform.SetParent(parent, false);
                Undo.RegisterCreatedObjectUndo(
                    edgeObject,
                    "Create Toggle Frame Edge");
            }

            RectTransform edgeRect =
                edgeObject.GetComponent<RectTransform>();
            edgeRect.anchorMin = anchorMin;
            edgeRect.anchorMax = anchorMax;
            edgeRect.pivot = new Vector2(0.5f, 0.5f);
            edgeRect.anchoredPosition = Vector2.zero;
            edgeRect.sizeDelta = sizeDelta;

            Image image = GetOrAddComponent<Image>(edgeObject);
            image.sprite = null;
            image.color = color;
            image.raycastTarget = false;
        }

        internal static void CompleteSceneChange(GameObject root, string label)
        {
            if (root == null || root.scene != EditorSceneManager.GetActiveScene())
            {
                return;
            }

            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log($"[{label}] Applied to {root.name} in {root.scene.path}.");
        }
    }
}
