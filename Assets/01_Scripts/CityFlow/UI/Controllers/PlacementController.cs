using System;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using CityFlow.UI.Controllers;
using CityFlow.UI.Controllers.Placement;
using CityFlow.View;

namespace CityFlow.UI
{
    [RequireComponent(typeof(BenefitHighlightRenderer))]
    public class PlacementController : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("Ghost Settings")]
        [Tooltip("마우스를 따라다닐 잔상(고스트) 프리팹 또는 스프라이트")]
        [SerializeField] private SpriteRenderer ghostRenderer;
        [SerializeField] private Color colorValid = new Color(0f, 1f, 0f, 0.5f);
        [SerializeField] private Color colorInvalid = new Color(1f, 0f, 0f, 0.5f);

        [Header("3D Ghost Volume")]
        [Tooltip("에셋 없이 건물 공간을 입체적으로 보여주는 3D 반투명 박스")]
        [SerializeField] private bool use3DGhostVolume = true;
        [SerializeField] private float ghostVolumeHeight = 1.0f;
        [SerializeField] private Color volumeValidColor = new Color(0f, 1f, 0f, 0.18f);
        [SerializeField] private Color volumeInvalidColor = new Color(1f, 0f, 0f, 0.18f);

        [Header("Cost Display")]
        [Tooltip("건설 비용을 고스트 위에 표시합니다")]
        [SerializeField] private bool showCostLabel = true;
        [SerializeField] private Color costAffordableColor = Color.white;
        [SerializeField] private Color costUnaffordableColor = new Color(1f, 0.35f, 0.35f);

        [Header("Debug / Testing")]
        [Tooltip("월~화 코어엔진 미연동 시 UI 단독 테스트를 위한 강제 성공 모드")]
        [SerializeField] private bool useFakeMode = false;
        [SerializeField] private bool useXYPlane = false;

        [Header("Economy Data")]
        [Tooltip("비용(Cost)을 조회하기 위한 타일 데이터 모음")]
        [SerializeField] private CityFlow.Configs.TileDataSO[] availableTiles;

        [Header("UI References")]
        [SerializeField] private ConfirmPopupController confirmPopup;

        [Header("Config")]
        [SerializeField] private CityFlow.Content.PopulationConfigSO populationConfig;
        [SerializeField] private CityFlow.Content.BuildingDefinitionSO hospitalDefinition;

        public bool IsBuildingMode => _isBuildingMode;
        public CityFlow.Content.PopulationConfigSO PopulationConfig => populationConfig;
        public CityFlow.Content.BuildingDefinitionSO HospitalDefinition => hospitalDefinition;

        private CityFlowServices _services;
        private bool _isBuildingMode = false;
        private TileType _currentType = TileType.Road;
        private string _currentSpecialBuildingId = string.Empty;
        private string _currentCompanyTypeId = string.Empty;
        private PlacementDirection _currentDirection = PlacementDirection.North;

        private PlacementInputHandler _inputHandler;
        private PlacementVisualManager _visualManager;
        private PlacementCostLabelManager _costLabelManager;

        private PlacementActionDispatcher _actionDispatcher;

        private bool _managersInitialized = false;

        private void Awake()
        {
            EnsureManagers();
        }

        private void EnsureManagers()
        {
            if (_managersInitialized) return;

            var uiRaycastBlocker = new UIRaycastBlocker();

            _inputHandler = new PlacementInputHandler(uiRaycastBlocker, confirmPopup);

            _visualManager = new PlacementVisualManager(
                ghostRenderer, colorValid, colorInvalid,
                use3DGhostVolume, ghostVolumeHeight, volumeValidColor, volumeInvalidColor,
                GetComponent<BenefitHighlightRenderer>(),
                populationConfig, hospitalDefinition);

            _costLabelManager = new PlacementCostLabelManager(showCostLabel, costAffordableColor, costUnaffordableColor);


            _actionDispatcher = new PlacementActionDispatcher(availableTiles, useFakeMode);

            _inputHandler.OnRotateRequested += HandleRotate;
            _inputHandler.OnDemolishRequested += HandleDemolish;
            _inputHandler.OnPlaceRequested += HandlePlace;
            _inputHandler.OnDragPlaceRequested += HandleDragPlace;

            _managersInitialized = true;
        }

        public void Initialize(CityFlowServices services)
        {
            _services = services;
            EnsureManagers();
            _visualManager.Initialize();
            _costLabelManager.Initialize();
            UpdateBuildingModelPreview();

        }

        public void SetBuildType(TileType type)
        {
            _currentType = type;
            _currentSpecialBuildingId = string.Empty;
            _currentCompanyTypeId = string.Empty;
            _currentDirection = PlacementDirection.North;

            _costLabelManager.ResetState();
            _visualManager.HideBenefitHighlights();
            _visualManager.UpdateGhostSprite(_currentType, availableTiles);
            _visualManager.UpdateGhostFootprint(_currentType, _currentDirection);
            UpdateBuildingModelPreview();

            var infraCoord = UnityEngine.Object.FindAnyObjectByType<InfrastructurePlacementCoordinator>();
            if (infraCoord != null && infraCoord.IsBuildingMode)
            {
                infraCoord.CancelPlacement();
            }

            enabled = true;
            ToggleBuildMode(true);
        }

        public void SetBuildType(CityFlow.Configs.TileDataSO tileData)
        {
            SetBuildType(tileData != null
                ? tileData.Category
                : TileType.Road);
            _currentCompanyTypeId =
                tileData?.CompanyTypeId?.Trim() ?? string.Empty;
        }

        public bool SetSpecialBuilding(string buildingId)
        {
            string normalizedId = buildingId?.Trim() ?? string.Empty;
            _currentType = TileType.SpecialBuilding;
            _currentSpecialBuildingId = normalizedId;
            _currentCompanyTypeId = string.Empty;
            _currentDirection = PlacementDirection.North;
            _visualManager.SetBuildingPreview(null);

            if (normalizedId.Length == 0 ||
                _services?.SpecialBuildings == null ||
                !_services.SpecialBuildings.IsBuildingUnlocked(normalizedId))
            {
                ToggleBuildMode(false);
                Debug.LogWarning(
                    "[PlacementController] The selected special building " +
                    "is invalid or locked. The previous build selection was cancelled.",
                    this);
                return false;
            }

            _costLabelManager.ResetState();
            _visualManager.HideBenefitHighlights();
            UpdateGhostSprite();
            _visualManager.UpdateGhostFootprint(
                _currentType,
                _currentDirection);
            UpdateBuildingModelPreview();

            var infraCoord = UnityEngine.Object.FindAnyObjectByType<
                InfrastructurePlacementCoordinator>();
            if (infraCoord != null && infraCoord.IsBuildingMode)
            {
                infraCoord.CancelPlacement();
            }

            enabled = true;
            ToggleBuildMode(true);
            return true;
        }

        public void ToggleBuildMode(bool isOn)
        {
            _isBuildingMode = isOn;

            if (isOn)
            {
                _visualManager.HideBenefitHighlights();
                UpdateGhostSprite();
                _visualManager.UpdateGhostFootprint(_currentType, _currentDirection);
            }

            _visualManager.SetGhostActive(isOn);
            BuildModeCursorFeedback.SetBuilding(this, isOn);

            if (!isOn)
            {
                _visualManager.HideBenefitHighlights();
                _costLabelManager.SetCostLabelActive(false);
            }
        }

        public void ConfigureGhost(SpriteRenderer renderer)
        {
            EnsureManagers();
            ghostRenderer = renderer;

            // 기존 매니저의 동적 리소스(3D 큐브 등) 해제 (메모리 누수 방지)
            _visualManager?.Cleanup();

            // Recreate visual manager with new ghost renderer
            _visualManager = new PlacementVisualManager(
                ghostRenderer, colorValid, colorInvalid,
                use3DGhostVolume, ghostVolumeHeight, volumeValidColor, volumeInvalidColor,
                GetComponent<BenefitHighlightRenderer>(),
                populationConfig, hospitalDefinition);

            _visualManager.Initialize();
            _visualManager.SetGhostActive(_isBuildingMode);
            UpdateBuildingModelPreview();
        }

        public void SetFakeMode(bool isOn)
        {
            useFakeMode = isOn;
            _actionDispatcher = new PlacementActionDispatcher(availableTiles, useFakeMode);
        }

        public void SetUseXYPlane(bool isOn)
        {
            useXYPlane = isOn;
        }

        public Vector2Int GetMouseGridCoordinate()
        {
            return _inputHandler?.GetMouseGridCoordinate(
                useXYPlane,
                _services?.WorldCoordinates) ?? default;
        }

        public void SetGhostFootprint(Vector2Int size)
        {
            _visualManager?.SetGhostFootprint(size);
        }

        public Color GetVisibleGhostColor(Color baseColor)
        {
            baseColor.a = Mathf.Max(baseColor.a, PlacementVisualManager.MinimumGhostAlpha);
            return baseColor;
        }

        private void OnDisable()
        {
            BuildModeCursorFeedback.SetBuilding(this, false);
            _visualManager?.HideBenefitHighlights();
        }

        private void OnDestroy()
        {
            _visualManager?.Cleanup();
            _costLabelManager?.Cleanup();
        }

        private void Update()
        {

            IWorldCoordinateSpace coordinateSpace =
                _services?.WorldCoordinates;
            Vector2Int gridCoord = _inputHandler.GetMouseGridCoordinate(
                useXYPlane,
                coordinateSpace);
            bool canPlace = _actionDispatcher.CheckCanPlace(
                gridCoord,
                _currentType,
                _currentDirection,
                _services,
                _currentSpecialBuildingId);
            bool isBuildingType = TileFootprint.IsBuilding(_currentType);

            _inputHandler.UpdateGlobalInput(_isBuildingMode, isBuildingType, gridCoord);

            if (_inputHandler.IsPointerOverBlockingUI())
            {
                _inputHandler.ResetPlacementDragState();
                _visualManager.SetGhostActive(false);
                _costLabelManager.SetCostLabelActive(false);
                _visualManager.HideBenefitHighlights();
                return;
            }

            if (!_isBuildingMode)
            {
                _inputHandler.ResetPlacementDragState();
                _visualManager.SetGhostActive(false);
                _costLabelManager.SetCostLabelActive(false);
                _visualManager.HideBenefitHighlights();
                return;
            }

            _inputHandler.UpdatePlacementInput(canPlace, gridCoord);

            _visualManager.SetGhostActive(true);

            Vector2Int rotatedSize = TileFootprint.GetRotatedSize(_currentType, _currentDirection);
            float surfaceZ = GetSurfaceMarkerZ(gridCoord);

            Vector3 ghostPos = GetGhostPosition(gridCoord, rotatedSize, surfaceZ);
            Vector3 buildingPreviewPos =
                GetBuildingPreviewPosition(
                    gridCoord,
                    rotatedSize);

            _visualManager.SyncGhostPosition(
                ghostPos,
                TileFootprint.ToAngle(_currentDirection),
                useXYPlane,
                coordinateSpace,
                buildingPreviewPos);
            _visualManager.UpdateColors(canPlace);
            _visualManager.UpdateBenefitPreview(gridCoord, _currentType, useXYPlane, _services);

            _costLabelManager.SyncPosition(
                ghostPos,
                surfaceZ,
                useXYPlane,
                coordinateSpace);

            long cost = _actionDispatcher.GetTileCost(
                _currentType,
                _currentSpecialBuildingId,
                _services);
            bool affordable = _services?.Economy == null || _services.Economy.Coins >= cost;
            _costLabelManager.UpdateCost(cost, affordable, canPlace, Time.deltaTime);
        }

        private void HandleRotate()
        {
            if (!TileFootprint.IsBuilding(_currentType))
            {
                return;
            }

            _currentDirection = TileFootprint.RotateClockwise(_currentDirection);
            _visualManager.UpdateGhostFootprint(_currentType, _currentDirection);
            _visualManager.SyncPlacementRotation(
                TileFootprint.ToAngle(_currentDirection),
                useXYPlane,
                _services?.WorldCoordinates);
            _costLabelManager.ResetState();
        }

        private bool HandleDemolish(Vector2Int coord)
        {
            return _actionDispatcher.TryDemolishAt(coord, _services);
        }

        private void HandlePlace(Vector2Int coord)
        {
            _actionDispatcher.PlaceInfrastructure(
                coord,
                _currentType,
                _currentDirection,
                _services,
                _currentSpecialBuildingId,
                _currentCompanyTypeId);
        }

        private void HandleDragPlace(Vector2Int from, Vector2Int to)
        {
            Vector2Int cursor = from;

            while (cursor.x != to.x)
            {
                cursor.x += Math.Sign(to.x - cursor.x);
                TryPlaceDragTile(cursor);
            }

            while (cursor.y != to.y)
            {
                cursor.y += Math.Sign(to.y - cursor.y);
                TryPlaceDragTile(cursor);
            }
        }

        private void TryPlaceDragTile(Vector2Int coord)
        {
            if (_actionDispatcher.CheckCanPlace(
                    coord,
                    _currentType,
                    _currentDirection,
                    _services,
                    _currentSpecialBuildingId))
            {
                _actionDispatcher.PlaceInfrastructure(
                    coord,
                    _currentType,
                    _currentDirection,
                    _services,
                    _currentSpecialBuildingId,
                    _currentCompanyTypeId);
            }
        }

        public bool TryDemolishAt(Vector2Int coord)
        {
            return _actionDispatcher.TryDemolishAt(coord, _services);
        }

        private void UpdateGhostSprite()
        {
            Sprite overrideSprite = null;
            if (_currentType == TileType.SpecialBuilding &&
                _services?.SpecialBuildings != null &&
                _services.SpecialBuildings.TryGetBuildOption(
                    _currentSpecialBuildingId,
                    out SpecialBuildingBuildOption option))
            {
                overrideSprite = option.Icon;
            }

            _visualManager.UpdateGhostSprite(
                _currentType,
                availableTiles,
                overrideSprite);
        }

        private void UpdateBuildingModelPreview()
        {
            if (_visualManager == null ||
                (!TileFootprint.IsBuilding(_currentType) &&
                 _currentType != TileType.Road))
            {
                _visualManager?.SetBuildingPreview(null);
                return;
            }

            GameObject preview = null;
            if (_currentType == TileType.SpecialBuilding)
            {
                SpecialBuildingView specialView =
                    FindAnyObjectByType<SpecialBuildingView>(
                        FindObjectsInactive.Include);
                specialView?.TryCreatePlacementPreview(
                    _currentSpecialBuildingId,
                    out preview);
            }
            else
            {
                MainCityView cityView =
                    FindAnyObjectByType<MainCityView>(
                        FindObjectsInactive.Include);
                cityView?.TryCreatePlacementPreview(
                    _currentType,
                    out preview);
            }

            _visualManager.SetBuildingPreview(preview);
        }

        public const float EmptyGroundMarkerZ = 0.12f;
        public const float RoadSurfaceMarkerZ = -0.05f;

        public float GetSurfaceMarkerZ(Vector2Int gridCoord)
        {
            if (_services != null && _services.TileData != null
                && _services.TileData.GetTileType(gridCoord) == TileType.Empty)
            {
                return EmptyGroundMarkerZ;
            }
            return RoadSurfaceMarkerZ;
        }

        public Vector3 GetGhostPosition(Vector2Int gridCoord, Vector2Int footprintSize)
        {
            return GetGhostPosition(gridCoord, footprintSize, GetSurfaceMarkerZ(gridCoord));
        }

        public Vector3 GetGhostPosition(Vector2Int gridCoord, Vector2Int footprintSize, float surfaceZ)
        {
            Vector2Int size = new Vector2Int(
                Mathf.Max(1, footprintSize.x),
                Mathf.Max(1, footprintSize.y)
            );
            float offsetX = (size.x - 1) * 0.5f;
            float offsetY = (size.y - 1) * 0.5f;

            IWorldCoordinateSpace coordinateSpace =
                _services?.WorldCoordinates;
            if (coordinateSpace != null)
            {
                return coordinateSpace.GridPointToWorld(
                    new Vector2(
                        gridCoord.x + 0.5f + offsetX,
                        gridCoord.y + 0.5f + offsetY),
                    -surfaceZ);
            }

            return useXYPlane
                ? new Vector3(gridCoord.x + 0.5f + offsetX, gridCoord.y + 0.5f + offsetY, surfaceZ)
                : new Vector3(gridCoord.x + offsetX, 0, gridCoord.y + offsetY);
        }

        private Vector3 GetBuildingPreviewPosition(
            Vector2Int gridCoord,
            Vector2Int footprintSize)
        {
            Vector2Int size = new Vector2Int(
                Mathf.Max(1, footprintSize.x),
                Mathf.Max(1, footprintSize.y));
            float centerX =
                gridCoord.x + size.x * 0.5f;
            float centerY =
                gridCoord.y + size.y * 0.5f;

            IWorldCoordinateSpace coordinateSpace =
                _services?.WorldCoordinates;
            if (coordinateSpace != null)
            {
                return coordinateSpace.GridPointToWorld(
                    new Vector2(centerX, centerY),
                    0.02f);
            }

            return useXYPlane
                ? new Vector3(centerX, centerY, -0.02f)
                : new Vector3(centerX, 0.02f, centerY);
        }
    }
}
