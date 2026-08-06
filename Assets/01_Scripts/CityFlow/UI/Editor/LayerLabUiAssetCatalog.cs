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
            Image background = toggle.targetGraphic as Image;
            if (background == null)
            {
                background = GetOrAddComponent<Image>(toggle.gameObject);
                toggle.targetGraphic = background;
            }

            ApplyImage(background, offSprite, Color.white, false);
            background.raycastTarget = true;

            Image checkmark = toggle.graphic as Image;
            if (checkmark != null)
            {
                ApplyImage(checkmark, onSprite, Color.white, false);
                checkmark.raycastTarget = false;
            }

            TMP_Text text = toggle.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                if (label != null)
                {
                    text.text = label;
                }

                StyleText(text, 16f, Color.white, FontStyles.Bold);
            }
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
