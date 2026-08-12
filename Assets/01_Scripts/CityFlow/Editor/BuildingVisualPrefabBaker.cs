#if UNITY_EDITOR
using CityFlow.Content;
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
        private const string PoliceSourcePath =
            "Assets/99_Download/Studio Horizon/" +
            "Simple Building Generic Free/Prefabs/Police_Station.prefab";
        private const string PoliceDefinitionPath =
            "Assets/05_ScriptableObjects/Buildings/" +
            "Building_PoliceStation.asset";
        private const string MallSourcePath =
            "Assets/99_Download/SimpleTown/Prefabs/Buildings/" +
            "Building_Mall.prefab";
        private const string PetrolStationSourcePath =
            "Assets/99_Download/SimpleTown/Prefabs/Buildings/" +
            "Building_PetrolStation.prefab";
        private const string PharmacySourcePath =
            "Assets/99_Download/SimpleTown/Prefabs/Buildings/" +
            "Building_Store_Drug.prefab";
        private const string CoffeeShopSourcePath =
            "Assets/99_Download/SimpleTown/Prefabs/Buildings/" +
            "Building_CoffeeShop.prefab";
        private const string CinemaSourcePath =
            "Assets/99_Download/SimpleTown/Prefabs/Buildings/" +
            "Building_Cinema.prefab";
        private const string AutoRepairSourcePath =
            "Assets/99_Download/SimpleTown/Prefabs/Buildings/" +
            "Building_AutoRepair.prefab";
        private const string MallDefinitionPath =
            "Assets/05_ScriptableObjects/Buildings/Building_Mall.asset";
        private const string PetrolStationDefinitionPath =
            "Assets/05_ScriptableObjects/Buildings/Building_PetrolStation.asset";
        private const string PharmacyDefinitionPath =
            "Assets/05_ScriptableObjects/Buildings/Building_StoreCorner_Drug.asset";
        private const string CoffeeShopDefinitionPath =
            "Assets/05_ScriptableObjects/Buildings/Building_CoffeeShop.asset";
        private const string CinemaDefinitionPath =
            "Assets/05_ScriptableObjects/Buildings/Building_Cinema.asset";
        private const string AutoRepairDefinitionPath =
            "Assets/05_ScriptableObjects/Buildings/Building_AutoRepair.asset";
        private const string BuildingFoundationPrefabPath =
            OutputFolder + "/BuildingFoundation.prefab";
        private const string DrivewayPrefabPath =
            "Assets/02_Prefabs/Environment/Driveways/" +
            "SimpleTownDriveway.prefab";
        private const string DrivewayBoundaryMaterialPath =
            "Assets/02_Prefabs/Environment/Driveways/" +
            "DrivewayBoundary_URP_Lit.mat";
        private const int ParkingSlotsPerDriveway = 2;
        private const string FuelIslandClearanceName =
            "FuelIslandClearance";
        private const string AuthoredParkingSurfaceName =
            "ParkingSurface";
        private const string PetrolStationGroundMaterialPath =
            OutputFolder + "/PetrolStationGround_URP_Lit.mat";
        private const float MallParkedVehicleScale = 0.93f;
        private static readonly Vector2 PetrolStationFuelIslandClearanceSize =
            new(1.206f, 0.264f);
        private static readonly Vector3 PetrolStationFuelIslandClearanceCenter =
            new(0f, -0.44f, 0f);
        private static readonly Vector3[] MallParkingPositions =
        {
            new(0.107f, 0.105f, 0f),
            new(0.107f, -0.108f, 0f),
            new(0.107f, -0.322f, 0f),
            new(0.107f, -0.535f, 0f),
            new(0.107f, -0.749f, 0f)
        };
        private static readonly Vector3[] PetrolStationParkingPositions =
        {
            new(-0.38f, -0.78f, 0f),
            new(0.38f, -0.78f, 0f)
        };

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
            BuildPoliceStationWrapper(true);
            BuildCommercialWrappers(true);
            CreateOrUpdateCatalog();
            UpdatePoliceDefinition();
            UpdateCommercialDefinitions();
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
            changed |= BuildPoliceStationWrapper(false);
            changed |= BuildCommercialWrappers(false);
            changed |= UpdatePoliceDefinition();
            changed |= UpdateCommercialDefinitions();

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

        private static bool BuildPoliceStationWrapper(
            bool overwrite)
        {
            return BuildTwoByTwoBuildingWithDriveway(
                "PoliceStationVisual_StudioHorizon",
                PoliceSourcePath,
                180f,
                2,
                overwrite);
        }

        private static bool BuildCommercialWrappers(bool overwrite)
        {
            bool changed = false;
            changed |= BuildAuthoredParkingBuilding(
                "MallVisual_SimpleTown",
                MallSourcePath,
                0f,
                MallParkingPositions,
                Vector3.right,
                overwrite,
                parkedVehicleScale: MallParkedVehicleScale);
            changed |= BuildAuthoredParkingBuilding(
                "PetrolStationVisual_SimpleTown",
                PetrolStationSourcePath,
                180f,
                PetrolStationParkingPositions,
                Vector3.left,
                overwrite,
                PetrolStationFuelIslandClearanceSize,
                PetrolStationFuelIslandClearanceCenter,
                plainParkingSurface: true);
            changed |= BuildCompactBuildingWithDriveway(
                "PharmacyVisual_SimpleTown",
                PharmacySourcePath,
                0f,
                overwrite);
            changed |= BuildCompactBuildingWithDriveway(
                "CoffeeShopVisual_SimpleTown",
                CoffeeShopSourcePath,
                0f,
                overwrite);
            changed |= BuildTwoByTwoBuildingWithDriveway(
                "CinemaVisual_SimpleTown",
                CinemaSourcePath,
                0f,
                2,
                overwrite);
            changed |= BuildTwoByTwoBuildingWithDriveway(
                "AutoRepairVisual_SimpleTown",
                AutoRepairSourcePath,
                0f,
                2,
                overwrite);
            return changed;
        }

        private static bool BuildAuthoredParkingBuilding(
            string outputName,
            string sourcePath,
            float modelYawDegrees,
            Vector3[] parkingPositions,
            Vector3 parkingForward,
            bool overwrite,
            Vector2 clearanceSize = default,
            Vector3 clearanceCenter = default,
            string parkingSurfaceSourcePath = null,
            string parkingSurfaceChildName = null,
            float parkingSurfaceYawDegrees = 0f,
            float parkedVehicleScale = 1f,
            bool plainParkingSurface = false)
        {
            string outputPath =
                $"{OutputFolder}/{outputName}.prefab";
            GameObject existing =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    outputPath);
            if (!overwrite &&
                IsAuthoredParkingVisualCurrent(
                    existing,
                    parkingPositions,
                    parkingForward,
                    clearanceSize,
                    clearanceCenter,
                    parkingSurfaceSourcePath,
                    parkingSurfaceChildName,
                    parkingSurfaceYawDegrees,
                    parkedVehicleScale,
                    plainParkingSurface))
            {
                return false;
            }

            GameObject source =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    sourcePath);
            GameObject parkingSurfaceSource =
                LoadAuthoredParkingSurfaceSource(
                    parkingSurfaceSourcePath,
                    parkingSurfaceChildName);
            Material plainSurfaceMaterial = plainParkingSurface
                ? LoadOrCreatePetrolStationGroundMaterial()
                : null;
            bool expectsParkingSurface =
                plainParkingSurface ||
                !string.IsNullOrEmpty(parkingSurfaceSourcePath) ||
                !string.IsNullOrEmpty(parkingSurfaceChildName);
            if (source == null ||
                parkingPositions == null ||
                parkingPositions.Length == 0 ||
                parkingForward.sqrMagnitude <= 0.0001f ||
                (plainParkingSurface && plainSurfaceMaterial == null) ||
                (!plainParkingSurface &&
                 expectsParkingSurface &&
                 parkingSurfaceSource == null))
            {
                Debug.LogWarning(
                    $"[BuildingVisualPrefabBaker] Missing source or authored " +
                    $"parking poses for {outputName}.");
                return false;
            }

            GameObject root = new(outputName);
            try
            {
                CreateFootprintMarker(
                    root.transform,
                    "AuthoredSurface",
                    new Vector2(2f, 2f),
                    Vector3.zero);
                if (clearanceSize.x > 0f && clearanceSize.y > 0f)
                {
                    CreateFootprintMarker(
                        root.transform,
                        FuelIslandClearanceName,
                        clearanceSize,
                        clearanceCenter);
                }

                if (plainParkingSurface)
                {
                    CreatePlainParkingSurface(
                        root.transform,
                        plainSurfaceMaterial);
                }
                else if (parkingSurfaceSource != null)
                {
                    CreateAuthoredParkingSurface(
                        root.transform,
                        parkingSurfaceSource,
                        parkingSurfaceYawDegrees);
                }

                GameObject model =
                    (GameObject)
                    PrefabUtility.InstantiatePrefab(source);
                model.name = "Model";
                model.transform.SetParent(root.transform, false);
                FitBuildingModel(
                    model.transform,
                    root.transform,
                    new Vector2(1.9f, 1.9f),
                    Vector2.zero,
                    modelYawDegrees);
                ConfigureLitRenderers(model);

                var slots = new Transform[parkingPositions.Length];
                for (int index = 0;
                     index < parkingPositions.Length;
                     index++)
                {
                    slots[index] = CreateParkingAnchor(
                        root.transform,
                        $"ParkingSlot_{index}",
                        parkingPositions[index],
                        parkingForward);
                }

                Transform entrance = CreateParkingAnchor(
                    root.transform,
                    "ParkingEntrance",
                    new Vector3(0f, -0.95f, 0f));
                Transform exit = CreateParkingAnchor(
                    root.transform,
                    "ParkingExit",
                    new Vector3(0f, -0.95f, 0f));
                ConfigureParkingLayout(
                    root,
                    slots,
                    entrance,
                    exit,
                    parkedVehicleScale);

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

        private static bool IsAuthoredParkingVisualCurrent(
            GameObject visual,
            Vector3[] parkingPositions,
            Vector3 parkingForward,
            Vector2 clearanceSize = default,
            Vector3 clearanceCenter = default,
            string parkingSurfaceSourcePath = null,
            string parkingSurfaceChildName = null,
            float parkingSurfaceYawDegrees = 0f,
            float parkedVehicleScale = 1f,
            bool plainParkingSurface = false)
        {
            if (visual == null ||
                parkingPositions == null ||
                parkingPositions.Length == 0 ||
                parkingForward.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            Transform surface =
                visual.transform.Find("AuthoredSurface");
            BuildingParkingLayout layout = visual != null
                ? visual.GetComponent<BuildingParkingLayout>()
                : null;
            if (visual.transform.Find("Model") == null ||
                surface == null ||
                !Approximately(
                    surface.localPosition,
                    Vector3.zero) ||
                !Approximately(
                    surface.localScale,
                    new Vector3(2f, 2f, 1f)) ||
                visual.transform.Find("Driveway_0") != null ||
                visual.transform.Find("BuildingFoundation") != null ||
                layout == null ||
                layout.ParkingSlotCount != parkingPositions.Length ||
                Mathf.Abs(
                    layout.ParkedVehicleScale -
                    Mathf.Clamp(parkedVehicleScale, 0.5f, 1f)) > 0.0001f)
            {
                return false;
            }

            Vector3 expectedForward = parkingForward.normalized;
            for (int index = 0;
                 index < parkingPositions.Length;
                 index++)
            {
                Transform slot = visual.transform.Find(
                    $"ParkingSlot_{index}");
                if (slot == null ||
                    !Approximately(
                        slot.localPosition,
                        parkingPositions[index]) ||
                    Vector3.Dot(
                        slot.forward.normalized,
                        expectedForward) < 0.999f)
                {
                    return false;
                }
            }

            if (visual.transform.Find(
                    $"ParkingSlot_{parkingPositions.Length}") != null)
            {
                return false;
            }

            Transform clearance =
                visual.transform.Find(FuelIslandClearanceName);
            bool expectsClearance =
                clearanceSize.x > 0f && clearanceSize.y > 0f;
            if (expectsClearance &&
                (clearance == null ||
                 !Approximately(
                     clearance.localPosition,
                     clearanceCenter) ||
                 !Approximately(
                     clearance.localScale,
                     new Vector3(
                         clearanceSize.x,
                         clearanceSize.y,
                         1f))))
            {
                return false;
            }

            if (!expectsClearance && clearance != null)
            {
                return false;
            }

            GameObject parkingSurfaceSource =
                LoadAuthoredParkingSurfaceSource(
                    parkingSurfaceSourcePath,
                    parkingSurfaceChildName);
            Material plainSurfaceMaterial = plainParkingSurface
                ? LoadOrCreatePetrolStationGroundMaterial()
                : null;
            bool expectsParkingSurface =
                plainParkingSurface ||
                !string.IsNullOrEmpty(parkingSurfaceSourcePath) ||
                !string.IsNullOrEmpty(parkingSurfaceChildName);
            Transform parkingSurface =
                visual.transform.Find(AuthoredParkingSurfaceName);
            if (plainParkingSurface &&
                (plainSurfaceMaterial == null ||
                 !IsPlainParkingSurfaceCurrent(
                     visual,
                     parkingSurface,
                     plainSurfaceMaterial)))
            {
                return false;
            }

            if (!plainParkingSurface &&
                expectsParkingSurface &&
                (parkingSurfaceSource == null ||
                 !IsAuthoredParkingSurfaceCurrent(
                     visual,
                     parkingSurface,
                     parkingSurfaceSource,
                     parkingSurfaceYawDegrees)))
            {
                return false;
            }

            if (!expectsParkingSurface && parkingSurface != null)
            {
                return false;
            }

            return HasParkingEntryAndExit(visual) &&
                   AreAllRenderersLit(visual);
        }

        private static bool BuildCompactBuildingWithDriveway(
            string outputName,
            string sourcePath,
            float modelYawDegrees,
            bool overwrite)
        {
            return BuildBuildingWithDriveway(
                outputName,
                sourcePath,
                modelYawDegrees,
                new Vector2Int(1, 2),
                2,
                overwrite);
        }

        // Authored 2x2 buildings share the regular building contract:
        // rear 2x1 model/foundation, front 2x1 common driveway.
        private static bool BuildTwoByTwoBuildingWithDriveway(
            string outputName,
            string sourcePath,
            float modelYawDegrees,
            int parkingSlotCount,
            bool overwrite)
        {
            return BuildBuildingWithDriveway(
                outputName,
                sourcePath,
                modelYawDegrees,
                new Vector2Int(2, 2),
                parkingSlotCount,
                overwrite);
        }

        private static bool BuildBuildingWithDriveway(
            string outputName,
            string sourcePath,
            float modelYawDegrees,
            Vector2Int footprint,
            int parkingSlotCount,
            bool overwrite)
        {
            string outputPath =
                $"{OutputFolder}/{outputName}.prefab";
            GameObject existing =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    outputPath);
            if (!overwrite &&
                IsDrivewayVisualCurrent(
                    existing,
                    footprint,
                    parkingSlotCount))
            {
                return false;
            }

            GameObject source =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    sourcePath);
            GameObject foundationPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    BuildingFoundationPrefabPath);
            GameObject drivewayPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    DrivewayPrefabPath);
            Material drivewayBoundaryMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    DrivewayBoundaryMaterialPath);
            if (source == null ||
                foundationPrefab == null ||
                drivewayPrefab == null ||
                drivewayBoundaryMaterial == null ||
                footprint.y != 2 ||
                footprint.x < 1 ||
                parkingSlotCount <= 0)
            {
                Debug.LogWarning(
                    $"[BuildingVisualPrefabBaker] Missing source, foundation, " +
                    $"driveway, or parking slots for {outputName}.");
                return false;
            }

            GameObject root = new(outputName);
            try
            {
                CreateFootprintMarker(
                    root.transform,
                    "BuildingSurface",
                    new Vector2(footprint.x, 1f),
                    new Vector3(0f, 0.5f, 0f));
                CreateSurfacePrefab(
                    root.transform,
                    "BuildingFoundation",
                    foundationPrefab,
                    new Vector2(footprint.x, 1f),
                    new Vector2(0f, 0.5f),
                    0f);
                CreateFootprintMarker(
                    root.transform,
                    "ParkingLot",
                    new Vector2(footprint.x, 1f),
                    new Vector3(0f, -0.5f, 0f));

                GameObject model =
                    (GameObject)
                    PrefabUtility.InstantiatePrefab(source);
                model.name = "Model";
                model.transform.SetParent(root.transform, false);
                FitBuildingModel(
                    model.transform,
                    root.transform,
                    new Vector2(
                        footprint.x - 0.1f,
                        0.9f),
                    new Vector2(0f, 0.5f),
                    modelYawDegrees);
                ConfigureLitRenderers(model);

                Transform[] parkingSlots =
                    CreateDrivewayParking(
                        root.transform,
                        drivewayPrefab,
                        footprint.x,
                        parkingSlotCount);
                CreateDrivewayPerimeter(
                    root.transform,
                    footprint.x,
                    drivewayBoundaryMaterial);
                Transform entrance =
                    CreateParkingAnchor(
                        root.transform,
                        "ParkingEntrance",
                        new Vector3(0f, -0.95f, 0f));
                Transform exit =
                    CreateParkingAnchor(
                        root.transform,
                        "ParkingExit",
                        new Vector3(0f, -0.95f, 0f));
                ConfigureParkingLayout(
                    root,
                    parkingSlots,
                    entrance,
                    exit);

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

        private static bool IsDrivewayVisualCurrent(
            GameObject visual,
            Vector2Int footprint,
            int parkingSlotCount)
        {
            if (visual == null ||
                footprint.y != 2 ||
                footprint.x < 1 ||
                parkingSlotCount <= 0 ||
                visual.transform.Find("ParkingLine_Center") != null)
            {
                return false;
            }

            Transform model = visual.transform.Find("Model");
            Transform buildingSurface =
                visual.transform.Find("BuildingSurface");
            Transform parkingLot =
                visual.transform.Find("ParkingLot");
            Transform foundation =
                visual.transform.Find("BuildingFoundation/Model");
            BuildingParkingLayout layout =
                visual.GetComponent<BuildingParkingLayout>();
            if (model == null ||
                buildingSurface == null ||
                parkingLot == null ||
                foundation == null ||
                layout == null ||
                layout.ParkingSlotCount != parkingSlotCount ||
                !Approximately(
                    buildingSurface.localPosition,
                    new Vector3(0f, 0.5f, 0f)) ||
                !Approximately(
                    buildingSurface.localScale,
                    new Vector3(footprint.x, 1f, 1f)) ||
                !Approximately(
                    parkingLot.localPosition,
                    new Vector3(0f, -0.5f, 0f)) ||
                !Approximately(
                    parkingLot.localScale,
                    new Vector3(footprint.x, 1f, 1f)))
            {
                return false;
            }

            GameObject foundationPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    BuildingFoundationPrefabPath);
            GameObject drivewayPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    DrivewayPrefabPath);
            if (foundationPrefab == null ||
                drivewayPrefab == null ||
                !UsesSameRendererAssets(
                    foundation.gameObject,
                    foundationPrefab))
            {
                return false;
            }

            int drivewayCount = Mathf.CeilToInt(
                parkingSlotCount /
                (float)ParkingSlotsPerDriveway);
            for (int drivewayIndex = 0;
                 drivewayIndex < drivewayCount;
                 drivewayIndex++)
            {
                Transform driveway = visual.transform.Find(
                    $"Driveway_{drivewayIndex}/PathDriveway");
                if (driveway == null ||
                    !UsesSameRendererAssets(
                        driveway.gameObject,
                        drivewayPrefab))
                {
                    return false;
                }
            }

            if (visual.transform.Find(
                    $"Driveway_{drivewayCount}") != null)
            {
                return false;
            }

            float lotWidth = footprint.x;
            int visibleParkingSlotCount =
                drivewayCount * ParkingSlotsPerDriveway;
            float slotWidth =
                lotWidth / visibleParkingSlotCount;
            for (int slotIndex = 0;
                 slotIndex < parkingSlotCount;
                 slotIndex++)
            {
                float expectedX =
                    lotWidth * 0.5f -
                    slotWidth * (slotIndex + 0.5f);
                Transform slot = visual.transform.Find(
                    $"ParkingSlot_{slotIndex}");
                if (slot == null ||
                    !Approximately(
                        slot.localPosition,
                        new Vector3(expectedX, -0.5f, 0f)) ||
                    Vector3.Dot(
                        slot.forward.normalized,
                        Vector3.down) < 0.999f)
                {
                    return false;
                }
            }

            if (visual.transform.Find(
                    $"ParkingSlot_{parkingSlotCount}") != null)
            {
                return false;
            }

            return HasDrivewayPerimeter(
                       visual,
                       footprint.x) &&
                   HasParkingEntryAndExit(visual) &&
                   AreAllRenderersLit(visual);
        }

        private static bool HasDrivewayPerimeter(
            GameObject visual,
            float lotWidth)
        {
            Material expectedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    DrivewayBoundaryMaterialPath);
            if (visual == null || expectedMaterial == null)
            {
                return false;
            }

            DrivewayBoundarySegment[] segments =
                DrivewayBoundaryLayout.CreatePerimeter(
                    1f,
                    lotWidth,
                    1f,
                    new Vector2(0f, -0.5f));
            for (int index = 0; index < segments.Length; index++)
            {
                DrivewayBoundarySegment segment = segments[index];
                Transform boundary = visual.transform.Find(segment.Name);
                Renderer renderer = boundary != null
                    ? boundary.GetComponent<Renderer>()
                    : null;
                if (boundary == null ||
                    renderer == null ||
                    renderer.sharedMaterial != expectedMaterial ||
                    !Approximately(
                        boundary.localPosition,
                        new Vector3(
                            segment.Center.x,
                            segment.Center.y,
                            -DrivewayBoundaryLayout.SurfaceOffsetTiles)) ||
                    !Approximately(
                        boundary.localScale,
                        new Vector3(
                            segment.Size.x,
                            segment.Size.y,
                            1f)))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreAllRenderersLit(GameObject visual)
        {
            Renderer[] renderers =
                visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return false;
            }

            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                Material[] materials =
                    renderers[rendererIndex].sharedMaterials;
                if (materials.Length == 0)
                {
                    return false;
                }

                for (int materialIndex = 0;
                     materialIndex < materials.Length;
                     materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material == null ||
                        material.shader == null ||
                        material.shader.name !=
                        "Universal Render Pipeline/Lit")
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool HasParkingEntryAndExit(
            GameObject visual)
        {
            Transform entrance =
                visual.transform.Find("ParkingEntrance");
            Transform exit =
                visual.transform.Find("ParkingExit");
            Vector3 expectedPosition =
                new(0f, -0.95f, 0f);
            return entrance != null &&
                   exit != null &&
                   Approximately(
                       entrance.localPosition,
                       expectedPosition) &&
                   Approximately(
                       exit.localPosition,
                       expectedPosition) &&
                   Vector3.Dot(
                       entrance.forward.normalized,
                       Vector3.down) > 0.999f &&
                   Vector3.Dot(
                       exit.forward.normalized,
                       Vector3.down) > 0.999f;
        }

        private static bool UsesSameRendererAssets(
            GameObject instance,
            GameObject source)
        {
            if (instance == null || source == null)
            {
                return false;
            }

            MeshFilter[] instanceFilters =
                instance.GetComponentsInChildren<MeshFilter>(true);
            MeshFilter[] sourceFilters =
                source.GetComponentsInChildren<MeshFilter>(true);
            Renderer[] instanceRenderers =
                instance.GetComponentsInChildren<Renderer>(true);
            Renderer[] sourceRenderers =
                source.GetComponentsInChildren<Renderer>(true);
            if (instanceFilters.Length == 0 ||
                instanceFilters.Length != sourceFilters.Length ||
                instanceRenderers.Length == 0 ||
                instanceRenderers.Length != sourceRenderers.Length)
            {
                return false;
            }

            for (int index = 0;
                 index < instanceFilters.Length;
                 index++)
            {
                if (instanceFilters[index].sharedMesh !=
                    sourceFilters[index].sharedMesh)
                {
                    return false;
                }
            }

            for (int rendererIndex = 0;
                 rendererIndex < instanceRenderers.Length;
                 rendererIndex++)
            {
                Material[] instanceMaterials =
                    instanceRenderers[rendererIndex].sharedMaterials;
                Material[] sourceMaterials =
                    sourceRenderers[rendererIndex].sharedMaterials;
                if (instanceMaterials.Length != sourceMaterials.Length)
                {
                    return false;
                }

                for (int materialIndex = 0;
                     materialIndex < instanceMaterials.Length;
                     materialIndex++)
                {
                    if (instanceMaterials[materialIndex] !=
                        sourceMaterials[materialIndex])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static GameObject LoadAuthoredParkingSurfaceSource(
            string sourcePath,
            string childName)
        {
            if (string.IsNullOrEmpty(sourcePath) ||
                string.IsNullOrEmpty(childName))
            {
                return null;
            }

            GameObject sourceRoot =
                AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Transform sourceChild = sourceRoot != null
                ? sourceRoot.transform.Find(childName)
                : null;
            GameObject source = sourceChild != null
                ? sourceChild.gameObject
                : null;
            return source != null &&
                   source.GetComponent<MeshFilter>() != null &&
                   source.GetComponent<MeshRenderer>() != null
                ? source
                : null;
        }

        private static void CreateAuthoredParkingSurface(
            Transform parent,
            GameObject source,
            float modelYawDegrees)
        {
            var surfaceRoot = new GameObject(
                AuthoredParkingSurfaceName);
            surfaceRoot.transform.SetParent(parent, false);

            var model = new GameObject("Model");
            model.transform.SetParent(surfaceRoot.transform, false);
            MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
            MeshRenderer sourceRenderer =
                source.GetComponent<MeshRenderer>();
            MeshFilter filter = model.AddComponent<MeshFilter>();
            MeshRenderer renderer = model.AddComponent<MeshRenderer>();
            filter.sharedMesh = sourceFilter.sharedMesh;
            renderer.sharedMaterials = sourceRenderer.sharedMaterials;

            FitSurfaceModel(
                model.transform,
                parent,
                new Vector2(2f, 2f),
                Vector2.zero,
                modelYawDegrees);
            ConfigureLitRenderers(model);
        }

        private static void CreatePlainParkingSurface(
            Transform parent,
            Material material)
        {
            var surfaceRoot = new GameObject(
                AuthoredParkingSurfaceName);
            surfaceRoot.transform.SetParent(parent, false);

            GameObject model = GameObject.CreatePrimitive(
                PrimitiveType.Quad);
            model.name = "Model";
            model.transform.SetParent(surfaceRoot.transform, false);
            model.transform.localPosition = new Vector3(
                0f,
                0f,
                -DrivewayBoundaryLayout.SurfaceOffsetTiles);
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = new Vector3(2f, 2f, 1f);

            Collider collider = model.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            MeshRenderer renderer = model.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            ConfigureLitRenderers(model);
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static Material LoadOrCreatePetrolStationGroundMaterial()
        {
            Shader shader = Shader.Find(
                "Universal Render Pipeline/Lit");
            if (shader == null)
            {
                return null;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                PetrolStationGroundMaterialPath);
            bool created = material == null;
            if (created)
            {
                material = new Material(shader)
                {
                    name = "PetrolStationGround_URP_Lit"
                };
                AssetDatabase.CreateAsset(
                    material,
                    PetrolStationGroundMaterialPath);
            }

            Color surfaceColor = new(0.2f, 0.22f, 0.24f, 1f);
            bool changed = material.shader != shader ||
                material.mainTexture != null ||
                material.GetColor("_BaseColor") != surfaceColor ||
                !Mathf.Approximately(
                    material.GetFloat("_Metallic"),
                    0f) ||
                !Mathf.Approximately(
                    material.GetFloat("_Smoothness"),
                    0.15f);
            if (changed)
            {
                material.shader = shader;
                material.mainTexture = null;
                material.SetTexture("_BaseMap", null);
                material.SetColor("_BaseColor", surfaceColor);
                material.SetColor("_Color", surfaceColor);
                material.SetFloat("_Metallic", 0f);
                material.SetFloat("_Smoothness", 0.15f);
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        private static bool IsPlainParkingSurfaceCurrent(
            GameObject visual,
            Transform surface,
            Material material)
        {
            if (visual == null || surface == null || material == null ||
                !surface.gameObject.activeSelf ||
                !Approximately(surface.localPosition, Vector3.zero) ||
                Mathf.Abs(Quaternion.Dot(
                    surface.localRotation,
                    Quaternion.identity)) < 0.9999f ||
                !Approximately(surface.localScale, Vector3.one) ||
                surface.GetComponentsInChildren<Collider>(true).Length != 0)
            {
                return false;
            }

            Transform model = surface.Find("Model");
            MeshFilter filter =
                model != null ? model.GetComponent<MeshFilter>() : null;
            MeshRenderer renderer =
                model != null ? model.GetComponent<MeshRenderer>() : null;
            if (model == null ||
                !model.gameObject.activeSelf ||
                filter == null ||
                filter.sharedMesh == null ||
                renderer == null ||
                !renderer.enabled ||
                renderer.forceRenderingOff ||
                renderer.sharedMaterial != material ||
                material.mainTexture != null ||
                !Approximately(
                    model.localPosition,
                    new Vector3(
                        0f,
                        0f,
                        -DrivewayBoundaryLayout.SurfaceOffsetTiles)) ||
                Mathf.Abs(Quaternion.Dot(
                    model.localRotation,
                    Quaternion.identity)) < 0.9999f ||
                !Approximately(
                    model.localScale,
                    new Vector3(2f, 2f, 1f)) ||
                !TryGetLocalRendererBounds(
                    model,
                    visual.transform,
                    out Bounds bounds))
            {
                return false;
            }

            return Mathf.Abs(bounds.center.x) <= 0.001f &&
                   Mathf.Abs(bounds.center.y) <= 0.001f &&
                   Mathf.Abs(bounds.size.x - 2f) <= 0.001f &&
                   Mathf.Abs(bounds.size.y - 2f) <= 0.001f &&
                   Mathf.Abs(
                       bounds.min.z +
                       DrivewayBoundaryLayout.SurfaceOffsetTiles) <= 0.001f &&
                   Mathf.Abs(bounds.size.z) <= 0.001f;
        }

        private static bool IsAuthoredParkingSurfaceCurrent(
            GameObject visual,
            Transform surface,
            GameObject source,
            float modelYawDegrees)
        {
            if (visual == null || surface == null || source == null ||
                !surface.gameObject.activeSelf ||
                !Approximately(surface.localPosition, Vector3.zero) ||
                Mathf.Abs(Quaternion.Dot(
                    surface.localRotation,
                    Quaternion.identity)) < 0.9999f ||
                !Approximately(surface.localScale, Vector3.one) ||
                surface.GetComponentsInChildren<Collider>(true).Length != 0)
            {
                return false;
            }

            Transform model = surface.Find("Model");
            Quaternion expectedRotation =
                Quaternion.Euler(-90f, 0f, 0f) *
                Quaternion.Euler(0f, modelYawDegrees, 0f);
            MeshRenderer renderer =
                model != null ? model.GetComponent<MeshRenderer>() : null;
            if (model == null ||
                !model.gameObject.activeSelf ||
                renderer == null ||
                !renderer.enabled ||
                renderer.forceRenderingOff ||
                Mathf.Abs(Quaternion.Dot(
                    model.localRotation,
                    expectedRotation)) < 0.9999f ||
                !UsesSameRendererAssets(model.gameObject, source) ||
                !TryGetLocalRendererBounds(
                    model,
                    visual.transform,
                    out Bounds bounds))
            {
                return false;
            }

            return Mathf.Abs(bounds.center.x) <= 0.001f &&
                   Mathf.Abs(bounds.center.y) <= 0.001f &&
                   Mathf.Abs(bounds.size.x - 2f) <= 0.001f &&
                   Mathf.Abs(bounds.size.y - 2f) <= 0.001f &&
                   Mathf.Abs(bounds.min.z) <= 0.001f;
        }

        private static bool Approximately(
            Vector3 actual,
            Vector3 expected)
        {
            return (actual - expected).sqrMagnitude <= 0.000001f;
        }

        private static void CreateFootprintMarker(
            Transform parent,
            string name,
            Vector2 size,
            Vector3 localPosition)
        {
            var marker = new GameObject(name);
            marker.transform.SetParent(parent, false);
            marker.transform.localPosition = localPosition;
            marker.transform.localScale =
                new Vector3(size.x, size.y, 1f);
        }

        private static void CreateSurfacePrefab(
            Transform parent,
            string name,
            GameObject sourcePrefab,
            Vector2 targetSize,
            Vector2 targetCenter,
            float modelYawDegrees)
        {
            var surfaceRoot = new GameObject(name);
            surfaceRoot.transform.SetParent(parent, false);

            GameObject model =
                (GameObject)
                PrefabUtility.InstantiatePrefab(sourcePrefab);
            model.name = "Model";
            model.transform.SetParent(surfaceRoot.transform, false);
            FitSurfaceModel(
                model.transform,
                parent,
                targetSize,
                targetCenter,
                modelYawDegrees);
            ConfigureLitRenderers(model);
        }

        private static Transform[] CreateDrivewayParking(
            Transform parent,
            GameObject drivewayPrefab,
            float lotWidth,
            int parkingSlotCount)
        {
            int drivewayCount = Mathf.CeilToInt(
                parkingSlotCount /
                (float)ParkingSlotsPerDriveway);
            float drivewayWidth = lotWidth / drivewayCount;

            for (int drivewayIndex = 0;
                 drivewayIndex < drivewayCount;
                 drivewayIndex++)
            {
                float drivewayX =
                    lotWidth * 0.5f -
                    drivewayWidth *
                    (drivewayIndex + 0.5f);
                var drivewayRoot = new GameObject(
                    $"Driveway_{drivewayIndex}");
                drivewayRoot.transform.SetParent(parent, false);
                drivewayRoot.transform.localPosition =
                    new Vector3(drivewayX, -0.5f, 0f);
                drivewayRoot.transform.localRotation =
                    Quaternion.Euler(0f, 0f, 90f);

                GameObject driveway =
                    (GameObject)
                    PrefabUtility.InstantiatePrefab(drivewayPrefab);
                driveway.name = "PathDriveway";
                driveway.transform.SetParent(
                    drivewayRoot.transform,
                    false);
                FitSurfaceModel(
                    driveway.transform,
                    drivewayRoot.transform,
                    new Vector2(1f, drivewayWidth),
                    Vector2.zero,
                    0f);
                ConfigureLitRenderers(driveway);
            }

            var parkingSlots =
                new Transform[parkingSlotCount];
            int visibleParkingSlotCount =
                drivewayCount * ParkingSlotsPerDriveway;
            float slotWidth =
                lotWidth / visibleParkingSlotCount;
            for (int slotIndex = 0;
                 slotIndex < parkingSlotCount;
                 slotIndex++)
            {
                float slotX =
                    lotWidth * 0.5f -
                    slotWidth * (slotIndex + 0.5f);
                parkingSlots[slotIndex] =
                    CreateParkingAnchor(
                        parent,
                        $"ParkingSlot_{slotIndex}",
                        new Vector3(slotX, -0.5f, 0f));
            }

            return parkingSlots;
        }

        private static void CreateDrivewayPerimeter(
            Transform parent,
            float lotWidth,
            Material material)
        {
            DrivewayBoundarySegment[] segments =
                DrivewayBoundaryLayout.CreatePerimeter(
                    1f,
                    lotWidth,
                    1f,
                    new Vector2(0f, -0.5f));
            for (int index = 0; index < segments.Length; index++)
            {
                DrivewayBoundarySegment segment = segments[index];
                GameObject boundary =
                    GameObject.CreatePrimitive(PrimitiveType.Quad);
                boundary.name = segment.Name;
                boundary.transform.SetParent(parent, false);
                boundary.transform.localPosition = new Vector3(
                    segment.Center.x,
                    segment.Center.y,
                    -DrivewayBoundaryLayout.SurfaceOffsetTiles);
                boundary.transform.localScale = new Vector3(
                    segment.Size.x,
                    segment.Size.y,
                    1f);

                Collider collider = boundary.GetComponent<Collider>();
                if (collider != null)
                {
                    Object.DestroyImmediate(collider);
                }

                Renderer renderer = boundary.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        private static void FitSurfaceModel(
            Transform model,
            Transform relativeTo,
            Vector2 targetSize,
            Vector2 targetCenter,
            float modelYawDegrees)
        {
            model.localPosition = Vector3.zero;
            model.localRotation =
                Quaternion.Euler(-90f, 0f, 0f) *
                Quaternion.Euler(0f, modelYawDegrees, 0f);
            model.localScale = Vector3.one;

            if (!TryGetLocalRendererBounds(
                    model,
                    relativeTo,
                    out Bounds sourceBounds))
            {
                return;
            }

            float scaleX = targetSize.x /
                           Mathf.Max(
                               0.0001f,
                               sourceBounds.size.x);
            float scaleY = targetSize.y /
                           Mathf.Max(
                               0.0001f,
                               sourceBounds.size.y);
            model.localScale = new Vector3(
                scaleX,
                Mathf.Min(scaleX, scaleY),
                scaleY);

            if (!TryGetLocalRendererBounds(
                    model,
                    relativeTo,
                    out Bounds fittedBounds))
            {
                return;
            }

            model.localPosition = new Vector3(
                targetCenter.x - fittedBounds.center.x,
                targetCenter.y - fittedBounds.center.y,
                -fittedBounds.min.z);
        }

        private static void ConfigureLitRenderers(
            GameObject root)
        {
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                renderer.enabled = true;
                renderer.forceRenderingOff = false;
                renderer.allowOcclusionWhenDynamic = false;
                renderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        private static Transform CreateParkingAnchor(
            Transform parent,
            string name,
            Vector3 localPosition)
        {
            return CreateParkingAnchor(
                parent,
                name,
                localPosition,
                Vector3.down);
        }

        private static Transform CreateParkingAnchor(
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 localForward)
        {
            var anchor = new GameObject(name);
            anchor.transform.SetParent(parent, false);
            anchor.transform.localPosition = localPosition;
            anchor.transform.localRotation =
                Quaternion.LookRotation(
                    localForward.normalized,
                    Vector3.back);
            return anchor.transform;
        }

        private static void ConfigureParkingLayout(
            GameObject root,
            Transform[] parkingSlots,
            Transform entrance,
            Transform exit,
            float parkedVehicleScale = 1f)
        {
            BuildingParkingLayout layout =
                root.AddComponent<BuildingParkingLayout>();
            var serialized = new SerializedObject(layout);
            SerializedProperty slots =
                serialized.FindProperty("parkingSlots");
            slots.arraySize = parkingSlots.Length;
            for (int index = 0;
                 index < parkingSlots.Length;
                 index++)
            {
                slots.GetArrayElementAtIndex(index)
                    .objectReferenceValue = parkingSlots[index];
            }
            serialized.FindProperty("entrance")
                .objectReferenceValue = entrance;
            serialized.FindProperty("exit")
                .objectReferenceValue = exit;
            serialized.FindProperty("parkedVehicleScale")
                .floatValue = Mathf.Clamp(
                    parkedVehicleScale,
                    0.5f,
                    1f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void FitBuildingModel(
            Transform model,
            Transform relativeTo,
            Vector2 targetSize,
            Vector2 targetCenter,
            float modelYawDegrees)
        {
            model.localPosition = Vector3.zero;
            model.localRotation =
                Quaternion.Euler(-90f, 0f, 0f) *
                Quaternion.Euler(0f, modelYawDegrees, 0f);
            model.localScale = Vector3.one;

            if (!TryGetLocalRendererBounds(
                    model,
                    relativeTo,
                    out Bounds sourceBounds))
            {
                return;
            }

            float scaleX = targetSize.x /
                           Mathf.Max(
                               0.0001f,
                               sourceBounds.size.x);
            float scaleY = targetSize.y /
                           Mathf.Max(
                               0.0001f,
                               sourceBounds.size.y);
            model.localScale = new Vector3(
                scaleX,
                Mathf.Min(scaleX, scaleY),
                scaleY);

            if (!TryGetLocalRendererBounds(
                    model,
                    relativeTo,
                    out Bounds fittedBounds))
            {
                return;
            }

            model.localPosition = new Vector3(
                targetCenter.x - fittedBounds.center.x,
                targetCenter.y - fittedBounds.center.y,
                -fittedBounds.max.z);
        }

        private static bool UpdatePoliceDefinition()
        {
            return UpdateDefinition(
                PoliceDefinitionPath,
                "PoliceStationVisual_StudioHorizon",
                new Vector2Int(2, 2));
        }

        private static bool UpdateCommercialDefinitions()
        {
            bool changed = false;
            changed |= UpdateDefinition(
                MallDefinitionPath,
                "MallVisual_SimpleTown",
                new Vector2Int(2, 2),
                MallParkingPositions.Length);
            changed |= UpdateDefinition(
                PetrolStationDefinitionPath,
                "PetrolStationVisual_SimpleTown",
                new Vector2Int(2, 2));
            changed |= UpdateDefinition(
                PharmacyDefinitionPath,
                "PharmacyVisual_SimpleTown",
                new Vector2Int(1, 2));
            changed |= UpdateDefinition(
                CoffeeShopDefinitionPath,
                "CoffeeShopVisual_SimpleTown",
                new Vector2Int(1, 2));
            changed |= UpdateDefinition(
                CinemaDefinitionPath,
                "CinemaVisual_SimpleTown",
                new Vector2Int(2, 2));
            changed |= UpdateDefinition(
                AutoRepairDefinitionPath,
                "AutoRepairVisual_SimpleTown",
                new Vector2Int(2, 2));
            return changed;
        }

        private static bool UpdateDefinition(
            string definitionPath,
            string visualName,
            Vector2Int footprint,
            int visitorParkingSlotCount = -1)
        {
            BuildingDefinitionSO definition =
                AssetDatabase.LoadAssetAtPath<BuildingDefinitionSO>(
                    definitionPath);
            GameObject visual =
                LoadWrapper(visualName);
            if (definition == null || visual == null)
            {
                return false;
            }

            var serialized = new SerializedObject(definition);
            SerializedProperty visualProperty =
                serialized.FindProperty("visualPrefab");
            SerializedProperty footprintProperty =
                serialized.FindProperty("footprint");
            SerializedProperty visitorParkingSlotCountProperty =
                serialized.FindProperty("visitorParkingSlotCount");
            bool changed = false;
            if (visualProperty.objectReferenceValue != visual)
            {
                visualProperty.objectReferenceValue = visual;
                changed = true;
            }

            if (footprintProperty.vector2IntValue != footprint)
            {
                footprintProperty.vector2IntValue = footprint;
                changed = true;
            }

            if (visitorParkingSlotCount > 0 &&
                visitorParkingSlotCountProperty != null &&
                visitorParkingSlotCountProperty.intValue !=
                visitorParkingSlotCount)
            {
                visitorParkingSlotCountProperty.intValue =
                    visitorParkingSlotCount;
                changed = true;
            }

            if (!changed)
            {
                return false;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return true;
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
            return TryGetLocalRendererBounds(
                root,
                root,
                out bounds);
        }

        private static bool TryGetLocalRendererBounds(
            Transform contentRoot,
            Transform relativeTo,
            out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;
            Renderer[] renderers =
                contentRoot.GetComponentsInChildren<Renderer>(true);

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
                        relativeTo.InverseTransformPoint(world);
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

            GameObject policeVisual =
                LoadWrapper(
                    "PoliceStationVisual_StudioHorizon");
            if (!IsDefinitionCurrent(
                    PoliceDefinitionPath,
                    policeVisual,
                    new Vector2Int(2, 2)) ||
                !IsDrivewayVisualCurrent(
                    policeVisual,
                    new Vector2Int(2, 2),
                    2))
            {
                Debug.LogError(
                    "[BuildingVisualPrefabBaker] Police station visual, " +
                    "Lit surfaces, or common 2x1 driveway layout is incomplete.");
                return;
            }

            GameObject mallVisual =
                LoadWrapper("MallVisual_SimpleTown");
            GameObject petrolStationVisual =
                LoadWrapper("PetrolStationVisual_SimpleTown");
            GameObject pharmacyVisual =
                LoadWrapper("PharmacyVisual_SimpleTown");
            GameObject coffeeShopVisual =
                LoadWrapper("CoffeeShopVisual_SimpleTown");
            GameObject cinemaVisual =
                LoadWrapper("CinemaVisual_SimpleTown");
            GameObject autoRepairVisual =
                LoadWrapper("AutoRepairVisual_SimpleTown");
            if (!IsDefinitionCurrent(
                    MallDefinitionPath,
                    mallVisual,
                    new Vector2Int(2, 2),
                    MallParkingPositions.Length) ||
                !IsAuthoredParkingVisualCurrent(
                    mallVisual,
                    MallParkingPositions,
                    Vector3.right,
                    parkedVehicleScale: MallParkedVehicleScale) ||
                !IsDefinitionCurrent(
                    PetrolStationDefinitionPath,
                    petrolStationVisual,
                    new Vector2Int(2, 2)) ||
                !IsAuthoredParkingVisualCurrent(
                    petrolStationVisual,
                    PetrolStationParkingPositions,
                    Vector3.left,
                    PetrolStationFuelIslandClearanceSize,
                    PetrolStationFuelIslandClearanceCenter,
                    plainParkingSurface: true) ||
                !IsDefinitionCurrent(
                    PharmacyDefinitionPath,
                    pharmacyVisual,
                    new Vector2Int(1, 2)) ||
                !IsDrivewayVisualCurrent(
                    pharmacyVisual,
                    new Vector2Int(1, 2),
                    2) ||
                !IsDefinitionCurrent(
                    CoffeeShopDefinitionPath,
                    coffeeShopVisual,
                    new Vector2Int(1, 2)) ||
                !IsDrivewayVisualCurrent(
                    coffeeShopVisual,
                    new Vector2Int(1, 2),
                    2) ||
                !IsDefinitionCurrent(
                    CinemaDefinitionPath,
                    cinemaVisual,
                    new Vector2Int(2, 2)) ||
                !IsDrivewayVisualCurrent(
                    cinemaVisual,
                    new Vector2Int(2, 2),
                    2) ||
                !IsDefinitionCurrent(
                    AutoRepairDefinitionPath,
                    autoRepairVisual,
                    new Vector2Int(2, 2)) ||
                !IsDrivewayVisualCurrent(
                    autoRepairVisual,
                    new Vector2Int(2, 2),
                    2))
            {
                Debug.LogError(
                    "[BuildingVisualPrefabBaker] Commercial visuals, " +
                    "footprints, authored parking, or common Lit driveway " +
                    "layouts are incomplete.");
                return;
            }

            Debug.Log(
                "[BuildingVisualPrefabBaker] Validated project-owned " +
                "building, commercial parking, and foundation prefabs.");
        }

        private static bool IsDefinitionCurrent(
            string definitionPath,
            GameObject visual,
            Vector2Int footprint,
            int visitorParkingSlotCount = -1)
        {
            BuildingDefinitionSO definition =
                AssetDatabase.LoadAssetAtPath<BuildingDefinitionSO>(
                    definitionPath);
            return definition != null &&
                   visual != null &&
                   definition.VisualPrefab == visual &&
                   definition.Footprint == footprint &&
                   (visitorParkingSlotCount <= 0 ||
                    definition.VisitorParkingSlotCount ==
                    visitorParkingSlotCount);
        }
    }
}
#endif
