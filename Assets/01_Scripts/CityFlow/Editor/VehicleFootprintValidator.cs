using System;
using CityFlow.Configs;
using CityFlow.Content;
using CityFlow.View;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CityFlow.EditorTools
{
    public static class VehicleFootprintValidator
    {
        private const float LengthToleranceTiles = 0.12f;
        private const string TrafficAssetRoot =
            "Assets/05_ScriptableObjects/CityFlow/Traffic";
        private const string ConfigAssetRoot =
            "Assets/05_ScriptableObjects";
        private const string TransitAssetRoot =
            "Assets/05_ScriptableObjects/CityFlow/Transit";
        private const string VehiclePrefabRoot =
            "Assets/02_Prefabs/Vehicles";

        [MenuItem(
            "Tools/GreenLight/Traffic/Validate Vehicle Footprints")]
        public static void ValidateAll()
        {
            int errors = 0;
            int warnings = 0;
            ValidateProfiles(ref errors);
            ValidateSimConfigs(ref errors);
            ValidateBusDefinitions(ref errors);
            ValidateBusPrefabs(ref errors, ref warnings);

            string summary =
                $"[VehicleFootprintValidator] Completed with {errors} error(s) and {warnings} warning(s).";
            if (errors > 0)
            {
                Debug.LogError(summary);
                return;
            }

            if (warnings > 0)
            {
                Debug.LogWarning(summary);
                return;
            }

            Debug.Log(summary);
        }

        private static void ValidateSimConfigs(ref int errors)
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:SimConfigAsset",
                new[] { ConfigAssetRoot });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                SimConfigAsset config =
                    AssetDatabase.LoadAssetAtPath<SimConfigAsset>(path);
                if (config == null)
                {
                    continue;
                }

                var serializedConfig = new SerializedObject(config);
                SerializedProperty profile =
                    serializedConfig.FindProperty(
                        "standardVehicleFootprint");
                if (profile?.objectReferenceValue != null)
                {
                    continue;
                }

                errors++;
                Debug.LogError(
                    $"[VehicleFootprintValidator] Missing standard vehicle profile: {path}",
                    config);
            }
        }

        private static void ValidateProfiles(ref int errors)
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:VehicleFootprintProfileSO",
                new[] { TrafficAssetRoot });
            if (guids.Length == 0)
            {
                errors++;
                Debug.LogError(
                    "[VehicleFootprintValidator] No vehicle footprint profiles were found.");
                return;
            }

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                VehicleFootprintProfileSO profile =
                    AssetDatabase.LoadAssetAtPath<VehicleFootprintProfileSO>(
                        path);
                if (profile == null || profile.LengthTiles <= 0f ||
                    profile.WidthTiles <= 0f ||
                    profile.MinimumGapTiles < 0f)
                {
                    errors++;
                    Debug.LogError(
                        $"[VehicleFootprintValidator] Invalid profile: {path}",
                        profile);
                }
            }
        }

        private static void ValidateBusDefinitions(ref int errors)
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:BusDefinitionSO",
                new[] { TransitAssetRoot });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                BusDefinitionSO definition =
                    AssetDatabase.LoadAssetAtPath<BusDefinitionSO>(path);
                if (definition == null)
                {
                    continue;
                }

                if (definition.VehicleFootprintProfile == null)
                {
                    errors++;
                    Debug.LogError(
                        $"[VehicleFootprintValidator] Missing footprint profile: {path}",
                        definition);
                }

                if (definition.VehicleVisualPrefab == null)
                {
                    errors++;
                    Debug.LogError(
                        $"[VehicleFootprintValidator] Missing vehicle visual Prefab: {path}",
                        definition);
                }
            }
        }

        private static void ValidateBusPrefabs(
            ref int errors,
            ref int warnings)
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:Prefab",
                new[] { VehiclePrefabRoot });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(path);
                BusWorldView view = prefab != null
                    ? prefab.GetComponentInChildren<BusWorldView>(true)
                    : null;
                if (view == null)
                {
                    continue;
                }

                var serializedView = new SerializedObject(view);
                BusDefinitionSO definition = serializedView
                    .FindProperty("definition")
                    ?.objectReferenceValue as BusDefinitionSO;
                GameObject visualPrefab = serializedView
                    .FindProperty("busVisualPrefab")
                    ?.objectReferenceValue as GameObject;
                if (visualPrefab == null && definition != null)
                {
                    visualPrefab = definition.VehicleVisualPrefab;
                }

                SerializedProperty visualScaleProperty =
                    serializedView.FindProperty("visualScale");
                float visualScale = visualScaleProperty != null
                    ? Mathf.Max(0.01f, visualScaleProperty.floatValue)
                    : 1f;

                if (definition == null || visualPrefab == null)
                {
                    errors++;
                    Debug.LogError(
                        $"[VehicleFootprintValidator] Incomplete BusWorldView references: {path}",
                        prefab);
                    continue;
                }

                if (!TryMeasureLongestRendererAxis(
                        visualPrefab,
                        visualScale,
                        out float measuredLength))
                {
                    errors++;
                    Debug.LogError(
                        $"[VehicleFootprintValidator] Vehicle visual has no renderer: {path}",
                        visualPrefab);
                    continue;
                }

                float configuredLength =
                    definition.VehicleFootprint.LengthTiles;
                float difference = Mathf.Abs(
                    configuredLength - measuredLength);
                if (difference <= LengthToleranceTiles)
                {
                    Debug.Log(
                        $"[VehicleFootprintValidator] {path}: configured {configuredLength:F2} tile(s), rendered {measuredLength:F2} tile(s).",
                        prefab);
                    continue;
                }

                warnings++;
                Debug.LogWarning(
                    $"[VehicleFootprintValidator] {path}: configured length {configuredLength:F2} differs from rendered length {measuredLength:F2} by {difference:F2} tile(s).",
                    prefab);
            }
        }

        private static bool TryMeasureLongestRendererAxis(
            GameObject visualPrefab,
            float visualScale,
            out float measuredLength)
        {
            measuredLength = 0f;
            GameObject instance = null;
            try
            {
                instance = Object.Instantiate(visualPrefab);
                instance.hideFlags = HideFlags.HideAndDontSave;
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;
                instance.transform.localScale =
                    Vector3.one * Mathf.Max(0.01f, visualScale);

                Renderer[] renderers =
                    instance.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    return false;
                }

                Bounds bounds = renderers[0].bounds;
                for (int index = 1; index < renderers.Length; index++)
                {
                    bounds.Encapsulate(renderers[index].bounds);
                }

                Vector3 size = bounds.size;
                measuredLength = Mathf.Max(size.x, size.y, size.z);
                return measuredLength > 0f;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
            finally
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        // Unity setup: run the GreenLight traffic validation menu before publishing Prefab changes.
    }
}
