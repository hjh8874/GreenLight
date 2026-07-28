using CityFlow.Content;
using CityFlow.Content.Transit;
using CityFlow.DebugTools;
using CityFlow.Gameplay.Save;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CityFlow.EditorTools
{
    public static class ContentFeaturePrototypeBaker
    {
        private const string SourceScene =
            "Assets/00_Scenes/Debug/CityFlowIntegrated_TerrainDecoration_han.unity";
        private const string TargetScene =
            "Assets/00_Scenes/Debug/PR151_ContentPrototype_cmt.unity";
        private const string PrefabPath =
            "Assets/02_Prefabs/Vehicles/CityBusContent.prefab";
        private const string BusConfigPath =
            "Assets/05_ScriptableObjects/CityFlow/Transit/CityBusDefinition.asset";
        private const string EmergencyConfigPath =
            "Assets/05_ScriptableObjects/CityFlow/Emergency/EmergencyIncidentConfig.asset";
        private const string FontPath =
            "Assets/03_Art/Fonts/NanumGothic SDF.asset";
        private const string BusVisualPrefabPath =
            "Assets/99_Download/SimpleTown/Prefabs/Vehicles/bus_blue.prefab";
        private const string BusMaterialPath =
            "Assets/03_Art/Materials/Vehicles/CityBus_URP.mat";

        private static readonly Color PanelColor =
            new(0.028f, 0.045f, 0.041f, 0.985f);
        private static readonly Color HeaderColor =
            new(0.045f, 0.085f, 0.071f, 1f);
        private static readonly Color CardColor =
            new(0.038f, 0.060f, 0.054f, 0.96f);
        private static readonly Color AccentColor =
            new(0.32f, 0.92f, 0.52f, 1f);
        private static readonly Color CyanColor =
            new(0.30f, 0.75f, 0.92f, 1f);
        private static readonly Color TextColor =
            new(0.91f, 0.97f, 0.94f, 1f);
        private static readonly Color MutedTextColor =
            new(0.63f, 0.84f, 0.73f, 1f);
        private static readonly Color DividerColor =
            new(0.16f, 0.32f, 0.26f, 0.85f);

        [MenuItem(
            "Tools/GreenLight/Content/Build City Bus Prototype")]
        public static void Build()
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    SourceScene))
            {
                Debug.LogError(
                    $"[ContentPrototypeBaker] Source Debug scene is missing: {SourceScene}");
                return;
            }

            TMP_FontAsset font =
                AssetDatabase.LoadAssetAtPath<
                    TMP_FontAsset>(FontPath);
            GameObject busVisualPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    BusVisualPrefabPath);
            Material busMaterial =
                CreateOrUpdateBusMaterial();

            if (font == null)
            {
                Debug.LogError(
                    $"[ContentPrototypeBaker] Required UI font is missing: {FontPath}");
                return;
            }

            if (busVisualPrefab == null)
            {
                Debug.LogError(
                    $"[ContentPrototypeBaker] Required bus visual is missing: {BusVisualPrefabPath}");
                return;
            }

            if (busMaterial == null)
            {
                return;
            }

            BusDefinitionSO busDefinition =
                CreateOrUpdateBusDefinition();
            EmergencyIncidentConfigSO emergencyConfig =
                CreateOrUpdateEmergencyConfig();

            CreateOrUpdatePrefab(
                busDefinition,
                emergencyConfig,
                font,
                busVisualPrefab,
                busMaterial);
            CreateOrUpdateScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "[ContentPrototypeBaker] Integrated-style prefab, configs, and copied Debug scene are ready.");
        }

        private static BusDefinitionSO
            CreateOrUpdateBusDefinition()
        {
            BusDefinitionSO asset =
                AssetDatabase.LoadAssetAtPath<
                    BusDefinitionSO>(BusConfigPath);

            if (asset == null)
            {
                asset =
                    ScriptableObject.CreateInstance<
                        BusDefinitionSO>();
                AssetDatabase.CreateAsset(
                    asset,
                    BusConfigPath);
            }

            SerializedObject serialized = new(asset);
            serialized.FindProperty("busId").stringValue =
                "prototype_city_bus";
            serialized.FindProperty("displayName").stringValue =
                "GreenLight City Bus";
            serialized.FindProperty("busType").enumValueIndex =
                (int)BusType.CityBus;
            serialized.FindProperty("secondsPerTile").floatValue =
                0.22f;
            serialized.FindProperty("stopWaitSeconds").floatValue =
                0.8f;
            serialized.FindProperty("passengerCapacity").intValue =
                20;
            serialized.FindProperty("boardingDemandPerStop").intValue =
                4;
            serialized.FindProperty("leavingDemandPerStop").intValue =
                2;
            serialized.FindProperty("routeColor").colorValue =
                CyanColor;

            SerializedProperty stops =
                serialized.FindProperty("initialStops");
            Vector2Int[] values =
            {
                new(5, 12),
                new(13, 11),
                new(8, 16),
                new(9, 8)
            };
            stops.arraySize = values.Length;

            for (int i = 0; i < values.Length; i++)
            {
                stops.GetArrayElementAtIndex(i)
                    .vector2IntValue = values[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static EmergencyIncidentConfigSO
            CreateOrUpdateEmergencyConfig()
        {
            EmergencyIncidentConfigSO asset =
                AssetDatabase.LoadAssetAtPath<
                    EmergencyIncidentConfigSO>(
                    EmergencyConfigPath);

            if (asset == null)
            {
                asset =
                    ScriptableObject.CreateInstance<
                        EmergencyIncidentConfigSO>();
                AssetDatabase.CreateAsset(
                    asset,
                    EmergencyConfigPath);
            }

            SerializedObject serialized = new(asset);
            serialized.FindProperty("minimumSpawnInterval")
                .floatValue = 5f;
            serialized.FindProperty("maximumSpawnInterval")
                .floatValue = 8f;
            serialized.FindProperty("maximumActiveIncidents")
                .intValue = 3;
            serialized.FindProperty("houseWeight").floatValue =
                1f;
            serialized.FindProperty("officeWeight").floatValue =
                1f;
            serialized.FindProperty("travelSecondsPerTile")
                .floatValue = 0.12f;
            serialized.FindProperty("treatmentSeconds")
                .floatValue = 1.2f;
            serialized.FindProperty("ambulancesPerHospital")
                .intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static Material CreateOrUpdateBusMaterial()
        {
            Shader shader =
                Shader.Find(
                    "GreenLight/CityFlow Opaque Unlit");

            if (shader == null)
            {
                Debug.LogError(
                    "[ContentPrototypeBaker] CityFlow URP shader is missing.");
                return null;
            }

            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(
                    BusMaterialPath);

            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "CityBus_URP"
                };
                AssetDatabase.CreateAsset(
                    material,
                    BusMaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            Color busColor =
                new(0.16f, 0.72f, 0.78f, 1f);
            material.SetColor("_BaseColor", busColor);
            material.SetColor("_Color", busColor);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void CreateOrUpdatePrefab(
            BusDefinitionSO busDefinition,
            EmergencyIncidentConfigSO emergencyConfig,
            TMP_FontAsset font,
            GameObject busVisualPrefab,
            Material busMaterial)
        {
            var root =
                new GameObject("CityBusContent");

            try
            {
                BusRoute route =
                    root.AddComponent<BusRoute>();
                BusStopRegistry registry =
                    root.GetComponent<BusStopRegistry>() ??
                    root.AddComponent<BusStopRegistry>();
                CityBusService cityBus =
                    root.GetComponent<CityBusService>() ??
                    root.AddComponent<CityBusService>();
                EmergencyIncidentSystem emergency =
                    root.AddComponent<
                        EmergencyIncidentSystem>();
                CityBusWorldView busWorldView =
                    root.AddComponent<CityBusWorldView>();
                CityBusStopWorldView busStopWorldView =
                    root.AddComponent<CityBusStopWorldView>();

                SetObjectReference(
                    cityBus,
                    "definition",
                    busDefinition);
                SetObjectReference(
                    cityBus,
                    "busRoute",
                    route);
                SetObjectReference(
                    cityBus,
                    "stopRegistry",
                    registry);
                SetObjectReference(
                    emergency,
                    "config",
                    emergencyConfig);
                SetObjectReference(
                    busWorldView,
                    "busRoute",
                    route);
                SetObjectReference(
                    busWorldView,
                    "busVisualPrefab",
                    busVisualPrefab);
                SetObjectReference(
                    busWorldView,
                    "busMaterial",
                    busMaterial);
                SetObjectReference(
                    busStopWorldView,
                    "stopRegistry",
                    registry);
                SetObjectReference(
                    busStopWorldView,
                    "stationMaterial",
                    busMaterial);

                SerializedObject busWorldSerialized =
                    new(busWorldView);
                busWorldSerialized.FindProperty(
                        "movementDuration")
                    .floatValue =
                    busDefinition.SecondsPerTile;
                busWorldSerialized.FindProperty("laneOffset")
                    .floatValue = 0.18f;
                busWorldSerialized
                    .ApplyModifiedPropertiesWithoutUndo();

                SerializedObject cityBusSerialized =
                    new(cityBus);
                cityBusSerialized.FindProperty("autoStart")
                    .boolValue = true;
                cityBusSerialized
                    .ApplyModifiedPropertiesWithoutUndo();

                SerializedObject routeSerialized =
                    new(route);
                routeSerialized.FindProperty("secondsPerTile")
                    .floatValue = busDefinition.SecondsPerTile;
                routeSerialized.FindProperty("stopWaitSeconds")
                    .floatValue = busDefinition.StopWaitSeconds;
                routeSerialized.FindProperty("loopRoute")
                    .boolValue = true;
                routeSerialized.FindProperty("autoStart")
                    .boolValue = false;
                routeSerialized.FindProperty(
                        "avoidImmediateUTurn")
                    .boolValue = true;
                routeSerialized
                    .ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(
                    root,
                    PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }



        private static void CreateHeader(
            RectTransform panel,
            TMP_FontAsset font)
        {
            GameObject headerObject = CreateUiObject(
                "Header",
                panel,
                typeof(Image));
            RectTransform header =
                headerObject.GetComponent<RectTransform>();
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.anchoredPosition = Vector2.zero;
            header.sizeDelta = new Vector2(0f, 84f);
            Image headerImage =
                headerObject.GetComponent<Image>();
            headerImage.color = HeaderColor;
            headerImage.raycastTarget = false;

            TMP_Text title = CreateText(
                header,
                "Title",
                "CITY OPERATIONS",
                new Vector2(22f, -17f),
                new Vector2(260f, 30f),
                22f,
                FontStyles.Bold,
                TextColor,
                TextAlignmentOptions.MidlineLeft,
                font);
            SetTopLeft(title.rectTransform);

            TMP_Text subtitle = CreateText(
                header,
                "Subtitle",
                "EMERGENCY RESPONSE CONTROL",
                new Vector2(23f, -50f),
                new Vector2(250f, 20f),
                10f,
                FontStyles.Bold,
                new Color(0.36f, 0.70f, 0.58f, 1f),
                TextAlignmentOptions.MidlineLeft,
                font);
            SetTopLeft(subtitle.rectTransform);

            CreateDot(
                header,
                "LiveDot",
                new Vector2(-60f, -31f),
                AccentColor);

            TMP_Text live = CreateText(
                header,
                "LiveText",
                "LIVE",
                new Vector2(-20f, -33f),
                new Vector2(40f, 18f),
                10f,
                FontStyles.Bold,
                MutedTextColor,
                TextAlignmentOptions.Center,
                font);
            SetTopRight(live.rectTransform);

            GameObject divider = CreateUiObject(
                "HeaderDivider",
                panel,
                typeof(Image));
            RectTransform dividerRect =
                divider.GetComponent<RectTransform>();
            dividerRect.anchorMin = new Vector2(0f, 1f);
            dividerRect.anchorMax = new Vector2(1f, 1f);
            dividerRect.pivot = new Vector2(0.5f, 1f);
            dividerRect.anchoredPosition =
                new Vector2(0f, -84f);
            dividerRect.sizeDelta = new Vector2(0f, 1f);
            Image dividerImage =
                divider.GetComponent<Image>();
            dividerImage.color = DividerColor;
            dividerImage.raycastTarget = false;
        }

        private static RectTransform CreateCard(
            RectTransform parent,
            string name,
            Vector2 position,
            Vector2 size,
            Color accent,
            string icon,
            string title,
            string subtitle,
            TMP_FontAsset font)
        {
            GameObject cardObject = CreateUiObject(
                name,
                parent,
                typeof(Image));
            RectTransform card =
                cardObject.GetComponent<RectTransform>();
            card.anchorMin = new Vector2(0f, 1f);
            card.anchorMax = new Vector2(0f, 1f);
            card.pivot = new Vector2(0f, 1f);
            card.anchoredPosition = position;
            card.sizeDelta = size;
            Image cardImage =
                cardObject.GetComponent<Image>();
            cardImage.color = CardColor;
            cardImage.raycastTarget = false;

            GameObject accentObject = CreateUiObject(
                "Accent",
                card,
                typeof(Image));
            RectTransform accentRect =
                accentObject.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.anchoredPosition = Vector2.zero;
            accentRect.sizeDelta = new Vector2(4f, 0f);
            Image accentImage =
                accentObject.GetComponent<Image>();
            accentImage.color = accent;
            accentImage.raycastTarget = false;

            GameObject iconObject = CreateUiObject(
                "Icon",
                card,
                typeof(Image));
            RectTransform iconRect =
                iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0f, 1f);
            iconRect.anchorMax = new Vector2(0f, 1f);
            iconRect.pivot = new Vector2(0f, 1f);
            iconRect.anchoredPosition =
                new Vector2(20f, -18f);
            iconRect.sizeDelta = new Vector2(40f, 40f);
            Image iconImage =
                iconObject.GetComponent<Image>();
            iconImage.color =
                new Color(
                    accent.r,
                    accent.g,
                    accent.b,
                    0.28f);
            iconImage.raycastTarget = false;

            CreateText(
                iconRect,
                "IconLabel",
                icon,
                Vector2.zero,
                Vector2.zero,
                16f,
                FontStyles.Bold,
                TextColor,
                TextAlignmentOptions.Center,
                font,
                true);

            TMP_Text titleText = CreateText(
                card,
                "Title",
                title,
                new Vector2(72f, -17f),
                new Vector2(170f, 22f),
                15f,
                FontStyles.Bold,
                TextColor,
                TextAlignmentOptions.MidlineLeft,
                font);
            SetTopLeft(titleText.rectTransform);

            TMP_Text subtitleText = CreateText(
                card,
                "Subtitle",
                subtitle,
                new Vector2(72f, -41f),
                new Vector2(190f, 18f),
                9f,
                FontStyles.Bold,
                MutedTextColor,
                TextAlignmentOptions.MidlineLeft,
                font);
            SetTopLeft(subtitleText.rectTransform);

            return card;
        }

        private static Image CreateProgressBar(
            Transform parent,
            Vector2 position,
            Vector2 size,
            Color fillColor)
        {
            GameObject trackObject = CreateUiObject(
                "PassengerTrack",
                parent,
                typeof(Image));
            RectTransform track =
                trackObject.GetComponent<RectTransform>();
            track.anchorMin = new Vector2(0f, 1f);
            track.anchorMax = new Vector2(0f, 1f);
            track.pivot = new Vector2(0f, 1f);
            track.anchoredPosition = position;
            track.sizeDelta = size;
            Image trackImage =
                trackObject.GetComponent<Image>();
            trackImage.color =
                new Color(0.13f, 0.22f, 0.19f, 1f);
            trackImage.raycastTarget = false;

            GameObject fillObject = CreateUiObject(
                "PassengerFill",
                track,
                typeof(Image));
            RectTransform fillRect =
                fillObject.GetComponent<RectTransform>();
            SetStretch(fillRect);
            Image fill =
                fillObject.GetComponent<Image>();
            fill.color = fillColor;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.fillAmount = 0f;
            fill.raycastTarget = false;
            return fill;
        }

        private static Image CreateDot(
            Transform parent,
            string name,
            Vector2 position,
            Color color)
        {
            GameObject dotObject = CreateUiObject(
                name,
                parent,
                typeof(Image));
            RectTransform dot =
                dotObject.GetComponent<RectTransform>();
            dot.anchorMin = new Vector2(1f, 1f);
            dot.anchorMax = new Vector2(1f, 1f);
            dot.pivot = new Vector2(1f, 1f);
            dot.anchoredPosition = position;
            dot.sizeDelta = new Vector2(8f, 8f);
            Image image =
                dotObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Button CreateButton(
            Transform parent,
            string name,
            string label,
            Vector2 position,
            Vector2 size,
            Color color,
            TMP_FontAsset font)
        {
            GameObject buttonObject = CreateUiObject(
                name,
                parent,
                typeof(Image),
                typeof(Button));
            RectTransform rect =
                buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Image image =
                buttonObject.GetComponent<Image>();
            image.color =
                new Color(
                    color.r,
                    color.g,
                    color.b,
                    0.22f);

            Button button =
                buttonObject.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor =
                new Color(1f, 1f, 1f, 1.25f);
            colors.pressedColor =
                new Color(0.78f, 0.88f, 0.83f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor =
                new Color(0.4f, 0.4f, 0.4f, 0.45f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            TMP_Text text = CreateText(
                rect,
                "Label",
                label,
                Vector2.zero,
                Vector2.zero,
                11f,
                FontStyles.Bold,
                TextColor,
                TextAlignmentOptions.Center,
                font,
                true);
            text.raycastTarget = false;
            return button;
        }

        private static TMP_Text CreateText(
            Transform parent,
            string name,
            string value,
            Vector2 anchoredPosition,
            Vector2 size,
            float fontSize,
            FontStyles style,
            Color color,
            TextAlignmentOptions alignment,
            TMP_FontAsset font = null,
            bool stretch = false)
        {
            GameObject textObject = CreateUiObject(
                name,
                parent,
                typeof(TextMeshProUGUI));
            RectTransform rect =
                textObject.GetComponent<RectTransform>();

            if (stretch)
            {
                SetStretch(rect);
            }
            else
            {
                rect.anchorMin =
                    new Vector2(0.5f, 0.5f);
                rect.anchorMax =
                    new Vector2(0.5f, 0.5f);
                rect.pivot =
                    new Vector2(0.5f, 0.5f);
                rect.anchoredPosition =
                    anchoredPosition;
                rect.sizeDelta = size;
            }

            TextMeshProUGUI text =
                textObject.GetComponent<
                    TextMeshProUGUI>();
            text.font = font ??
                AssetDatabase.LoadAssetAtPath<
                    TMP_FontAsset>(FontPath);
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode =
                TextWrappingModes.NoWrap;
            text.overflowMode =
                TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private static void CreateOrUpdateScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    TargetScene) != null)
            {
                FileUtil.ReplaceFile(
                    SourceScene,
                    TargetScene);
                AssetDatabase.ImportAsset(
                    TargetScene,
                    ImportAssetOptions.ForceUpdate);
            }
            else if (!AssetDatabase.CopyAsset(
                         SourceScene,
                         TargetScene))
            {
                throw new System.InvalidOperationException(
                    $"Could not copy {SourceScene}.");
            }

            Scene scene =
                EditorSceneManager.OpenScene(
                    TargetScene,
                    OpenSceneMode.Single);

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    PrefabPath);
            GameObject instance =
                PrefabUtility.InstantiatePrefab(
                    prefab,
                    scene) as GameObject;

            if (instance == null)
            {
                throw new System.InvalidOperationException(
                    "Could not instantiate the prototype prefab.");
            }

            Undo.RegisterCreatedObjectUndo(
                instance,
                "Add PR151 content prototype");

            var scenarioObject =
                new GameObject("PR151_DebugPrototypeScenario");
            SceneManager.MoveGameObjectToScene(
                scenarioObject,
                scene);
            scenarioObject.AddComponent<
                ContentFeaturePrototypeScenario>();
            Undo.RegisterCreatedObjectUndo(
                scenarioObject,
                "Add PR151 Debug scenario");

            DisablePlayerSaveLifecycle();
            EnsureCamera(scene);
            EnsureDirectionalLight(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = instance;
        }

        private static void DisablePlayerSaveLifecycle()
        {
            AutoSaveService[] autoSaveServices =
                Object.FindObjectsByType<AutoSaveService>(
                    FindObjectsInactive.Include);

            foreach (AutoSaveService service
                     in autoSaveServices)
            {
                service.enabled = false;
                EditorUtility.SetDirty(service);
            }

            GameSaveLifecycleService[] lifecycleServices =
                Object.FindObjectsByType<
                    GameSaveLifecycleService>(
                    FindObjectsInactive.Include);

            foreach (GameSaveLifecycleService service
                     in lifecycleServices)
            {
                service.enabled = false;
                EditorUtility.SetDirty(service);
            }
        }

        private static void EnsureCamera(Scene scene)
        {
            Camera[] cameras =
                Object.FindObjectsByType<Camera>(
                    FindObjectsInactive.Include);

            if (cameras.Length > 0)
            {
                return;
            }

            var cameraObject = new GameObject(
                "Main Camera",
                typeof(Camera),
                typeof(AudioListener));
            SceneManager.MoveGameObjectToScene(
                cameraObject,
                scene);
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position =
                new Vector3(10f, 10f, -10f);
            Undo.RegisterCreatedObjectUndo(
                cameraObject,
                "Add prototype camera");
        }

        private static void EnsureDirectionalLight(
            Scene scene)
        {
            Light[] lights =
                Object.FindObjectsByType<Light>(
                    FindObjectsInactive.Include);

            foreach (Light light in lights)
            {
                if (light.type == LightType.Directional)
                {
                    return;
                }
            }

            var lightObject = new GameObject(
                "Directional Light",
                typeof(Light));
            SceneManager.MoveGameObjectToScene(
                lightObject,
                scene);
            Light directional =
                lightObject.GetComponent<Light>();
            directional.type = LightType.Directional;
            directional.intensity = 1f;
            lightObject.transform.rotation =
                Quaternion.Euler(50f, -30f, 0f);
            Undo.RegisterCreatedObjectUndo(
                lightObject,
                "Add prototype light");
        }

        private static GameObject CreateUiObject(
            string name,
            Transform parent,
            params System.Type[] components)
        {
            var gameObject =
                new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);

            foreach (System.Type component in components)
            {
                gameObject.AddComponent(component);
            }

            return gameObject;
        }

        private static void SetTopLeft(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
        }

        private static void SetTopRight(RectTransform rect)
        {
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
        }

        private static void SetStretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        private static void SetObjectReference(
            Object target,
            string propertyName,
            Object value)
        {
            SerializedObject serialized = new(target);
            SerializedProperty property =
                serialized.FindProperty(propertyName);

            if (property == null)
            {
                throw new System.InvalidOperationException(
                    $"Serialized property '{propertyName}' is missing on {target.GetType().Name}.");
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
