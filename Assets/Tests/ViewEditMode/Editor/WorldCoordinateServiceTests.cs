using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.UI;
using CityFlow.UI.Controllers;
using CityFlow.WorldCoordinates;
using CityFlow.WorldGrid;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CityFlow.Tests
{
    public sealed class WorldCoordinateServiceTests
    {
        private const string ProfilePath =
            "Assets/05_ScriptableObjects/WorldCoordinates/" +
            "WorldCoordinateProfile.asset";
        private const string PrefabPath =
            "Assets/02_Prefabs/WorldCoordinates/" +
            "WorldCoordinateSystem.prefab";
        private const string WorldGridPrefabPath =
            "Assets/02_Prefabs/WorldGrid/WorldGridSystem.prefab";

        [Test]
        public void SystemPrefab_RegistersDefaultXzCoordinateSpace()
        {
            GameObject instance = null;

            try
            {
                WorldCoordinateProfileSO profile =
                    AssetDatabase.LoadAssetAtPath<WorldCoordinateProfileSO>(
                        ProfilePath);
                GameObject prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                Assert.NotNull(profile);
                Assert.NotNull(prefab);
                Assert.AreEqual(WorldCoordinatePlane.XZ, profile.Plane);
                Assert.IsNull(
                    new SerializedObject(profile).FindProperty("tileSize"));

                instance = Object.Instantiate(prefab);
                WorldCoordinateService coordinateService =
                    instance.GetComponent<WorldCoordinateService>();
                WorldRootOrientationService orientationService =
                    instance.GetComponent<WorldRootOrientationService>();
                Assert.NotNull(coordinateService);
                Assert.NotNull(orientationService);
                Assert.AreSame(profile, coordinateService.Profile);
                Assert.AreSame(
                    coordinateService,
                    orientationService.CoordinateService);

                var services = new CityFlowServices(null, null, null);
                coordinateService.Initialize(services);

                Assert.AreSame(
                    coordinateService,
                    services.WorldCoordinates);
                Assert.AreEqual(
                    GridUtil.TileSize,
                    coordinateService.TileSize);
                AssertVector(
                    new Vector3(2.5f, 0f, 3.5f),
                    coordinateService.GridToWorld(new Vector2Int(2, 3)));
                Assert.AreEqual(
                    new Vector2Int(2, 3),
                    coordinateService.WorldToGrid(
                        new Vector3(2.5f, -4f, 3.5f)));
            }
            finally
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void WorldGridOrigin_MapsCentralLogicalTileToBoardOrigin()
        {
            GameObject coordinateInstance = null;
            GameObject worldGridInstance = null;

            try
            {
                GameObject coordinatePrefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                GameObject worldGridPrefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(
                        WorldGridPrefabPath);
                Assert.NotNull(coordinatePrefab);
                Assert.NotNull(worldGridPrefab);

                coordinateInstance = Object.Instantiate(coordinatePrefab);
                worldGridInstance = Object.Instantiate(worldGridPrefab);
                WorldCoordinateService coordinateService =
                    coordinateInstance.GetComponent<WorldCoordinateService>();
                WorldGridService worldGrid =
                    worldGridInstance.GetComponent<WorldGridService>();
                var services = new CityFlowServices(null, null, null);

                worldGrid.Initialize(services);
                coordinateService.Initialize(services);

                Assert.AreEqual(
                    new Vector2Int(90, 90),
                    coordinateService.GridOrigin);
                AssertVector(
                    new Vector3(0.5f, 0f, 0.5f),
                    coordinateService.GridToWorld(
                        new Vector2Int(90, 90)));
                Assert.AreEqual(
                    new Vector2Int(90, 90),
                    coordinateService.WorldToGrid(
                        new Vector3(0.5f, 0f, 0.5f)));
            }
            finally
            {
                if (worldGridInstance != null)
                {
                    Object.DestroyImmediate(worldGridInstance);
                }

                if (coordinateInstance != null)
                {
                    Object.DestroyImmediate(coordinateInstance);
                }
            }
        }

        [Test]
        public void OrientationService_AppliesXzPoseToRegisteredWorldRoot()
        {
            GameObject systemInstance = null;
            GameObject cityObject = null;

            try
            {
                GameObject prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                Assert.NotNull(prefab);

                systemInstance = Object.Instantiate(prefab);
                systemInstance.transform.position = new Vector3(4f, 2f, 6f);
                WorldCoordinateService coordinateService =
                    systemInstance.GetComponent<WorldCoordinateService>();
                WorldRootOrientationService orientationService =
                    systemInstance.GetComponent<WorldRootOrientationService>();
                cityObject = new GameObject("CoordinateRootTestCity");
                var cityView = cityObject.AddComponent<CityFlow.View.MainCityView>();
                var services = new CityFlowServices(null, null, null);

                orientationService.Initialize(services);
                Assert.IsTrue(
                    services.RegisterWorldCoordinateRoot(cityView));
                coordinateService.Initialize(services);

                Assert.IsTrue(orientationService.IsApplied);
                AssertVector(
                    coordinateService.Origin,
                    cityView.transform.position);
                Assert.That(
                    Quaternion.Angle(
                        coordinateService.CoordinateRotation,
                        cityView.transform.rotation),
                    Is.LessThan(0.001f));
                AssertVector(Vector3.up, -cityView.transform.forward);
            }
            finally
            {
                if (cityObject != null)
                {
                    Object.DestroyImmediate(cityObject);
                }

                if (systemInstance != null)
                {
                    Object.DestroyImmediate(systemInstance);
                }
            }
        }

        [Test]
        public void XzPlane_MapsSurfaceOffsetAndRayToExpectedTile()
        {
            WorldCoordinateProfileSO profile = null;
            GameObject instance = null;

            try
            {
                profile = ScriptableObject.CreateInstance<
                    WorldCoordinateProfileSO>();
                var profileObject = new SerializedObject(profile);
                profileObject.FindProperty("plane").enumValueIndex =
                    (int)WorldCoordinatePlane.XZ;
                profileObject.ApplyModifiedPropertiesWithoutUndo();

                instance = new GameObject("WorldCoordinateServiceTest");
                WorldCoordinateService coordinateService =
                    instance.AddComponent<WorldCoordinateService>();
                var serviceObject = new SerializedObject(coordinateService);
                serviceObject.FindProperty("profile").objectReferenceValue =
                    profile;
                serviceObject.ApplyModifiedPropertiesWithoutUndo();

                AssertVector(Vector3.up, coordinateService.GroundNormal);
                AssertVector(
                    new Vector3(2.5f, 1f, 3.5f),
                    coordinateService.GridToWorld(
                        new Vector2Int(2, 3),
                        1f));

                var ray = new Ray(
                    new Vector3(2.5f, 10f, 3.5f),
                    Vector3.down);
                Assert.IsTrue(
                    coordinateService.TryRayToGrid(
                        ray,
                        out Vector2Int tile,
                        out Vector3 hitPoint));
                Assert.AreEqual(new Vector2Int(2, 3), tile);
                AssertVector(new Vector3(2.5f, 0f, 3.5f), hitPoint);
            }
            finally
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }

                if (profile != null)
                {
                    Object.DestroyImmediate(profile);
                }
            }
        }

        [Test]
        public void PlacementGhost_UsesCenteredXzFootprint()
        {
            GameObject systemInstance = null;
            GameObject controllerObject = null;

            try
            {
                GameObject prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                systemInstance = Object.Instantiate(prefab);
                WorldCoordinateService coordinateService =
                    systemInstance.GetComponent<WorldCoordinateService>();
                var services = new CityFlowServices(null, null, null);
                coordinateService.Initialize(services);

                controllerObject = new GameObject("PlacementCoordinateTest");
                PlacementController controller =
                    controllerObject.AddComponent<PlacementController>();
                controller.Initialize(services);

                Vector3 position = controller.GetGhostPosition(
                    new Vector2Int(2, 3),
                    new Vector2Int(2, 3),
                    PlacementController.RoadSurfaceMarkerZ);

                AssertVector(new Vector3(3f, 0.05f, 4.5f), position);
            }
            finally
            {
                if (controllerObject != null)
                {
                    Object.DestroyImmediate(controllerObject);
                }

                if (systemInstance != null)
                {
                    Object.DestroyImmediate(systemInstance);
                }
            }
        }

        [Test]
        public void BenefitHighlight_UsesCoordinateSurface()
        {
            GameObject systemInstance = null;
            GameObject highlightObject = null;

            try
            {
                GameObject prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                systemInstance = Object.Instantiate(prefab);
                WorldCoordinateService coordinateService =
                    systemInstance.GetComponent<WorldCoordinateService>();
                var services = new CityFlowServices(null, null, null);
                coordinateService.Initialize(services);

                highlightObject = new GameObject("BenefitCoordinateTest");
                BenefitHighlightRenderer renderer =
                    highlightObject.AddComponent<BenefitHighlightRenderer>();
                renderer.ShowHighlights(
                    new[] { new Vector2Int(2, 3) },
                    System.Array.Empty<Vector2Int>(),
                    coordinateSpace: coordinateService);

                Assert.AreEqual(1, highlightObject.transform.childCount);
                Transform tile = highlightObject.transform.GetChild(0);
                Assert.IsTrue(tile.gameObject.activeSelf);
                AssertVector(new Vector3(2.5f, 0.09f, 3.5f), tile.position);
                Assert.That(
                    Quaternion.Angle(
                        coordinateService.CoordinateRotation,
                        tile.rotation),
                    Is.LessThan(0.001f));
            }
            finally
            {
                if (highlightObject != null)
                {
                    Object.DestroyImmediate(highlightObject);
                }

                if (systemInstance != null)
                {
                    Object.DestroyImmediate(systemInstance);
                }
            }
        }

        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f));
        }
    }
}
