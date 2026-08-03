using System.Linq;
using UnityEditor;
using UnityEngine;

internal static class CodexSchoolVisualRepair
{
    private const string MeshPath =
        "Assets/02_Prefabs/Buildings/School_Right_StudioHorizon.obj";
    private const string MaterialPath =
        "Assets/99_Download/Studio Horizon/Simple Building Generic Free/Materials/Simple Building 01.mat";
    private const string PrefabPath =
        "Assets/02_Prefabs/Buildings/SchoolVisual_StudioHorizon.prefab";
    private const string CatalogPath =
        "Assets/05_ScriptableObjects/Resources/CityFlow/BuildingVisualCatalog.asset";

    [MenuItem("Geon Tools/Repair School Visual")]
    private static void Repair()
    {
        Mesh mesh = AssetDatabase
            .LoadAllAssetsAtPath(MeshPath)
            .OfType<Mesh>()
            .FirstOrDefault();
        Material material =
            AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

        if (mesh == null || material == null)
        {
            Debug.LogError(
                "[CodexSchoolVisualRepair] Mesh 또는 Material을 찾지 못했습니다.");
            return;
        }

        var root = new GameObject("SchoolVisual_StudioHorizon");
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        root.AddComponent<MeshRenderer>().sharedMaterial = material;
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        GameObject prefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        ScriptableObject catalog =
            AssetDatabase.LoadAssetAtPath<ScriptableObject>(CatalogPath);
        var serializedCatalog = new SerializedObject(catalog);
        serializedCatalog.FindProperty("schoolPrefab").objectReferenceValue =
            prefab;
        serializedCatalog.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();

        Debug.Log("[CodexSchoolVisualRepair] 학교 프리팹 복구 완료");
    }
}
