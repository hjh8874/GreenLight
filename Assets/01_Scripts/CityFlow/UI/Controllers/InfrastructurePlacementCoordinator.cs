using System;
using CityFlow.Contracts;
using CityFlow.UI.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Linq;
using CityFlow.Content.Transit;
using CityFlow.Bootstrap;
using CityFlow.DebugTools;
using CityFlow.UI.Controllers.Placement;
using CityFlow.View;

namespace CityFlow.UI.Controllers
{
    public class InfrastructurePlacementCoordinator : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("Settings")]
        [SerializeField] private SpriteRenderer ghostRenderer;
        [SerializeField] private Color colorValid = new Color(0f, 1f, 0f, 0.5f);
        [SerializeField] private Color colorInvalid = new Color(1f, 0f, 0f, 0.5f);

        private CityFlowServices _services;
        private IEconomyService _economy;
        private IIntersectionFacilityService _facilityService;
        private ITrafficRuleService _trafficRuleService;
        private IHighwayService _highwayService;
        private IBusStopInfrastructureService _busStopService;
        private BusStopRegistry _busStopRegistry;
        private IPlacementService _placement;
        private MainCityView _cityView;
        private CityBusStopWorldView _busStopWorldView;

        private bool _isBuildingMode = false;
        private InfrastructureDataSO _currentData;
        private string _currentGroupId;
        private PlacementController _originalPlacementController;
        private int _frameStarted = 0;
        private bool _wasOriginalBuildingMode = false;
        private Vector2Int? _lastRemovedCoord;
        private Vector2Int? _rightClickStartCoord;
        private Vector2Int? _pendingHighwayStart;
        private Vector2Int? _lastPreviewCursor;
        private GameObject _placementPreview;
        private Material _placementPreviewMaterial;
        private Renderer[] _placementPreviewRenderers =
            Array.Empty<Renderer>();
        private Quaternion _placementPreviewLocalRotation =
            Quaternion.identity;
        private readonly UIRaycastBlocker _uiRaycastBlocker = new UIRaycastBlocker();
        
        // Configuration Constants (Balancing Defaults)
        private const float UNDO_REFUND_RATE = 1.0f;
        private const float DEMOLISH_REFUND_RATE = 0.5f;

        public Func<bool> IsBuildMenuOpen { get; set; }

        public bool IsBuildingMode => _isBuildingMode;
        public event System.Action PlacementSucceeded;
        public event System.Action PlacementRejected;
        public event System.Action DemolitionSucceeded;

        public void Initialize(CityFlowServices services)
        {
            _services = services;
            _economy = services.Economy;
            _placement = services.Placement;
            _facilityService = services.Placement as IIntersectionFacilityService;
            _trafficRuleService = services.Placement as ITrafficRuleService;
            _highwayService = services.Placement as IHighwayService;
            _busStopService = services.Placement as IBusStopInfrastructureService;
            _busStopRegistry = FindFirstObjectByType<BusStopRegistry>();
            ResolvePreviewSources();
            
            if (_economy == null)
            {
                services.EconomyRegistered += (eco) => _economy = eco;
            }

            // Find the original PlacementController in the scene to cross-cancel modes
            _originalPlacementController = FindFirstObjectByType<PlacementController>();
        }

        private bool _isDemolishMode = false;

        public void StartPlacement(InfrastructureDataSO data)
        {
            if (data != null &&
                data.Kind == InfrastructureKind.BusStop &&
                !TryResolveBusStopRegistry())
            {
                Debug.LogWarning(
                    "[InfrastructurePlacementCoordinator] " +
                    "Bus-stop placement requires an active BusStopRegistry.");
                return;
            }

            CancelPlacement(); // Ensure clean state
            _currentData = data;
            _isBuildingMode = true;
            _isDemolishMode = false;
            _currentGroupId = Guid.NewGuid().ToString();
            _frameStarted = Time.frameCount; // Prevent immediate placement on the same frame

            if (_originalPlacementController != null)
            {
                _wasOriginalBuildingMode = _originalPlacementController.IsBuildingMode;
                _originalPlacementController.ToggleBuildMode(false);
                _originalPlacementController.SetGhostFootprint(Vector2Int.one);
                _originalPlacementController.enabled = false; // Update()를 완전히 멈춰서 클릭 간섭 방지
            }

            if (ghostRenderer != null)
            {
                ghostRenderer.gameObject.SetActive(false);
                if (data.Icon != null) ghostRenderer.sprite = data.Icon;
            }
            RebuildPlacementPreview(Vector2Int.zero);
            SetPlacementPreviewActive(true);
            if (ghostRenderer != null)
            {
                ghostRenderer.gameObject.SetActive(
                    _placementPreview == null);
            }
            BuildModeCursorFeedback.SetBuilding(this, true);
            Debug.Log($"[InfrastructurePlacementCoordinator] Started placement mode for: {data.InfrastructureName}");
        }

        public void StartDemolishMode()
        {
            CancelPlacement(); // Ensure clean state
            _isBuildingMode = true;
            _isDemolishMode = true;
            _currentData = null;
            _pendingHighwayStart = null;
            ClearPlacementPreview();
            _frameStarted = Time.frameCount;

            if (_originalPlacementController != null)
            {
                _wasOriginalBuildingMode = _originalPlacementController.IsBuildingMode;
                _originalPlacementController.ToggleBuildMode(false);
                _originalPlacementController.SetGhostFootprint(Vector2Int.one);
                _originalPlacementController.enabled = false;
            }

            if (ghostRenderer != null)
            {
                ghostRenderer.gameObject.SetActive(true);
                ghostRenderer.sprite = null; // 해체 모드는 망치 커서 없이 기존 빨간 고스트만 사용합니다.
                ghostRenderer.color = Color.red;
            }
            BuildModeCursorFeedback.SetBuilding(this, false);
            Debug.Log("[InfrastructurePlacementCoordinator] Started Demolish mode.");
        }

        public void CancelPlacement()
        {
            if (!_isBuildingMode &&
                !_isDemolishMode &&
                _placementPreview == null)
            {
                return;
            }

            _isBuildingMode = false;
            _isDemolishMode = false;
            _currentData = null;
            _pendingHighwayStart = null;
            ClearPlacementPreview();
            if (ghostRenderer != null)
            {
                ghostRenderer.gameObject.SetActive(false);
            }
            BuildModeCursorFeedback.SetBuilding(this, false);

            // 원래 PlacementController를 다시 활성화하여 도로/건물 건설이 가능하도록 복원
            if (_originalPlacementController != null)
            {
                _originalPlacementController.enabled = true;
                _originalPlacementController.ToggleBuildMode(_wasOriginalBuildingMode);
            }
            Debug.Log("[InfrastructurePlacementCoordinator] Cancelled placement mode. Original controller restored.");
        }

        private void OnDisable()
        {
            ClearPlacementPreview();
            BuildModeCursorFeedback.SetBuilding(this, false);
        }

        private void OnDestroy()
        {
            ClearPlacementPreview();
            SafeDestroy(_placementPreviewMaterial);
            _placementPreviewMaterial = null;
        }

        private void Update()
        {
            if (!_isBuildingMode) return;
            if (!_isDemolishMode && _currentData == null) return;

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.wasReleasedThisFrame)
            {
                _lastRemovedCoord = null;
                _rightClickStartCoord = null;
            }

            bool isPointerOverBlockingUI = _uiRaycastBlocker.IsPointerOverBlockingUI();
            if (isPointerOverBlockingUI)
            {
                if (ghostRenderer != null) ghostRenderer.gameObject.SetActive(false);
                SetPlacementPreviewActive(false);
                return;
            }

            if (HandleRightClickDemolition(mouse))
            {
                return;
            }

            Vector2Int gridCoord = GetMouseGridCoordinate();
            bool previewDependsOnCursor =
                _currentData != null &&
                (_currentData.Kind == InfrastructureKind.BusStop ||
                 (_currentData.Kind == InfrastructureKind.Highway &&
                  _pendingHighwayStart.HasValue));
            if (previewDependsOnCursor &&
                (!_lastPreviewCursor.HasValue ||
                 _lastPreviewCursor.Value != gridCoord))
            {
                RebuildPlacementPreview(gridCoord);
            }

            SetPlacementPreviewActive(
                !_isDemolishMode);
            if (ghostRenderer != null)
            {
                ghostRenderer.gameObject.SetActive(
                    _isDemolishMode ||
                    _placementPreview == null);
            }

            UpdateGhostPosition(gridCoord);

            if (_isDemolishMode)
            {
                if (ghostRenderer != null)
                {
                    ghostRenderer.color = Color.red; // Always red for demolish
                }
            }
            else
            {
                bool canPlace =
                    CheckCanPlace(
                        gridCoord,
                        _currentData);
                Color ghostColor =
                    canPlace
                        ? colorValid
                        : colorInvalid;
                ghostColor.a = 1f;
                if (ghostRenderer != null)
                {
                    ghostRenderer.color = ghostColor;
                }
                UpdatePlacementPreviewColor(
                    ghostColor);
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (Time.frameCount > _frameStarted + 1) // Ensure it's not the exact frame we clicked the UI button
                {
                    if (_isDemolishMode)
                    {
                        if (!TryDemolishInfrastructureAt(gridCoord))
                        {
                            Debug.Log($"[InfrastructurePlacementCoordinator] No infrastructure to demolish at {gridCoord}");
                        }
                    }
                    else
                    {
                        TryPurchaseAndPlace(gridCoord);
                    }
                }
            }
        }

        private bool HandleRightClickDemolition(Mouse mouse)
        {
            if (mouse == null)
            {
                return false;
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                if (IsBuildMenuOpen?.Invoke() != true)
                {
                    _rightClickStartCoord = null;
                    return false;
                }

                _rightClickStartCoord = GetMouseGridCoordinate();
            }

            if (!mouse.rightButton.isPressed || !_rightClickStartCoord.HasValue)
            {
                return false;
            }

            if (IsBuildMenuOpen?.Invoke() != true)
            {
                _rightClickStartCoord = null;
                return false;
            }

            Vector2Int currentCoord = GetMouseGridCoordinate();
            UpdateGhostPosition(currentCoord);

            if (!_lastRemovedCoord.HasValue || _lastRemovedCoord.Value != currentCoord)
            {
                bool removed = _originalPlacementController != null
                    ? _originalPlacementController.TryDemolishAt(currentCoord)
                    : TryDemolishInfrastructureAt(currentCoord);

                if (removed)
                {
                    _lastRemovedCoord = currentCoord;
                }
            }

            return true;
        }

        private void UpdateGhostPosition(Vector2Int gridCoord)
        {
            Vector3 position =
                _originalPlacementController != null
                ? _originalPlacementController.GetGhostPosition(gridCoord, Vector2Int.one)
                : _services?.WorldCoordinates != null
                    ? _services.WorldCoordinates.GridToWorld(gridCoord)
                    : new Vector3(gridCoord.x, 0f, gridCoord.y);

            Quaternion rotation =
                _services?.WorldCoordinates != null
                    ? _services.WorldCoordinates
                        .CoordinateRotation
                    : Quaternion.identity;

            if (ghostRenderer != null)
            {
                ghostRenderer.transform.SetPositionAndRotation(
                    position,
                    rotation);
            }

            if (_placementPreview == null)
            {
                return;
            }

            if (_currentData != null &&
                _currentData.Kind == InfrastructureKind.Highway &&
                _pendingHighwayStart.HasValue)
            {
                Vector2 midpoint = new Vector2(
                    (_pendingHighwayStart.Value.x +
                     gridCoord.x) * 0.5f + 0.5f,
                    (_pendingHighwayStart.Value.y +
                     gridCoord.y) * 0.5f + 0.5f);
                position =
                    _services?.WorldCoordinates != null
                        ? _services.WorldCoordinates
                            .GridPointToWorld(
                                midpoint,
                                0.02f)
                        : new Vector3(
                            midpoint.x,
                            0.02f,
                            midpoint.y);
            }

            _placementPreview.transform
                .SetPositionAndRotation(
                    position,
                    rotation *
                    _placementPreviewLocalRotation);
        }

        private void ResolvePreviewSources()
        {
            _cityView ??=
                FindAnyObjectByType<MainCityView>(
                    FindObjectsInactive.Include);
            _busStopWorldView ??=
                FindAnyObjectByType<
                    CityBusStopWorldView>(
                    FindObjectsInactive.Include);
        }

        private void RebuildPlacementPreview(
            Vector2Int cursor)
        {
            ClearPlacementPreview();
            _lastPreviewCursor = cursor;
            if (_currentData == null)
            {
                return;
            }

            ResolvePreviewSources();
            GameObject preview = null;
            if (_currentData.Kind ==
                InfrastructureKind.BusStop)
            {
                _busStopWorldView
                    ?.TryCreatePlacementPreview(
                        cursor,
                        out preview);
            }
            else
            {
                _cityView
                    ?.TryCreateInfrastructurePlacementPreview(
                        _currentData,
                        _pendingHighwayStart,
                        cursor,
                        out preview);
            }

            if (preview == null)
            {
                return;
            }

            _placementPreview = preview;
            _placementPreview.transform.SetParent(
                transform,
                true);
            _placementPreviewLocalRotation =
                preview.transform.localRotation;
            _placementPreviewMaterial ??=
                PlacementVisualManager
                    .CreateLightingIndependentPreviewMaterial(
                        "InfrastructurePlacementPreviewMaterial");
            _placementPreviewRenderers =
                PlacementVisualManager
                    .ConfigurePreviewObject(
                        _placementPreview,
                        _placementPreviewMaterial);
            UpdatePlacementPreviewColor(colorValid);
        }

        private void SetPlacementPreviewActive(
            bool active)
        {
            if (_placementPreview != null &&
                _placementPreview.activeSelf != active)
            {
                _placementPreview.SetActive(active);
            }
        }

        private void UpdatePlacementPreviewColor(
            Color color)
        {
            if (_placementPreviewMaterial == null)
            {
                return;
            }

            PlacementVisualManager.ApplyPreviewColor(
                _placementPreviewMaterial,
                _placementPreviewRenderers,
                color);
        }

        private void ClearPlacementPreview()
        {
            if (_placementPreview != null)
            {
                _placementPreview.SetActive(false);
                SafeDestroy(_placementPreview);
            }

            _placementPreview = null;
            _placementPreviewLocalRotation =
                Quaternion.identity;
            _placementPreviewRenderers =
                Array.Empty<Renderer>();
            _lastPreviewCursor = null;
        }

        private static void SafeDestroy(
            UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            bool isTemporaryEditorObject =
                Application.isEditor &&
                (target.hideFlags & HideFlags.DontSave) != 0;
            if (!Application.isPlaying ||
                isTemporaryEditorObject)
            {
                DestroyImmediate(target);
            }
            else
            {
                Destroy(target);
            }
        }

        private Vector2Int GetMouseGridCoordinate()
        {
            if (_originalPlacementController != null)
            {
                return _originalPlacementController.GetMouseGridCoordinate();
            }

            // Fallback if no PlacementController exists (sandbox without it)
            if (Camera.main == null)
            {
                return Vector2Int.zero;
            }

            Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            Ray ray = Camera.main.ScreenPointToRay(mousePos);
            if (_services?.WorldCoordinates != null)
            {
                return _services.WorldCoordinates.TryRayToGrid(
                    ray,
                    out Vector2Int coordinate,
                    out _)
                    ? coordinate
                    : Vector2Int.zero;
            }

            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

            if (groundPlane.Raycast(ray, out float enter))
            {
                Vector3 hitPoint = ray.GetPoint(enter);
                return GridUtil.WorldToGridXZ(hitPoint);
            }
            
            // Fallback for 2D/XY plane cases just in case
            Vector3 fallbackWorldPos = Camera.main.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, Mathf.Abs(Camera.main.transform.position.z)));
            return GridUtil.WorldToGrid(fallbackWorldPos);
        }

        private bool CheckCanPlace(Vector2Int coord, InfrastructureDataSO data)
        {
            if (_facilityService == null || _trafficRuleService == null || _highwayService == null)
            {
                Debug.LogWarning($"[InfrastructurePlacementCoordinator] Service null check failed: facility={_facilityService != null}, traffic={_trafficRuleService != null}. Initialize() was{(_placement == null ? " NOT" : "")} called.");
                return false;
            }

            return data.Kind switch
            {
                InfrastructureKind.Signal => _facilityService.CanPlaceSignal(coord),
                InfrastructureKind.Roundabout => _facilityService.CanPlaceRoundabout(coord),
                InfrastructureKind.Overpass => _facilityService.CanPlaceOverpass(coord),
                InfrastructureKind.Oneway => _trafficRuleService.CanPlaceOneway(coord),
                InfrastructureKind.TurnRestriction => _trafficRuleService.CanPlaceTurnSign(coord),
                InfrastructureKind.PriorityRoad => _facilityService.CanPlacePriorityRoad(coord),
                InfrastructureKind.BusStop =>
                    _busStopService != null &&
                    TryResolveBusStopRegistry() &&
                    _busStopService.CanPlaceBusStop(coord),
                InfrastructureKind.Highway => _pendingHighwayStart.HasValue
                    ? _highwayService.CanPlaceHighway(_pendingHighwayStart.Value, coord)
                    : _highwayService.CanSelectHighwayRamp(coord),
                _ => false
            };
        }

        private void TryPurchaseAndPlace(Vector2Int coord)
        {
            if (!CheckCanPlace(coord, _currentData))
            {
                Debug.Log($"[InfrastructurePlacementCoordinator] Cannot place {_currentData.InfrastructureName} at {coord}");
                PlacementRejected?.Invoke();
                return;
            }

            if (_currentData.Kind == InfrastructureKind.Highway && !_pendingHighwayStart.HasValue)
            {
                _pendingHighwayStart = coord;
                RebuildPlacementPreview(coord);
                UpdateGhostPosition(coord);
                Debug.Log($"[InfrastructurePlacementCoordinator] 고속도로 시작 램프 선택: {coord}. 끝 램프를 선택하세요.");
                return;
            }

            long cost = _currentData.Kind == InfrastructureKind.Highway
                ? _highwayService.HighwayCost(_pendingHighwayStart.Value, coord)
                : _currentData.Cost;
            
            // Transaction: TrySpend first
            if (cost > 0)
            {
                if (_economy == null)
                {
                    Debug.LogWarning("[InfrastructurePlacementCoordinator] EconomyService is null, but cost > 0. Placement blocked to prevent free placement.");
                    PlacementRejected?.Invoke();
                    return;
                }

                if (!_economy.TrySpend(cost))
                {
                    Debug.LogWarning("[InfrastructurePlacementCoordinator] Not enough coins.");
                    PlacementRejected?.Invoke();
                    return;
                }
            }

            // Transaction: TryPlace
            bool placeSuccess = _currentData.Kind switch
            {
                InfrastructureKind.Signal => _facilityService.TryPlaceSignal(coord, _currentData.GreenSlots),
                InfrastructureKind.Roundabout => _facilityService.TryPlaceRoundabout(coord),
                InfrastructureKind.Overpass => _facilityService.TryPlaceOverpass(coord),
                InfrastructureKind.Oneway => _trafficRuleService.TryPlaceOneway(coord, _currentData.OnewayDir),
                InfrastructureKind.TurnRestriction => _trafficRuleService.TryPlaceTurnSign(coord, _currentData.TurnMode),
                InfrastructureKind.PriorityRoad => _facilityService.TryPlacePriorityRoad(coord, _currentData.PriorityAxis),
                InfrastructureKind.BusStop =>
                    _busStopService != null &&
                    _busStopService.TryPlaceBusStop(coord),
                InfrastructureKind.Highway => _highwayService.TryPlaceHighway(_pendingHighwayStart.Value, coord),
                _ => false
            };

            if (!placeSuccess)
            {
                // Rollback
                if (_economy != null && cost > 0)
                {
                    _economy.AddCoins(cost, "Placement Rollback Refund");
                    Debug.Log($"[InfrastructurePlacementCoordinator] Placement failed. Refunded {cost} coins.");
                }
                PlacementRejected?.Invoke();
                return;
            }

            Vector2Int eventCoord = _pendingHighwayStart ?? coord;
            if (_currentData.Kind == InfrastructureKind.Highway)
            {
                _pendingHighwayStart = null;
                RebuildPlacementPreview(coord);
            }
            if (_currentData.Kind == InfrastructureKind.BusStop)
            {
                if (!TryCommitBusStopRegistration(coord, cost))
                {
                    return;
                }
            }

            if (_services != null && _services.Events != null)
            {
                _services.Events.Publish(new InfrastructureChangedEvent(eventCoord, false));
            }

            Debug.Log($"[InfrastructurePlacementCoordinator] Successfully placed {_currentData.InfrastructureName} at {coord} for {cost} coins.");
            PlacementSucceeded?.Invoke();
            CompletePlacement();
        }

        private void CompletePlacement()
        {
            _wasOriginalBuildingMode = false;
            CancelPlacement();
        }

        private bool TryCommitBusStopRegistration(
            Vector2Int coord,
            long cost)
        {
            if (TryResolveBusStopRegistry())
            {
                _busStopRegistry.RegisterBusStop(coord);
                return true;
            }

            bool rolledBack =
                _busStopService != null &&
                _busStopService.TryRemoveBusStop(coord);

            if (rolledBack && _economy != null && cost > 0)
            {
                _economy.AddCoins(
                    cost,
                    "Bus Stop Registry Rollback Refund");
            }

            if (rolledBack)
            {
                Debug.LogError(
                    "[InfrastructurePlacementCoordinator] " +
                    $"Bus-stop placement at {coord} was rolled back because " +
                    "BusStopRegistry became unavailable.");
            }
            else
            {
                Debug.LogError(
                    "[InfrastructurePlacementCoordinator] " +
                    $"BusStopRegistry became unavailable after placing a bus " +
                    $"stop at {coord}, and placement rollback failed.");
            }

            return false;
        }

        private bool TryResolveBusStopRegistry()
        {
            if (_busStopRegistry == null)
            {
                _busStopRegistry =
                    FindAnyObjectByType<BusStopRegistry>();
            }

            return _busStopRegistry != null;
        }

        // --- Demolish Logic (LIFO priority handling) ---
        public bool TryDemolishInfrastructureAt(Vector2Int coord)
        {
            if (_facilityService == null || _trafficRuleService == null || _highwayService == null) return false;

            if (_busStopService != null &&
                BusStopInfrastructurePolicy.TryResolveLogicalStop(
                    coord,
                    _busStopService.BusStopTiles,
                    IsRoad,
                    out Vector2Int logicalBusStop))
            {
                if (!TryResolveBusStopRegistry())
                {
                    Debug.LogError(
                        "[InfrastructurePlacementCoordinator] " +
                        $"Cannot remove bus stop at {logicalBusStop} without an active BusStopRegistry.");
                    return false;
                }

                if (!_busStopService.TryRemoveBusStop(logicalBusStop))
                {
                    return false;
                }

                if (!_busStopRegistry.RemoveBusStop(logicalBusStop))
                {
                    Debug.LogWarning(
                        "[InfrastructurePlacementCoordinator] " +
                        $"Bus stop at {logicalBusStop} was removed from placement data but was missing from BusStopRegistry.");
                }

                ProcessRefundAndEvent(
                    InfrastructureKind.BusStop,
                    logicalBusStop);
                return true;
            }

            if (_highwayService.IsHighwayRamp(coord) &&
                _highwayService.TryRemoveHighway(coord))
            {
                ProcessRefundAndEvent(InfrastructureKind.Highway, coord);
                return true;
            }

            // 1. Try Turn Restriction first (Rule: TurnSign -> Signal)
            if (_trafficRuleService.GetTurnMode(coord).HasValue)
            {
                if (_trafficRuleService.TryRemoveTurnSign(coord))
                {
                    ProcessRefundAndEvent(InfrastructureKind.TurnRestriction, coord);
                    return true;
                }
            }

            // 2. Try Signal
            var signalTiles = _facilityService.SignalTiles;
            if (signalTiles != null && signalTiles.Contains(coord))
            {
                if (_facilityService.TryRemoveSignal(coord))
                {
                    ProcessRefundAndEvent(InfrastructureKind.Signal, coord);
                    return true;
                }
            }

            // 3. Try Roundabout
            var roundabouts = _facilityService.RoundaboutTiles;
            if (roundabouts != null && roundabouts.Contains(coord))
            {
                if (_facilityService.TryRemoveRoundabout(coord))
                {
                    ProcessRefundAndEvent(InfrastructureKind.Roundabout, coord);
                    return true;
                }
            }

            // 4. Try Overpass
            var overpasses = _facilityService.OverpassTiles;
            if (overpasses != null && overpasses.Contains(coord))
            {
                if (_facilityService.TryRemoveOverpass(coord))
                {
                    ProcessRefundAndEvent(InfrastructureKind.Overpass, coord);
                    return true;
                }
            }

            // 5. Try Priority Road
            var priorityRoads = _facilityService.PriorityRoadTiles;
            if (priorityRoads != null && priorityRoads.Contains(coord))
            {
                if (_facilityService.TryRemovePriorityRoad(coord))
                {
                    ProcessRefundAndEvent(InfrastructureKind.PriorityRoad, coord);
                    return true;
                }
            }

            // 6. Try Oneway
            if (_trafficRuleService.GetOnewayDir(coord) != Vector2Int.zero)
            {
                if (_trafficRuleService.TryRemoveOneway(coord))
                {
                    ProcessRefundAndEvent(InfrastructureKind.Oneway, coord);
                    return true;
                }
            }

            return false;
        }

        private bool IsRoad(Vector2Int tile) =>
            _services?.TileData?.GetTileType(tile) == TileType.Road;

        private void ProcessRefundAndEvent(InfrastructureKind kind, Vector2Int coord)
        {
            long originalCost = 0;
            
            // 씬에 존재하는 슬롯들을 통해 동적으로 가격을 조회합니다.
            var slots = UnityEngine.Object.FindObjectsByType<InfrastructureSlotController>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None);
            var slot = slots.FirstOrDefault(s => s.InfraData != null && s.InfraData.Kind == kind);
            if (slot != null)
            {
                originalCost = slot.InfraData.Cost;
            }

            long refundAmount = (long)(originalCost * DEMOLISH_REFUND_RATE);

            if (_economy != null && refundAmount > 0)
            {
                _economy.AddCoins(refundAmount, "Demolish Refund");
            }
            
            if (_services != null && _services.Events != null)
            {
                _services.Events.Publish(new InfrastructureChangedEvent(coord, true));
            }

            DemolitionSucceeded?.Invoke();
            
            Debug.Log($"[InfrastructurePlacementCoordinator] Demolished {kind}. Refunded {refundAmount} coins (Original Cost: {originalCost}, Rate: {DEMOLISH_REFUND_RATE}).");
        }
    }
}
