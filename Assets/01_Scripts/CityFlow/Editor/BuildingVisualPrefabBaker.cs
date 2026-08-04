#if UNITY_EDITOR
using CityFlow.View;
using UnityEditor;
using UnityEngine;

namespace CityFlow.Editor
{
    [InitializeOnLoad]
    public static class BuildingVisualPrefabBaker
    {
        private const string OutputFolder =
            "Assets/02_Prefabs/Buildings";
        private const string CatalogPath =
            "Assets/05_ScriptableObjects/Resources/CityFlow/BuildingVisualCatalog.asset";
        private const string HouseSourcePath =
            "Assets/99_Download/SimpleTown/Prefabs/Buildings/Building_Garage_02.prefab";
        private const string OfficeSourcePath =
            "Assets/99_Download/SimpleTown/Prefabs/Buildings/Building_OfficeLarge_Blue.prefab";
        private const string FoundationSourcePath =
            "Assets/99_Download/SimpleTown/Prefabs/Props/path_straight_mesh.prefab";
        private const string FoundationMaterialPath =
            "Assets/02_Prefabs/Environment/Roads/Materials/" +
            "SimpleTownRoad_URP_Unlit.mat";
        static BuildingVisualPrefabBaker()
        {
            EditorApplication.delayCall +=
                EnsureGeneratedAssets;
        }

        [MenuItem(
            "Tools/GreenLight/Buildings/Rebuild Building Visual Prefabs")]
        public static void RebuildGeneratedAssets()
        {
            BuildWrapper(
                "HouseVisual",
                HouseSourcePath,
                true);
            BuildWrapper(
                "OfficeVisual",
                OfficeSourcePath,
                true);
            BuildFoundationWrapper(true);
            CreateOrUpdateCatalog();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ValidateGeneratedAssets();
        }

        private static void EnsureGeneratedAssets()
        {
            bool changed = false;
            changed |= BuildWrapper(
                "HouseVisual",
                HouseSourcePath,
                false);
            changed |= BuildWrapper(
                "OfficeVisual",
                OfficeSourcePath,
                false);
            changed |= BuildFoundationWrapper(false);

            if (changed ||
                AssetDatabase
                    .LoadAssetAtPath<BuildingVisualCatalogSO>(
                        CatalogPath) == null)
            {
                CreateOrUpdateCatalog();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            ValidateGeneratedAssets();
        }

        private static bool BuildWrapper(
            string outputName,
            string sourcePath,
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
                    $"[BuildingVisualPrefabBaker] Missing source: {sourcePath}");
                return false;
            }

            GameObject root = new(outputName);
            try
            {
                GameObject model =
                    (GameObject)
                    PrefabUtility.InstantiatePrefab(source);
                model.name = "Model";
                model.transform.SetParent(root.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation =
                    Quaternion.identity;
                model.transform.localScale = Vector3.one;

                if (TryGetLocalRendererBounds(
                        root.transform,
                        out Bounds bounds))
                {
                    BoxCollider collider =
                        root.AddComponent<BoxCollider>();
                    collider.center = bounds.center;
                    collider.size = bounds.size;
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

        private static bool BuildFoundationWrapper(
            bool overwrite)
        {
            const string outputName =
                "BuildingFoundation";
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
                    FoundationSourcePath);
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(
                    FoundationMaterialPath);
            if (source == null || material == null)
            {
                Debug.LogWarning(
                    "[BuildingVisualPrefabBaker] Missing foundation source or material.");
                return false;
            }

            GameObject root = new(outputName);
            try
            {
                GameObject model =
                    (GameObject)
                    PrefabUtility.InstantiatePrefab(source);
                model.name = "Model";
                model.transform.SetParent(root.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation =
                    Quaternion.identity;
                model.transform.localScale = Vector3.one;

                Renderer[] renderers =
                    model.GetComponentsInChildren<Renderer>(
                        true);
                foreach (Renderer renderer in renderers)
                {
                    Material[] materials =
                        renderer.sharedMaterials;
                    for (int i = 0;
                         i < materials.Length;
                         i++)
                    {
                        materials[i] = material;
                    }
                    renderer.sharedMaterials = materials;
                }

                if (TryGetLocalRendererBounds(
                        root.transform,
                        out Bounds bounds))
                {
                    BoxCollider collider =
                        root.AddComponent<BoxCollider>();
                    collider.center = bounds.center;
                    collider.size = bounds.size;
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

        private static bool TryGetLocalRendererBounds(
            Transform root,
            out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);

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
                        bounds =
                            new Bounds(
                                local,
                                Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(local);
                    }
                }
            }

            return hasBounds;
        }

        private static void CreateOrUpdateCatalog()
        {
            BuildingVisualCatalogSO catalog =
                AssetDatabase
                    .LoadAssetAtPath<BuildingVisualCatalogSO>(
                        CatalogPath);
            if (catalog == null)
            {
                catalog =
                    ScriptableObject.CreateInstance<
                        BuildingVisualCatalogSO>();
                AssetDatabase.CreateAsset(
                    catalog,
                    CatalogPath);
            }

            var serialized =
                new SerializedObject(catalog);
            serialized.FindProperty("housePrefab")
                .objectReferenceValue =
                LoadWrapper("HouseVisual");
            serialized.FindProperty("officePrefab")
                .objectReferenceValue =
                LoadWrapper("OfficeVisual");
            serialized.FindProperty("schoolPrefab")
                .objectReferenceValue =
                LoadWrapper("SchoolVisual_StudioHorizon");
            serialized.FindProperty("foundationPrefab")
                .objectReferenceValue =
                LoadWrapper("BuildingFoundation");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        private static GameObject LoadWrapper(string name)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{OutputFolder}/{name}.prefab");
        }

        private static void ValidateGeneratedAssets()
        {
            BuildingVisualCatalogSO catalog =
                AssetDatabase
                    .LoadAssetAtPath<BuildingVisualCatalogSO>(
                        CatalogPath);
            if (catalog == null ||
                catalog.HousePrefab == null ||
                catalog.OfficePrefab == null ||
                catalog.SchoolPrefab == null ||
                catalog.FoundationPrefab == null)
            {
                Debug.LogError(
                    "[BuildingVisualPrefabBaker] Building catalog is incomplete.");
                return;
            }

            Debug.Log(
                "[BuildingVisualPrefabBaker] Validated project-owned building and foundation prefabs.");
        }
    }
}
#endif
