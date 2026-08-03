using System;
using CityFlow.Bootstrap;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using CityFlow.UI.Controllers;
using CityFlow.UI.Controllers.Placement;
using CityFlow.Contracts;
using CityFlow.UI;
using CityFlow.UI.Data;
using CityFlow.View;
using CityFlow.DebugTools;
using Object = UnityEngine.Object;

namespace Tests.EditMode
{
    public class PlacementControllerTests
    {
        private GameObject _cameraGo;

        private sealed class AutoDirectionPlacementService : IPlacementService
        {
            public bool CanPlace(
                Vector2Int tile,
                TileType type,
                PlacementDirection direction = PlacementDirection.North) => true;

            public bool Place(
                Vector2Int tile,
                TileType type,
                PlacementDirection direction = PlacementDirection.North) => true;

            public bool Remove(Vector2Int tile) => true;

            public bool TryResolveAutoDirection(
                Vector2Int tile,
                TileType type,
                out PlacementDirection direction,
                IReadOnlyList<PlacementDirection> priority = null)
            {
                direction = PlacementDirection.East;
                return true;
            }
        }

        private sealed class DefaultAutoDirectionPlacementService : IPlacementService
        {
            public bool CanPlace(
                Vector2Int tile,
                TileType type,
                PlacementDirection direction = PlacementDirection.North) => true;

            public bool Place(
                Vector2Int tile,
                TileType type,
                PlacementDirection direction = PlacementDirection.North) => true;

            public bool Remove(Vector2Int tile) => true;
        }

        private sealed class CountingPlacementService : IPlacementService
        {
            public int RemoveCalls { get; private set; }

            public bool CanPlace(
                Vector2Int tile,
                TileType type,
                PlacementDirection direction = PlacementDirection.North) => true;

            public bool Place(
                Vector2Int tile,
                TileType type,
                PlacementDirection direction = PlacementDirection.North) => true;

            public bool Remove(Vector2Int tile)
            {
                RemoveCalls++;
                return true;
            }
        }

        [SetUp]
        public void Setup()
        {
            _cameraGo = new GameObject("MainCamera");
            _cameraGo.tag = "MainCamera";
            var cam = _cameraGo.AddComponent<Camera>();
            cam.transform.position = new Vector3(0, 10, -10);
            cam.transform.LookAt(Vector3.zero);
        }

        [TearDown]
        public void Teardown()
        {
            if (_cameraGo != null)
            {
                Object.DestroyImmediate(_cameraGo);
            }
        }

        [Test]
        public void GetMouseGridCoordinate_Parameterless_UsesInstanceField()
        {
            var go = new GameObject("Controller");
            var controller = go.AddComponent<PlacementController>();
            controller.Initialize(null);

            var cam = _cameraGo.GetComponent<Camera>();

            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                using (StateEvent.From(mouse, out var eventPtr))
                {
                    mouse.position.WriteValueIntoEvent(new Vector2(100, 100), eventPtr);
                    InputSystem.QueueEvent(eventPtr);
                }
                InputSystem.Update();

                controller.SetUseXYPlane(true);
                var coordXY = controller.GetMouseGridCoordinate();

                controller.SetUseXYPlane(false);
                var coordXZ = controller.GetMouseGridCoordinate();

                Assert.AreNotEqual(coordXY, coordXZ, "XY plane and XZ plane should yield different coordinates for the same screen position.");
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
            }

            Object.DestroyImmediate(go);
        }

