using CityFlow.Content;
using CityFlow.Content.Transit;
using CityFlow.View;
using UnityEditor;
using UnityEngine;

namespace CityFlow.EditorTools
{
    public static class PoliceVehiclePrototypeBaker
    {
        private const string SourceVisualPrefabPath =
            "Assets/99_Download/Pack_Cars/Prefabs/Police.prefab";
        private const string StandardFootprintPath =
            "Assets/05_ScriptableObjects/CityFlow/Traffic/StandardVehicleFootprint.asset";
        private const string ConfigFolder =
            "Assets/05_ScriptableObjects/CityFlow/Police";
        private const string ConfigPath =
            ConfigFolder + "/PoliceDispatchConfig.asset";
        private const string VisualPrefabPath =
            "Assets/02_Prefabs/Vehicles/PoliceVehicleVisual.prefab";
        private const string VehiclePrefabPath =
            "Assets/02_Prefabs/Vehicles/PoliceVehicle.prefab";
        private const string ContentPrefabPath =
            "Assets/02_Prefabs/Vehicles/PoliceContent.prefab";

        [MenuItem(
            "Tools/GreenLight/Content/Build Police Vehicle Prototype")]
        public static void Build()
        {
            GameObject sourceVisual =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    SourceVisualPrefabPath);
            VehicleFootprintProfileSO standardFootprint =
                AssetDatabase.LoadAssetAtPath<
                    VehicleFootprintProfileSO>(
                    StandardFootprintPath);

            if (sourceVisual == null || standardFootprint == null)
            {
                Debug.LogError(
                    "[PoliceVehiclePrototypeBaker] Pack_Cars Police prefab or StandardVehicleFootprint asset is missing.");
                return;
            }

            EnsureFolder(ConfigFolder);
            GameObject visualPrefab =
                CreateOrUpdateVisualPrefab(sourceVisual);
            PoliceDispatchConfigSO config =
                CreateOrUpdateConfig(
                    visualPrefab,
                    standardFootprint);
            GameObject vehiclePrefab =
                CreateOrUpdateVehiclePrefab(config);
            CreateOrUpdateContentPrefab(
                config,
                vehiclePrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "[PoliceVehiclePrototypeBaker] Police config and prefabs are ready. Place PoliceContent.prefab in a feature test scene.");
        }

