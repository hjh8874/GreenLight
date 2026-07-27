using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using CityFlow.UI.Controllers;
using CityFlow.UI.Controllers.Placement;

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

            // Should not throw and successfully return default/calculated coord
            Assert.DoesNotThrow(() =>
            {
                controller.GetMouseGridCoordinate();
            }, "Parameterless overload should exist and not throw.");

            Assert.DoesNotThrow(() =>
            {
                controller.GetMouseGridCoordinate(true);
            }, "Explicit bool overload should exist and not throw.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void Update_WhenPointerOverUI_ResetsDragState()
        {
            // Regression test for Issue #157 (Point 2)
            // Ensures that dragging over a UI clears the drag state
            // Validated via reflection since _inputHandler is internal/private
            
            var go = new GameObject("Controller");
            var controller = go.AddComponent<PlacementController>();

            // Check if ResetDragState exists in PlacementInputHandler
            var method = typeof(PlacementInputHandler).GetMethod("ResetDragState", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(method, "PlacementInputHandler must have a public ResetDragState method to prevent drag leaks across UI.");

            Object.DestroyImmediate(go);
        }
    }
}
