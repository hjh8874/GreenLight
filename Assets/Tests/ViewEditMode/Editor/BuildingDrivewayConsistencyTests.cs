using System.Reflection;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CityFlow.Tests
{
    public sealed class BuildingDrivewayConsistencyTests
    {
        private const string PoliceVisualPath =
            "Assets/02_Prefabs/Buildings/" +
            "PoliceStationVisual_StudioHorizon.prefab";
        private const string FoundationPath =
            "Assets/02_Prefabs/Buildings/BuildingFoundation.prefab";
        private const string DrivewayPath =
            "Assets/02_Prefabs/Environment/Driveways/" +
            "SimpleTownDriveway.prefab";
        private const string BuildingFolder =
            "Assets/02_Prefabs/Buildings/";
        private const string DefinitionFolder =
            "Assets/05_ScriptableObjects/Buildings/";
        private const string MallSourcePath =
            "Assets/99_Download/SimpleTown/Prefabs/Buildings/" +
            "Building_Mall.prefab";
        private const string PetrolStationGroundMaterialPath =
            BuildingFolder + "PetrolStationGround_URP_Lit.mat";

        [Test]
        public void PoliceStation_UsesCommonLitTwoByOneDriveway()
        {
            GameObject policeVisual =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    PoliceVisualPath);
            GameObject foundationPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    FoundationPath);
            GameObject drivewayPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    DrivewayPath);
            BuildingDefinitionSO policeDefinition =
                AssetDatabase.LoadAssetAtPath<BuildingDefinitionSO>(
                    DefinitionFolder + "Building_PoliceStation.asset");

            Assert.NotNull(policeVisual);
            Assert.NotNull(foundationPrefab);
            Assert.NotNull(drivewayPrefab);
            Assert.NotNull(policeDefinition);
            Assert.AreSame(policeVisual, policeDefinition.VisualPrefab);
            Assert.IsFalse(policeDefinition.CanReceiveVisitors);

            GameObject instance =
                Object.Instantiate(policeVisual);
            try
            {
                Transform buildingMarker =
                    instance.transform.Find("BuildingSurface");
                Transform parkingMarker =
                    instance.transform.Find("ParkingLot");
                Transform foundation =
                    instance.transform.Find("BuildingFoundation/Model");
                Transform driveway =
                    instance.transform.Find("Driveway_0/PathDriveway");

                Assert.NotNull(buildingMarker);
                Assert.NotNull(parkingMarker);
                Assert.NotNull(foundation);
                Assert.NotNull(driveway);
                Assert.IsNull(
                    buildingMarker.GetComponentInChildren<Renderer>(true));
                Assert.IsNull(
                    parkingMarker.GetComponentInChildren<Renderer>(true));

                AssertUsesSameMeshAndMaterial(
                    foundation.gameObject,
                    foundationPrefab);
                AssertUsesSameMeshAndMaterial(
                    driveway.gameObject,
                    drivewayPrefab);
                AssertLitMaterials(instance);

                Assert.IsTrue(TryGetLocalRendererBounds(
                    foundation,
                    instance.transform,
                    out Bounds foundationBounds));
                Assert.That(
                    foundationBounds.size.x,
                    Is.EqualTo(2f).Within(0.001f));
                Assert.That(
                    foundationBounds.size.y,
                    Is.EqualTo(1f).Within(0.001f));
                Assert.That(
                    foundationBounds.min.z,
                    Is.EqualTo(0f).Within(0.001f));

                Assert.IsTrue(TryGetLocalRendererBounds(
                    driveway,
                    instance.transform,
                    out Bounds drivewayBounds));
                Assert.That(
                    drivewayBounds.size.x,
                    Is.EqualTo(2f).Within(0.001f));
                Assert.That(
                    drivewayBounds.size.y,
                    Is.EqualTo(1f).Within(0.001f));
                Assert.That(
                    drivewayBounds.min.z,
                    Is.EqualTo(0f).Within(0.001f));

                BuildingParkingLayout layout =
                    instance.GetComponent<BuildingParkingLayout>();
                Assert.NotNull(layout);
                Assert.AreEqual(2, layout.ParkingSlotCount);
                AssertParkingSlot(instance.transform, 0, 0.5f);
                AssertParkingSlot(instance.transform, 1, -0.5f);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void SpecialBuildingNightLighting_TargetsAuthoredModel()
        {
            const string catalogPath =
                "Assets/05_ScriptableObjects/Buildings/" +
                "SpecialBuildingCatalog.asset";
            BuildingCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<BuildingCatalogSO>(
                    catalogPath);
            Assert.NotNull(catalog);
            Assert.IsTrue(catalog.TryGet(
                "police_station",
                out BuildingDefinitionSO definition));

            GameObject viewObject =
                new("Special Building Lighting Test");
            GameObject parentObject =
                new("Visual Parent");
            GameObject visual = null;
            try
            {
                SpecialBuildingView view =
                    viewObject.AddComponent<SpecialBuildingView>();
                MethodInfo createConfiguredVisual =
                    typeof(SpecialBuildingView).GetMethod(
                        "CreateConfiguredVisual",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                Assert.NotNull(createConfiguredVisual);

                visual =
                    (GameObject)createConfiguredVisual.Invoke(
                        view,
                        new object[]
                        {
                            definition,
                            parentObject.transform,
                            "Police Lighting Visual"
                        });
                Assert.NotNull(visual);

                Transform model = visual.transform.Find("Model");
                Transform driveway =
                    visual.transform.Find("Driveway_0");
                Transform foundation =
                    visual.transform.Find("BuildingFoundation");
                Assert.NotNull(model);
                Assert.NotNull(driveway);
                Assert.NotNull(foundation);
                Assert.NotNull(
                    model.GetComponent<BuildingNightLighting>());
                Assert.IsNull(
                    visual.GetComponent<BuildingNightLighting>());
                Assert.IsNull(
                    driveway.GetComponentInChildren<
                        BuildingNightLighting>(true));
                Assert.IsNull(
                    foundation.GetComponentInChildren<
                        BuildingNightLighting>(true));
            }
            finally
            {
                Object.DestroyImmediate(visual);
                Object.DestroyImmediate(parentObject);
                Object.DestroyImmediate(viewObject);
            }
        }

        [TestCase(PlacementDirection.North)]
        [TestCase(PlacementDirection.East)]
        [TestCase(PlacementDirection.South)]
        [TestCase(PlacementDirection.West)]
        public void PoliceStationParking_RotatesSlotsTowardFront(
            PlacementDirection direction)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    PoliceVisualPath);
            Assert.NotNull(prefab);

            GameObject instance = Object.Instantiate(prefab);
            try
            {
                instance.transform.rotation = Quaternion.Euler(
                    0f,
                    0f,
                    TileFootprint.ToAngle(direction));
                BuildingParkingLayout layout =
                    instance.GetComponent<BuildingParkingLayout>();
                Assert.NotNull(layout);
                Assert.IsTrue(layout.TryGetParkingPose(
                    0,
                    out BuildingParkingPose firstPose));
                Assert.IsTrue(layout.TryGetParkingPose(
                    1,
                    out BuildingParkingPose secondPose));

                Vector2Int front =
                    TileFootprint.GetFrontOffset(direction);
                Vector3 expectedForward =
                    new(front.x, front.y, 0f);
                Assert.Greater(
                    Vector3.Dot(
                        firstPose.WorldForward.normalized,
                        expectedForward),
                    0.999f);
                Assert.Greater(
                    Vector3.Dot(
                        secondPose.WorldForward.normalized,
                        expectedForward),
                    0.999f);
                Assert.That(
                    firstPose.WorldPosition.z,
                    Is.EqualTo(0f).Within(0.0001f));
                Assert.That(
                    secondPose.WorldPosition.z,
                    Is.EqualTo(0f).Within(0.0001f));
                Assert.Greater(
                    Vector3.Distance(
                        firstPose.WorldPosition,
                        secondPose.WorldPosition),
                    0.9f);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void MallAndPetrolStation_UseAuthoredParkingSurfaces()
        {
            AssertAuthoredCommercialVisual(
                BuildingFolder + "MallVisual_SimpleTown.prefab",
                DefinitionFolder + "Building_Mall.asset",
                new Vector2Int(2, 2),
                new[]
                {
                    new Vector3(0.107f, 0.105f, 0f),
                    new Vector3(0.107f, -0.108f, 0f),
                    new Vector3(0.107f, -0.322f, 0f),
                    new Vector3(0.107f, -0.535f, 0f),
                    new Vector3(0.107f, -0.749f, 0f)
                },
                Vector3.right);
            AssertAuthoredCommercialVisual(
                BuildingFolder +
                "PetrolStationVisual_SimpleTown.prefab",
                DefinitionFolder +
                "Building_PetrolStation.asset",
                new Vector2Int(2, 2),
                new[]
                {
                    new Vector3(-0.38f, -0.78f, 0f),
                    new Vector3(0.38f, -0.78f, 0f)
                },
                Vector3.left);
        }

        [Test]
        public void Mall_ParkingSlotsMatchFivePaintedBays()
        {
            GameObject visual =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    BuildingFolder + "MallVisual_SimpleTown.prefab");
            BuildingDefinitionSO definition =
                AssetDatabase.LoadAssetAtPath<BuildingDefinitionSO>(
                    DefinitionFolder + "Building_Mall.asset");
            Assert.NotNull(visual);
            Assert.NotNull(definition);
            Assert.AreEqual(5, definition.VisitorParkingSlotCount);

            BuildingParkingLayout layout =
                visual.GetComponent<BuildingParkingLayout>();
            Assert.NotNull(layout);
            Assert.AreEqual(5, layout.ParkingSlotCount);
            Assert.That(
                layout.ParkedVehicleScale,
                Is.EqualTo(0.93f).Within(0.0001f));
            Assert.IsTrue(layout.TryGetParkingPose(
                0,
                out BuildingParkingPose firstPose));
            Assert.That(
                firstPose.PresentationScale,
                Is.EqualTo(0.93f).Within(0.0001f));
            float maximumParkedVehicleWidth =
                GetMaximumNormalVehicleVisualWidth() *
                layout.ParkedVehicleScale;
            for (int index = 0; index < 5; index++)
            {
                Transform slot = visual.transform.Find(
                    $"ParkingSlot_{index}");
                Assert.NotNull(slot);
                Assert.Greater(
                    Vector3.Dot(slot.forward, Vector3.right),
                    0.999f);
                if (index == 0)
                {
                    continue;
                }

                Transform previous = visual.transform.Find(
                    $"ParkingSlot_{index - 1}");
                Assert.That(
                    Vector3.Distance(
                        previous.localPosition,
                        slot.localPosition),
                    Is.GreaterThanOrEqualTo(
                        maximumParkedVehicleWidth));
            }
        }

        [Test]
        public void MallParkingPresentationScale_TransitionsAndRestores()
        {
            const float frameDelta = 1f / 60f;
            float scale = MainCityView.MoveParkingPresentationScaleMultiplier(
                1f,
                0.93f,
                frameDelta);
            Assert.That(scale, Is.GreaterThan(0.93f).And.LessThan(1f));

            for (int frame = 1; frame < 15; frame++)
            {
                scale = MainCityView.MoveParkingPresentationScaleMultiplier(
                    scale,
                    0.93f,
                    frameDelta);
            }

            Assert.That(scale, Is.EqualTo(0.93f).Within(0.0001f));

            scale = MainCityView.MoveParkingPresentationScaleMultiplier(
                scale,
                1f,
                frameDelta);
            Assert.That(scale, Is.GreaterThan(0.93f).And.LessThan(1f));

            for (int frame = 1; frame < 15; frame++)
            {
                scale = MainCityView.MoveParkingPresentationScaleMultiplier(
                    scale,
                    1f,
                    frameDelta);
            }

            Assert.That(scale, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void PetrolStation_UsesPlainTwoByTwoSurface()
        {
            GameObject visual =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    BuildingFolder +
                    "PetrolStationVisual_SimpleTown.prefab");
            Assert.NotNull(visual);
            BuildingParkingLayout parkingLayout =
                visual.GetComponent<BuildingParkingLayout>();
            Assert.NotNull(parkingLayout);
            Assert.That(
                parkingLayout.ParkedVehicleScale,
                Is.EqualTo(1f).Within(0.0001f));

            Transform surfaceModel =
                visual.transform.Find("ParkingSurface/Model");
            Transform surfaceRoot =
                visual.transform.Find("ParkingSurface");
            Assert.NotNull(surfaceRoot);
            Assert.NotNull(surfaceModel);
            Assert.IsTrue(surfaceRoot.gameObject.activeSelf);
            Assert.That(
                Quaternion.Dot(
                    surfaceRoot.localRotation,
                    Quaternion.identity),
                Is.GreaterThan(0.9999f));
            Assert.IsTrue(surfaceModel.gameObject.activeSelf);
            MeshRenderer surfaceRenderer =
                surfaceModel.GetComponent<MeshRenderer>();
            Assert.NotNull(surfaceRenderer);
            Assert.IsTrue(surfaceRenderer.enabled);
            Assert.IsFalse(surfaceRenderer.forceRenderingOff);
            Assert.That(
                AssetDatabase.GetAssetPath(
                    surfaceRenderer.sharedMaterial),
                Is.EqualTo(PetrolStationGroundMaterialPath));
            Assert.That(
                surfaceRenderer.sharedMaterial.mainTexture,
                Is.Null);
            Assert.That(
                surfaceRenderer.sharedMaterial.GetTexture("_BaseMap"),
                Is.Null);
            MeshFilter surfaceFilter =
                surfaceModel.GetComponent<MeshFilter>();
            Assert.NotNull(surfaceFilter);
            Assert.NotNull(surfaceFilter.sharedMesh);
            AssertLitMaterials(surfaceModel.gameObject);
            Assert.IsEmpty(
                surfaceModel.GetComponentsInChildren<Collider>(true));
            Assert.IsTrue(TryGetLocalRendererBounds(
                surfaceModel,
                visual.transform,
                out Bounds bounds));
            Assert.That(bounds.center.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(bounds.center.y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(bounds.size.x, Is.EqualTo(2f).Within(0.001f));
            Assert.That(bounds.size.y, Is.EqualTo(2f).Within(0.001f));
            Assert.That(
                bounds.min.z,
                Is.EqualTo(-DrivewayBoundaryLayout.SurfaceOffsetTiles)
                    .Within(0.001f));
            Assert.That(bounds.size.z, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void PetrolStation_ParkingSlotsStayInFrontOfFuelIsland()
        {
            GameObject visual =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    BuildingFolder +
                    "PetrolStationVisual_SimpleTown.prefab");
            Assert.NotNull(visual);

            Transform surface = visual.transform.Find("AuthoredSurface");
            Transform fuelIsland =
                visual.transform.Find("FuelIslandClearance");
            Transform surfaceModel =
                visual.transform.Find("ParkingSurface/Model");
            Assert.NotNull(surface);
            Assert.NotNull(fuelIsland);
            Assert.NotNull(surfaceModel);
            Assert.IsTrue(TryGetLocalRendererBounds(
                surfaceModel,
                visual.transform,
                out Bounds surfaceBounds));

            var lot = new Rect(
                surfaceBounds.min.x,
                surfaceBounds.min.y,
                surfaceBounds.size.x,
                surfaceBounds.size.y);
            var island = new Rect(
                fuelIsland.localPosition.x -
                fuelIsland.localScale.x * 0.5f,
                fuelIsland.localPosition.y -
                fuelIsland.localScale.y * 0.5f,
                fuelIsland.localScale.x,
                fuelIsland.localScale.y);
            VehicleFootprint footprint =
                VehicleFootprint.StandardDefault;
            float maximumNormalVehicleVisualWidth =
                GetMaximumNormalVehicleVisualWidth();
            Rect[] parkedVehicles = new Rect[2];

            for (int index = 0; index < parkedVehicles.Length; index++)
            {
                Transform slot = visual.transform.Find(
                    $"ParkingSlot_{index}");
                Assert.NotNull(slot);
                Assert.Greater(
                    Vector3.Dot(
                        slot.forward.normalized,
                        -visual.transform.right.normalized),
                    0.999f);

                parkedVehicles[index] = new Rect(
                    slot.localPosition.x -
                    footprint.LengthTiles * 0.5f,
                    slot.localPosition.y -
                    maximumNormalVehicleVisualWidth * 0.5f,
                    footprint.LengthTiles,
                    maximumNormalVehicleVisualWidth);
                Rect parkedVehicle = parkedVehicles[index];
                Assert.That(
                    parkedVehicle.xMin,
                    Is.GreaterThanOrEqualTo(lot.xMin));
                Assert.That(
                    parkedVehicle.xMax,
                    Is.LessThanOrEqualTo(lot.xMax));
                Assert.That(
                    parkedVehicle.yMin,
                    Is.GreaterThanOrEqualTo(lot.yMin));
                Assert.That(
                    parkedVehicle.yMax,
                    Is.LessThanOrEqualTo(lot.yMax));
                Assert.IsFalse(parkedVehicle.Overlaps(island));
                Assert.That(
                    island.yMin - parkedVehicle.yMax,
                    Is.GreaterThanOrEqualTo(0.08f));
            }

            Assert.That(
                parkedVehicles[1].xMin - parkedVehicles[0].xMax,
                Is.GreaterThanOrEqualTo(
                    footprint.MinimumGapTiles));
        }

        [TestCase(
            BuildingFolder + "PharmacyVisual_SimpleTown.prefab",
            DefinitionFolder + "Building_StoreCorner_Drug.asset",
            1)]
        [TestCase(
            BuildingFolder + "CoffeeShopVisual_SimpleTown.prefab",
            DefinitionFolder + "Building_CoffeeShop.asset",
            1)]
        [TestCase(
            BuildingFolder + "CinemaVisual_SimpleTown.prefab",
            DefinitionFolder + "Building_Cinema.asset",
            2)]
        [TestCase(
            BuildingFolder + "AutoRepairVisual_SimpleTown.prefab",
            DefinitionFolder + "Building_AutoRepair.asset",
            2)]
        public void CommercialBuilding_UsesCommonLitDriveway(
            string visualPath,
            string definitionPath,
            int footprintWidth)
        {
            GameObject visual =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    visualPath);
            BuildingDefinitionSO definition =
                AssetDatabase.LoadAssetAtPath<BuildingDefinitionSO>(
                    definitionPath);
            GameObject foundationPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    FoundationPath);
            GameObject drivewayPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    DrivewayPath);

            Assert.NotNull(visual);
            Assert.NotNull(definition);
            Assert.NotNull(foundationPrefab);
            Assert.NotNull(drivewayPrefab);
            Assert.AreSame(visual, definition.VisualPrefab);
            Assert.AreEqual(
                new Vector2Int(footprintWidth, 2),
                definition.Footprint);
            Assert.IsTrue(definition.CanReceiveVisitors);
            Assert.AreEqual(0, definition.VisitorParkingSlotStart);
            Assert.Greater(definition.VisitDwellHours, 0f);

            GameObject instance = Object.Instantiate(visual);
            try
            {
                Transform buildingMarker =
                    instance.transform.Find("BuildingSurface");
                Transform parkingMarker =
                    instance.transform.Find("ParkingLot");
                Transform foundation =
                    instance.transform.Find("BuildingFoundation/Model");
                Transform driveway =
                    instance.transform.Find("Driveway_0/PathDriveway");
                Assert.NotNull(instance.transform.Find("Model"));
                Assert.NotNull(buildingMarker);
                Assert.NotNull(parkingMarker);
                Assert.NotNull(foundation);
                Assert.NotNull(driveway);
                Assert.IsNull(instance.transform.Find("AuthoredSurface"));
                Assert.AreEqual(
                    new Vector3(footprintWidth, 1f, 1f),
                    buildingMarker.localScale);
                Assert.AreEqual(
                    new Vector3(footprintWidth, 1f, 1f),
                    parkingMarker.localScale);

                AssertUsesSameMeshAndMaterial(
                    foundation.gameObject,
                    foundationPrefab);
                AssertUsesSameMeshAndMaterial(
                    driveway.gameObject,
                    drivewayPrefab);
                AssertLitMaterials(instance);

                Assert.IsTrue(TryGetLocalRendererBounds(
                    foundation,
                    instance.transform,
                    out Bounds foundationBounds));
                Assert.That(
                    foundationBounds.size.x,
                    Is.EqualTo(footprintWidth).Within(0.001f));
                Assert.That(
                    foundationBounds.size.y,
                    Is.EqualTo(1f).Within(0.001f));
                Assert.IsTrue(TryGetLocalRendererBounds(
                    driveway,
                    instance.transform,
                    out Bounds drivewayBounds));
                Assert.That(
                    drivewayBounds.size.x,
                    Is.EqualTo(footprintWidth).Within(0.001f));
                Assert.That(
                    drivewayBounds.size.y,
                    Is.EqualTo(1f).Within(0.001f));

                BuildingParkingLayout layout =
                    instance.GetComponent<BuildingParkingLayout>();
                Assert.NotNull(layout);
                Assert.AreEqual(2, layout.ParkingSlotCount);
                Assert.AreEqual(
                    definition.VisitorParkingSlotCount,
                    layout.ParkingSlotCount);
                AssertParkingSlot(
                    instance.transform,
                    0,
                    footprintWidth * 0.25f);
                AssertParkingSlot(
                    instance.transform,
                    1,
                    footprintWidth * -0.25f);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void AssertAuthoredCommercialVisual(
            string visualPath,
            string definitionPath,
            Vector2Int expectedFootprint,
            Vector3[] expectedPositions,
            Vector3 expectedForward)
        {
            GameObject visual =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    visualPath);
            BuildingDefinitionSO definition =
                AssetDatabase.LoadAssetAtPath<BuildingDefinitionSO>(
                    definitionPath);
            Assert.NotNull(visual);
            Assert.NotNull(definition);
            Assert.AreSame(visual, definition.VisualPrefab);
            Assert.AreEqual(expectedFootprint, definition.Footprint);
            Assert.IsTrue(definition.CanReceiveVisitors);
            Assert.AreEqual(0, definition.VisitorParkingSlotStart);
            Assert.Greater(definition.VisitDwellHours, 0f);
            Assert.NotNull(visual.transform.Find("Model"));
            Assert.NotNull(visual.transform.Find("AuthoredSurface"));
            Assert.IsNull(visual.transform.Find("BuildingFoundation"));
            Assert.IsNull(visual.transform.Find("Driveway_0"));
            AssertLitMaterials(visual);

            BuildingParkingLayout layout =
                visual.GetComponent<BuildingParkingLayout>();
            Assert.NotNull(layout);
            Assert.AreEqual(
                expectedPositions.Length,
                layout.ParkingSlotCount);
            Assert.AreEqual(
                definition.VisitorParkingSlotCount,
                layout.ParkingSlotCount);
            for (int index = 0;
                 index < expectedPositions.Length;
                 index++)
            {
                Transform slot = visual.transform.Find(
                    $"ParkingSlot_{index}");
                Assert.NotNull(slot);
                Assert.That(
                    Vector3.Distance(
                        slot.localPosition,
                        expectedPositions[index]),
                    Is.LessThan(0.0001f));
                Assert.Greater(
                    Vector3.Dot(
                        slot.forward.normalized,
                        expectedForward.normalized),
                    0.999f);
            }
        }

        private static void AssertParkingSlot(
            Transform root,
            int slotIndex,
            float expectedX)
        {
            Transform slot = root.Find($"ParkingSlot_{slotIndex}");
            Assert.NotNull(slot);
            Assert.That(
                slot.localPosition.x,
                Is.EqualTo(expectedX).Within(0.0001f));
            Assert.That(
                slot.localPosition.y,
                Is.EqualTo(-0.5f).Within(0.0001f));
            Assert.That(
                slot.localPosition.z,
                Is.EqualTo(0f).Within(0.0001f));
            Assert.Greater(
                Vector3.Dot(slot.forward, Vector3.down),
                0.999f);
        }

        private static void AssertUsesSameMeshAndMaterial(
            GameObject instance,
            GameObject sourcePrefab)
        {
            MeshFilter instanceFilter =
                instance.GetComponentInChildren<MeshFilter>(true);
            MeshFilter sourceFilter =
                sourcePrefab.GetComponentInChildren<MeshFilter>(true);
            Renderer instanceRenderer =
                instance.GetComponentInChildren<Renderer>(true);
            Renderer sourceRenderer =
                sourcePrefab.GetComponentInChildren<Renderer>(true);

            Assert.NotNull(instanceFilter);
            Assert.NotNull(sourceFilter);
            Assert.NotNull(instanceRenderer);
            Assert.NotNull(sourceRenderer);
            Assert.AreSame(
                sourceFilter.sharedMesh,
                instanceFilter.sharedMesh);
            Assert.AreSame(
                sourceRenderer.sharedMaterial,
                instanceRenderer.sharedMaterial);
        }

        private static void AssertLitMaterials(GameObject root)
        {
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            Assert.IsNotEmpty(renderers);

            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                Material[] materials =
                    renderers[rendererIndex].sharedMaterials;
                Assert.IsNotEmpty(materials);
                for (int materialIndex = 0;
                     materialIndex < materials.Length;
                     materialIndex++)
                {
                    Assert.NotNull(materials[materialIndex]);
                    Assert.AreEqual(
                        "Universal Render Pipeline/Lit",
                        materials[materialIndex].shader.name);
                }
            }
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

            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                Bounds localBounds = renderer.localBounds;
                Vector3 min = localBounds.min;
                Vector3 max = localBounds.max;

                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 localPoint = new(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    Vector3 relativePoint =
                        relativeTo.InverseTransformPoint(
                            renderer.transform.TransformPoint(localPoint));

                    if (!hasBounds)
                    {
                        bounds = new Bounds(
                            relativePoint,
                            Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(relativePoint);
                    }
                }
            }

            return hasBounds;
        }

        private static float GetMaximumNormalVehicleVisualWidth()
        {
            VehicleVisualCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<VehicleVisualCatalogSO>(
                    "Assets/05_ScriptableObjects/Resources/CityFlow/" +
                    "VehicleVisualCatalog.asset");
            Assert.NotNull(catalog);
            Assert.IsNotEmpty(catalog.NormalVehiclePrefabs);

            FieldInfo scaleField = typeof(MainCityView).GetField(
                "VehicleBodyLengthTiles",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(scaleField);
            float runtimeScale =
                (float)scaleField.GetRawConstantValue();
            float maximumWidth = 0f;

            GameObject[] prefabs = catalog.NormalVehiclePrefabs;
            for (int index = 0; index < prefabs.Length; index++)
            {
                Assert.NotNull(prefabs[index]);
                GameObject instance = Object.Instantiate(prefabs[index]);
                try
                {
                    Assert.IsTrue(TryGetLocalRendererBounds(
                        instance.transform,
                        instance.transform,
                        out Bounds bounds));
                    maximumWidth = Mathf.Max(
                        maximumWidth,
                        bounds.size.y * runtimeScale);
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }

            Assert.Greater(maximumWidth, 0f);
            return maximumWidth;
        }
    }
}
