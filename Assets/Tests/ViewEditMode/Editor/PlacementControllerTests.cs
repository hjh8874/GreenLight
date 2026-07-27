using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using CityFlow.UI.Controllers;
using CityFlow.UI.Controllers.Placement;
using CityFlow.Sim;
using CityFlow.UI;

namespace Tests.EditMode
{
    public class PlacementControllerTests
    {
        private GameObject _cameraGo;

        [SetUp]
        public void Setup()
        {
            _cameraGo = new GameObject("MainCamera");
            _cameraGo.tag = "MainCamera";
            _cameraGo.AddComponent<Camera>();
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

            var cam = _cameraGo.GetComponent<Camera>();
            cam.transform.position = new Vector3(0, 10, -10);
            cam.transform.LookAt(Vector3.zero);

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
            }
            finally
            {
                blockedProp.SetValue(null, false); // cleanup
                InputSystem.RemoveDevice(mouse);
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Update_WhenNotBuildingMode_DoesNotResetDemolishDragState()
        {
            var go = new GameObject("Controller");
            var controller = go.AddComponent<PlacementController>();

            var handlerField = typeof(PlacementController).GetField("_inputHandler", BindingFlags.NonPublic | BindingFlags.Instance);
            var inputHandler = (PlacementInputHandler)handlerField.GetValue(controller);

            int demolishRequests = 0;
            inputHandler.OnDemolishRequested += (coord) => { demolishRequests++; return true; };

            var updateMethod = typeof(PlacementController).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);

            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                controller.ToggleBuildMode(false);

                // 1. Right click down at pos 100,100
                using (StateEvent.From(mouse, out var eventPtr))
                {
                    mouse.rightButton.WriteValueIntoEvent(1f, eventPtr);
                    mouse.position.WriteValueIntoEvent(new Vector2(100, 100), eventPtr);
                    InputSystem.QueueEvent(eventPtr);
                }
                InputSystem.Update();
                updateMethod.Invoke(controller, null);

                // 2. Move to 200,200
                using (StateEvent.From(mouse, out var eventPtr2))
                {
                    mouse.position.WriteValueIntoEvent(new Vector2(200, 200), eventPtr2);
                    InputSystem.QueueEvent(eventPtr2);
                }
                InputSystem.Update();
                updateMethod.Invoke(controller, null);

                Assert.AreEqual(2, demolishRequests, "Right-click drag demolish should continue across frames even when building mode is off.");
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                Object.DestroyImmediate(go);
            }
        }
    }
}
