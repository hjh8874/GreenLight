using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityFlow.Environment.Editor
{
    public static class EnvironmentVisualSystemBaker
    {
        private const string ArtRoot = "Assets/03_Art/VFX/Environment";
        private const string ConfigRoot =
            "Assets/05_ScriptableObjects/Environment";
        private const string PrefabRoot = "Assets/02_Prefabs/Environment";
        private const string BirdMeshPath = ArtRoot + "/BirdV.asset";
        private const string BirdMaterialPath = ArtRoot + "/Bird.mat";
        private const string BirdProfilePath =
            ConfigRoot + "/BirdFlockProfile.asset";
        private const string PrefabPath =
            PrefabRoot + "/EnvironmentVisualSystem.prefab";

        [MenuItem(
            "Tools/GreenLight/Environment/Bake Environment Visual System")]
        public static void BakeEnvironmentVisualSystem()
        {
            EnsureFolder(ArtRoot);
            EnsureFolder(ConfigRoot);
            EnsureFolder(PrefabRoot);

            Mesh birdMesh = LoadOrCreateBirdMesh();
            Material birdMaterial = LoadOrCreateBirdMaterial();
            BirdFlockProfileSO profile =
                LoadOrCreateAsset<BirdFlockProfileSO>(BirdProfilePath);

            BakePrefab(profile, birdMesh, birdMaterial);

            EditorUtility.SetDirty(birdMesh);
            EditorUtility.SetDirty(birdMaterial);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabPath);
            Debug.Log(
                "[EnvironmentVisualSystemBaker] Environment visuals baked. " +
                $"Place {PrefabPath} beside CityBootstrap.");
        }

        private static Mesh LoadOrCreateBirdMesh()
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(BirdMeshPath);
            bool isNew = mesh == null;
            mesh ??= new Mesh { name = "BirdV" };
            mesh.Clear();

            mesh.vertices = new[]
            {
                new Vector3(0f, 0.2f, 0f),
                new Vector3(0f, -0.18f, 0f),
                new Vector3(-0.04f, 0.07f, 0f),
                new Vector3(-0.3f, 0.2f, 0f),
                new Vector3(-0.6f, 0.1f, 0f),
                new Vector3(-0.3f, 0.06f, 0f),
                new Vector3(-0.05f, -0.03f, 0f),
                new Vector3(0.04f, 0.07f, 0f),
                new Vector3(0.3f, 0.2f, 0f),
                new Vector3(0.6f, 0.1f, 0f),
                new Vector3(0.3f, 0.06f, 0f),
                new Vector3(0.05f, -0.03f, 0f)
            };
            mesh.triangles = new[]
            {
                0, 2, 7,
                2, 6, 7,
                6, 11, 7,
                6, 1, 11,
                2, 3, 5,
                3, 4, 5,
                2, 5, 6,
                7, 10, 8,
                8, 10, 9,
                7, 11, 10,
                7, 2, 0,
                7, 6, 2,
                7, 11, 6,
                11, 1, 6,
                5, 3, 2,
                5, 4, 3,
                6, 5, 2,
                8, 10, 7,
                9, 10, 8,
                10, 11, 7
            };
            mesh.uv = new[]
            {
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 0f),
                new Vector2(0.47f, 0.68f),
                new Vector2(0.25f, 1f),
                new Vector2(0f, 0.72f),
                new Vector2(0.25f, 0.55f),
                new Vector2(0.46f, 0.4f),
                new Vector2(0.53f, 0.68f),
                new Vector2(0.75f, 1f),
                new Vector2(1f, 0.72f),
                new Vector2(0.75f, 0.55f),
                new Vector2(0.54f, 0.4f)
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (isNew)
            {
                AssetDatabase.CreateAsset(mesh, BirdMeshPath);
            }

            return mesh;
        }

        private static Material LoadOrCreateBirdMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                BirdMaterialPath);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Unlit/Color");
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "Bird",
                    enableInstancing = true
                };
                AssetDatabase.CreateAsset(material, BirdMaterialPath);
            }
            else
            {
                material.shader = shader;
                material.enableInstancing = true;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.white);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", Color.white);
            }

            if (material.HasProperty("_Cull"))
            {
                material.SetFloat("_Cull", (float)CullMode.Off);
            }

            return material;
        }

        private static void BakePrefab(
            BirdFlockProfileSO profile,
            Mesh birdMesh,
            Material birdMaterial)
        {
            GameObject root = new("EnvironmentVisualSystem");
            try
            {
                root.AddComponent<EnvironmentVisualSystem>();

                GameObject birdFlock = new("Bird Flock");
                birdFlock.transform.SetParent(root.transform, false);
                BirdFlockVisual birdVisual =
                    birdFlock.AddComponent<BirdFlockVisual>();
                birdVisual.EditorConfigure(profile, birdMesh, birdMaterial);

                GameObject villageBird = new("Village Bird Departure");
                villageBird.transform.SetParent(root.transform, false);
                VillageBirdDepartureVisual villageBirdVisual =
                    villageBird.AddComponent<VillageBirdDepartureVisual>();
                villageBirdVisual.EditorConfigure(
                    profile,
                    birdMesh,
                    birdMaterial);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static T LoadOrCreateAsset<T>(string path)
            where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[index]);
                }

                current = next;
            }
        }

        // Unity setup:
        // Run Tools > GreenLight > Environment > Bake Environment Visual System.
        // Place the generated prefab beside CityBootstrap.
    }
}
