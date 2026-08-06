#if UNITY_EDITOR
using System.Collections.Generic;
using CityFlow.View;
using UnityEditor;
using UnityEngine;

namespace CityFlow.Editor
{
    public static class VehicleWheelDustPrefabBaker
    {
        private const string CatalogPath =
            "Assets/05_ScriptableObjects/Resources/CityFlow/VehicleVisualCatalog.asset";
        private const string DustMaterialPath =
            "Assets/99_Download/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/cfxr smoke cloud x4 ab.mat";

        [MenuItem("Tools/GreenLight/Vehicles/Ensure Wheel Dust Sources")]
        public static void EnsureDustSources()
        {
            VehicleVisualCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<VehicleVisualCatalogSO>(
                    CatalogPath);
            Material dustMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    DustMaterialPath);
            if (catalog == null || dustMaterial == null)
            {
                Debug.LogWarning(
                    "[VehicleWheelDustPrefabBaker] Vehicle catalog or Cartoon FX smoke material is missing.");
                return;
            }

            var prefabs = new HashSet<GameObject>();
            AddPrefabs(prefabs, catalog.NormalVehiclePrefabs);
            AddPrefab(prefabs, catalog.SchoolBusPrefab);
            AddPrefab(prefabs, catalog.AmbulancePrefab);
            AddPrefabs(prefabs, catalog.CityBusPrefabs);

            bool changed = false;
            foreach (GameObject prefab in prefabs)
            {
                changed |= EnsureDustSource(prefab, dustMaterial);
            }

            if (changed)
            {
                AssetDatabase.SaveAssets();
                Debug.Log(
                    "[VehicleWheelDustPrefabBaker] Wheel dust sources updated.");
            }
        }

        private static bool EnsureDustSource(
            GameObject prefab,
            Material material)
        {
            string path = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                VehicleWheelDustSource source =
                    root.GetComponent<VehicleWheelDustSource>();
                if (source != null &&
                    source.ParticleMaterial == material)
                {
                    return false;
                }

                source ??=
                    root.AddComponent<VehicleWheelDustSource>();
                source.Configure(material);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void AddPrefabs(
            HashSet<GameObject> destination,
            IReadOnlyList<GameObject> prefabs)
        {
            if (prefabs == null)
            {
                return;
            }

            for (int i = 0; i < prefabs.Count; i++)
            {
                AddPrefab(destination, prefabs[i]);
            }
        }

        private static void AddPrefab(
            HashSet<GameObject> destination,
            GameObject prefab)
        {
            if (prefab != null)
            {
                destination.Add(prefab);
            }
        }
    }
}
#endif
