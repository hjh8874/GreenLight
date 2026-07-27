using System;
using UnityEngine;
using UnityEngine.InputSystem;
using CityFlow.Contracts;

namespace CityFlow.UI.Controllers.Placement
{
    internal class PlacementInputHandler
    {
        public event Action OnRotateRequested;
        public event Func<Vector2Int, bool> OnDemolishRequested;
        public event Action<Vector2Int> OnPlaceRequested;
        public event Action<Vector2Int, Vector2Int> OnDragPlaceRequested;

        private readonly UIRaycastBlocker _uiRaycastBlocker;
        private readonly ConfirmPopupController _confirmPopup;

        private Vector2Int? _rightClickStartCoord = null;
        private Vector2Int? _lastRemovedCoord = null;
        private Vector2Int? _lastPlacedCoord = null;

        public PlacementInputHandler(UIRaycastBlocker uiRaycastBlocker, ConfirmPopupController confirmPopup)
        {
            _uiRaycastBlocker = uiRaycastBlocker;
            _confirmPopup = confirmPopup;
        }

        public void ResetAllDragStates()
        {
            ResetPlacementDragState();
            ResetDemolishDragState();
        }

        public void ResetPlacementDragState()
        {
            _lastPlacedCoord = null;
        }

        public void ResetDemolishDragState()
        {
            _lastRemovedCoord = null;
            _rightClickStartCoord = null;
        }

        public bool IsPointerOverBlockingUI()
        {
            if (_confirmPopup != null && _confirmPopup.gameObject.activeInHierarchy)
            {
                return true;
            }
            return _uiRaycastBlocker.IsPointerOverBlockingUI();
        }

        public Vector2Int GetMouseGridCoordinate(bool useXYPlane)
        {
            Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

            if (useXYPlane && Camera.main != null)
            {
                Ray xyRay = Camera.main.ScreenPointToRay(mousePos);
                Plane xyPlane = new Plane(Vector3.forward, Vector3.zero);
                if (xyPlane.Raycast(xyRay, out float xyEnter))
                {
                    return GridUtil.WorldToGrid(xyRay.GetPoint(xyEnter));
                }
            }

            Ray ray = Camera.main.ScreenPointToRay(mousePos);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (groundPlane.Raycast(ray, out float enter))
            {
                return GridUtil.WorldToGridXZ(ray.GetPoint(enter));
            }

            return GridUtil.WorldToGrid(Camera.main.ScreenToWorldPoint(mousePos));
        }

        public void UpdateInput(bool isBuildingMode, bool isBuildingType, bool canPlace, Vector2Int gridCoord)
        {
            // R키 회전
            if (isBuildingMode && isBuildingType && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                OnRotateRequested?.Invoke();
            }

            // 우클릭 철거
            if (Mouse.current != null)
            {
                bool rightPressed = Mouse.current.rightButton.isPressed;
                bool rightPressedThisFrame = Mouse.current.rightButton.wasPressedThisFrame;

                if (rightPressedThisFrame && !IsPointerOverBlockingUI())
                {
                    _rightClickStartCoord = gridCoord; // Use the already resolved one
                }

                if (rightPressed && !IsPointerOverBlockingUI() && _rightClickStartCoord.HasValue)
                {
                    if (_lastRemovedCoord == null || _lastRemovedCoord.Value != gridCoord)
                    {
                        bool success = OnDemolishRequested?.Invoke(gridCoord) ?? false;
                        if (success)
                        {
                            _lastRemovedCoord = gridCoord;
                        }
                    }
                }

                if (Mouse.current.rightButton.wasReleasedThisFrame)
                {
                    _lastRemovedCoord = null;
                    _rightClickStartCoord = null;
                }
            }

            if (!isBuildingMode || IsPointerOverBlockingUI()) return;

            // 좌클릭 건설
            if (Mouse.current != null)
            {
                if (Mouse.current.leftButton.isPressed)
                {
                    if (_lastPlacedCoord == null)
                    {
                        if (canPlace)
                        {
                            OnPlaceRequested?.Invoke(gridCoord);
                        }
                        _lastPlacedCoord = gridCoord;
                    }
                    else if (_lastPlacedCoord.Value != gridCoord)
                    {
                        OnDragPlaceRequested?.Invoke(_lastPlacedCoord.Value, gridCoord);
                        _lastPlacedCoord = gridCoord;
                    }
                }

                if (Mouse.current.leftButton.wasReleasedThisFrame)
                {
                    _lastPlacedCoord = null;
                }
            }
        }
    }
}
