using CityFlow.Content;
using CityFlow.Content.Transit;
using CityFlow.Gameplay.Progression;
using CityFlow.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityFlow.EditorTools
{
    public static class SchoolBusPrototypeBaker
    {
        private const string SourceScene =
            "Assets/00_Scenes/Debug/PR151_ContentPrototype_cmt.unity";
        private const string TargetScene =
            "Assets/00_Scenes/Debug/CityFlowIntegrated_Lee.unity";
        private const string PrefabPath =
            "Assets/02_Prefabs/Vehicles/SchoolBusContent.prefab";
        private const string DefinitionPath =
            "Assets/05_ScriptableObjects/CityFlow/Transit/SchoolBusDefinition.asset";
        private const string LargeVehicleFootprintPath =
            "Assets/05_ScriptableObjects/CityFlow/Traffic/LargeVehicleFootprint.asset";
        private const string SchedulePath =
            "Assets/05_ScriptableObjects/CityFlow/Transit/KoreanSchoolBusSchedule.asset";
        private const string DebugTimeSettingsPath =
            "Assets/05_ScriptableObjects/CityFlow/Transit/SchoolBusDebugGameTimeSettings.asset";
        private const string VisualPrefabPath =
            "Assets/02_Prefabs/Vehicles/SchoolBusVisual.prefab";
        private const string MaterialPath =
            "Assets/03_Art/Materials/Vehicles/SchoolBus_URP.mat";

        private static readonly Color SchoolBusYellow =
            new(0.96f, 0.64f, 0.08f, 1f);

        [MenuItem(
            "Tools/GreenLight/Content/Build School Bus Prototype")]
        public static void Build()
        {
            SceneAsset source =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    SourceScene);
            GameObject vehicle =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    VisualPrefabPath);

            if (source == null || vehicle == null)
            {
                Debug.LogError(
                    "[SchoolBusPrototypeBaker] Debug source scene or vehicle prefab is missing.");
                return;
            }

            Material material = CreateOrUpdateMaterial();
            BusDefinitionSO definition =
                CreateOrUpdateDefinition(vehicle);
            SchoolBusScheduleSO schedule =
                CreateOrUpdateSchedule();
            GameTimeSettingsSO debugTimeSettings =
                CreateOrUpdateDebugTimeSettings();

            CreateOrUpdatePrefab(
                definition,
                schedule,
                material);
            CreateOrUpdateDebugScene(debugTimeSettings);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "[SchoolBusPrototypeBaker] School bus prefab, settings, material, and existing Debug scene are ready.");
        }

        private static Material CreateOrUpdateMaterial()
        {
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(
                    MaterialPath);

            if (material == null)
            {
                Material cityBusMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(
                        "Assets/03_Art/Materials/Vehicles/CityBus_URP.mat");
                Shader shader = cityBusMaterial != null
                    ? cityBusMaterial.shader
                    : Shader.Find(
                        "Universal Render Pipeline/Lit");

                if (shader == null)
                {
                    throw new System.InvalidOperationException(
                        "A URP Lit shader is required.");
                }

                material = new Material(shader)
                {
                    name = "SchoolBus_URP"
                };
                AssetDatabase.CreateAsset(
                    material,
                    MaterialPath);
            }

            material.color = SchoolBusYellow;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor(
                    "_BaseColor",
                    SchoolBusYellow);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static BusDefinitionSO
            CreateOrUpdateDefinition(GameObject vehicle)
        {
            BusDefinitionSO definition =
                AssetDatabase.LoadAssetAtPath<BusDefinitionSO>(
                    DefinitionPath);

            if (definition == null)
            {
                definition =
                    ScriptableObject.CreateInstance<
                        BusDefinitionSO>();
                AssetDatabase.CreateAsset(
                    definition,
                    DefinitionPath);
            }

            SerializedObject serialized =
                new(definition);
            serialized.FindProperty("busId").stringValue =
                "school_bus";
            serialized.FindProperty("displayName").stringValue =
                "School Bus";
            serialized.FindProperty("busType").enumValueIndex =
                (int)BusType.SchoolBus;
            serialized.FindProperty("secondsPerTile").floatValue =
                0.24f;
            serialized.FindProperty("stopWaitSeconds").floatValue =
                1.2f;
            serialized.FindProperty("passengerCapacity").intValue =
                12;
            serialized.FindProperty("boardingDemandPerStop").intValue =
                3;
            serialized.FindProperty("leavingDemandPerStop").intValue =
                12;
            serialized.FindProperty("vehicleFootprintProfile")
                .objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<
                    VehicleFootprintProfileSO>(
                    LargeVehicleFootprintPath);
            serialized.FindProperty("vehicleLengthTiles").floatValue =
                0.8f;
            serialized.FindProperty("vehicleWidthTiles").floatValue =
                0.24f;
            serialized.FindProperty("routeColor").colorValue =
                SchoolBusYellow;
            serialized.FindProperty("vehicleVisualPrefab")
                .objectReferenceValue = vehicle;
            serialized.FindProperty("initialStops").arraySize = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static void CreateOrUpdatePrefab(
            BusDefinitionSO definition,
            SchoolBusScheduleSO schedule,
            Material material)
        {
            GameObject root =
                new("SchoolBusContent");

            try
            {
                BusRoute route =
                    root.AddComponent<BusRoute>();
                BusStopRegistry stopRegistry =
                    root.AddComponent<BusStopRegistry>();
                SchoolBusService service =
                    root.AddComponent<SchoolBusService>();
                BusWorldView worldView =
                    root.AddComponent<BusWorldView>();

                SetReference(
                    service,
                    "definition",
                    definition);
                SetReference(
                    service,
                    "schedule",
                    schedule);
                SetReference(
                    service,
                    "stopRegistry",
                    stopRegistry);
                SetReference(
                    service,
                    "busRoute",
                    route);
                SetValue(
                    service,
                    "maxResidentialStopsPerTrip",
                    4);
                SetValue(
                    service,
                    "schoolWaitSeconds",
                    3f);
                SetValue(
                    service,
                    "autoStart",
                    true);

                SetValue(route, "loopRoute", false);
                SetValue(route, "autoStart", false);
                SetValue(
                    route,
                    "secondsPerTile",
                    definition.SecondsPerTile);
                SetValue(
                    route,
                    "stopWaitSeconds",
                    definition.StopWaitSeconds);
                SetValue(
                    route,
                    "avoidImmediateUTurn",
                    true);

                SetReference(
                    worldView,
                    "definition",
                    definition);
                SetReference(
                    worldView,
                    "busRoute",
                    route);
                SetReference(
                    worldView,
                    "busVisualPrefab",
                    definition.VehicleVisualPrefab);
                SetValue(
                    worldView,
                    "visualScale",
                    0.76f);
                SetReference(
                    worldView,
                    "busMaterial",
                    null);
                SetValue(
                    worldView,
                    "movementDuration",
                    definition.SecondsPerTile);
                SetValue(
                    worldView,
                    "schoolParkingSlot",
                    1);
                SetValue(
                    worldView,
                    "parkingApproachDistance",
                    0.7f);

                PrefabUtility.SaveAsPrefabAsset(
                    root,
                    PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static SchoolBusScheduleSO
            CreateOrUpdateSchedule()
        {
            SchoolBusScheduleSO schedule =
                AssetDatabase.LoadAssetAtPath<
                    SchoolBusScheduleSO>(SchedulePath);

            if (schedule == null)
            {
                schedule =
                    ScriptableObject.CreateInstance<
                        SchoolBusScheduleSO>();
                AssetDatabase.CreateAsset(
                    schedule,
                    SchedulePath);
            }

            SerializedObject serialized =
                new(schedule);
            serialized.FindProperty("morningStartHour")
                .intValue = 7;
            serialized.FindProperty("morningEndHour")
                .intValue = 9;
            serialized.FindProperty("afternoonStartHour")
                .intValue = 15;
            serialized.FindProperty("afternoonEndHour")
                .intValue = 17;
            serialized.FindProperty("operateOnWeekends")
                .boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(schedule);
            return schedule;
        }

        private static GameTimeSettingsSO
            CreateOrUpdateDebugTimeSettings()
        {
            GameTimeSettingsSO settings =
                AssetDatabase.LoadAssetAtPath<GameTimeSettingsSO>(
                    DebugTimeSettingsPath);

            if (settings == null)
            {
                settings =
                    ScriptableObject.CreateInstance<
                        GameTimeSettingsSO>();
                AssetDatabase.CreateAsset(
                    settings,
                    DebugTimeSettingsPath);
            }

            SerializedObject serialized =
                new(settings);
            serialized.FindProperty("realMinutesPerGameDay")
                .floatValue = 0.4f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            return settings;
        }

        private static void CreateOrUpdateDebugScene(
            GameTimeSettingsSO debugTimeSettings)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    TargetScene) == null)
            {
                throw new System.InvalidOperationException(
                    $"The existing School Bus Debug scene is missing: {TargetScene}");
            }

            Scene scene =
                EditorSceneManager.OpenScene(
                    TargetScene,
                    OpenSceneMode.Single);

            RemoveLegacySchoolBusComponents();

            SchoolBusService existing =
                Object.FindAnyObjectByType<
                    SchoolBusService>(
                    FindObjectsInactive.Include);

            if (existing == null)
            {
                GameObject prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(
                        PrefabPath);
                GameObject instance =
                    (GameObject)PrefabUtility
                        .InstantiatePrefab(prefab, scene);
                instance.name = "SchoolBusContent";
            }

            ConfigurePrototypeClock(debugTimeSettings);
            EnsureCamera(scene);
            EnsureDirectionalLight(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(
                scene,
                TargetScene);
        }

        private static void ConfigurePrototypeClock(
            GameTimeSettingsSO debugTimeSettings)
        {
            MonoBehaviour[] behaviours =
                Object.FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include);

            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null)
                {
                    continue;
                }

                string typeName =
                    behaviour.GetType().FullName;
                SerializedObject serialized =
                    new(behaviour);

                if (typeName ==
                    "CityFlow.Gameplay.Progression.GameCalendarService")
                {
                    SerializedProperty timeSettings =
                        serialized.FindProperty(
                            "timeSettings");
                    SerializedProperty startHour =
                        serialized.FindProperty("startHour");

                    if (timeSettings != null)
                    {
                        timeSettings.objectReferenceValue =
                            debugTimeSettings;
                    }

                    if (startHour != null)
                    {
                        startHour.intValue = 7;
                    }

                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(behaviour);
                }
                else if (typeName ==
                         "CityFlow.Gameplay.Save.GameSaveLifecycleService")
                {
                    SerializedProperty loadOnStart =
                        serialized.FindProperty("loadOnStart");
                    if (loadOnStart == null)
                    {
                        continue;
                    }

                    loadOnStart.boolValue = false;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(behaviour);
                }
            }
        }

        private static void RemoveLegacySchoolBusComponents()
        {
            SchoolBusService[] services =
                Object.FindObjectsByType<SchoolBusService>(
                    FindObjectsInactive.Include);

            foreach (SchoolBusService service in services)
            {
                string prefabPath =
                    PrefabUtility
                        .GetPrefabAssetPathOfNearestInstanceRoot(
                            service.gameObject);
                if (prefabPath == PrefabPath)
                {
                    continue;
                }

                GameObject owner = service.gameObject;
                BusRoute legacyRoute =
                    owner.GetComponent<BusRoute>();
                Component legacyView =
                    owner.GetComponent(
                        "SchoolBusRouteView");

                if (legacyView != null)
                {
                    Object.DestroyImmediate(
                        legacyView,
                        true);
                }

                Object.DestroyImmediate(service, true);

                if (legacyRoute != null)
                {
                    Object.DestroyImmediate(
                        legacyRoute,
                        true);
                }
            }
        }

        private static void EnsureCamera(Scene scene)
        {
            if (Object.FindAnyObjectByType<Camera>(
                    FindObjectsInactive.Include) != null)
            {
                return;
            }

            GameObject cameraObject =
                new(
                    "Main Camera",
                    typeof(Camera),
                    typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            SceneManager.MoveGameObjectToScene(
                cameraObject,
                scene);
        }

        private static void EnsureDirectionalLight(
            Scene scene)
        {
            Light[] lights =
                Object.FindObjectsByType<Light>(
                    FindObjectsInactive.Include);

            foreach (Light existingLight in lights)
            {
                if (existingLight.type ==
                    LightType.Directional)
                {
                    return;
                }
            }

            GameObject lightObject =
                new("Directional Light", typeof(Light));
            SceneManager.MoveGameObjectToScene(
                lightObject,
                scene);
            Light directional =
                lightObject.GetComponent<Light>();
            directional.type = LightType.Directional;
            lightObject.transform.rotation =
                Quaternion.Euler(50f, -30f, 0f);
        }

        private static void SetReference(
            Object target,
            string propertyName,
            Object value)
        {
            SerializedObject serialized =
                new(target);
            serialized.FindProperty(propertyName)
                .objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetValue(
            Object target,
            string propertyName,
            int value)
        {
            SerializedObject serialized =
                new(target);
            serialized.FindProperty(propertyName)
                .intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetValue(
            Object target,
            string propertyName,
            float value)
        {
            SerializedObject serialized =
                new(target);
            serialized.FindProperty(propertyName)
                .floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetValue(
            Object target,
            string propertyName,
            bool value)
        {
            SerializedObject serialized =
                new(target);
            serialized.FindProperty(propertyName)
                .boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
