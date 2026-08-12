#if UNITY_EDITOR
using System.Collections.Generic;
using CityFlow.View;
using UnityEditor;
using UnityEngine;

namespace CityFlow.Editor
{
    [InitializeOnLoad]
    public static class VehicleVisualPrefabBaker
    {
        private const string OutputFolder =
            "Assets/02_Prefabs/Vehicles";
        private const string CatalogPath =
            "Assets/05_ScriptableObjects/Resources/CityFlow/VehicleVisualCatalog.asset";
        private const string SchoolBusDefinitionPath =
            "Assets/05_ScriptableObjects/CityFlow/Transit/SchoolBusDefinition.asset";
        private const string PoliceConfigPath =
            "Assets/05_ScriptableObjects/CityFlow/Police/" +
            "PoliceDispatchConfig.asset";
        private const string DustMaterialPath =
            "Assets/99_Download/JMO Assets/Cartoon FX Remaster/" +
            "CFXR Assets/Graphics/cfxr smoke cloud x4 ab.mat";
        private const int CurrentGenerationVersion = 3;

        private static readonly
            (string Name, string Source, bool ReverseForward)[]
            NormalVehicles =
        {
            ("NormalCar_SimpleTownBlue",
                "Assets/99_Download/SimpleTown/Prefabs/Vehicles/car_blue.prefab",
                true),
            ("NormalCar_SimpleTownGreen",
                "Assets/99_Download/SimpleTown/Prefabs/Vehicles/car_green.prefab",
                true),
            ("NormalCar_SimpleTownRed",
                "Assets/99_Download/SimpleTown/Prefabs/Vehicles/car_red.prefab",
                true),
            ("NormalCar_Hatchback",
                "Assets/99_Download/Pack_Cars/Prefabs/Hatchback.prefab",
                false),
            ("NormalCar_Hothatch",
                "Assets/99_Download/Pack_Cars/Prefabs/Hothatch.prefab",
                false),
            ("NormalCar_Jeep",
                "Assets/99_Download/Pack_Cars/Prefabs/Jeep.prefab",
                false),
            ("NormalCar_SportCar",
                "Assets/99_Download/Pack_Cars/Prefabs/SportCar.prefab",
                false),
            ("NormalCar_SportCar2",
                "Assets/99_Download/Pack_Cars/Prefabs/SportCar 2.prefab",
                false),
            ("NormalCar_SportSedan",
                "Assets/99_Download/Pack_Cars/Prefabs/SportSedan.prefab",
                false),
            ("NormalCar_StationWagon",
                "Assets/99_Download/Pack_Cars/Prefabs/StationWagon.prefab",
                false),
        };

        private static readonly
            (string Name, string Source, bool ReverseForward)
            SchoolBus =
            ("SchoolBusVisual",
                "Assets/99_Download/Pack_Cars/Prefabs/SchoolBus.prefab",
                false);
        private static readonly
            (string Name, string Source, bool ReverseForward)
            Ambulance =
            ("AmbulanceVisual",
                "Assets/99_Download/Pack_Cars/Prefabs/Ambulance.prefab",
                false);
        private static readonly
            (string Name, string Source, bool ReverseForward)
            Police =
            ("PoliceVehicleVisual",
                "Assets/99_Download/Pack_Cars/Prefabs/Police.prefab",
                false);
        private static readonly
            (string Name, string Source, bool ReverseForward)[]
            CityBuses =
        {
            ("CityBus_Blue",
                "Assets/99_Download/SimpleTown/Prefabs/Vehicles/bus_blue.prefab",
                true),
            ("CityBus_Brown",
                "Assets/99_Download/SimpleTown/Prefabs/Vehicles/bus_brown.prefab",
                true),
            ("CityBus_Grey",
                "Assets/99_Download/SimpleTown/Prefabs/Vehicles/bus_grey.prefab",
                true),
        };

        static VehicleVisualPrefabBaker()
        {
            EditorApplication.delayCall += EnsureGeneratedAssets;
        }

