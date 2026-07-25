using System;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using CityFlow.UI.Controllers;
using CityFlow.UI.Controllers.Placement;

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
        [Tooltip("도로 예산제(스펙 2026-07-17): 도로 배치 모드에서 \"도로 N/M\" 카운터. 미할당 시 표시 생략.")]
        [SerializeField] private TextMeshProUGUI roadBudgetText;
        [Tooltip("도로 확장권(스펙 §2단계): \"+10칸\" 구매 버튼. 미할당 시 표시 생략.")]
        [SerializeField] private UnityEngine.UI.Button roadExpandButton;
        [Tooltip("확장권 가격 라벨. 코인 부족 시 빨강. 미할당 시 표시 생략.")]
        [SerializeField] private TextMeshProUGUI roadExpandCostText;
        [SerializeField] private Color expandAffordableColor = Color.white;
        [SerializeField] private Color expandUnaffordableColor = new Color(1f, 0.35f, 0.35f);

        [Header("Config")]
        [SerializeField] private CityFlow.Content.PopulationConfigSO populationConfig;
        [SerializeField] private CityFlow.Content.BuildingDefinitionSO hospitalDefinition;

        public bool IsBuildingMode => _isBuildingMode;
        public CityFlow.Content.PopulationConfigSO PopulationConfig => populationConfig;
        public CityFlow.Content.BuildingDefinitionSO HospitalDefinition => hospitalDefinition;

        private CityFlowServices _services;
        private bool _isBuildingMode = false;
        private TileType _currentType = TileType.Road; 
        private PlacementDirection _currentDirection = PlacementDirection.North;

        private PlacementInputHandler _inputHandler;
        private PlacementVisualManager _visualManager;
        private PlacementCostLabelManager _costLabelManager;
        private PlacementBudgetUI _budgetUI;
        private PlacementActionDispatcher _actionDispatcher;

        private void Awake()
        {
            var uiRaycastBlocker = new UIRaycastBlocker();
            
            _inputHandler = new PlacementInputHandler(uiRaycastBlocker, confirmPopup);
            
            _visualManager = new PlacementVisualManager(
                ghostRenderer, colorValid, colorInvalid,
                use3DGhostVolume, ghostVolumeHeight, volumeValidColor, volumeInvalidColor,
                GetComponent<BenefitHighlightRenderer>(),
                populationConfig, hospitalDefinition);
                
            _costLabelManager = new PlacementCostLabelManager(showCostLabel, costAffordableColor, costUnaffordableColor);
            
            _budgetUI = new PlacementBudgetUI(
                roadBudgetText, roadExpandButton, roadExpandCostText, 
                expandAffordableColor, expandUnaffordableColor);
                
            _actionDispatcher = new PlacementActionDispatcher(availableTiles, useFakeMode);

            _inputHandler.OnRotateRequested += HandleRotate;
            _inputHandler.OnDemolishRequested += HandleDemolish;
            _inputHandler.OnPlaceRequested += HandlePlace;
            _inputHandler.OnDragPlaceRequested += HandleDragPlace;
        }

        public void Initialize(CityFlowServices services)
        {
            _services = services;
            _visualManager.Initialize();
            _costLabelManager.Initialize();
            
            _budgetUI.Initialize(() => _actionDispatcher.HandleRoadExpandClicked(_services));
        }

        public void SetBuildType(TileType type)
        {
            _currentType = type;
            _currentDirection = PlacementDirection.North;
            
            _costLabelManager.ResetState();
            _visualManager.HideBenefitHighlights();
            _visualManager.UpdateGhostSprite(_currentType, availableTiles);
            _visualManager.UpdateGhostFootprint(_currentType, _currentDirection);

            var infraCoord = UnityEngine.Object.FindAnyObjectByType<InfrastructurePlacementCoordinator>();
            if (infraCoord != null && infraCoord.IsBuildingMode)
            {
                infraCoord.CancelPlacement();
            }

            enabled = true;
            ToggleBuildMode(true);
        }

        public void ToggleBuildMode(bool isOn)
        {
            _isBuildingMode = isOn;
            
            if (isOn)
            {
                _visualManager.HideBenefitHighlights();
                _visualManager.UpdateGhostSprite(_currentType, availableTiles);
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

        public Vector2Int GetMouseGridCoordinate(bool useXYPlane = false)
        {
            return _inputHandler?.GetMouseGridCoordinate(useXYPlane) ?? default;
        }

        public void SetGhostFootprint(TileType currentType, PlacementDirection direction)
        {
            _visualManager?.UpdateGhostFootprint(currentType, direction);
        }

        public void SetGhostFootprint(Vector2Int size)
        {
            _visualManager?.SetGhostFootprint(size);
        }

        public Color GetVisibleGhostColor(bool canPlace)
        {
            return canPlace ? colorValid : colorInvalid;
        }

        public Color GetVisibleGhostColor(Color baseColor)
        {
            baseColor.a = Mathf.Max(baseColor.a, 0.75f);
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
            _budgetUI.UpdateUI(_isBuildingMode, _currentType, _services);
            
            Vector2Int gridCoord = _inputHandler.GetMouseGridCoordinate(useXYPlane);
            bool canPlace = _actionDispatcher.CheckCanPlace(gridCoord, _currentType, _currentDirection, _services);
            bool isBuildingType = TileFootprint.IsBuilding(_currentType);

            _inputHandler.UpdateInput(_isBuildingMode, isBuildingType, canPlace, gridCoord);

            if (!_isBuildingMode || _inputHandler.IsPointerOverBlockingUI())
            {
                _visualManager.SetGhostActive(false);
                _costLabelManager.SetCostLabelActive(false);
                _visualManager.HideBenefitHighlights();
                return;
            }

            _visualManager.SetGhostActive(true);
            
            Vector2Int rotatedSize = TileFootprint.GetRotatedSize(_currentType, _currentDirection);
            float surfaceZ = GetSurfaceMarkerZ(gridCoord);
            
            Vector3 ghostPos = GetGhostPosition(gridCoord, rotatedSize, surfaceZ);
            
            _visualManager.SyncGhostPosition(ghostPos, TileFootprint.ToAngle(_currentDirection), useXYPlane);
            _visualManager.UpdateColors(canPlace);
            _visualManager.UpdateBenefitPreview(gridCoord, _currentType, useXYPlane, _services);

            _costLabelManager.SyncPosition(ghostPos, surfaceZ, useXYPlane);
            
            long cost = _actionDispatcher.GetTileCost(_currentType);
            bool affordable = _services?.Economy == null || _services.Economy.Coins >= cost;
            _costLabelManager.UpdateCost(cost, affordable, canPlace, Time.deltaTime);
        }

        private void HandleRotate()
        {
            _currentDirection = TileFootprint.RotateClockwise(_currentDirection);
            _visualManager.UpdateGhostFootprint(_currentType, _currentDirection);
            _costLabelManager.ResetState();
        }

        private void HandleDemolish(Vector2Int coord)
        {
            _actionDispatcher.TryDemolishAt(coord, _services);
        }

        private void HandlePlace(Vector2Int coord)
        {
            _actionDispatcher.PlaceInfrastructure(coord, _currentType, _currentDirection, _services);
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
            if (_actionDispatcher.CheckCanPlace(coord, _currentType, _currentDirection, _services))
            {
                _actionDispatcher.PlaceInfrastructure(coord, _currentType, _currentDirection, _services);
            }
        }
        
        public bool TryDemolishAt(Vector2Int coord)
        {
            return _actionDispatcher.TryDemolishAt(coord, _services);
        }

        public float GetSurfaceMarkerZ(Vector2Int gridCoord)
        {
            if (_services != null && _services.TileData != null
                && _services.TileData.GetTileType(gridCoord) == TileType.Empty)
            {
                return 0.12f; // EmptyGroundMarkerZ
            }
            return -0.05f; // RoadSurfaceMarkerZ
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

            return useXYPlane
                ? new Vector3(gridCoord.x + 0.5f + offsetX, gridCoord.y + 0.5f + offsetY, surfaceZ)
                : new Vector3(gridCoord.x + offsetX, 0, gridCoord.y + offsetY);
        }
    }
}