        [Test]
        public void Update_WhenPointerOverUI_ResetsDragState()
        {
            var go = new GameObject("Controller");
            var controller = go.AddComponent<PlacementController>();

            var ghostGo = new GameObject("Ghost");
            var renderer = ghostGo.AddComponent<SpriteRenderer>();
            controller.ConfigureGhost(renderer);

            var handlerField = typeof(PlacementController).GetField("_inputHandler", BindingFlags.NonPublic | BindingFlags.Instance);
            var inputHandler = (PlacementInputHandler)handlerField.GetValue(controller);

            bool dragRequested = false;
            inputHandler.OnDragPlaceRequested += (start, end) => dragRequested = true;

            var updateMethod = typeof(PlacementController).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
            var blockedProp = typeof(OfflineSettlementPopup).GetProperty("IsInteractionBlocked", BindingFlags.Public | BindingFlags.Static);

            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                controller.SetBuildType(TileType.Road);

                // 1. Mouse left click down
                using (StateEvent.From(mouse, out var eventPtr))
                {
                    mouse.leftButton.WriteValueIntoEvent(1f, eventPtr);
                    mouse.position.WriteValueIntoEvent(new Vector2(100, 100), eventPtr);
                    InputSystem.QueueEvent(eventPtr);
                }
                InputSystem.Update();
                updateMethod.Invoke(controller, null);

                // 2. UI block true, trigger Update (should reset drag state)
                blockedProp.SetValue(null, true);
                updateMethod.Invoke(controller, null);

                // 3. UI block false, drag to new pos
                blockedProp.SetValue(null, false);
                using (StateEvent.From(mouse, out var eventPtr2))
                {
                    mouse.position.WriteValueIntoEvent(new Vector2(200, 200), eventPtr2);
                    InputSystem.QueueEvent(eventPtr2);
                }
                InputSystem.Update();
                updateMethod.Invoke(controller, null);

                Assert.IsFalse(dragRequested, "Drag should not be requested after resetting drag state via UI block.");

                // 4. Release mouse, start new drag
                using (StateEvent.From(mouse, out var eventPtr3))
                {
                    mouse.leftButton.WriteValueIntoEvent(0f, eventPtr3);
                    InputSystem.QueueEvent(eventPtr3);
                }
                InputSystem.Update();
                updateMethod.Invoke(controller, null);

                using (StateEvent.From(mouse, out var eventPtr4))
                {
                    mouse.leftButton.WriteValueIntoEvent(1f, eventPtr4);
                    mouse.position.WriteValueIntoEvent(new Vector2(300, 300), eventPtr4);
                    InputSystem.QueueEvent(eventPtr4);
                }
                InputSystem.Update();
                updateMethod.Invoke(controller, null);

                using (StateEvent.From(mouse, out var eventPtr5))
                {
                    mouse.position.WriteValueIntoEvent(new Vector2(400, 400), eventPtr5);
                    InputSystem.QueueEvent(eventPtr5);
                }
                InputSystem.Update();
                updateMethod.Invoke(controller, null);

                Assert.IsTrue(dragRequested, "Drag should resume after releasing and starting a new drag.");
            }
            finally
            {
                blockedProp.SetValue(null, false); // cleanup
                InputSystem.RemoveDevice(mouse);
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(ghostGo);
            }
        }



        [Test]
        public void Update_WhenGhostRendererNull_DoesNotProcessPlacementInput()
        {
            var go = new GameObject("Controller");
            var controller = go.AddComponent<PlacementController>();
            controller.Initialize(null);

            var handlerField = typeof(PlacementController).GetField("_inputHandler", BindingFlags.NonPublic | BindingFlags.Instance);
            var inputHandler = (PlacementInputHandler)handlerField.GetValue(controller);

            bool placeRequested = false;
            inputHandler.OnPlaceRequested += (coord) => placeRequested = true;

            var updateMethod = typeof(PlacementController).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);

            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                controller.SetBuildType(TileType.Road);

                // Mouse left click down
                using (StateEvent.From(mouse, out var eventPtr))
                {
                    mouse.leftButton.WriteValueIntoEvent(1f, eventPtr);
                    mouse.position.WriteValueIntoEvent(new Vector2(100, 100), eventPtr);
                    InputSystem.QueueEvent(eventPtr);
                }
                InputSystem.Update();
                updateMethod.Invoke(controller, null);

                Assert.IsFalse(placeRequested, "Place should not be requested when ghost renderer is null.");
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                Object.DestroyImmediate(go);
            }
        }

        [TestCase(TileType.House)]
        [TestCase(TileType.Office)]
        [TestCase(TileType.School)]
        [TestCase(TileType.Hospital)]
        public void HandleRotate_IgnoresStandardBuildingDirection(
            TileType buildingType)
        {
            var services = new CityFlow.Bootstrap.CityFlowServices(
                new SimEventHub(),
                null,
                null);
            var controllerObject =
                new GameObject("BuildingRotationTest");
            var controller =
                controllerObject.AddComponent<PlacementController>();

            try
            {
                controller.Initialize(services);
                controller.SetBuildType(buildingType);

                MethodInfo rotateMethod =
                    typeof(PlacementController).GetMethod(
                        "HandleRotate",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance);
                Assert.NotNull(rotateMethod);
                rotateMethod.Invoke(controller, null);

                Assert.AreEqual(
                    PlacementDirection.North,
                    ReadPrivateField<PlacementDirection>(
                        controller,
                        "_currentDirection"));
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
            }
        }

        [Test]
        public void HandleRotate_IgnoresSpecialBuildingDirection()
        {
            var services = new CityFlow.Bootstrap.CityFlowServices(
                new SimEventHub(),
                null,
                null);
            services.RegisterSpecialBuildings(
                new TestSpecialBuildingService(isUnlocked: true));

            var controllerObject =
                new GameObject("SpecialBuildingRotationTest");
            var controller =
                controllerObject.AddComponent<PlacementController>();

            try
            {
                controller.Initialize(services);
                Assert.IsTrue(controller.SetSpecialBuilding("mall"));

                MethodInfo rotateMethod =
                    typeof(PlacementController).GetMethod(
                        "HandleRotate",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance);
                Assert.NotNull(rotateMethod);
                rotateMethod.Invoke(controller, null);

                Assert.AreEqual(
                    PlacementDirection.North,
                    ReadPrivateField<PlacementDirection>(
                        controller,
                        "_currentDirection"));
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
            }
        }

        [Test]
        public void HandleRotate_DoesNotRotateRoad()
        {
            var services =
                new CityFlow.Bootstrap.CityFlowServices(
                    new SimEventHub(),
                    null,
                    null);
            var controllerObject =
                new GameObject("RoadRotationGuardTest");
            var controller =
                controllerObject.AddComponent<
                    PlacementController>();

            try
            {
                controller.Initialize(services);
                controller.SetBuildType(TileType.Road);

                MethodInfo rotateMethod =
                    typeof(PlacementController).GetMethod(
                        "HandleRotate",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance);
                Assert.NotNull(rotateMethod);
                rotateMethod.Invoke(controller, null);

                Assert.AreEqual(
                    PlacementDirection.North,
                    ReadPrivateField<
                        PlacementDirection>(
                        controller,
                        "_currentDirection"),
                    "R 회전은 건물 타입에만 적용되어야 한다.");
            }
            finally
            {
                Object.DestroyImmediate(
                    controllerObject);
            }
        }

        [Test]
        public void ResolvePlacementDirection_UsesAutoDirectionFromPlacementContract()
        {
            var controllerObject = new GameObject("ContractAutoDirectionTest");
            var controller = controllerObject.AddComponent<PlacementController>();
            var placement = new AutoDirectionPlacementService();

            try
            {
                controller.Initialize(new CityFlow.Bootstrap.CityFlowServices(
                    new SimEventHub(),
                    null,
                    placement));
                controller.SetBuildType(TileType.House);

                MethodInfo resolveMethod = typeof(PlacementController).GetMethod(
                    "ResolvePlacementDirection",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var direction = (PlacementDirection)resolveMethod.Invoke(
                    controller,
                    new object[] { Vector2Int.zero });

                Assert.AreEqual(PlacementDirection.East, direction);
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
            }
        }

        [Test]
        public void ResolvePlacementDirection_DefaultContractFallsBackToNorth()
        {
            var controllerObject = new GameObject("DefaultAutoDirectionTest");
            var controller = controllerObject.AddComponent<PlacementController>();
            var placement = new DefaultAutoDirectionPlacementService();

            try
            {
                controller.Initialize(new CityFlow.Bootstrap.CityFlowServices(
                    new SimEventHub(),
                    null,
                    placement));
                controller.SetBuildType(TileType.House);

                MethodInfo resolveMethod = typeof(PlacementController).GetMethod(
                    "ResolvePlacementDirection",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var direction = (PlacementDirection)resolveMethod.Invoke(
                    controller,
                    new object[] { Vector2Int.zero });

                Assert.AreEqual(PlacementDirection.North, direction);
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
            }
        }

        [Test]
        public void GhostRotation_UsesCurrentPlacementDirection()
        {
            var ghostObject = new GameObject("RotatingGhost");
            var renderer = ghostObject.AddComponent<SpriteRenderer>();
            var manager = new PlacementVisualManager(
                renderer,
                Color.green,
                Color.red,
                false,
                1f,
                Color.green,
                Color.red,
                null,
                null,
                null);

            try
            {
                manager.Initialize();
                var coordinates = new TestCoordinateSpace();

                manager.SyncGhostPosition(
                    Vector3.zero,
                    TileFootprint.ToAngle(PlacementDirection.East),
                    false,
                    coordinates);

                Quaternion expected =
                    coordinates.CoordinateRotation *
                    Quaternion.Euler(0f, 0f, 90f);
                Assert.Less(
                    Quaternion.Angle(expected, renderer.transform.rotation),
                    0.01f,
                    "고스트 아이콘도 현재 배치 방향과 같은 각도로 회전해야 한다.");
            }
            finally
            {
                manager.Cleanup();
                Object.DestroyImmediate(ghostObject);
            }
        }

        [Test]
        public void BuildingModelPreview_FollowsGhostPositionAndRotation()
        {
            var ghostObject = new GameObject("BuildingPreviewGhost");
            var renderer = ghostObject.AddComponent<SpriteRenderer>();
            var manager = new PlacementVisualManager(
                renderer,
                Color.green,
                Color.red,
                true,
                1f,
                Color.green,
                Color.red,
                null,
                null,
                null);
            GameObject preview =
                GameObject.CreatePrimitive(PrimitiveType.Cube);

            try
            {
                manager.Initialize();
                manager.SetBuildingPreview(preview);
                manager.SetGhostActive(true);

                var coordinates = new TestCoordinateSpace();
                Vector3 expectedPosition =
                    new Vector3(4f, 0.02f, 5f);
                manager.SyncGhostPosition(
                    Vector3.zero,
                    TileFootprint.ToAngle(
                        PlacementDirection.East),
                    false,
                    coordinates,
                    expectedPosition);

                Quaternion expectedRotation =
                    coordinates.CoordinateRotation *
                    Quaternion.Euler(0f, 0f, 90f);
                Assert.AreSame(
                    preview,
                    manager.BuildingPreviewObject);
                Assert.That(preview.activeSelf, Is.True);
                Assert.That(
                    preview.transform.position,
                    Is.EqualTo(expectedPosition));
                Assert.Less(
                    Quaternion.Angle(
                        expectedRotation,
                        preview.transform.rotation),
                    0.01f,
                    "실제 건물 모델 고스트도 현재 배치 방향과 같은 각도로 회전해야 한다.");
                Assert.That(
                    preview.GetComponent<Collider>().enabled,
                    Is.False);

                var previewRenderer =
                    preview.GetComponent<Renderer>();
                Assert.That(
                    previewRenderer.sharedMaterial.shader.name,
                    Does.Contain("Unlit"),
                    "건물 미리보기는 시간대 조명의 영향을 받지 않는 셰이더를 사용해야 한다.");

                var properties =
                    new MaterialPropertyBlock();
                manager.UpdateColors(canPlace: true);
                previewRenderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetColor("_BaseColor"),
                    Is.EqualTo(new Color(0f, 1f, 0f, 1f)),
                    "설치 가능한 건물 미리보기는 불투명 초록색이어야 한다.");

                manager.UpdateColors(canPlace: false);
                previewRenderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetColor("_BaseColor"),
                    Is.EqualTo(new Color(1f, 0f, 0f, 1f)),
                    "설치 불가능한 건물 미리보기는 불투명 빨간색이어야 한다.");

                GameObject volume =
                    ReadPrivateField<GameObject>(
                        manager,
                        "_ghostVolumeObj");
                Assert.That(
                    volume.activeSelf,
                    Is.False,
                    "실제 모델 미리보기가 있으면 범용 박스 고스트는 겹쳐 보이지 않아야 한다.");
                Assert.That(
                    volume.GetComponent<Renderer>()
                        .sharedMaterial.shader.name,
                    Does.Contain("Unlit"),
                    "범용 박스 고스트도 시간대 조명의 영향을 받지 않아야 한다.");
                Assert.That(
                    volume.GetComponent<Renderer>()
                        .sharedMaterial.color,
                    Is.EqualTo(new Color(1f, 0f, 0f, 1f)),
                    "범용 박스 고스트도 설치 불가능할 때 불투명 빨간색이어야 한다.");
                Assert.That(
                    renderer.color,
                    Is.EqualTo(new Color(1f, 0f, 0f, 1f)),
                    "바닥 고스트도 설치 불가능할 때 불투명 빨간색이어야 한다.");
            }
            finally
            {
                manager.Cleanup();
                Object.DestroyImmediate(ghostObject);
            }
        }

        [Test]
        public void RoadSelection_UsesFlatRoadPlacementPreview()
        {
            var cityObject = new GameObject("RoadPreviewCityView");
            cityObject.AddComponent<MainCityView>();
            var controllerObject =
                new GameObject("RoadPreviewPlacementController");
            var controller =
                controllerObject.AddComponent<PlacementController>();
            var ghostObject = new GameObject("RoadPreviewGhost");
            var ghostRenderer =
                ghostObject.AddComponent<SpriteRenderer>();

            try
            {
                controller.Initialize(null);
                controller.ConfigureGhost(ghostRenderer);
                controller.SetBuildType(TileType.Road);

                PlacementVisualManager manager =
                    ReadPrivateField<PlacementVisualManager>(
                        controller,
                        "_visualManager");
                GameObject preview =
                    manager.BuildingPreviewObject;
                Assert.That(
                    preview,
                    Is.Not.Null,
                    "도로 선택 시 범용 박스 대신 실제 도로 형태 미리보기가 생성되어야 한다.");
                Assert.That(
                    preview.transform.localScale,
                    Is.EqualTo(
                        new Vector3(
                            GridUtil.TileSize,
                            GridUtil.TileSize,
                            0.08f)),
                    "도로 미리보기는 실제 도로와 같은 납작한 두께를 사용해야 한다.");

                GameObject volume =
                    ReadPrivateField<GameObject>(
                        manager,
                        "_ghostVolumeObj");
                Assert.That(
                    volume.activeSelf,
                    Is.False,
                    "도로 모델 미리보기가 있으면 건물용 범용 박스가 표시되면 안 된다.");
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(ghostObject);
                Object.DestroyImmediate(cityObject);
            }
        }

        [TestCase(TileType.Road, "roadPrefab")]
        [TestCase(TileType.House, "housePrefab")]
        [TestCase(TileType.Office, "officePrefab")]
        [TestCase(TileType.School, "schoolPrefab")]
        [TestCase(TileType.Hospital, "hospitalPrefab")]
        public void TilePlacementPreview_UsesInstalledRuntimePrefab(
            TileType type,
            string prefabFieldName)
        {
            var cityObject =
                new GameObject("InstalledPrefabCityView");
            MainCityView cityView =
                cityObject.AddComponent<MainCityView>();
            var sourcePrefab =
                new GameObject("FutureRuntimeAsset");
            GameObject marker =
                GameObject.CreatePrimitive(
                    PrimitiveType.Cube);
            marker.name = "FutureAssetMarker";
            marker.transform.SetParent(
                sourcePrefab.transform,
                false);
            SetPrivateField(
                cityView,
                prefabFieldName,
                sourcePrefab);

            GameObject preview = null;
            try
            {
                Assert.That(
                    cityView.TryCreatePlacementPreview(
                        type,
                        out preview),
                    Is.True);
                Assert.That(
                    preview.transform.Find(
                        type == TileType.Road
                            ? "FutureAssetMarker"
                            : "BuildingBody/FutureAssetMarker"),
                    Is.Not.Null,
                    "실제 건설 프리팹을 바꾸면 미리보기도 같은 프리팹을 사용해야 한다.");
            }
            finally
            {
                Object.DestroyImmediate(preview);
                Object.DestroyImmediate(sourcePrefab);
                Object.DestroyImmediate(cityObject);
            }
        }

        [Test]
        public void SignalPlacementPreview_UsesInstalledRuntimePrefab()
        {
            var cityObject =
                new GameObject("SignalPreviewCityView");
            MainCityView cityView =
                cityObject.AddComponent<MainCityView>();
            var signalPrefab =
                new GameObject("FutureSignalAsset");
            GameObject marker =
                GameObject.CreatePrimitive(
                    PrimitiveType.Cube);
            marker.name = "FutureSignalMarker";
            marker.transform.SetParent(
                signalPrefab.transform,
                false);
            SetPrivateField(
                cityView,
                "signalPrefab",
                signalPrefab);
            InfrastructureDataSO data =
                ScriptableObject.CreateInstance<
                    InfrastructureDataSO>();
            data.Kind = InfrastructureKind.Signal;

            GameObject preview = null;
            try
            {
                Assert.That(
                    cityView
                        .TryCreateInfrastructurePlacementPreview(
                            data,
                            null,
                            Vector2Int.zero,
                            out preview),
                    Is.True);
                Assert.That(
                    preview.GetComponentInChildren<
                        Renderer>(true),
                    Is.Not.Null);
                Assert.That(
                    preview.transform.Find(
                        "Signal_0_0/FutureSignalMarker"),
                    Is.Not.Null,
                    "실제 신호등 프리팹을 바꾸면 인프라 미리보기도 같은 프리팹을 사용해야 한다.");
            }
            finally
            {
                Object.DestroyImmediate(preview);
                Object.DestroyImmediate(data);
                Object.DestroyImmediate(signalPrefab);
                Object.DestroyImmediate(cityObject);
            }
        }

        [Test]
        public void InfrastructureCoordinator_ShowsRuntimeModelPreview()
        {
            var cityObject =
                new GameObject(
                    "InfrastructurePreviewCityView");
            cityObject.AddComponent<MainCityView>();
            var coordinatorObject =
                new GameObject(
                    "InfrastructurePreviewCoordinator");
            InfrastructurePlacementCoordinator coordinator =
                coordinatorObject.AddComponent<
                    InfrastructurePlacementCoordinator>();
            InfrastructureDataSO data =
                ScriptableObject.CreateInstance<
                    InfrastructureDataSO>();
            data.Kind = InfrastructureKind.Signal;
            data.InfrastructureName = "Signal";

            try
            {
                coordinator.StartPlacement(data);
                FieldInfo previewField =
                    typeof(
                        InfrastructurePlacementCoordinator)
                    .GetField(
                        "_placementPreview",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance);
                Assert.NotNull(previewField);
                GameObject preview =
                    previewField.GetValue(coordinator)
                        as GameObject;
                Assert.That(
                    preview,
                    Is.Not.Null,
                    "인프라 배치도 아이콘이 아니라 실제 월드 비주얼 미리보기를 생성해야 한다.");
                Assert.That(
                    preview.activeSelf,
                    Is.True);
                Assert.That(
                    preview.GetComponentsInChildren<
                        Renderer>(true).Length,
                    Is.GreaterThan(0));
            }
            finally
            {
                Object.DestroyImmediate(data);
                Object.DestroyImmediate(
                    coordinatorObject);
                Object.DestroyImmediate(cityObject);
            }
        }

        [Test]
        public void BusStopPlacementPreview_UsesInstalledRuntimePrefab()
        {
            var viewObject =
                new GameObject("BusStopPreviewView");
            CityBusStopWorldView view =
                viewObject.AddComponent<
                    CityBusStopWorldView>();
            var stationPrefab =
                new GameObject("FutureBusStopAsset");
            GameObject marker =
                GameObject.CreatePrimitive(
                    PrimitiveType.Cube);
            marker.name = "FutureBusStopMarker";
            marker.transform.SetParent(
                stationPrefab.transform,
                false);
            SetPrivateField(
                view,
                "stationPrefab",
                stationPrefab);

            GameObject preview = null;
            try
            {
                Assert.That(
                    view.TryCreatePlacementPreview(
                        out preview),
                    Is.True);
                Assert.That(
                    preview.transform.Find(
                        "FutureBusStopMarker"),
                    Is.Not.Null,
                    "실제 버스정류장 프리팹을 바꾸면 미리보기도 같은 프리팹을 사용해야 한다.");
            }
            finally
            {
                Object.DestroyImmediate(preview);
                Object.DestroyImmediate(stationPrefab);
                Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void InvalidSpecialSelection_CancelsPreviousRoadMode()
        {
            var services = new CityFlow.Bootstrap.CityFlowServices(
                new SimEventHub(),
                null,
                null);
            services.RegisterSpecialBuildings(
                new TestSpecialBuildingService(isUnlocked: false));

            var controllerObject = new GameObject(
                "InvalidSpecialSelectionTest");
            var controller =
                controllerObject.AddComponent<PlacementController>();

            try
            {
                controller.Initialize(services);
                controller.SetBuildType(TileType.Road);

                LogAssert.Expect(
                    LogType.Warning,
                    "[PlacementController] The selected special building " +
                    "is invalid or locked. The previous build selection was cancelled.");
                bool selected = controller.SetSpecialBuilding("mall");

                Assert.IsFalse(selected);
                Assert.IsFalse(
                    controller.IsBuildingMode,
                    "특수 건물 선택 실패 뒤에는 직전 도로 모드가 남아 있으면 안 된다.");
                Assert.AreEqual(
                    TileType.SpecialBuilding,
                    ReadPrivateField<TileType>(controller, "_currentType"));
                Assert.AreEqual(
                    "mall",
                    ReadPrivateField<string>(
                        controller,
                        "_currentSpecialBuildingId"));
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
            }
        }

        [Test]
        public void SpecialSelection_AfterRoadMode_ReplacesSelection()
        {
            var services = new CityFlow.Bootstrap.CityFlowServices(
                new SimEventHub(),
                null,
                null);
            services.RegisterSpecialBuildings(
                new TestSpecialBuildingService(isUnlocked: true));

            var controllerObject = new GameObject(
                "SpecialSelectionTransitionTest");
            var controller =
                controllerObject.AddComponent<PlacementController>();

            try
            {
                controller.Initialize(services);
                controller.SetBuildType(TileType.Road);

                Assert.IsTrue(controller.SetSpecialBuilding("mall"));
                Assert.IsTrue(controller.IsBuildingMode);
                Assert.AreEqual(
                    TileType.SpecialBuilding,
                    ReadPrivateField<TileType>(controller, "_currentType"));
                Assert.AreEqual(
                    "mall",
                    ReadPrivateField<string>(
                        controller,
                        "_currentSpecialBuildingId"));
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
            }
        }

        private static T ReadPrivateField<T>(
            PlacementController controller,
            string fieldName)
        {
            FieldInfo field = typeof(PlacementController).GetField(
                fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            return (T)field.GetValue(controller);
        }

        private static T ReadPrivateField<T>(
            PlacementVisualManager manager,
            string fieldName)
        {
            FieldInfo field =
                typeof(PlacementVisualManager).GetField(
                    fieldName,
                    BindingFlags.NonPublic |
                    BindingFlags.Instance);
            Assert.NotNull(field);
            return (T)field.GetValue(manager);
        }

        [Test]
        public void RightClick_DuringPlacementMode_CancelsPlacement()
        {
            var go = new GameObject("Coordinator");
            var coordinator = go.AddComponent<InfrastructurePlacementCoordinator>();
            var data = ScriptableObject.CreateInstance<InfrastructureDataSO>();
            data.Kind = InfrastructureKind.Signal;

            coordinator.StartPlacement(data);

            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                using (StateEvent.From(mouse, out var eventPtr))
                {
                    mouse.rightButton.WriteValueIntoEvent(1f, eventPtr);
                    InputSystem.QueueEvent(eventPtr);
                }
                InputSystem.Update();

                // Reflection to call private Update method
                var updateMethod = typeof(InfrastructurePlacementCoordinator).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
                updateMethod.Invoke(coordinator, null);

                // Verify placement is cancelled (IsPlacing property or something)
                var isPlacingField = typeof(InfrastructurePlacementCoordinator).GetField("_isBuildingMode", BindingFlags.NonPublic | BindingFlags.Instance);
                bool isPlacing = (bool)isPlacingField.GetValue(coordinator);

                Assert.IsFalse(isPlacing, "Right click should cancel placement.");
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                Object.DestroyImmediate(data);
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RightClickDragDemolition_StopsWhenMenuCloses()
        {
            var go = new GameObject("Coordinator");
            var coordinator = go.AddComponent<InfrastructurePlacementCoordinator>();

            bool isMenuOpen = true;
            coordinator.IsBuildMenuOpen = () => isMenuOpen;

            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                // Reflection to set internal state
                SetPrivateField(coordinator, "_isBuildingMode", true); // Required for Update to not return immediately
                SetPrivateField(coordinator, "_isDemolishMode", true);
                SetPrivateField(coordinator, "_rightClickStartCoord", new Vector2Int(0, 0));

                using (StateEvent.From(mouse, out var eventPtr))
                {
                    mouse.rightButton.WriteValueIntoEvent(1f, eventPtr);
                    InputSystem.QueueEvent(eventPtr);
                }
                InputSystem.Update();

                isMenuOpen = false; // Simulate menu closing during drag

                var updateMethod = typeof(InfrastructurePlacementCoordinator).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
                updateMethod.Invoke(coordinator, null);

                var startCoordField = typeof(InfrastructurePlacementCoordinator).GetField("_rightClickStartCoord", BindingFlags.NonPublic | BindingFlags.Instance);
                var startCoord = startCoordField.GetValue(coordinator);

                Assert.IsNull(startCoord, "Demolition drag should stop and reset when menu closes.");
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RightClickDemolition_IsBlockedWhenMenuStateUnavailable()
        {
            var go = new GameObject("Controller");
            var controller = go.AddComponent<PlacementController>();
            var placement = new CountingPlacementService();
            var mouse = InputSystem.AddDevice<Mouse>();

            try
            {
                controller.SetFakeMode(false);
                controller.Initialize(new CityFlowServices(
                    new SimEventHub(),
                    new TestTileData(),
                    placement));

                using (StateEvent.From(mouse, out var eventPtr))
                {
                    mouse.rightButton.WriteValueIntoEvent(1f, eventPtr);
                    InputSystem.QueueEvent(eventPtr);
                }
                InputSystem.Update();

                MethodInfo updateMethod = typeof(PlacementController).GetMethod(
                    "Update",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                updateMethod.Invoke(controller, null);

                Assert.AreEqual(
                    0,
                    placement.RemoveCalls,
                    "메뉴 상태를 확인할 수 없으면 철거를 실행하면 안 된다.");
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RightClickDragDemolition_StopsWhenMenuStateUnavailable()
        {
            var go = new GameObject("Coordinator");
            var coordinator =
                go.AddComponent<InfrastructurePlacementCoordinator>();
            var mouse = InputSystem.AddDevice<Mouse>();

            try
            {
                SetPrivateField(coordinator, "_isBuildingMode", true);
                SetPrivateField(coordinator, "_isDemolishMode", true);
                SetPrivateField(
                    coordinator,
                    "_rightClickStartCoord",
                    new Vector2Int(0, 0));

                using (StateEvent.From(mouse, out var eventPtr))
                {
                    mouse.rightButton.WriteValueIntoEvent(1f, eventPtr);
                    InputSystem.QueueEvent(eventPtr);
                }
                InputSystem.Update();

                MethodInfo updateMethod = typeof(
                    InfrastructurePlacementCoordinator).GetMethod(
                    "Update",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                updateMethod.Invoke(coordinator, null);

                object startCoord = typeof(
                    InfrastructurePlacementCoordinator).GetField(
                    "_rightClickStartCoord",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(coordinator);

                Assert.IsNull(
                    startCoord,
                    "메뉴 상태를 확인할 수 없으면 철거 드래그를 중단해야 한다.");
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                Object.DestroyImmediate(go);
            }
        }

        private sealed class TestTileData : IReadOnlyTileData
        {
            public CongestionLevel GetCongestion(Vector2Int tile) => CongestionLevel.Free;
            public float GetDensity01(Vector2Int tile) => 0f;
            public int GetQueueCount(Vector2Int tile, Dir entryDir) => 0;
            public TileType GetTileType(Vector2Int tile) => TileType.Empty;
            public PlacementDirection GetDirection(Vector2Int tile) => PlacementDirection.North;
            public Vector2Int GetFootprintSize(TileType type) => Vector2Int.one;
            public bool TryGetFootprintAnchor(Vector2Int tile, out Vector2Int anchor) { anchor = tile; return false; }
            public bool IsFootprintAnchor(Vector2Int tile) => false;
            public bool TryGetConstructionProgress01(Vector2Int tile, out float progress01) { progress01 = 0f; return false; }
            public bool TryGetConstructionTargetType(Vector2Int tile, out TileType targetType) { targetType = TileType.Empty; return false; }
        }

        private sealed class TestEconomyService : IEconomyService
        {
            public long Coins { get; set; }
            public event Action<long> CoinsChanged;
            public bool TrySpend(long amount)
            {
                if (Coins >= amount)
                {
                    Coins -= amount;
                    CoinsChanged?.Invoke(Coins);
                    return true;
                }
                return false;
            }
            public void AddCoins(long amount, string reason)
            {
                Coins += amount;
                CoinsChanged?.Invoke(Coins);
            }
        }

        [Test]
        public void HandlePlace_SinglePlacement_CancelsOnSuccess()
        {
            var go = new GameObject("Controller");
            var controller = go.AddComponent<PlacementController>();
            CityFlow.Configs.TileDataSO tileData = null;

            try
            {
                var economy = new TestEconomyService { Coins = 100 };
                var services = new CityFlowServices(
                    new SimEventHub(),
                    new TestTileData(),
                    new DefaultAutoDirectionPlacementService(),
                    null,
                    economy
                );

                tileData = ScriptableObject.CreateInstance<
                    CityFlow.Configs.TileDataSO>();
                SetPrivateField(tileData, "buildCost", 100);
                SetPrivateField(tileData, "category", TileType.Hospital);
                SetPrivateField(
                    controller,
                    "availableTiles",
                    new[] { tileData });
                controller.SetFakeMode(false);

                controller.Initialize(services);
                controller.ToggleBuildMode(true);
                controller.SetBuildType(TileType.Hospital);

                MethodInfo updateMethod = typeof(PlacementController).GetMethod(
                    "HandlePlace",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                updateMethod.Invoke(controller, new object[] { new Vector2Int(0, 0) });

                Assert.IsFalse(
                    controller.IsBuildingMode,
                    "성공한 단발 건물 배치 뒤에는 배치 모드를 종료해야 한다.");
            }
            finally
            {
                Object.DestroyImmediate(tileData);
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void HandlePlace_SinglePlacement_MaintainsModeOnFailure()
        {
            var go = new GameObject("Controller");
            var controller = go.AddComponent<PlacementController>();
            CityFlow.Configs.TileDataSO tileData = null;

            try
            {
                var economy = new TestEconomyService { Coins = 0 };
                var services = new CityFlowServices(
                    new SimEventHub(),
                    new TestTileData(),
                    new DefaultAutoDirectionPlacementService(),
                    null,
                    economy
                );

                tileData = ScriptableObject.CreateInstance<
                    CityFlow.Configs.TileDataSO>();
                SetPrivateField(tileData, "buildCost", 100);
                SetPrivateField(tileData, "category", TileType.Hospital);
                SetPrivateField(
                    controller,
                    "availableTiles",
                    new[] { tileData });
                controller.SetFakeMode(false);

                controller.Initialize(services);
                controller.ToggleBuildMode(true);
                controller.SetBuildType(TileType.Hospital);

                MethodInfo updateMethod = typeof(PlacementController).GetMethod(
                    "HandlePlace",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                updateMethod.Invoke(
                    controller,
                    new object[] { new Vector2Int(0, 0) });

                Assert.IsTrue(
                    controller.IsBuildingMode,
                    "자금 부족으로 배치가 실패하면 배치 모드를 유지해야 한다.");
            }
            finally
            {
                Object.DestroyImmediate(tileData);
                Object.DestroyImmediate(go);
            }
        }
        private static void SetPrivateField<TTarget, TValue>(
            TTarget target,
            string fieldName,
            TValue value)
        {
            FieldInfo field = typeof(TTarget).GetField(
                fieldName,
                BindingFlags.NonPublic |
                BindingFlags.Instance);
            Assert.NotNull(field);
            field.SetValue(target, value);
        }

        private sealed class TestCoordinateSpace : IWorldCoordinateSpace
        {
            public WorldCoordinatePlane Plane => WorldCoordinatePlane.XZ;
            public float TileSize => 1f;
            public Vector3 Origin => Vector3.zero;
            public Vector3 GridXAxis => Vector3.right;
            public Vector3 GridYAxis => Vector3.forward;
            public Vector3 GroundNormal => Vector3.up;
            public Quaternion CoordinateRotation =>
                Quaternion.Euler(90f, 0f, 0f);

            public Vector3 GridToWorld(
                Vector2Int tile,
                float surfaceOffset = 0f) =>
                new Vector3(tile.x + 0.5f, surfaceOffset, tile.y + 0.5f);

            public Vector3 GridPointToWorld(
                Vector2 gridPoint,
                float surfaceOffset = 0f) =>
                new Vector3(gridPoint.x, surfaceOffset, gridPoint.y);

            public Vector2 WorldToGridPoint(Vector3 worldPosition) =>
                new Vector2(worldPosition.x, worldPosition.z);

            public Vector2Int WorldToGrid(Vector3 worldPosition) =>
                Vector2Int.FloorToInt(WorldToGridPoint(worldPosition));

            public bool TryRayToGrid(
                Ray ray,
                out Vector2Int tile,
                out Vector3 worldHitPoint)
            {
                tile = default;
                worldHitPoint = default;
                return false;
            }
        }

        private sealed class TestSpecialBuildingService :
            ISpecialBuildingService
        {
            private readonly bool _isUnlocked;

            public TestSpecialBuildingService(bool isUnlocked)
            {
                _isUnlocked = isUnlocked;
            }

            public int BuildingCount => 0;

            public event Action<SpecialBuildingChangedEvent> BuildingChanged;
            public event Action BuildingsRestored;
            public event Action BuildOptionsChanged;
            public event Action<HappinessEffectChangedEvent>
                HappinessEffectChanged;

            public bool CanPlace(
                string buildingId,
                Vector2Int anchor,
                PlacementDirection direction = PlacementDirection.North) =>
                false;

            public bool TryPlace(
                string buildingId,
                Vector2Int anchor,
                PlacementDirection direction = PlacementDirection.North) =>
                false;

            public bool TryRemove(Vector2Int tile) => false;

            public bool TryGetBuilding(
                Vector2Int tile,
                out SpecialBuildingInstance building)
            {
                building = default;
                return false;
            }

            public bool IsBuildingUnlocked(string buildingId) =>
                _isUnlocked &&
                string.Equals(
                    buildingId,
                    "mall",
                    StringComparison.Ordinal);

            public bool TryGetBuildOption(
                string buildingId,
                out SpecialBuildingBuildOption option)
            {
                option = default;
                return false;
            }

            public SpecialBuildingInstance[] CreateBuildingSnapshot() =>
                Array.Empty<SpecialBuildingInstance>();

            public SpecialBuildingBuildOption[] CreateBuildOptionSnapshot() =>
                Array.Empty<SpecialBuildingBuildOption>();

            public HappinessEffectDescriptor[]
                CreateActiveHappinessEffectSnapshot() =>
                Array.Empty<HappinessEffectDescriptor>();
        }
    }
}
