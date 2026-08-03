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
        private const string SchoolSourcePath =
            "Assets/03_Art/Tripo/School_Building_Tripo/" +
            "tripo_convert_b81e7628-2d08-401e-bf5d-8cc42c694deb.fbx";
        private const string SchoolRepairMaterialPath =
            "Assets/02_Prefabs/Buildings/Materials/" +
            "SchoolRepair_URP_Unlit.mat";
        private const string SchoolRepairMeshPath =
            "Assets/02_Prefabs/Buildings/Meshes/" +
            "SchoolEntranceGapPatch.asset";
        private const string SchoolCleanedFrontMeshPath =
            "Assets/02_Prefabs/Buildings/Meshes/" +
            "SchoolCleanedFrontMesh.asset";
        private const string SchoolProtrusionMeshName =
            "tripo_part_new_0";
        private const string SchoolCleanedFrontMeshName =
            "SchoolCleanedFrontMesh";
        private static Quaternion SchoolModelRotation =>
            Quaternion.Euler(90f, 0f, 0f) *
            Quaternion.Euler(0f, 180f, 0f);

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
            BuildSchoolWrapper(true);
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
            changed |= BuildSchoolWrapper(false);

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

        private static bool BuildSchoolWrapper(bool overwrite)
        {
            const string outputName = "SchoolVisual";
            string outputPath =
                $"{OutputFolder}/{outputName}.prefab";
            GameObject existing =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    outputPath);
            if (!overwrite &&
                IsSchoolWrapperCurrent(existing))
            {
                return false;
            }

            GameObject source =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    SchoolSourcePath);
            if (source == null)
            {
                Debug.LogWarning(
                    $"[BuildingVisualPrefabBaker] Missing source: {SchoolSourcePath}");
                return false;
            }

            Material repairMaterial =
                CreateOrUpdateSchoolRepairMaterial();
            Mesh repairMesh =
                CreateOrUpdateSchoolRepairMesh();
            Mesh cleanedFrontMesh =
                CreateOrUpdateSchoolCleanedFrontMesh(
                    source);
            if (cleanedFrontMesh == null)
            {
                return false;
            }
            GameObject root = new(outputName);
            try
            {
                GameObject model =
                    Object.Instantiate(source);
                model.name = "Model";
                model.transform.SetParent(root.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation =
                    SchoolModelRotation;
                model.transform.localScale = Vector3.one;

                ReplaceSchoolProtrusion(
                    model,
                    cleanedFrontMesh);
                AddSchoolEntrancePatch(
                    model.transform,
                    repairMesh,
                    repairMaterial);

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

        private static bool IsSchoolWrapperCurrent(
            GameObject prefab)
        {
            if (prefab == null)
            {
                return false;
            }

            Transform model =
                prefab.transform.Find("Model");
            Transform patch =
                model?.Find("EntranceRightGapPatch");
            MeshFilter cleanedFront =
                FindMeshFilter(
                    model,
                    SchoolProtrusionMeshName);
            return model != null &&
                   patch != null &&
                   Quaternion.Angle(
                       model.localRotation,
                       SchoolModelRotation) <
                   0.01f &&
                   cleanedFront != null &&
                   cleanedFront.sharedMesh != null &&
                   cleanedFront.sharedMesh.name ==
                       SchoolCleanedFrontMeshName;
        }

        private static void ReplaceSchoolProtrusion(
            GameObject model,
            Mesh cleanedMesh)
        {
            MeshFilter filter =
                FindMeshFilter(
                    model.transform,
                    SchoolProtrusionMeshName);
            if (filter != null)
            {
                filter.sharedMesh = cleanedMesh;
            }
        }

        private static MeshFilter FindMeshFilter(
            Transform root,
            string meshObjectName)
        {
            if (root == null)
            {
                return null;
            }

            MeshFilter[] filters =
                root.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter filter in filters)
            {
                if (filter.gameObject.name == meshObjectName)
                {
                    return filter;
                }
            }

            return null;
        }

        private static void AddSchoolEntrancePatch(
            Transform model,
            Mesh mesh,
            Material material)
        {
            GameObject patch =
                new("EntranceRightGapPatch");
            patch.transform.SetParent(model, false);
            patch.transform.localPosition = Vector3.zero;
            patch.transform.localRotation =
                Quaternion.identity;
            patch.transform.localScale = Vector3.one;

            MeshFilter filter =
                patch.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer =
                patch.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
        }

        private static Material
            CreateOrUpdateSchoolRepairMaterial()
        {
            EnsureFolder(
                "Assets/02_Prefabs/Buildings/Materials");
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(
                    SchoolRepairMaterialPath);
            Shader shader =
                Resources.Load<Shader>(
                    "CityFlowOpaqueUnlit");
            shader ??=
                Shader.Find(
                    "GreenLight/CityFlow Opaque Unlit");
            shader ??=
                Shader.Find(
                    "Universal Render Pipeline/Unlit");
            shader ??= Shader.Find("Unlit/Color");

            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "SchoolRepair_URP_Unlit"
                };
                AssetDatabase.CreateAsset(
                    material,
                    SchoolRepairMaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            Color repairColor =
                new Color32(211, 181, 126, 255);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor(
                    "_BaseColor",
                    repairColor);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor(
                    "_Color",
                    repairColor);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh
            CreateOrUpdateSchoolRepairMesh()
        {
            EnsureFolder(
                "Assets/02_Prefabs/Buildings/Meshes");
            Mesh mesh =
                AssetDatabase.LoadAssetAtPath<Mesh>(
                    SchoolRepairMeshPath);
            if (mesh == null)
            {
                mesh = new Mesh
                {
                    name = "SchoolEntranceGapPatch"
                };
                AssetDatabase.CreateAsset(
                    mesh,
                    SchoolRepairMeshPath);
            }

            const float xMin = 0.125f;
            const float xMax = 0.162f;
            const float yFront = -0.326f;
            const float yBack = -0.282f;
            const float zMin = 0.014f;
            const float zMax = 0.056f;
            mesh.Clear();
            mesh.vertices = new[]
            {
                new Vector3(xMin, yFront, zMin),
                new Vector3(xMax, yFront, zMin),
                new Vector3(xMax, yFront, zMax),
                new Vector3(xMin, yBack, zMin),
                new Vector3(xMax, yBack, zMin),
                new Vector3(xMax, yBack, zMax)
            };
            mesh.triangles = new[]
            {
                0, 2, 1,
                3, 4, 5,
                0, 3, 5,
                0, 5, 2,
                1, 2, 5,
                1, 5, 4,
                0, 1, 4,
                0, 4, 3
            };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Mesh
            CreateOrUpdateSchoolCleanedFrontMesh(
                GameObject source)
        {
            EnsureFolder(
                "Assets/02_Prefabs/Buildings/Meshes");
            Mesh existing =
                AssetDatabase.LoadAssetAtPath<Mesh>(
                    SchoolCleanedFrontMeshPath);
            if (existing != null)
            {
                return existing;
            }

            MeshFilter sourceFilter =
                FindMeshFilter(
                    source.transform,
                    SchoolProtrusionMeshName);
            Mesh sourceMesh = sourceFilter?.sharedMesh;
            if (sourceMesh == null)
            {
                Debug.LogError(
                    "[BuildingVisualPrefabBaker] " +
                    "School front source mesh is missing.");
                return existing;
            }

            Mesh cleaned = Object.Instantiate(sourceMesh);
            cleaned.name = SchoolCleanedFrontMeshName;
            int[] sourceTriangles = sourceMesh.triangles;
            var keptTriangles =
                new System.Collections.Generic.List<int>(
                    sourceTriangles.Length - 6);
            for (int triangle = 0;
                 triangle < sourceTriangles.Length / 3;
                 triangle++)
            {
                if (triangle == 1 || triangle == 2)
                {
                    continue;
                }

                int index = triangle * 3;
                keptTriangles.Add(sourceTriangles[index]);
                keptTriangles.Add(sourceTriangles[index + 1]);
                keptTriangles.Add(sourceTriangles[index + 2]);
            }

            cleaned.triangles = keptTriangles.ToArray();
            cleaned.RecalculateBounds();
            if (existing == null)
            {
                AssetDatabase.CreateAsset(
                    cleaned,
                    SchoolCleanedFrontMeshPath);
                return cleaned;
            }

            EditorUtility.CopySerialized(cleaned, existing);
            Object.DestroyImmediate(cleaned);
            existing.name = SchoolCleanedFrontMeshName;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int separator = path.LastIndexOf('/');
            string parent = path.Substring(0, separator);
            string name = path.Substring(separator + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
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
                LoadWrapper("SchoolVisual");
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
