using System;
using System.Collections;
using System.Reflection;
using CityFlow.Contracts;
using CityFlow.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CityFlow.Tests
{
    public sealed class ParkingAndDrivewayGuardTests
    {
        private static readonly (string Path, float Width)[]
            CommonDrivewayVisuals =
            {
                (
                    "Assets/02_Prefabs/Buildings/" +
                    "PoliceStationVisual_StudioHorizon.prefab",
                    2f),
                (
                    "Assets/02_Prefabs/Buildings/" +
                    "PharmacyVisual_SimpleTown.prefab",
                    1f),
                (
                    "Assets/02_Prefabs/Buildings/" +
                    "CoffeeShopVisual_SimpleTown.prefab",
                    1f),
                (
                    "Assets/02_Prefabs/Buildings/" +
                    "CinemaVisual_SimpleTown.prefab",
                    2f),
                (
                    "Assets/02_Prefabs/Buildings/" +
                    "AutoRepairVisual_SimpleTown.prefab",
                    2f)
            };

        [TestCase(TileType.House, 1f)]
        [TestCase(TileType.Office, 2f)]
        [TestCase(TileType.School, 2f)]
        [TestCase(TileType.Hospital, 2f)]
        public void RuntimeDriveways_KeepPerimeterInsideTheirLot(
            TileType type,
            float expectedWidth)
        {
            GameObject cityObject = new("Driveway Perimeter Test View");
            GameObject preview = null;
            try
            {
                MainCityView cityView =
                    cityObject.AddComponent<MainCityView>();
                Assert.IsTrue(
                    cityView.TryCreatePlacementPreview(
                        type,
                        out preview));

                AssertPerimeter(
                    preview.transform,
                    expectedWidth,
                    1f,
                    -0.5f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(preview);
                UnityEngine.Object.DestroyImmediate(cityObject);
            }
        }

        [Test]
        public void ProjectOwnedCommonDriveways_HaveLitInsetPerimeter()
        {
            for (int index = 0;
                 index < CommonDrivewayVisuals.Length;
                 index++)
            {
                (string path, float width) =
                    CommonDrivewayVisuals[index];
                GameObject prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.NotNull(prefab, path);
                AssertPerimeter(
                    prefab.transform,
                    width,
                    1f,
                    -0.5f);

                string[] names =
                {
                    "DrivewayPerimeter_Left",
                    "DrivewayPerimeter_Right",
                    "DrivewayPerimeter_Rear",
                    "DrivewayPerimeter_Front"
                };
                for (int nameIndex = 0;
                     nameIndex < names.Length;
                     nameIndex++)
                {
                    Renderer renderer = prefab.transform
                        .Find(names[nameIndex])
                        .GetComponent<Renderer>();
                    Assert.NotNull(renderer.sharedMaterial, path);
                    Assert.AreEqual(
                        "Universal Render Pipeline/Lit",
                        renderer.sharedMaterial.shader.name,
                        path);
                }
            }
        }

        [Test]
        public void CommuteVehicles_UseSharedParkingReservationRegistry()
        {
            GameObject cityObject = new("Shared Parking Registry Test View");
            GameObject buildingObject = new("Shared Parking Registry Building");
            GameObject firstVehicle = new("First Commute Vehicle");
            GameObject secondVehicle = new("Second Commute Vehicle");

            try
            {
                cityObject.transform.position =
                    new Vector3(20000f, 20000f, 0f);
                MainCityView cityView =
                    cityObject.AddComponent<MainCityView>();
                buildingObject.transform.SetParent(
                    cityObject.transform,
                    false);
                GameObject slot = new("ParkingSlot_0");
                slot.transform.SetParent(
                    buildingObject.transform,
                    false);
                RegisterTileVisual(
                    cityView,
                    new Vector2Int(4, 7),
                    buildingObject);

                firstVehicle.transform.SetParent(
                    cityObject.transform,
                    false);
                secondVehicle.transform.SetParent(
                    cityObject.transform,
                    false);
                Type routeVehicleType = typeof(MainCityView)
                    .GetNestedType(
                        "RouteVehicle",
                        BindingFlags.NonPublic);
                Assert.NotNull(routeVehicleType);
                object firstRouteVehicle = CreateRouteVehicle(
                    routeVehicleType,
                    firstVehicle);
                object secondRouteVehicle = CreateRouteVehicle(
                    routeVehicleType,
                    secondVehicle);
                MethodInfo reserve = typeof(MainCityView).GetMethod(
                    "TryReserveCommuteParkingPose",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo release = typeof(MainCityView).GetMethod(
                    "ReleaseCommuteParkingReservation",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(reserve);
                Assert.NotNull(release);

                object[] firstArguments =
                {
                    firstRouteVehicle,
                    new Vector2Int(4, 7),
                    0
                };
                object[] secondArguments =
                {
                    secondRouteVehicle,
                    new Vector2Int(4, 7),
                    0
                };
                Assert.IsTrue((bool)reserve.Invoke(
                    cityView,
                    firstArguments));
                Assert.IsFalse((bool)reserve.Invoke(
                    cityView,
                    secondArguments),
                    "통근·방문 차량도 버스·구급차와 같은 슬롯 예약부를 사용해야 한다.");

                release.Invoke(
                    cityView,
                    new[] { firstRouteVehicle });
                Assert.IsTrue((bool)reserve.Invoke(
                    cityView,
                    secondArguments),
                    "출차한 차량의 슬롯은 다음 차량에 정확히 한 번만 반환되어야 한다.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(secondVehicle);
                UnityEngine.Object.DestroyImmediate(firstVehicle);
                UnityEngine.Object.DestroyImmediate(buildingObject);
                UnityEngine.Object.DestroyImmediate(cityObject);
            }
        }

        [Test]
        public void HiddenPooledVehicle_DoesNotOccupyParkingPose()
        {
            GameObject cityObject = new("Hidden Parking Occupant Test View");
            GameObject buildingObject = new("Hidden Parking Occupant Building");
            GameObject hiddenVehicle =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject requestingVehicle = new("Visible Requesting Vehicle");

            try
            {
                cityObject.transform.position =
                    new Vector3(30000f, 30000f, 0f);
                MainCityView cityView =
                    cityObject.AddComponent<MainCityView>();
                buildingObject.transform.SetParent(
                    cityObject.transform,
                    false);
                GameObject slot = new("ParkingSlot_0");
                slot.transform.SetParent(
                    buildingObject.transform,
                    false);
                RegisterTileVisual(
                    cityView,
                    new Vector2Int(2, 3),
                    buildingObject);

                hiddenVehicle.transform.SetParent(
                    cityObject.transform,
                    false);
                hiddenVehicle.transform.localPosition = Vector3.zero;
                hiddenVehicle.AddComponent<VehicleNightLighting>();
                hiddenVehicle.GetComponent<Renderer>().enabled = false;
                requestingVehicle.transform.SetParent(
                    cityObject.transform,
                    false);

                Assert.IsTrue(
                    cityView.TryReserveBuildingParkingPose(
                        new Vector2Int(2, 3),
                        0,
                        requestingVehicle.transform,
                        out _,
                        out _),
                    "렌더러가 꺼진 풀 차량은 실제 주차 차량으로 계산하면 안 된다.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(requestingVehicle);
                UnityEngine.Object.DestroyImmediate(hiddenVehicle);
                UnityEngine.Object.DestroyImmediate(buildingObject);
                UnityEngine.Object.DestroyImmediate(cityObject);
            }
        }

        private static object CreateRouteVehicle(
            Type routeVehicleType,
            GameObject vehicleObject)
        {
            object routeVehicle = Activator.CreateInstance(
                routeVehicleType,
                nonPublic: true);
            routeVehicleType.GetField(
                    "Object",
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(routeVehicle, vehicleObject);
            return routeVehicle;
        }

        private static void AssertPerimeter(
            Transform root,
            float lotWidth,
            float lotLength,
            float centerY)
        {
            string[] names =
            {
                "DrivewayPerimeter_Left",
                "DrivewayPerimeter_Right",
                "DrivewayPerimeter_Rear",
                "DrivewayPerimeter_Front"
            };
            float minX = -lotWidth * 0.5f;
            float maxX = lotWidth * 0.5f;
            float minY = centerY - lotLength * 0.5f;
            float maxY = centerY + lotLength * 0.5f;

            for (int index = 0; index < names.Length; index++)
            {
                Transform boundary = root.Find(names[index]);
                Assert.NotNull(boundary, $"{root.name}/{names[index]}");
                Assert.NotNull(boundary.GetComponent<Renderer>());
                float boundaryMinX = boundary.localPosition.x -
                    boundary.localScale.x * 0.5f;
                float boundaryMaxX = boundary.localPosition.x +
                    boundary.localScale.x * 0.5f;
                float boundaryMinY = boundary.localPosition.y -
                    boundary.localScale.y * 0.5f;
                float boundaryMaxY = boundary.localPosition.y +
                    boundary.localScale.y * 0.5f;
                Assert.GreaterOrEqual(
                    boundaryMinX,
                    minX - 0.0001f);
                Assert.LessOrEqual(
                    boundaryMaxX,
                    maxX + 0.0001f);
                Assert.GreaterOrEqual(
                    boundaryMinY,
                    minY - 0.0001f);
                Assert.LessOrEqual(
                    boundaryMaxY,
                    maxY + 0.0001f);
            }
        }

        private static void RegisterTileVisual(
            MainCityView cityView,
            Vector2Int tile,
            GameObject visualObject)
        {
            Type tileVisualType = typeof(MainCityView).GetNestedType(
                "TileVisual",
                BindingFlags.NonPublic);
            Assert.NotNull(tileVisualType);
            object tileVisual = Activator.CreateInstance(
                tileVisualType,
                nonPublic: true);
            tileVisualType.GetField(
                    "Object",
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(tileVisual, visualObject);

            FieldInfo visualsField = typeof(MainCityView).GetField(
                "tileVisuals",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(visualsField);
            IDictionary visuals =
                (IDictionary)visualsField.GetValue(cityView);
            visuals.Add(tile, tileVisual);
        }
    }
}