        private static GameObject CreateOrUpdateVisualPrefab(
            GameObject sourceVisual)
        {
            GameObject root = new("PoliceVehicleVisual");
            try
            {
                GameObject model = PrefabUtility.InstantiatePrefab(
                    sourceVisual) as GameObject;
                if (model == null)
                {
                    throw new System.InvalidOperationException(
                        "Could not instantiate the Pack_Cars Police prefab.");
                }

                model.name = "Model";
                model.transform.SetParent(root.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation =
                    Quaternion.LookRotation(
                        Vector3.right,
                        Vector3.back);
                model.transform.localScale = Vector3.one;

                if (TryGetLocalBounds(root.transform, out Bounds bounds))
                {
                    model.transform.localPosition = new Vector3(
                        -bounds.center.x,
                        -bounds.center.y,
                        -bounds.max.z);

                    if (TryGetLocalBounds(
                            root.transform,
                            out Bounds alignedBounds))
                    {
                        BoxCollider collider =
                            root.AddComponent<BoxCollider>();
                        collider.center = alignedBounds.center;
                        collider.size = alignedBounds.size;
                    }
                }

                root.AddComponent<VehicleWheelDustSource>();
                return PrefabUtility.SaveAsPrefabAsset(
                    root,
                    VisualPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static PoliceDispatchConfigSO CreateOrUpdateConfig(
            GameObject visualPrefab,
            VehicleFootprintProfileSO standardFootprint)
        {
            PoliceDispatchConfigSO config =
                AssetDatabase.LoadAssetAtPath<
                    PoliceDispatchConfigSO>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<
                    PoliceDispatchConfigSO>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

            SerializedObject serialized = new(config);
            serialized.FindProperty("vehiclesPerStation")
                .intValue = 2;
            serialized.FindProperty("maximumActiveCalls")
                .intValue = 8;
            serialized.FindProperty("travelSecondsPerTile")
                .floatValue = 0.45f;
            serialized.FindProperty("defaultHandlingSeconds")
                .floatValue = 2f;
            serialized.FindProperty("routeRetrySeconds")
                .floatValue = 2f;
            serialized.FindProperty("maximumOutboundRouteRetries")
                .intValue = 3;
            serialized.FindProperty("maximumReturnRouteRetries")
                .intValue = 5;
            serialized.FindProperty("enableDailyPatrol")
                .boolValue = true;
            serialized.FindProperty("patrolStartHour")
                .intValue = 10;
            serialized.FindProperty("patrolAreaSize")
                .intValue = 40;
            serialized.FindProperty("patrolVehiclesPerStation")
                .intValue = 1;
            serialized.FindProperty("vehicleFootprintProfile")
                .objectReferenceValue = standardFootprint;
            serialized.FindProperty("vehicleVisualPrefab")
                .objectReferenceValue = visualPrefab;
            serialized.FindProperty("visualScale")
                .floatValue = 1f;
            serialized.FindProperty("visualDepth")
                .floatValue = -0.38f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            return config;
        }

        private static GameObject CreateOrUpdateVehiclePrefab(
            PoliceDispatchConfigSO config)
        {
            GameObject root = new("PoliceVehicle");
            try
            {
                BusRoute route = root.AddComponent<BusRoute>();
                PoliceVehicleAgent agent =
                    root.AddComponent<PoliceVehicleAgent>();
                AmbulanceWorldView worldView =
                    root.AddComponent<AmbulanceWorldView>();

                SetReference(agent, "route", route);
                SetReference(agent, "config", config);
                SetReference(agent, "worldView", worldView);
                SetReference(worldView, "route", route);

                SetValue(
                    route,
                    "secondsPerTile",
                    config.TravelSecondsPerTile);
                SetValue(
                    route,
                    "stopWaitSeconds",
                    config.DefaultHandlingSeconds);
                SetValue(route, "loopRoute", false);
                SetValue(route, "autoStart", false);
                SetValue(route, "avoidImmediateUTurn", true);

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
            PoliceDispatchConfigSO config,
            GameObject vehiclePrefab)
        {
            GameObject root = new("PoliceContent");
            try
            {
                PoliceCallSystem calls =
                    root.AddComponent<PoliceCallSystem>();
                PoliceDispatchService dispatch =
                    root.AddComponent<PoliceDispatchService>();
                PolicePatrolScheduler patrol =
                    root.AddComponent<PolicePatrolScheduler>();

                SetReference(calls, "config", config);
                SetReference(calls, "patrolScheduler", patrol);
                SetVector2Int(calls, "testTarget", new Vector2Int(100, 100));
                SetReference(dispatch, "callSystem", calls);
                SetReference(dispatch, "config", config);
                SetReference(dispatch, "patrolScheduler", patrol);
                SetReference(
                    dispatch,
                    "policeVehiclePrefab",
                    vehiclePrefab);
                SetReference(patrol, "config", config);

                PrefabUtility.SaveAsPrefabAsset(
                    root,
                    ContentPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static bool TryGetLocalBounds(
            Transform root,
            out Bounds bounds)
        {
            bounds = default;
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            bool found = false;

            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                Bounds local = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 localCorner = new(
                        (corner & 1) == 0
                            ? local.min.x
                            : local.max.x,
                        (corner & 2) == 0
                            ? local.min.y
                            : local.max.y,
                        (corner & 4) == 0
                            ? local.min.z
                            : local.max.z);
                    Vector3 point = root.InverseTransformPoint(
                        renderer.transform.TransformPoint(
                            localCorner));
                    if (!found)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }

            return found;
        }

        private static void EnsureFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = $"{current}/{parts[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(
                        current,
                        parts[index]);
                }

                current = next;
            }
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
            serialized.FindProperty(propertyName).boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetValue(
            Object target,
            string propertyName,
            float value)
        {
            SerializedObject serialized = new(target);
            serialized.FindProperty(propertyName).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetVector2Int(
            Object target,
            string propertyName,
            Vector2Int value)
        {
            SerializedObject serialized = new(target);
            serialized.FindProperty(propertyName).vector2IntValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
