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
        public event Action OnPlacementRejected;
        public event Action OnCancelPlacementRequested;

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

        public void ResetPlacementDragState()
        {
            _lastPlacedCoord = null;
        }

        public bool IsPointerOverBlockingUI()
        {
            if (_confirmPopup != null && _confirmPopup.gameObject.activeInHierarchy)
            {
                return true;
            }
            return _uiRaycastBlocker.IsPointerOverBlockingUI();
        }

        public Vector2Int GetMouseGridCoordinate(
            bool useXYPlane,
            IWorldCoordinateSpace coordinateSpace = null)
        {
            Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

            if (Camera.main == null) return Vector2Int.zero;

            Ray ray = Camera.main.ScreenPointToRay(mousePos);
            if (coordinateSpace != null)
            {
                return coordinateSpace.TryRayToGrid(
                    ray,
                    out Vector2Int coordinate,
                    out _)
                    ? coordinate
                    : Vector2Int.zero;
            }

            if (useXYPlane)
            {
                Plane xyPlane = new Plane(Vector3.forward, Vector3.zero);
                if (xyPlane.Raycast(ray, out float xyEnter))
                {
                    return GridUtil.WorldToGrid(ray.GetPoint(xyEnter));
                }
            }

            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (groundPlane.Raycast(ray, out float enter))
            {
                return GridUtil.WorldToGridXZ(ray.GetPoint(enter));
            }

            return GridUtil.WorldToGrid(Camera.main.ScreenToWorldPoint(mousePos));
        }

        public void UpdateGlobalInput(bool isBuildingMode, bool isBuildingType, Vector2Int gridCoord, bool isBuildMenuOpen)
        {
            // R키 회전
            if (isBuildingMode && isBuildingType && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                OnRotateRequested?.Invoke();
            }

            // 우클릭 철거 또는 배치 취소
            if (Mouse.current != null)
            {
                bool rightPressed = Mouse.current.rightButton.isPressed;
                bool rightPressedThisFrame = Mouse.current.rightButton.wasPressedThisFrame;

                if (rightPressedThisFrame && !IsPointerOverBlockingUI())
                {
                    if (isBuildingMode)
                    {
                        OnCancelPlacementRequested?.Invoke();
                        _rightClickStartCoord = null;
                        return; // 배치 취소 시 이 프레임은 종료
                    }
                    else
                    {
                        _rightClickStartCoord = gridCoord; // Use the already resolved one
                    }
                }

                if (rightPressed && !IsPointerOverBlockingUI() && _rightClickStartCoord.HasValue)
                {
                    if (_lastRemovedCoord == null || _lastRemovedCoord.Value != gridCoord)
                    {
                        if (!isBuildMenuOpen)
                        {
                            return; // 건설 탭이 열려있지 않으면 철거 불가
                        }

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

        }

        public void UpdatePlacementInput(bool canPlace, Vector2Int gridCoord)
        {
            if (IsPointerOverBlockingUI()) return;

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
                        else
                        {
                            OnPlacementRejected?.Invoke();
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
