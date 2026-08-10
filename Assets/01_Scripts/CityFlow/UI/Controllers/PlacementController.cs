using System;
using System.Collections.Generic;
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

        [Header("Placement Timing")]
        [Tooltip("Minimum real-time interval between successful road or building placements.")]
        [SerializeField, Min(0f)] private float placementIntervalSeconds = 0.15f;
        [Tooltip("Maximum number of placement requests retained while input is faster than construction.")]
        [SerializeField, Min(1)] private int maximumQueuedPlacements = 3;
        [Tooltip("Queued placement requests older than this real-time duration are discarded.")]
        [SerializeField, Min(0.05f)] private float maximumPlacementBacklogSeconds = 0.35f;

        [Header("Placement Diagnostics")]
        [Tooltip("Logs the caller and stack trace whenever active placement mode is turned off.")]
        [SerializeField] private bool logPlacementModeDiagnostics = true;

        public bool IsBuildingMode => _isBuildingMode;
        public CityFlow.Content.PopulationConfigSO PopulationConfig => populationConfig;
        public CityFlow.Content.BuildingDefinitionSO HospitalDefinition => hospitalDefinition;
        public event Action PlacementSucceeded;
        public event Action<Vector2Int, TileType> PlacementConfirmed;
        public event Action PlacementRejected;
        public event Action DemolitionSucceeded;

        private CityFlowServices _services;
        private bool _isBuildingMode = false;
        private TileType _currentType = TileType.Road;
        private string _currentSpecialBuildingId = string.Empty;
        private string _currentCompanyTypeId = string.Empty;
        private PlacementDirection _currentDirection = PlacementDirection.North;

        private PlacementInputHandler _inputHandler;
        private PlacementVisualManager _visualManager;
        private PlacementCostLabelManager _costLabelManager;
        private MainCityView _cityView;
        private Vector2Int? _lastModelPreviewCoord;
        private readonly Queue<PendingPlacementRequest> _pendingPlacements = new();
        private readonly HashSet<Vector2Int> _queuedPlacementCoordinates = new();
        private float _nextPlacementTime;
        private Vector2Int? _roadStrokeLastAcceptedCoord;

        public Func<bool> IsBuildMenuOpen { get; set; }

        private PlacementActionDispatcher _actionDispatcher;

        private bool _managersInitialized = false;

        private void Awake()
        {
            EnsureManagers();
        }

        private void EnsureManagers()
        {
            if (AreManagersReady())
            {
                _managersInitialized = true;
                return;
            }

            ReleaseManagers();

            var uiRaycastBlocker = new UIRaycastBlocker();

            _inputHandler = new PlacementInputHandler(uiRaycastBlocker, confirmPopup);

            _visualManager = new PlacementVisualManager(
                ghostRenderer, colorValid, colorInvalid,
                use3DGhostVolume, ghostVolumeHeight, volumeValidColor, volumeInvalidColor,
                GetComponent<BenefitHighlightRenderer>(),
                populationConfig, hospitalDefinition,
                transform);

            _costLabelManager = new PlacementCostLabelManager(showCostLabel, costAffordableColor, costUnaffordableColor);


            _actionDispatcher = new PlacementActionDispatcher(availableTiles, useFakeMode);

            _inputHandler.OnRotateRequested += HandleRotate;
            _inputHandler.OnDemolishRequested += HandleDemolish;
            _inputHandler.OnPlaceRequested += HandlePlace;
            _inputHandler.OnDragPlaceRequested += HandleDragPlace;
            _inputHandler.OnPlacementStrokeEnded += HandlePlacementStrokeEnded;
            _inputHandler.OnPlacementRejected += HandlePlacementRejected;
            _inputHandler.OnCancelPlacementRequested += CancelPlacement;

            _managersInitialized = true;
        }

        private bool AreManagersReady()
        {
            return _managersInitialized &&
                   _inputHandler != null &&
                   _visualManager != null &&
                   _costLabelManager != null &&
                   _actionDispatcher != null;
        }

        private void ReleaseManagers()
        {
            if (_inputHandler != null)
            {
                _inputHandler.OnRotateRequested -= HandleRotate;
                _inputHandler.OnDemolishRequested -= HandleDemolish;
                _inputHandler.OnPlaceRequested -= HandlePlace;
                _inputHandler.OnDragPlaceRequested -= HandleDragPlace;
                _inputHandler.OnPlacementStrokeEnded -= HandlePlacementStrokeEnded;
                _inputHandler.OnPlacementRejected -= HandlePlacementRejected;
                _inputHandler.OnCancelPlacementRequested -= CancelPlacement;
            }

            _visualManager?.Cleanup();
            _costLabelManager?.Cleanup();

            _inputHandler = null;
            _visualManager = null;
            _costLabelManager = null;
            _actionDispatcher = null;
            _managersInitialized = false;
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
            EnsureManagers();
            ClearPendingPlacements();
            if (!IsTileTypeUnlocked(type))
            {
                ToggleBuildMode(false);
                Debug.LogWarning(
                    $"[PlacementController] {type} 건물은 연구 완료 후 건설할 수 있습니다.",
                    this);
                return;
            }

            _currentType = type;
            _currentSpecialBuildingId = string.Empty;
            _currentCompanyTypeId = string.Empty;
            _currentDirection = PlacementDirection.North;
            _lastModelPreviewCoord = null;

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

        public bool IsTileTypeUnlocked(TileType type)
        {
            EnsureManagers();
            return _actionDispatcher == null ||
                   _actionDispatcher.IsTileTypeUnlocked(
                        type,
                        _services);
        }

        public bool SetSpecialBuilding(string buildingId)
        {
            ClearPendingPlacements();
            string normalizedId = buildingId?.Trim() ?? string.Empty;
            _currentType = TileType.SpecialBuilding;
            _currentSpecialBuildingId = normalizedId;
            _currentCompanyTypeId = string.Empty;
            _currentDirection = PlacementDirection.North;
            _lastModelPreviewCoord = null;
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

        private void CancelPlacement()
        {
            ToggleBuildMode(false);
        }

        public void ToggleBuildMode(bool isOn)
        {
            EnsureManagers();
            bool wasBuildingMode = _isBuildingMode;
            _isBuildingMode = isOn;

            if (Application.isPlaying &&
                logPlacementModeDiagnostics &&
                wasBuildingMode &&
                !isOn)
            {
                System.Diagnostics.StackFrame callerFrame =
                    new System.Diagnostics.StackTrace(1, false)
                        .GetFrame(0);
                string caller =
                    callerFrame?.GetMethod()?.DeclaringType?.Name +
                    "." +
                    callerFrame?.GetMethod()?.Name;
                Debug.LogWarning(
                    "[PlacementModeDiagnostics] Build mode turned off. " +
                    $"caller={caller}, type={_currentType}, " +
                    $"componentEnabled={enabled}, " +
                    $"activeInHierarchy={gameObject.activeInHierarchy}, " +
                    $"buildMenuOpen={IsBuildMenuOpen?.Invoke() ?? false}, " +
                    $"frame={Time.frameCount}\n" +
                    StackTraceUtility.ExtractStackTrace(),
                    this);
            }

            if (isOn)
            {
                _visualManager.HideBenefitHighlights();
                UpdateGhostSprite();
                _visualManager.UpdateGhostFootprint(_currentType, _currentDirection);
                if (_visualManager.BuildingPreviewObject == null)
                {
                    UpdateBuildingModelPreview();
                }
            }

            _visualManager.SetGhostActive(isOn);
            BuildModeCursorFeedback.SetBuilding(this, isOn);

            if (!isOn)
            {
                ClearPendingPlacements();
                _visualManager.HideBenefitHighlights();
                _visualManager.SetBuildingPreview(null);
                _lastModelPreviewCoord = null;
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
                populationConfig, hospitalDefinition,
                transform);

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
            if (Application.isPlaying &&
                logPlacementModeDiagnostics &&
                _isBuildingMode)
            {
                Debug.LogWarning(
                    "[PlacementModeDiagnostics] PlacementController was disabled " +
                    $"while build mode remained active. type={_currentType}, " +
                    $"activeInHierarchy={gameObject.activeInHierarchy}, " +
                    $"frame={Time.frameCount}\n" +
                    StackTraceUtility.ExtractStackTrace(),
                    this);
            }

            ClearPendingPlacements();
            BuildModeCursorFeedback.SetBuilding(this, false);
            _visualManager?.HideBenefitHighlights();
            _visualManager?.SetGhostActive(false);
            _visualManager?.SetBuildingPreview(null);
            _costLabelManager?.SetCostLabelActive(false);
            _lastModelPreviewCoord = null;
        }

        private void OnDestroy()
        {
            ReleaseManagers();
        }

        private void Update()
        {
            EnsureManagers();

            IWorldCoordinateSpace coordinateSpace =
                _services?.WorldCoordinates;
            Vector2Int cursorCoord = _inputHandler.GetMouseGridCoordinate(
                useXYPlane,
                coordinateSpace);
            UpdatePlacementDirection(cursorCoord);
            Vector2Int gridCoord = ResolvePlacementAnchor(
                cursorCoord,
                _currentType,
                _currentDirection);
            bool canPlace = _actionDispatcher.CheckCanPlace(
                gridCoord,
                _currentType,
                _currentDirection,
                _services,
                _currentSpecialBuildingId);
            bool isBuildingType = TileFootprint.IsBuilding(_currentType);

            bool isBuildMenuOpen = IsBuildMenuOpen?.Invoke() ?? false;
            _inputHandler.UpdateGlobalInput(
                _isBuildingMode,
                isBuildingType,
                cursorCoord,
                isBuildMenuOpen);

            if (_inputHandler.IsPointerOverBlockingUI())
            {
                ClearPendingPlacements();
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

            if (!_lastModelPreviewCoord.HasValue ||
                _lastModelPreviewCoord.Value != gridCoord)
            {
                UpdateBuildingModelPreview(gridCoord);
            }

            _inputHandler.UpdatePlacementInput(canPlace, gridCoord);
            ProcessPendingPlacements();

            _visualManager.SetGhostActive(true);

            Vector2Int rotatedSize = TileFootprint.GetRotatedSize(_currentType, _currentDirection);
            float surfaceZ = GetSurfaceMarkerZ(gridCoord);

            Vector3 ghostPos = GetGhostPosition(gridCoord, rotatedSize, surfaceZ);
            Vector3 buildingPreviewPos =
                GetBuildingPreviewPosition(
                    gridCoord,
                    rotatedSize);
            Quaternion? buildingPreviewRotation =
                GetBuildingPreviewRotation(gridCoord);

            _visualManager.SyncGhostPosition(
                ghostPos,
                TileFootprint.ToAngle(_currentDirection),
                useXYPlane,
                coordinateSpace,
                buildingPreviewPos,
                buildingPreviewRotation);
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

            // 건물 방향은 현재 마우스 타일의 인접 도로를 향해 Sim 계층에서 결정한다.
            // 도로·일방통행 등 인프라 도구의 회전 동작은 이 분기와 무관하게 유지한다.
        }

        private bool HandleDemolish(Vector2Int coord)
        {
            bool removed = _actionDispatcher.TryDemolishAt(coord, _services);
            if (removed)
            {
                DemolitionSucceeded?.Invoke();
            }

            return removed;
        }

        private void HandlePlace(Vector2Int coord)
        {
            if (_currentType == TileType.Road)
            {
                _roadStrokeLastAcceptedCoord = null;
            }

            EnqueuePlacement(
                coord,
                _currentDirection,
                notifyOnRejected: true);
            ProcessPendingPlacements();
        }

        private void HandleDragPlace(Vector2Int from, Vector2Int to)
        {
            Vector2Int cursor =
                _currentType == TileType.Road &&
                _roadStrokeLastAcceptedCoord.HasValue
                    ? _roadStrokeLastAcceptedCoord.Value
                    : from;

            while (cursor.x != to.x)
            {
                cursor.x += Math.Sign(to.x - cursor.x);
                if (!EnqueuePlacement(
                        cursor,
                        ResolvePlacementDirection(cursor),
                        notifyOnRejected: false))
                {
                    _lastModelPreviewCoord = null;
                    return;
                }
            }

            while (cursor.y != to.y)
            {
                cursor.y += Math.Sign(to.y - cursor.y);
                if (!EnqueuePlacement(
                        cursor,
                        ResolvePlacementDirection(cursor),
                        notifyOnRejected: false))
                {
                    _lastModelPreviewCoord = null;
                    return;
                }
            }

            _lastModelPreviewCoord = null;
        }

        private bool EnqueuePlacement(
            Vector2Int coord,
            PlacementDirection direction,
            bool notifyOnRejected)
        {
            DiscardStalePlacementBacklog();
            if (_currentType == TileType.Road &&
                !CanAcceptRoadPlacement(
                    _pendingPlacements.Count,
                    maximumQueuedPlacements))
            {
                return false;
            }

            if (!_queuedPlacementCoordinates.Add(coord))
            {
                return true;
            }

            _pendingPlacements.Enqueue(
                new PendingPlacementRequest(
                    coord,
                    _currentType,
                    direction,
                    _currentSpecialBuildingId,
                    _currentCompanyTypeId,
                    notifyOnRejected,
                    Time.unscaledTime));
            if (_currentType == TileType.Road)
            {
                _roadStrokeLastAcceptedCoord = coord;
            }
            else
            {
                CollapsePlacementBacklogToLatest();
            }

            return true;
        }

        private void HandlePlacementStrokeEnded()
        {
            if (_currentType != TileType.Road)
            {
                return;
            }

            ClearPendingPlacements();
            _roadStrokeLastAcceptedCoord = null;
        }

        private void ProcessPendingPlacements()
        {
            const int MaxRequestsCheckedPerFrame = 32;

            if (_pendingPlacements.Count == 0)
            {
                return;
            }

            if (DiscardStalePlacementBacklog() ||
                Time.unscaledTime < _nextPlacementTime)
            {
                return;
            }

            int checkedRequestCount = 0;
            while (_pendingPlacements.Count > 0 &&
                   checkedRequestCount < MaxRequestsCheckedPerFrame)
            {
                checkedRequestCount++;
                PendingPlacementRequest request =
                    _pendingPlacements.Dequeue();
                _queuedPlacementCoordinates.Remove(request.Coord);

                if (!_actionDispatcher.CheckCanPlace(
                        request.Coord,
                        request.Type,
                        request.Direction,
                        _services,
                        request.SpecialBuildingId))
                {
                    if (request.NotifyOnRejected)
                    {
                        PlacementRejected?.Invoke();
                    }

                    continue;
                }

                bool placed = _actionDispatcher.PlaceInfrastructure(
                    request.Coord,
                    request.Type,
                    request.Direction,
                    _services,
                    request.SpecialBuildingId,
                    request.CompanyTypeId);
                _lastModelPreviewCoord = null;

                if (!placed)
                {
                    if (request.NotifyOnRejected)
                    {
                        PlacementRejected?.Invoke();
                    }

                    continue;
                }

                PlacementConfirmed?.Invoke(
                    request.Coord,
                    request.Type);
                PlacementSucceeded?.Invoke();

                if (Application.isPlaying &&
                    logPlacementModeDiagnostics)
                {
                    Debug.Log(
                        "[PlacementModeDiagnostics] Placement completed. " +
                        $"type={request.Type}, coord={request.Coord}, " +
                        $"buildMode={_isBuildingMode}, " +
                        $"componentEnabled={enabled}, " +
                        $"activeInHierarchy={gameObject.activeInHierarchy}, " +
                        $"buildMenuOpen={IsBuildMenuOpen?.Invoke() ?? false}, " +
                        $"frame={Time.frameCount}",
                        this);
                }

                _nextPlacementTime =
                    Time.unscaledTime +
                    Mathf.Max(0f, placementIntervalSeconds);
                return;
            }
        }

        private bool DiscardStalePlacementBacklog()
        {
            if (_pendingPlacements.Count == 0)
            {
                return false;
            }

            float backlogAge =
                Time.unscaledTime -
                _pendingPlacements.Peek().EnqueuedAt;
            if (!IsPlacementBacklogStale(
                    Time.unscaledTime,
                    _pendingPlacements.Peek().EnqueuedAt,
                    maximumPlacementBacklogSeconds))
            {
                return false;
            }

            int discardedCount = _pendingPlacements.Count;
            ClearPendingPlacements();
            Debug.LogWarning(
                "[PlacementController] Discarded stale placement backlog. " +
                $"count={discardedCount}, age={backlogAge:F2}s.",
                this);
            return true;
        }

        private void CollapsePlacementBacklogToLatest()
        {
            int capacity = Mathf.Max(1, maximumQueuedPlacements);
            if (!ShouldCollapsePlacementBacklog(
                    _pendingPlacements.Count,
                    capacity))
            {
                return;
            }

            PendingPlacementRequest latest = default;
            while (_pendingPlacements.Count > 0)
            {
                latest = _pendingPlacements.Dequeue();
            }

            _queuedPlacementCoordinates.Clear();
            _pendingPlacements.Enqueue(latest);
            _queuedPlacementCoordinates.Add(latest.Coord);
        }

        internal static bool IsPlacementBacklogStale(
            float currentTime,
            float enqueuedAt,
            float maximumAge)
        {
            return currentTime - enqueuedAt >
                Mathf.Max(0.05f, maximumAge);
        }

        internal static bool ShouldCollapsePlacementBacklog(
            int pendingCount,
            int maximumCount)
        {
            return pendingCount > Mathf.Max(1, maximumCount);
        }

        internal static bool CanAcceptRoadPlacement(
            int pendingCount,
            int maximumCount)
        {
            return pendingCount < Mathf.Max(1, maximumCount);
        }

        private void ClearPendingPlacements()
        {
            _pendingPlacements.Clear();
            _queuedPlacementCoordinates.Clear();
            _nextPlacementTime = 0f;
            _roadStrokeLastAcceptedCoord = null;
        }

        private void HandlePlacementRejected()
        {
            PlacementRejected?.Invoke();
        }

        private PlacementDirection ResolvePlacementDirection(Vector2Int coord)
        {
            if (!TileFootprint.IsBuilding(_currentType))
            {
                return _currentDirection;
            }

            if (UsesFrontParkingTile(_currentType) &&
                _services?.TileData != null)
            {
                System.Collections.Generic.IReadOnlyList<
                    PlacementDirection> order =
                    CameraAutoDirectionOrder() ??
                    _cameraOrderBuffer;
                for (int index = 0;
                     index < order.Count;
                     index++)
                {
                    PlacementDirection candidate = order[index];
                    Vector2Int roadTile =
                        coord +
                        TileFootprint.GetFrontOffset(candidate);
                    if (_services.TileData.GetTileType(roadTile) ==
                        TileType.Road)
                    {
                        return candidate;
                    }
                }

                return PlacementDirection.North;
            }

            if (_services?.Placement != null &&
                _services.Placement.TryResolveAutoDirection(
                    coord,
                    _currentType,
                    out PlacementDirection direction,
                    CameraAutoDirectionOrder()))
            {
                return direction;
            }

            return PlacementDirection.North;
        }

        private static bool UsesFrontParkingTile(TileType type) =>
            type == TileType.House ||
            type == TileType.Office ||
            type == TileType.School ||
            type == TileType.Hospital;

        private static Vector2Int ResolvePlacementAnchor(
            Vector2Int cursorCoord,
            TileType type,
            PlacementDirection direction)
        {
            return UsesFrontParkingTile(type)
                ? TileFootprint.GetAnchorFromFrontTile(
                    cursorCoord,
                    type,
                    direction)
                : cursorCoord;
        }

        private void UpdatePlacementDirection(Vector2Int coord)
        {
            PlacementDirection resolvedDirection =
                ResolvePlacementDirection(coord);
            if (_currentDirection == resolvedDirection)
            {
                return;
            }

            _currentDirection = resolvedDirection;
            _visualManager.UpdateGhostFootprint(
                _currentType,
                _currentDirection);
        }

        // 여러 면이 도로일 때의 타이브레이크(환 결정 2026-07-31): 기본 카메라에서
        // 건물 정면이 플레이어 쪽(화면 아래)으로 보이는 방향을 우선한다 — 포켓시티 감.
        // 카메라·좌표계가 없으면 null 반환 → Sim 기본 순서(N→E→S→W).
        private readonly PlacementDirection[] _cameraOrderBuffer =
        {
            PlacementDirection.North, PlacementDirection.East,
            PlacementDirection.South, PlacementDirection.West,
        };

        private System.Collections.Generic.IReadOnlyList<PlacementDirection> CameraAutoDirectionOrder()
        {
            Camera cam = Camera.main;
            IWorldCoordinateSpace space = _services?.WorldCoordinates;
            if (cam == null || space == null) return null;

            Vector3 origin = space.GridToWorld(Vector2Int.zero);
            System.Array.Sort(_cameraOrderBuffer, (a, b) =>
            {
                int byScore = CameraFacingScore(b, cam, space, origin)
                    .CompareTo(CameraFacingScore(a, cam, space, origin));
                return byScore != 0 ? byScore : ((int)a).CompareTo((int)b);   // 동점 = 결정론 유지
            });
            return _cameraOrderBuffer;
        }

        private static float CameraFacingScore(
            PlacementDirection dir, Camera cam, IWorldCoordinateSpace space, Vector3 origin)
        {
            Vector2Int front = TileFootprint.GetFrontOffset(dir);
            Vector3 world = space.GridXAxis * front.x + space.GridYAxis * front.y;
            Vector3 screen = cam.WorldToScreenPoint(origin + world) - cam.WorldToScreenPoint(origin);
            var flat = new Vector2(screen.x, screen.y);
            if (flat.sqrMagnitude < 1e-6f) return float.MinValue;
            return Vector2.Dot(flat.normalized, Vector2.down);
        }

        public bool TryDemolishAt(Vector2Int coord)
        {
            return HandleDemolish(coord);
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

        private void UpdateBuildingModelPreview(
            Vector2Int? previewTile = null)
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
                _cityView ??=
                    FindAnyObjectByType<MainCityView>(
                        FindObjectsInactive.Include);
                if (previewTile.HasValue)
                {
                    _cityView?.TryCreatePlacementPreview(
                        previewTile.Value,
                        _currentType,
                        out preview);
                }
                else
                {
                    _cityView?.TryCreatePlacementPreview(
                        _currentType,
                        out preview);
                }
            }

            _visualManager.SetBuildingPreview(preview);
            _lastModelPreviewCoord = previewTile;
        }

        public const float EmptyGroundMarkerZ = 0.12f;
        public const float RoadSurfaceMarkerZ = 0.12f;
        public const float OverlappingPreviewOffset = 0.01f;

        public float GetSurfaceMarkerZ(Vector2Int gridCoord)
        {
            _cityView ??=
                FindAnyObjectByType<MainCityView>(
                    FindObjectsInactive.Include);
            if (_cityView != null)
            {
                return _cityView.RoadSurfaceZ;
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
            if (_currentType != TileType.SpecialBuilding)
            {
                _cityView ??=
                    FindAnyObjectByType<MainCityView>(
                        FindObjectsInactive.Include);
                if (_cityView != null)
                {
                    Vector3 position = _cityView
                        .GetPlacementPreviewWorldPosition(
                            gridCoord,
                            _currentType,
                            _currentDirection);
                    if (DoesPreviewOverlapExistingTiles(
                            gridCoord,
                            footprintSize))
                    {
                        Vector3 surfaceNormal =
                            _services?.WorldCoordinates
                                ?.GroundNormal ??
                            (useXYPlane
                                ? Vector3.back
                                : Vector3.up);
                        position +=
                            surfaceNormal.normalized *
                            OverlappingPreviewOffset;
                    }

                    return position;
                }
            }

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

        private bool DoesPreviewOverlapExistingTiles(
            Vector2Int gridCoord,
            Vector2Int footprintSize)
        {
            if (_services?.TileData == null)
            {
                return false;
            }

            int width = Mathf.Max(1, footprintSize.x);
            int height = Mathf.Max(1, footprintSize.y);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (_services.TileData.GetTileType(
                            gridCoord +
                            new Vector2Int(x, y)) !=
                        TileType.Empty)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private Quaternion? GetBuildingPreviewRotation(
            Vector2Int gridCoord)
        {
            if (_currentType == TileType.SpecialBuilding)
            {
                return null;
            }

            _cityView ??=
                FindAnyObjectByType<MainCityView>(
                    FindObjectsInactive.Include);
            return _cityView != null
                ? _cityView.GetPlacementPreviewWorldRotation(
                    gridCoord,
                    _currentType,
                    _currentDirection)
                : null;
        }

        private readonly struct PendingPlacementRequest
        {
            public PendingPlacementRequest(
                Vector2Int coord,
                TileType type,
                PlacementDirection direction,
                string specialBuildingId,
                string companyTypeId,
                bool notifyOnRejected,
                float enqueuedAt)
            {
                Coord = coord;
                Type = type;
                Direction = direction;
                SpecialBuildingId = specialBuildingId;
                CompanyTypeId = companyTypeId;
                NotifyOnRejected = notifyOnRejected;
                EnqueuedAt = enqueuedAt;
            }

            public Vector2Int Coord { get; }
            public TileType Type { get; }
            public PlacementDirection Direction { get; }
            public string SpecialBuildingId { get; }
            public string CompanyTypeId { get; }
            public bool NotifyOnRejected { get; }
            public float EnqueuedAt { get; }
        }
    }
}
