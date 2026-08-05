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
        public void RightClick_DuringPlacement_DemolishesWithoutCancellingPlacement()
        {
            var handler = new PlacementInputHandler(
                new UIRaycastBlocker(),
                null);
            Vector2Int target = new(4, 7);
            bool cancelRequested = false;
            Vector2Int? demolishedCoord = null;
            handler.OnCancelPlacementRequested +=
                () => cancelRequested = true;
            handler.OnDemolishRequested += coord =>
            {
                demolishedCoord = coord;
                return true;
            };

            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                using (StateEvent.From(mouse, out var eventPtr))
                {
                    mouse.rightButton.WriteValueIntoEvent(1f, eventPtr);
                    InputSystem.QueueEvent(eventPtr);
                }
                InputSystem.Update();

                handler.UpdateGlobalInput(
                    true,
                    true,
                    target,
                    true);

                Assert.That(cancelRequested, Is.False);
                Assert.That(demolishedCoord, Is.EqualTo(target));
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
            }
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
            var blockedProp = typeof(WeeklySettlementPopup).GetProperty("IsInteractionBlocked", BindingFlags.Public | BindingFlags.Static);

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
            Material sourceMaterial =
                preview.GetComponent<Renderer>().sharedMaterial;

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
                Assert.AreNotSame(
                    sourceMaterial,
                    previewRenderer.sharedMaterial,
                    "미리보기는 원본 에셋을 변경하지 않는 전용 재질 복사본을 사용해야 한다.");
                Assert.That(
                    previewRenderer.sharedMaterial.shader.name,
                    Does.Contain("Unlit").Or.EqualTo("Sprites/Default"),
                    "미리보기 재질은 시간대 조명의 영향을 받지 않아야 한다.");
                Assert.AreSame(
                    sourceMaterial.mainTexture,
                    previewRenderer.sharedMaterial.mainTexture,
                    "전용 복사본도 실제 설치 모델의 텍스처를 유지해야 한다.");

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
                Assert.That(
                    renderer.gameObject.activeSelf,
                    Is.False,
                    "실제 모델 미리보기가 있으면 기존 바닥 사각형은 겹쳐 보이면 안 된다.");
            }
            finally
            {
                manager.Cleanup();
                Object.DestroyImmediate(ghostObject);
            }
        }

        [Test]
        public void BuildingModelPreview_CleanupRemovesOwnedPreviewImmediately()
        {
            var owner = new GameObject("PlacementPreviewOwner");
            var ghostObject = new GameObject("PlacementPreviewGhost");
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
                null,
                owner.transform);
            GameObject preview =
                GameObject.CreatePrimitive(PrimitiveType.Cube);

            try
            {
                manager.Initialize();
                manager.SetBuildingPreview(preview);

                Assert.AreSame(
                    owner.transform,
                    preview.transform.parent,
                    "배치 미리보기는 컨트롤러 수명 아래에 있어야 한다.");

                manager.Cleanup();

                Assert.That(
                    preview == null,
                    Is.True,
                    "에디터의 임시 미리보기는 Cleanup 즉시 제거되어야 한다.");
            }
            finally
            {
                manager.Cleanup();
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(ghostObject);
            }
        }

        [Test]
        public void BuildingModelPreview_ErrorShaderFallsBackToPreviewMaterial()
        {
            Shader errorShader =
                Shader.Find("Hidden/InternalErrorShader");
            Assert.That(errorShader, Is.Not.Null);

            var ghostObject = new GameObject("ErrorShaderPreviewGhost");
            var ghostRenderer =
                ghostObject.AddComponent<SpriteRenderer>();
            var manager = new PlacementVisualManager(
                ghostRenderer,
                Color.green,
                Color.red,
                false,
                1f,
                Color.green,
                Color.red,
                null,
                null,
                null);
            GameObject preview =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            var errorMaterial = new Material(errorShader);
            Renderer previewRenderer =
                preview.GetComponent<Renderer>();
            previewRenderer.sharedMaterial = errorMaterial;

            try
            {
                manager.Initialize();
                manager.SetBuildingPreview(preview);

                Assert.AreNotSame(
                    errorMaterial,
                    previewRenderer.sharedMaterial,
                    "Error Shader 재질은 미리보기 전용 재질로 교체되어야 한다.");
                Assert.That(
                    previewRenderer.sharedMaterial.shader.name,
                    Does.Contain("Unlit"));
            }
            finally
            {
                manager.Cleanup();
                Object.DestroyImmediate(errorMaterial);
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
                    preview.transform.Find("RoadModel"),
                    Is.Not.Null,
                    "도로 미리보기는 실제 설치와 같은 도로 모델을 포함해야 한다.");
                Assert.That(
                    preview.transform.Find("RoadPerimeter"),
                    Is.Not.Null,
                    "도로 미리보기는 실제 설치와 같은 연결 테두리를 포함해야 한다.");
                Renderer roadRenderer =
                    preview.transform
                        .Find("RoadModel")
                        ?.GetComponentInChildren<Renderer>();
                Assert.That(roadRenderer, Is.Not.Null);
                Assert.That(
                    roadRenderer.bounds.size.x,
                    Is.EqualTo(1f).Within(0.01f),
                    "도로는 그리드 경계선 중심부터 반대편 경계선 중심까지 한 칸 폭만 차지해야 한다.");
                Assert.That(
                    roadRenderer.bounds.size.y,
                    Is.EqualTo(1f).Within(0.01f),
                    "도로는 그리드 선 두께를 실제 설치 면적에 더하지 않아야 한다.");

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

        [Test]
        public void RoadPreview_OffsetsOnlyWhenItOverlapsInstalledTiles()
        {
            var cityObject =
                new GameObject("OverlapPreviewCityView");
            MainCityView cityView =
                cityObject.AddComponent<MainCityView>();
            var controllerObject =
                new GameObject("OverlapPreviewController");
            PlacementController controller =
                controllerObject.AddComponent<PlacementController>();
            var tileData =
                new CityFlow.Fakes.FakeFlowReader(10, 10);
            var services =
                new CityFlow.Bootstrap.CityFlowServices(
                    new SimEventHub(),
                    tileData,
                    null);
            MethodInfo getPreviewPosition =
                typeof(PlacementController).GetMethod(
                    "GetBuildingPreviewPosition",
                    BindingFlags.NonPublic |
                    BindingFlags.Instance);

            try
            {
                controller.Initialize(services);
                controller.SetBuildType(TileType.Road);
                SetPrivateField(
                    controller,
                    "_cityView",
                    cityView);

                var emptyTile = new Vector2Int(1, 1);
                Vector3 emptyPreview =
                    (Vector3)getPreviewPosition.Invoke(
                        controller,
                        new object[]
                        {
                            emptyTile,
                            Vector2Int.one
                        });
                Vector3 emptyInstalled =
                    cityView.GetPlacementPreviewWorldPosition(
                        emptyTile,
                        TileType.Road);
                Assert.That(
                    emptyPreview,
                    Is.EqualTo(emptyInstalled),
                    "빈 타일 미리보기는 실제 설치 위치와 정확히 같아야 한다.");

                var occupiedRoadTile =
                    new Vector2Int(5, 1);
                Vector3 overlappingPreview =
                    (Vector3)getPreviewPosition.Invoke(
                        controller,
                        new object[]
                        {
                            occupiedRoadTile,
                            Vector2Int.one
                        });
                Vector3 overlappingInstalled =
                    cityView.GetPlacementPreviewWorldPosition(
                        occupiedRoadTile,
                        TileType.Road);
                Assert.That(
                    Vector3.Distance(
                        overlappingPreview,
                        overlappingInstalled),
                    Is.EqualTo(
                        PlacementController
                            .OverlappingPreviewOffset)
                        .Within(0.0001f),
                    "기존 표면과 겹치는 미리보기만 Z-fighting 방지 간격을 가져야 한다.");
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(cityObject);
            }
        }

        [Test]
        public void PlacementGhost_UsesUnifiedRoadSurfaceHeight()
        {
            var cityObject =
                new GameObject("GhostSurfaceCityView");
            MainCityView cityView =
                cityObject.AddComponent<MainCityView>();
            var controllerObject =
                new GameObject("GhostSurfaceController");
            PlacementController controller =
                controllerObject.AddComponent<PlacementController>();

            try
            {
                SetPrivateField(
                    controller,
                    "_cityView",
                    cityView);

                Assert.That(
                    controller.GetSurfaceMarkerZ(Vector2Int.zero),
                    Is.EqualTo(cityView.RoadSurfaceZ)
                        .Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(cityObject);
            }
        }

        [Test]
        public void HouseGhostFootprint_FollowsResolvedAutoDirection()
        {
            var controllerObject =
                new GameObject("DirectionalHouseGhostController");
            PlacementController controller =
                controllerObject.AddComponent<PlacementController>();
            var ghostObject =
                new GameObject("DirectionalHouseGhost");
            SpriteRenderer ghostRenderer =
                ghostObject.AddComponent<SpriteRenderer>();
            var placement = new AutoDirectionPlacementService();

            try
            {
                controller.ConfigureGhost(ghostRenderer);
                controller.Initialize(
                    new CityFlow.Bootstrap.CityFlowServices(
                        new SimEventHub(),
                        null,
                        placement));
                controller.SetBuildType(TileType.House);

                Assert.That(
                    ghostRenderer.transform.localScale,
                    Is.EqualTo(new Vector3(1f, 2f, 1f)));

                MethodInfo updateDirectionMethod =
                    typeof(PlacementController).GetMethod(
                        "UpdatePlacementDirection",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance);
                Assert.That(updateDirectionMethod, Is.Not.Null);
                updateDirectionMethod.Invoke(
                    controller,
                    new object[] { Vector2Int.zero });

                Assert.That(
                    ghostRenderer.transform.localScale,
                    Is.EqualTo(new Vector3(2f, 1f, 1f)),
                    "1x2 거주지 고스트는 자동 방향이 East면 2x1로 바뀌어야 한다.");
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(ghostObject);
            }
        }

        [Test]
        public void HousePlacement_UsesCursorTileAsFrontParkingTile()
        {
            var controllerObject =
                new GameObject("ParkingCursorPlacementController");
            PlacementController controller =
                controllerObject.AddComponent<PlacementController>();
            Vector2Int cursor = new Vector2Int(10, 20);
            var tileData = new ParkingCursorTileData(
                cursor + Vector2Int.right);

            try
            {
                SetPrivateField(
                    controller,
                    "_services",
                    new CityFlowServices(
                        new SimEventHub(),
                        tileData,
                        new DefaultAutoDirectionPlacementService()));
                SetPrivateField(
                    controller,
                    "_currentType",
                    TileType.House);

                MethodInfo directionMethod =
                    typeof(PlacementController).GetMethod(
                        "ResolvePlacementDirection",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance);
                MethodInfo anchorMethod =
                    typeof(PlacementController).GetMethod(
                        "ResolvePlacementAnchor",
                        BindingFlags.NonPublic |
                        BindingFlags.Static);
                Assert.That(directionMethod, Is.Not.Null);
                Assert.That(anchorMethod, Is.Not.Null);

                PlacementDirection direction =
                    (PlacementDirection)directionMethod.Invoke(
                        controller,
                        new object[] { cursor });
                Vector2Int anchor =
                    (Vector2Int)anchorMethod.Invoke(
                        null,
                        new object[]
                        {
                            cursor,
                            TileType.House,
                            direction
                        });

                Assert.That(
                    direction,
                    Is.EqualTo(PlacementDirection.East));
                Assert.That(
                    anchor,
                    Is.EqualTo(new Vector2Int(9, 20)),
                    "동쪽 도로 옆 커서 칸이 주차장이고 건물 앵커는 그 뒤로 이동해야 한다.");
                Assert.That(
                    anchor + Vector2Int.right,
                    Is.EqualTo(cursor),
                    "커서가 가리킨 칸은 회전된 2x1 거주지의 동쪽 주차장 칸이어야 한다.");
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
            }
        }

        [Test]
        public void HousePreviewPosition_UsesRotatedFootprintCenter()
        {
            var cityObject =
                new GameObject("RotatedHousePreviewCityView");
            MainCityView cityView =
                cityObject.AddComponent<MainCityView>();

            try
            {
                Vector2Int anchor = new Vector2Int(2, 3);
                Vector3 north =
                    cityView.GetPlacementPreviewWorldPosition(
                        anchor,
                        TileType.House,
                        PlacementDirection.North);
                Vector3 east =
                    cityView.GetPlacementPreviewWorldPosition(
                        anchor,
                        TileType.House,
                        PlacementDirection.East);

                Assert.That(
                    north,
                    Is.EqualTo(new Vector3(2.5f, 4f, 0f)),
                    "1x2 거주지의 North 미리보기는 세로 풋프린트 중앙에 있어야 한다.");
                Assert.That(
                    east,
                    Is.EqualTo(new Vector3(3f, 3.5f, 0f)),
                    "2x1로 회전한 거주지 미리보기는 가로 풋프린트 중앙에 있어야 한다.");
            }
            finally
            {
                Object.DestroyImmediate(cityObject);
            }
        }

        [Test]
        public void HouseConstructionVisual_UsesOneByTwoTargetFootprint()
        {
            var cityObject =
                new GameObject("HouseConstructionFootprintCityView");
            MainCityView cityView =
                cityObject.AddComponent<MainCityView>();

            try
            {
                MethodInfo scaleMethod =
                    typeof(MainCityView).GetMethod(
                        "GetBuildingBodyScale",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance);
                MethodInfo positionMethod =
                    typeof(MainCityView).GetMethod(
                        "GetBuildingBodyPosition",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance);
                Assert.That(scaleMethod, Is.Not.Null);
                Assert.That(positionMethod, Is.Not.Null);

                Vector3 scale =
                    (Vector3)scaleMethod.Invoke(
                        cityView,
                        new object[]
                        {
                            TileType.UnderConstruction,
                            TileType.House
                        });
                Vector3 position =
                    (Vector3)positionMethod.Invoke(
                        cityView,
                        new object[]
                        {
                            TileType.UnderConstruction,
                            TileType.House
                        });

                Assert.That(
                    scale.x,
                    Is.EqualTo(1f).Within(0.0001f));
                Assert.That(
                    scale.y,
                    Is.EqualTo(2f).Within(0.0001f));
                Assert.That(
                    position.x,
                    Is.EqualTo(0f).Within(0.0001f));
                Assert.That(
                    position.y,
                    Is.EqualTo(0f).Within(0.0001f),
                    "공사 중 비주얼은 1x2 점유 범위 중앙에서 벗어나면 안 된다.");
            }
            finally
            {
                Object.DestroyImmediate(cityObject);
            }
        }

        [Test]
        public void SelectedStructure_UsesLowAlphaOverlayAndPreservesOriginal()
        {
            var selectionObject =
                new GameObject("HouseSelectionController");
            TileSelectionController selection =
                selectionObject.AddComponent<TileSelectionController>();
            var highlightObject =
                new GameObject("HouseSelectionHighlight");
            highlightObject.AddComponent<SpriteRenderer>();
            GameObject structure =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            Renderer structureRenderer =
                structure.GetComponent<Renderer>();
            Material originalMaterial =
                structureRenderer.sharedMaterial;

            try
            {
                selection.Configure(
                    null,
                    null,
                    highlightObject);

                MethodInfo applyMethod =
                    typeof(TileSelectionController).GetMethod(
                        "ApplySelectedRenderer",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance);
                MethodInfo overlayColorMethod =
                    typeof(TileSelectionController).GetMethod(
                        "GetSelectionOverlayColor",
                        BindingFlags.NonPublic |
                        BindingFlags.Static);
                MethodInfo deselectMethod =
                    typeof(TileSelectionController).GetMethod(
                        "DeselectTile",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance);
                Assert.That(applyMethod, Is.Not.Null);
                Assert.That(overlayColorMethod, Is.Not.Null);
                Assert.That(deselectMethod, Is.Not.Null);
                applyMethod.Invoke(
                    selection,
                    new object[] { structureRenderer });

                Color overlayColor =
                    (Color)overlayColorMethod.Invoke(null, null);
                Transform overlay = structure.transform.Find(
                    "Cube (Selection Overlay)");

                Assert.That(
                    highlightObject.activeSelf,
                    Is.False,
                    "별도 바닥 고스트는 표시하지 않아야 한다.");
                Assert.That(
                    structureRenderer.sharedMaterial,
                    Is.SameAs(originalMaterial),
                    "선택 중에도 원본 구조물 재질은 교체하면 안 된다.");
                Assert.That(
                    overlay,
                    Is.Not.Null,
                    "선택 표시는 실제 형상을 복제한 투명 레이어여야 한다.");
                Assert.That(
                    overlay.GetComponent<Renderer>(),
                    Is.Not.Null);
                Assert.That(
                    overlayColor.a,
                    Is.EqualTo(0.08f).Within(0.0001f),
                    "선택 레이어는 원본을 가리지 않는 낮은 알파를 유지해야 한다.");

                deselectMethod.Invoke(selection, null);
                Assert.That(
                    structureRenderer.sharedMaterial,
                    Is.SameAs(originalMaterial),
                    "선택 해제 시 원본 재질을 복원해야 한다.");
                Assert.That(
                    structure.transform.Find(
                        "Cube (Selection Overlay)"),
                    Is.Null,
                    "선택 해제 시 투명 레이어를 제거해야 한다.");
            }
            finally
            {
                Object.DestroyImmediate(selectionObject);
                Object.DestroyImmediate(highlightObject);
                Object.DestroyImmediate(structure);
            }
        }

        [Test]
        public void SelectedFootprintTile_ResolvesVisualAnchor()
        {
            var selectionObject =
                new GameObject("FootprintSelectionController");
            Vector2Int anchor = new Vector2Int(10, 20);
            Vector2Int occupiedTile = anchor + Vector2Int.up;
            var tileData = new FootprintAnchorTileData(anchor);

            try
            {
                TileSelectionController selection =
                    selectionObject.AddComponent<
                        TileSelectionController>();
                selection.Initialize(
                    new CityFlowServices(
                        new SimEventHub(),
                        tileData,
                        null));

                MethodInfo resolveMethod =
                    typeof(TileSelectionController).GetMethod(
                        "ResolveVisualAnchor",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance);
                Assert.That(resolveMethod, Is.Not.Null);

                Vector2Int resolved =
                    (Vector2Int)resolveMethod.Invoke(
                        selection,
                        new object[] { occupiedTile });

                Assert.That(
                    resolved,
                    Is.EqualTo(anchor),
                    "풋프린트의 비앵커 타일을 선택해도 실제 건물 시각 오브젝트의 앵커를 사용해야 한다.");
            }
            finally
            {
                Object.DestroyImmediate(selectionObject);
            }
        }

        [Test]
        public void SelectedHouseHighlight_HidesAfterBuildingIsRemoved()
        {
            var selectionObject =
                new GameObject("RemovedHouseSelectionController");
            TileSelectionController selection =
                selectionObject.AddComponent<TileSelectionController>();
            var highlightObject =
                new GameObject("RemovedHouseSelectionHighlight");
            highlightObject.AddComponent<SpriteRenderer>();
            GameObject structure =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            Renderer structureRenderer =
                structure.GetComponent<Renderer>();
            Material originalMaterial =
                structureRenderer.sharedMaterial;
            var tileData = new EastFacingHouseTileData();

            try
            {
                selection.Configure(
                    null,
                    null,
                    highlightObject);
                selection.Initialize(
                    new CityFlowServices(
                        new SimEventHub(),
                        tileData,
                        null));

                MethodInfo selectMethod =
                    typeof(TileSelectionController).GetMethod(
                        "SelectTile",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance);
                MethodInfo clearMethod =
                    typeof(TileSelectionController).GetMethod(
                        "ClearSelectionIfRemoved",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance);
                Assert.That(selectMethod, Is.Not.Null);
                Assert.That(clearMethod, Is.Not.Null);
                selectMethod.Invoke(
                    selection,
                    new object[] { new Vector2Int(10, 20) });
                MethodInfo applyMethod =
                    typeof(TileSelectionController).GetMethod(
                        "ApplySelectedRenderer",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance);
                Assert.That(applyMethod, Is.Not.Null);
                applyMethod.Invoke(
                    selection,
                    new object[] { structureRenderer });
                Assert.That(
                    structureRenderer.sharedMaterial,
                    Is.SameAs(originalMaterial));
                Assert.That(
                    structure.transform.Find(
                        "Cube (Selection Overlay)"),
                    Is.Not.Null);

                tileData.Type = TileType.Empty;
                clearMethod.Invoke(selection, null);

                Assert.That(
                    highlightObject.activeSelf,
                    Is.False,
                    "선택한 건물이 해체되면 별도 고스트는 비활성 상태여야 한다.");
                Assert.That(
                    structureRenderer.sharedMaterial,
                    Is.SameAs(originalMaterial),
                    "해체 시 실제 구조물 선택 표시도 정리되어야 한다.");
                Assert.That(
                    structure.transform.Find(
                        "Cube (Selection Overlay)"),
                    Is.Null,
                    "해체 시 투명 선택 레이어도 제거되어야 한다.");
            }
            finally
            {
                Object.DestroyImmediate(selectionObject);
                Object.DestroyImmediate(highlightObject);
                Object.DestroyImmediate(structure);
            }
        }

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
        public void BuildingDriveway_RotatesLaneTowardRoadAccess()
        {
            var cityObject =
                new GameObject("DrivewayOrientationCityView");
            MainCityView cityView =
                cityObject.AddComponent<MainCityView>();
            GameObject preview = null;
            GameObject roadPreview = null;

            try
            {
                Assert.That(
                    cityView.TryCreatePlacementPreview(
                        TileType.House,
                        out preview),
                    Is.True);
                Transform driveway =
                    preview.transform.Find("Driveway_0");
                Assert.That(
                    driveway,
                    Is.Not.Null,
                    "복사한 SimpleTown 주차장 프리팹이 건물 미리보기에 포함되어야 한다.");
                Assert.That(
                    Mathf.DeltaAngle(
                        driveway.localEulerAngles.z,
                        90f),
                    Is.EqualTo(0f).Within(0.01f),
                    "주차장 차선은 건물 출입 방향과 나란해지도록 평면상 90도 회전해야 한다.");

                Transform body =
                    preview.transform.Find("BuildingBody");
                Transform foundation =
                    preview.transform.Find(
                        "BuildingFoundation");
                Transform buildingLot =
                    preview.transform.Find("BuildingLot");
                Assert.That(body, Is.Not.Null);
                Assert.That(
                    foundation,
                    Is.Not.Null,
                    "SimpleTown 보도 에셋 기반 건물 바닥이 포함되어야 한다.");
                Assert.That(
                    buildingLot,
                    Is.Null,
                    "건물 아래에 별도 바닥판을 생성하지 않아야 한다.");

                Renderer[] bodyRenderers =
                    body.GetComponentsInChildren<Renderer>(true);
                Assert.That(bodyRenderers, Is.Not.Empty);
                float bodyBaseZ = float.NegativeInfinity;
                for (int index = 0;
                     index < bodyRenderers.Length;
                     index++)
                {
                    bodyBaseZ = Mathf.Max(
                        bodyBaseZ,
                        bodyRenderers[index].bounds.max.z);
                }

                Renderer[] foundationRenderers =
                    foundation.GetComponentsInChildren<
                        Renderer>(true);
                Assert.That(
                    foundationRenderers,
                    Is.Not.Empty);
                Bounds foundationBounds =
                    foundationRenderers[0].bounds;
                float foundationTopZ =
                    float.PositiveInfinity;
                for (int index = 0;
                     index < foundationRenderers.Length;
                     index++)
                {
                    foundationBounds.Encapsulate(
                        foundationRenderers[index].bounds);
                    foundationTopZ = Mathf.Min(
                        foundationTopZ,
                        foundationRenderers[index]
                            .bounds.min.z);
                }
                Assert.That(
                    foundationBounds.size.x,
                    Is.EqualTo(1f).Within(0.01f),
                    "거주지 건물 바닥은 1그리드 폭을 경계선 중심까지 정확히 채워야 한다.");
                Assert.That(
                    foundationBounds.size.y,
                    Is.EqualTo(1f).Within(0.01f),
                    "건물 바닥은 건물 본체가 놓이는 1그리드 깊이를 경계선 중심까지 정확히 채워야 한다.");
                Assert.That(
                    bodyBaseZ,
                    Is.EqualTo(foundationTopZ)
                        .Within(0.0001f),
                    "건물 밑면은 바닥 프리팹 윗면에 놓여야 한다.");

                Renderer drivewayRenderer =
                    driveway.GetComponentInChildren<Renderer>();
                Assert.That(drivewayRenderer, Is.Not.Null);
                Assert.That(
                    drivewayRenderer.bounds.min.z,
                    Is.EqualTo(foundationTopZ)
                        .Within(0.0001f),
                    "주차장과 건물 바닥의 윗면 높이가 같아야 한다.");

                Assert.That(
                    cityView.TryCreatePlacementPreview(
                        TileType.Road,
                        out roadPreview),
                    Is.True);
                roadPreview.transform.position =
                    cityView
                        .GetPlacementPreviewWorldPosition(
                            Vector2Int.zero,
                            TileType.Road);
                Renderer roadRenderer =
                    roadPreview.transform
                        .Find("RoadModel")
                        ?.GetComponentInChildren<Renderer>();
                Assert.That(roadRenderer, Is.Not.Null);
                Assert.That(
                    roadRenderer.bounds.min.z,
                    Is.EqualTo(
                            drivewayRenderer.bounds.min.z)
                        .Within(0.0001f),
                    "도로와 주차장의 윗면 높이가 같아야 한다.");
                Assert.That(
                    drivewayRenderer.bounds.size.x,
                    Is.EqualTo(1f).Within(0.01f),
                    "주거지 주차장 하나는 그리드 한 칸 폭과 정확히 같아야 한다.");
                Assert.That(
                    drivewayRenderer.bounds.size.y,
                    Is.EqualTo(1f).Within(0.01f),
                    "주거지 주차장 하나는 그리드 한 칸 깊이와 정확히 같아야 한다.");

                Transform secondDriveway =
                    preview.transform.Find("Driveway_1");
                Assert.That(
                    secondDriveway,
                    Is.Null,
                    "주거지는 주차장 프리팹 하나만 사용해야 한다.");
                Assert.That(
                    drivewayRenderer.bounds.center.x,
                    Is.EqualTo(0f).Within(0.01f),
                    "주거지 주차장은 건물 전면 중앙에 정렬되어야 한다.");

                Transform drivewayBoundary =
                    preview.transform.Find(
                        "DrivewayBoundary_1");
                Assert.That(
                    drivewayBoundary,
                    Is.Null,
                    "주차장 프리팹이 하나면 프리팹 사이 경계선을 만들지 않아야 한다.");

                Transform firstSlot =
                    preview.transform.Find("ParkingSlot_0");
                Transform secondSlot =
                    preview.transform.Find("ParkingSlot_1");
                Assert.That(firstSlot, Is.Not.Null);
                Assert.That(secondSlot, Is.Not.Null);
                Assert.That(
                    firstSlot.localPosition.x,
                    Is.EqualTo(0.25f).Within(0.0001f),
                    "첫 차량은 한 칸 주차장의 오른쪽 슬롯을 사용해야 한다.");
                Assert.That(
                    secondSlot.localPosition.x,
                    Is.EqualTo(-0.25f).Within(0.0001f),
                    "두 번째 차량은 한 칸 주차장의 왼쪽 슬롯을 사용해야 한다.");
            }
            finally
            {
                Object.DestroyImmediate(roadPreview);
                Object.DestroyImmediate(preview);
                Object.DestroyImmediate(cityObject);
            }
        }

        [Test]
        public void OfficeParking_UsesThreeDrivewaysAcrossTwoGridCells()
        {
            var cityObject =
                new GameObject("OfficeParkingLayoutCityView");
            MainCityView cityView =
                cityObject.AddComponent<MainCityView>();
            GameObject preview = null;

            try
            {
                Assert.That(
                    cityView.TryCreatePlacementPreview(
                        TileType.Office,
                        out preview),
                    Is.True);

                Bounds combinedBounds = default;
                for (int index = 0; index < 3; index++)
                {
                    Transform driveway =
                        preview.transform.Find(
                            $"Driveway_{index}");
                    Assert.That(
                        driveway,
                        Is.Not.Null,
                        "차량 여섯 대인 회사에는 2칸짜리 주차장 프리팹 세 개가 필요하다.");

                    Renderer renderer =
                        driveway.GetComponentInChildren<Renderer>();
                    Assert.That(renderer, Is.Not.Null);
                    Assert.That(
                        renderer.bounds.size.x,
                        Is.EqualTo(2f / 3f)
                            .Within(0.01f),
                        "회사 주차장 각 프리팹 폭은 2그리드 폭의 1/3이어야 한다.");
                    Assert.That(
                        renderer.bounds.size.y,
                        Is.EqualTo(1f).Within(0.01f),
                        "회사 주차장 깊이는 그리드 한 칸과 정확히 같아야 한다.");

                    if (index == 0)
                    {
                        combinedBounds = renderer.bounds;
                    }
                    else
                    {
                        combinedBounds.Encapsulate(
                            renderer.bounds);
                    }
                }

                Assert.That(
                    preview.transform.Find("Driveway_3"),
                    Is.Null);
                Assert.That(
                    combinedBounds.size.x,
                    Is.EqualTo(2f).Within(0.01f),
                    "세 프리팹을 합친 회사 주차장 폭은 정확히 2그리드여야 한다.");
                Assert.That(
                    combinedBounds.size.y,
                    Is.EqualTo(1f).Within(0.01f),
                    "회사 주차장 깊이는 정확히 1그리드여야 한다.");
                Assert.That(
                    combinedBounds.center.x,
                    Is.EqualTo(0f).Within(0.01f),
                    "회사 주차장 세 개는 건물 전면 중앙에 정렬되어야 한다.");
                Assert.That(
                    combinedBounds.center.y,
                    Is.EqualTo(-0.5f).Within(0.01f),
                    "회사 주차장은 건물 바로 앞 1그리드 영역에 배치되어야 한다.");

                Transform firstBoundary =
                    preview.transform.Find(
                        "DrivewayBoundary_1");
                Transform secondBoundary =
                    preview.transform.Find(
                        "DrivewayBoundary_2");
                Assert.That(firstBoundary, Is.Not.Null);
                Assert.That(secondBoundary, Is.Not.Null);
                Assert.That(
                    firstBoundary.localPosition.x,
                    Is.EqualTo(1f / 3f)
                        .Within(0.0001f));
                Assert.That(
                    secondBoundary.localPosition.x,
                    Is.EqualTo(-1f / 3f)
                        .Within(0.0001f));
                Assert.That(
                    firstBoundary.localScale.x,
                    Is.EqualTo(0.015f)
                        .Within(0.0001f),
                    "회사 프리팹 경계선 폭은 내부 선과 같은 비율로 축소되어야 한다.");
            }
            finally
            {
                Object.DestroyImmediate(preview);
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
                Assert.That(
                    preview.transform.Find(
                        "Signal_0_0/Selection"),
                    Is.Null,
                    "신호등 아래에 흰색 선택판을 생성하지 않아야 한다.");
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
        public void RightClick_DuringInfrastructurePlacementMode_KeepsPlacementActive()
        {
            var go = new GameObject("Coordinator");
            var coordinator = go.AddComponent<InfrastructurePlacementCoordinator>();
            var data = ScriptableObject.CreateInstance<InfrastructureDataSO>();
            data.Kind = InfrastructureKind.Signal;
            coordinator.IsBuildMenuOpen = () => true;

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

                var isPlacingField = typeof(InfrastructurePlacementCoordinator).GetField("_isBuildingMode", BindingFlags.NonPublic | BindingFlags.Instance);
                bool isPlacing = (bool)isPlacingField.GetValue(coordinator);

                Assert.IsTrue(
                    isPlacing,
                    "Right click demolition must keep the infrastructure preview active.");
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

        private sealed class EastFacingHouseTileData : IReadOnlyTileData
        {
            public TileType Type { get; set; } = TileType.House;

            public CongestionLevel GetCongestion(Vector2Int tile) =>
                CongestionLevel.Free;
            public float GetDensity01(Vector2Int tile) => 0f;
            public int GetQueueCount(Vector2Int tile, Dir entryDir) => 0;
            public TileType GetTileType(Vector2Int tile) => Type;
            public PlacementDirection GetDirection(Vector2Int tile) =>
                PlacementDirection.East;
            public Vector2Int GetFootprintSize(TileType type) =>
                TileFootprint.GetSize(type);
            public bool TryGetFootprintAnchor(
                Vector2Int tile,
                out Vector2Int anchor)
            {
                anchor = tile;
                return true;
            }
            public bool IsFootprintAnchor(Vector2Int tile) => true;
            public bool TryGetConstructionProgress01(
                Vector2Int tile,
                out float progress01)
            {
                progress01 = 0f;
                return false;
            }
            public bool TryGetConstructionTargetType(
                Vector2Int tile,
                out TileType targetType)
            {
                targetType = TileType.Empty;
                return false;
            }
        }

        private sealed class ParkingCursorTileData : IReadOnlyTileData
        {
            private readonly Vector2Int roadTile;

            public ParkingCursorTileData(Vector2Int roadTile)
            {
                this.roadTile = roadTile;
            }

            public CongestionLevel GetCongestion(Vector2Int tile) =>
                CongestionLevel.Free;
            public float GetDensity01(Vector2Int tile) => 0f;
            public int GetQueueCount(Vector2Int tile, Dir entryDir) => 0;
            public TileType GetTileType(Vector2Int tile) =>
                tile == roadTile ? TileType.Road : TileType.Empty;
            public PlacementDirection GetDirection(Vector2Int tile) =>
                PlacementDirection.North;
            public Vector2Int GetFootprintSize(TileType type) =>
                TileFootprint.GetSize(type);
            public bool TryGetFootprintAnchor(
                Vector2Int tile,
                out Vector2Int anchor)
            {
                anchor = tile;
                return false;
            }
            public bool IsFootprintAnchor(Vector2Int tile) => false;
            public bool TryGetConstructionProgress01(
                Vector2Int tile,
                out float progress01)
            {
                progress01 = 0f;
                return false;
            }
            public bool TryGetConstructionTargetType(
                Vector2Int tile,
                out TileType targetType)
            {
                targetType = TileType.Empty;
                return false;
            }
        }

        private sealed class FootprintAnchorTileData : IReadOnlyTileData
        {
            private readonly Vector2Int anchor;

            public FootprintAnchorTileData(Vector2Int anchor)
            {
                this.anchor = anchor;
            }

            public CongestionLevel GetCongestion(Vector2Int tile) =>
                CongestionLevel.Free;
            public float GetDensity01(Vector2Int tile) => 0f;
            public int GetQueueCount(Vector2Int tile, Dir entryDir) => 0;
            public TileType GetTileType(Vector2Int tile) => TileType.House;
            public PlacementDirection GetDirection(Vector2Int tile) =>
                PlacementDirection.North;
            public Vector2Int GetFootprintSize(TileType type) =>
                TileFootprint.GetSize(type);
            public bool TryGetFootprintAnchor(
                Vector2Int tile,
                out Vector2Int footprintAnchor)
            {
                footprintAnchor = anchor;
                return true;
            }
            public bool IsFootprintAnchor(Vector2Int tile) => tile == anchor;
            public bool TryGetConstructionProgress01(
                Vector2Int tile,
                out float progress01)
            {
                progress01 = 0f;
                return false;
            }
            public bool TryGetConstructionTargetType(
                Vector2Int tile,
                out TileType targetType)
            {
                targetType = TileType.Empty;
                return false;
            }
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

        [Test]
        public void HandlePlace_RoadPlacement_KeepsPlacementModeOnSuccess()
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
                SetPrivateField(tileData, "buildCost", 0);
                SetPrivateField(tileData, "category", TileType.Road);
                SetPrivateField(
                    controller,
                    "availableTiles",
                    new[] { tileData });
                controller.SetFakeMode(false);

                controller.Initialize(services);
                controller.ToggleBuildMode(true);
                controller.SetBuildType(TileType.Road);

                MethodInfo updateMethod = typeof(PlacementController).GetMethod(
                    "HandlePlace",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                updateMethod.Invoke(
                    controller,
                    new object[] { new Vector2Int(0, 0) });

                Assert.IsTrue(
                    controller.IsBuildingMode,
                    "Road placement must remain selected for continuous building.");
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
