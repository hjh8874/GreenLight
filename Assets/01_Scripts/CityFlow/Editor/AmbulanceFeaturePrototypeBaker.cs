using CityFlow.Content;
using CityFlow.Content.Transit;
using CityFlow.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityFlow.EditorTools
{
    public static class AmbulanceFeaturePrototypeBaker
    {
        private const string DebugScenePath =
            "Assets/00_Scenes/Debug/CityFlowIntegrated_Lee.unity";
        private const string ContentPrefabPath =
            "Assets/02_Prefabs/Vehicles/AmbulanceContent.prefab";
        private const string VehiclePrefabPath =
            "Assets/02_Prefabs/Vehicles/AmbulanceVehicle.prefab";
        private const string ConfigPath =
            "Assets/05_ScriptableObjects/CityFlow/Emergency/EmergencyIncidentConfig.asset";
        private const string SourceVisualPrefabPath =
            "Assets/99_Download/SimpleTown/Prefabs/Vehicles/ambo_mesh.prefab";
        private const string VisualMaterialPath =
            "Assets/03_Art/Materials/Vehicles/Ambulance_URP.mat";
        private const string VisualPrefabPath =
            "Assets/02_Prefabs/Vehicles/AmbulanceVehicleVisual.prefab";
        private const string CityBusPrefabPath =
            "Assets/02_Prefabs/Vehicles/CityBusContent.prefab";

        [MenuItem(
            "Tools/GreenLight/Content/Build Ambulance Prototype")]
        public static void Build()
        {
            SceneAsset debugScene =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    DebugScenePath);
            GameObject sourceVisualPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    SourceVisualPrefabPath);

            if (debugScene == null ||
                sourceVisualPrefab == null)
            {
                Debug.LogError(
                    "[AmbulanceFeaturePrototypeBaker] Existing Lee Debug scene or ambulance visual prefab is missing.");
                return;
            }

            Material visualMaterial =
                CreateOrUpdateVisualMaterial(
                    sourceVisualPrefab);
            GameObject visualPrefab =
                CreateOrUpdateVisualPrefab(
                    sourceVisualPrefab,
                    visualMaterial);
            EmergencyIncidentConfigSO config =
                CreateOrUpdateConfig(visualPrefab);
            GameObject vehiclePrefab =
                CreateOrUpdateVehiclePrefab(config);
            CreateOrUpdateContentPrefab(
                config,
                vehiclePrefab);
            RemoveEmergencyFromCityBusPrefab();
            AddContentToExistingDebugScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "[AmbulanceFeaturePrototypeBaker] Ambulance config, prefabs, and existing Lee Debug scene are ready.");
        }

        private static Material CreateOrUpdateVisualMaterial(
            GameObject sourceVisualPrefab)
        {
            Shader shader =
                Shader.Find(
                    "GreenLight/CityFlow Opaque Unlit");

            if (shader == null)
            {
                throw new System.InvalidOperationException(
                    "GreenLight/CityFlow Opaque Unlit shader is missing.");
            }

            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(
                    VisualMaterialPath);

            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "Ambulance_URP"
                };
                AssetDatabase.CreateAsset(
                    material,
                    VisualMaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            Renderer sourceRenderer =
                sourceVisualPrefab
                    .GetComponentInChildren<Renderer>(
                        true);
            Texture texture =
                sourceRenderer != null &&
                sourceRenderer.sharedMaterial != null
                    ? sourceRenderer
                        .sharedMaterial.mainTexture
                    : null;

            material.SetTexture("_BaseMap", texture);
            material.SetColor(
                "_BaseColor",
                Color.white);
            material.SetColor(
                "_Color",
                Color.white);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject CreateOrUpdateVisualPrefab(
            GameObject sourceVisualPrefab,
            Material visualMaterial)
        {
            GameObject root =
                PrefabUtility.InstantiatePrefab(
                    sourceVisualPrefab)
                as GameObject;

            if (root == null)
            {
                throw new System.InvalidOperationException(
                    "Could not instantiate the ambulance visual source prefab.");
            }

            try
            {
                PrefabUtility.UnpackPrefabInstance(
                    root,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                root.name = "AmbulanceVehicleVisual";

                Renderer[] renderers =
                    root.GetComponentsInChildren<Renderer>(
                        true);

                for (int i = 0;
                     i < renderers.Length;
                     i++)
                {
                    Material[] materials =
                        renderers[i].sharedMaterials;

                    for (int materialIndex = 0;
                         materialIndex < materials.Length;
                         materialIndex++)
                    {
                        materials[materialIndex] =
                            visualMaterial;
                    }

                    renderers[i].sharedMaterials =
                        materials;
                }

                return PrefabUtility.SaveAsPrefabAsset(
                    root,
                    VisualPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static EmergencyIncidentConfigSO
            CreateOrUpdateConfig(GameObject visualPrefab)
        {
            EmergencyIncidentConfigSO config =
                AssetDatabase.LoadAssetAtPath<
                    EmergencyIncidentConfigSO>(
                    ConfigPath);

            if (config == null)
            {
                config =
                    ScriptableObject.CreateInstance<
                        EmergencyIncidentConfigSO>();
                AssetDatabase.CreateAsset(
                    config,
                    ConfigPath);
            }

            SerializedObject serialized = new(config);
            serialized.FindProperty("minimumSpawnInterval")
                .floatValue = 5f;
            serialized.FindProperty("maximumSpawnInterval")
                .floatValue = 8f;
            serialized.FindProperty(
                    "minimumDispatchIntervalDays")
                .intValue = 1;
            serialized.FindProperty(
                    "maximumDispatchIntervalDays")
                .intValue = 3;
            serialized.FindProperty("maximumActiveIncidents")
                .intValue = 3;
            serialized.FindProperty("houseWeight")
                .floatValue = 1f;
            serialized.FindProperty("officeWeight")
                .floatValue = 1f;
            serialized.FindProperty("schoolWeight")
                .floatValue = 0.7f;
            serialized.FindProperty("specialBuildingWeight")
                .floatValue = 0.4f;
            serialized.FindProperty("recentTargetHistorySize")
                .intValue = 3;
            serialized.FindProperty("travelSecondsPerTile")
                .floatValue = 0.45f;
            serialized.FindProperty("treatmentSeconds")
                .floatValue = 2f;
            serialized.FindProperty("ambulancesPerHospital")
                .intValue = 1;
            serialized.FindProperty("routeRetrySeconds")
                .floatValue = 2f;
            serialized.FindProperty("vehicleVisualPrefab")
                .objectReferenceValue = visualPrefab;
            serialized.FindProperty("visualScale")
                .floatValue = 0.085f;
            serialized.FindProperty("visualDepth")
                .floatValue = -0.38f;
            serialized.FindProperty("vehicleLengthTiles")
                .floatValue = 0.56f;
            serialized.FindProperty("vehicleWidthTiles")
                .floatValue = 0.24f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            return config;
        }

        private static GameObject CreateOrUpdateVehiclePrefab(
            EmergencyIncidentConfigSO config)
        {
            GameObject root =
                new("AmbulanceVehicle");

            try
            {
                BusRoute route =
                    root.AddComponent<BusRoute>();
                AmbulanceVehicleAgent agent =
                    root.AddComponent<
                        AmbulanceVehicleAgent>();
                AmbulanceWorldView worldView =
                    root.AddComponent<
                        AmbulanceWorldView>();

                SetReference(
                    agent,
                    "route",
                    route);
                SetReference(
                    agent,
                    "config",
                    config);
                SetReference(
                    agent,
                    "worldView",
                    worldView);
                SetReference(
                    worldView,
                    "route",
                    route);
                SetReference(
                    worldView,
                    "config",
                    config);

                SetValue(
                    route,
                    "secondsPerTile",
                    config.TravelSecondsPerTile);
                SetValue(
                    route,
                    "stopWaitSeconds",
                    config.TreatmentSeconds);
                SetValue(
                    route,
                    "loopRoute",
                    false);
                SetValue(
                    route,
                    "autoStart",
                    false);
                SetValue(
                    route,
                    "avoidImmediateUTurn",
                    true);

                return PrefabUtility.SaveAsPrefabAsset(
                    root,
                    VehiclePrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void CreateOrUpdateContentPrefab(
            EmergencyIncidentConfigSO config,
            GameObject vehiclePrefab)
        {
            GameObject root =
                new("AmbulanceContent");

            try
            {
                EmergencyIncidentSystem incidents =
                    root.AddComponent<
                        EmergencyIncidentSystem>();
                AmbulanceDispatchService dispatch =
                    root.AddComponent<
                        AmbulanceDispatchService>();

                SetReference(
                    incidents,
                    "config",
                    config);
                SetValue(
                    incidents,
                    "enableAutomaticSpawn",
                    true);
                SetValue(
                    incidents,
                    "useExternalAmbulanceTransport",
                    true);

                SetReference(
                    dispatch,
                    "incidentSystem",
                    incidents);
                SetReference(
                    dispatch,
                    "config",
                    config);
                SetReference(
                    dispatch,
                    "ambulanceVehiclePrefab",
                    vehiclePrefab);

                PrefabUtility.SaveAsPrefabAsset(
                    root,
                    ContentPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void RemoveEmergencyFromCityBusPrefab()
        {
            GameObject root =
                PrefabUtility.LoadPrefabContents(
                    CityBusPrefabPath);

            try
            {
                EmergencyIncidentSystem emergency =
                    root.GetComponent<
                        EmergencyIncidentSystem>();

                if (emergency == null)
                {
                    return;
                }

                Object.DestroyImmediate(emergency);
                PrefabUtility.SaveAsPrefabAsset(
                    root,
                    CityBusPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void AddContentToExistingDebugScene()
        {
            Scene scene =
                EditorSceneManager.OpenScene(
                    DebugScenePath,
                    OpenSceneMode.Single);

            if (Object.FindAnyObjectByType<
                    AmbulanceDispatchService>(
                    FindObjectsInactive.Include) == null)
            {
                GameObject prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(
                        ContentPrefabPath);
                GameObject instance =
                    PrefabUtility.InstantiatePrefab(
                        prefab,
                        scene) as GameObject;

                if (instance == null)
                {
                    throw new System.InvalidOperationException(
                        "Could not add AmbulanceContent to the existing Debug scene.");
                }

                instance.name = "AmbulanceContent";
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(
                scene,
                DebugScenePath);
        }

        private static void SetReference(
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

        private static void SetValue(
            Object target,
            string propertyName,
            bool value)
        {
            SerializedObject serialized = new(target);
            serialized.FindProperty(propertyName)
                .boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetValue(
            Object target,
            string propertyName,
            float value)
        {
            SerializedObject serialized = new(target);
            serialized.FindProperty(propertyName)
                .floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
