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

            WeeklySettlementPopupBaker.Bake();
            BakeButton(activeScene);
        }

        private static void BakeButton(Scene activeScene)
        {
            CoinHarvestButton existingButton = FindInActiveScene<CoinHarvestButton>(activeScene);

            if (existingButton != null)
            {
                Selection.activeGameObject = existingButton.gameObject;
                EditorGUIUtility.PingObject(existingButton.gameObject);
                Debug.Log("[ManualCoinHarvestUiBaker] The active scene already contains a coin harvest button.");
                return;
            }

            Transform topBar = FindTransform(activeScene, TopBarName);

            if (topBar == null)
            {
                Debug.LogError(
                    $"[ManualCoinHarvestUiBaker] '{TopBarName}' was not found in " +
                    $"scene '{activeScene.name}'.");
                return;
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
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-18f, -8f);
            rect.sizeDelta = new Vector2(184f, 40f);

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
            CoinHarvestButton controller = root.AddComponent<CoinHarvestButton>();
            controller.Configure(button, label);

            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(activeScene);
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log(
                $"[ManualCoinHarvestUiBaker] Coin harvest UI baked below " +
                $"'{TopBarName}' in scene '{activeScene.name}'.");
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

        private static Transform FindTransform(Scene scene, string targetName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform[] transforms = root.GetComponentsInChildren<Transform>(true);

                foreach (Transform candidate in transforms)
                {
                    if (candidate.name == targetName)
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        private static T FindInActiveScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);

                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        // Unity setup: Open the target scene, then use Tools > GreenLight > UI > Bake Manual Coin Harvest UI.
    }
}