        [MenuItem("Tools/GreenLight/Vehicles/Rebuild Vehicle Visual Prefabs")]
        public static void RebuildGeneratedAssets()
        {
            for (int i = 0; i < NormalVehicles.Length; i++)
            {
                BuildWrapper(
                    NormalVehicles[i].Name,
                    NormalVehicles[i].Source,
                    NormalVehicles[i].ReverseForward,
                    true);
            }

            BuildWrapper(
                SchoolBus.Name,
                SchoolBus.Source,
                SchoolBus.ReverseForward,
                true);
            BuildWrapper(
                Ambulance.Name,
                Ambulance.Source,
                Ambulance.ReverseForward,
                true);
            BuildWrapper(
                Police.Name,
                Police.Source,
                Police.ReverseForward,
                true);
            for (int i = 0; i < CityBuses.Length; i++)
            {
                BuildWrapper(
                    CityBuses[i].Name,
                    CityBuses[i].Source,
                    CityBuses[i].ReverseForward,
                    true);
            }
            CreateOrUpdateCatalog();
            UpdateSchoolBusDefinition();
            UpdatePoliceConfig();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void EnsureGeneratedAssets()
        {
            VehicleVisualCatalogSO existingCatalog =
                AssetDatabase.LoadAssetAtPath<VehicleVisualCatalogSO>(
                    CatalogPath);
            bool rebuildExisting =
                existingCatalog == null ||
                existingCatalog.GeneratedVersion !=
                    CurrentGenerationVersion ||
                existingCatalog.CityBusPrefabs == null ||
                existingCatalog.CityBusPrefabs.Length !=
                    CityBuses.Length;
            bool rebuildPolice =
                PoliceVisualNeedsRebuild();
            bool changed = false;
            for (int i = 0; i < NormalVehicles.Length; i++)
            {
                changed |= BuildWrapper(
                    NormalVehicles[i].Name,
                    NormalVehicles[i].Source,
                    NormalVehicles[i].ReverseForward,
                    rebuildExisting);
            }

            changed |= BuildWrapper(
                SchoolBus.Name,
                SchoolBus.Source,
                SchoolBus.ReverseForward,
                rebuildExisting);
            changed |= BuildWrapper(
                Ambulance.Name,
                Ambulance.Source,
                Ambulance.ReverseForward,
                rebuildExisting);
            changed |= BuildWrapper(
                Police.Name,
                Police.Source,
                Police.ReverseForward,
                rebuildExisting || rebuildPolice);
            for (int i = 0; i < CityBuses.Length; i++)
            {
                changed |= BuildWrapper(
                    CityBuses[i].Name,
                    CityBuses[i].Source,
                    CityBuses[i].ReverseForward,
                    rebuildExisting);
            }

            if (changed ||
                AssetDatabase.LoadAssetAtPath<VehicleVisualCatalogSO>(
                    CatalogPath) == null)
            {
                CreateOrUpdateCatalog();
                UpdateSchoolBusDefinition();
                UpdatePoliceConfig();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            else if (UpdatePoliceConfig())
            {
                AssetDatabase.SaveAssets();
            }

            ValidateGeneratedAssets();
        }

        internal static GameObject RebuildPoliceVisual()
        {
            return BuildWrapper(
                    Police.Name,
                    Police.Source,
                    Police.ReverseForward,
                    true)
                ? LoadWrapper(Police.Name)
                : null;
        }

        private static bool PoliceVisualNeedsRebuild()
        {
            GameObject prefab = LoadWrapper(Police.Name);
            Material dustMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    DustMaterialPath);
            if (prefab == null || dustMaterial == null)
            {
                return true;
            }

            string path = AssetDatabase.GetAssetPath(prefab);
            GameObject root =
                PrefabUtility.LoadPrefabContents(path);
            try
            {
                Bounds bounds =
                    CalculateLocalRendererBounds(root.transform);
                float groundContact =
                    CalculateGroundContactZ(
                        root.transform,
                        bounds.max.z);
                VehicleWheelDustSource dustSource =
                    root.GetComponent<VehicleWheelDustSource>();
                return Mathf.Abs(bounds.size.x - 1f) > 0.001f ||
                       Mathf.Abs(groundContact) > 0.001f ||
                       dustSource == null ||
                       dustSource.ParticleMaterial != dustMaterial;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool BuildWrapper(
            string outputName,
            string sourcePath,
            bool reverseForward,
            bool overwrite)
        {
            string outputPath =
                $"{OutputFolder}/{outputName}.prefab";
            if (!overwrite &&
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    outputPath) != null)
            {
                return false;
            }

            GameObject source =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    sourcePath);
            if (source == null)
            {
                Debug.LogWarning(
                    $"[VehicleVisualPrefabBaker] Missing source: {sourcePath}");
                return false;
            }

            GameObject root =
                new(outputName);
            try
            {
                GameObject model =
                    (GameObject)PrefabUtility.InstantiatePrefab(
                        source);
                model.name = "Model";
                model.transform.SetParent(root.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation =
                    Quaternion.LookRotation(
                        reverseForward
                            ? Vector3.left
                            : Vector3.right,
                        Vector3.back);
                model.transform.localScale = Vector3.one;

                Bounds bounds =
                    CalculateLocalRendererBounds(root.transform);
                float length =
                    Mathf.Max(0.0001f, bounds.size.x);
                model.transform.localScale =
                    Vector3.one / length;

                bounds =
                    CalculateLocalRendererBounds(root.transform);
                Vector3 center = bounds.center;
                float groundContactZ =
                    CalculateGroundContactZ(
                        root.transform,
                        bounds.max.z);
                model.transform.localPosition +=
                    new Vector3(
                        -center.x,
                        -center.y,
                        -groundContactZ);

                bounds =
                    CalculateLocalRendererBounds(root.transform);
                BoxCollider collider =
                    root.AddComponent<BoxCollider>();
                collider.center = bounds.center;
                collider.size = bounds.size;

                Material dustMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(
                        DustMaterialPath);
                if (dustMaterial != null)
                {
                    VehicleWheelDustSource dustSource =
                        root.AddComponent<VehicleWheelDustSource>();
                    dustSource.Configure(dustMaterial);
                }

                PrefabUtility.SaveAsPrefabAsset(
                    root,
                    outputPath);
                return true;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Bounds CalculateLocalRendererBounds(
            Transform root)
        {
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds localBounds = default;

            for (int i = 0; i < renderers.Length; i++)
            {
                Bounds worldBounds = renderers[i].bounds;
                Vector3 min = worldBounds.min;
                Vector3 max = worldBounds.max;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 world = new(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    Vector3 local =
                        root.InverseTransformPoint(world);
                    if (!hasBounds)
                    {
                        localBounds =
                            new Bounds(local, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(local);
                    }
                }
            }

            return hasBounds
                ? localBounds
                : new Bounds(
                    Vector3.zero,
                    Vector3.one);
        }

        private static float CalculateGroundContactZ(
            Transform root,
            float fallback)
        {
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            bool foundWheel = false;
            float groundContact =
                float.NegativeInfinity;

            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i].name.Contains(
                        "wheel",
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foundWheel = true;
                Bounds localBounds =
                    renderers[i].localBounds;
                Vector3 min = localBounds.min;
                Vector3 max = localBounds.max;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = new(
                        (corner & 1) == 0
                            ? min.x
                            : max.x,
                        (corner & 2) == 0
                            ? min.y
                            : max.y,
                        (corner & 4) == 0
                            ? min.z
                            : max.z);
                    float localZ =
                        root.InverseTransformPoint(
                            renderers[i].transform
                                .TransformPoint(point)).z;
                    groundContact =
                        Mathf.Max(
                            groundContact,
                            localZ);
                }
            }

            return foundWheel
                ? groundContact
                : fallback;
        }

        private static void CreateOrUpdateCatalog()
        {
            VehicleVisualCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<VehicleVisualCatalogSO>(
                    CatalogPath);
            if (catalog == null)
            {
                catalog =
                    ScriptableObject.CreateInstance<
                        VehicleVisualCatalogSO>();
                AssetDatabase.CreateAsset(
                    catalog,
                    CatalogPath);
            }

            var serialized =
                new SerializedObject(catalog);
            SerializedProperty normal =
                serialized.FindProperty(
                    "normalVehiclePrefabs");
            normal.arraySize = NormalVehicles.Length;
            for (int i = 0; i < NormalVehicles.Length; i++)
            {
                normal.GetArrayElementAtIndex(i)
                    .objectReferenceValue =
                    LoadWrapper(NormalVehicles[i].Name);
            }

            serialized.FindProperty("schoolBusPrefab")
                .objectReferenceValue =
                LoadWrapper(SchoolBus.Name);
            serialized.FindProperty("ambulancePrefab")
                .objectReferenceValue =
                LoadWrapper(Ambulance.Name);
            SerializedProperty cityBus =
                serialized.FindProperty(
                    "cityBusPrefabs");
            cityBus.arraySize = CityBuses.Length;
            for (int i = 0; i < CityBuses.Length; i++)
            {
                cityBus.GetArrayElementAtIndex(i)
                    .objectReferenceValue =
                    LoadWrapper(CityBuses[i].Name);
            }
            serialized.FindProperty("generatedVersion")
                .intValue = CurrentGenerationVersion;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        private static void UpdateSchoolBusDefinition()
        {
            Object definition =
                AssetDatabase.LoadMainAssetAtPath(
                    SchoolBusDefinitionPath);
            if (definition == null)
            {
                return;
            }

            var serialized =
                new SerializedObject(definition);
            serialized.FindProperty("vehicleVisualPrefab")
                .objectReferenceValue =
                LoadWrapper(SchoolBus.Name);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
        }

        private static bool UpdatePoliceConfig()
        {
            Object config =
                AssetDatabase.LoadMainAssetAtPath(
                    PoliceConfigPath);
            GameObject visual = LoadWrapper(Police.Name);
            if (config == null || visual == null)
            {
                return false;
            }

            var serialized = new SerializedObject(config);
            SerializedProperty property =
                serialized.FindProperty("vehicleVisualPrefab");
            if (property == null ||
                property.objectReferenceValue == visual)
            {
                return false;
            }

            property.objectReferenceValue = visual;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            return true;
        }

        private static GameObject LoadWrapper(string name)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{OutputFolder}/{name}.prefab");
        }

        private static void ValidateGeneratedAssets()
        {
            VehicleVisualCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<VehicleVisualCatalogSO>(
                    CatalogPath);
            if (catalog == null ||
                catalog.NormalVehiclePrefabs == null ||
                catalog.NormalVehiclePrefabs.Length !=
                    NormalVehicles.Length ||
                catalog.SchoolBusPrefab == null ||
                catalog.AmbulancePrefab == null ||
                LoadWrapper(Police.Name) == null ||
                catalog.CityBusPrefabs == null ||
                catalog.CityBusPrefabs.Length !=
                    CityBuses.Length)
            {
                Debug.LogError(
                    "[VehicleVisualPrefabBaker] Vehicle catalog is incomplete.");
                return;
            }

            var prefabs = new List<GameObject>(
                catalog.NormalVehiclePrefabs)
            {
                catalog.SchoolBusPrefab,
                catalog.AmbulancePrefab,
                LoadWrapper(Police.Name)
            };
            prefabs.AddRange(
                catalog.CityBusPrefabs);

            for (int i = 0; i < prefabs.Count; i++)
            {
                string path =
                    AssetDatabase.GetAssetPath(prefabs[i]);
                GameObject root =
                    PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Bounds bounds =
                        CalculateLocalRendererBounds(
                            root.transform);
                    float groundContact =
                        CalculateGroundContactZ(
                            root.transform,
                            bounds.max.z);
                    if (Mathf.Abs(bounds.size.x - 1f) >
                            0.001f ||
                        Mathf.Abs(groundContact) >
                            0.001f)
                    {
                        Debug.LogError(
                            $"[VehicleVisualPrefabBaker] Invalid generated vehicle: {path} " +
                            $"(length={bounds.size.x:F4}, ground={groundContact:F4})");
                        return;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            Debug.Log(
                $"[VehicleVisualPrefabBaker] Validated {prefabs.Count} project-owned vehicle prefabs.");
        }
    }
}
#endif
