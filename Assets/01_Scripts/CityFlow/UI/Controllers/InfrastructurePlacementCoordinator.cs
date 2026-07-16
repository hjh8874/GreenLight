using System;
using CityFlow.Contracts;
using CityFlow.UI.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Linq;
using CityFlow.Bootstrap;

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
        private IPlacementService _placement;

        private bool _isBuildingMode = false;
        private InfrastructureDataSO _currentData;
        private string _currentGroupId;
        private PlacementController _originalPlacementController;
        private int _frameStarted = 0;
        private bool _wasOriginalBuildingMode = false;
        private readonly UIRaycastBlocker _uiRaycastBlocker = new UIRaycastBlocker();
        
        // Configuration Constants (Balancing Defaults)
        private const float UNDO_REFUND_RATE = 1.0f;
        private const float DEMOLISH_REFUND_RATE = 0.5f;

        public bool IsBuildingMode => _isBuildingMode;

        public void Initialize(CityFlowServices services)
        {
            _services = services;
            _economy = services.Economy;
            _placement = services.Placement;
            _facilityService = services.Placement as IIntersectionFacilityService;
            _trafficRuleService = services.Placement as ITrafficRuleService;
            
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
                _originalPlacementController.enabled = false; // Update()를 완전히 멈춰서 클릭 간섭 방지
            }

            if (ghostRenderer != null)
            {
                ghostRenderer.gameObject.SetActive(true);
                if (data.Icon != null) ghostRenderer.sprite = data.Icon;
            }
            Debug.Log($"[InfrastructurePlacementCoordinator] Started placement mode for: {data.InfrastructureName}");
        }

        public void StartDemolishMode()
        {
            CancelPlacement(); // Ensure clean state
            _isBuildingMode = true;
            _isDemolishMode = true;
            _currentData = null;
            _frameStarted = Time.frameCount;

            if (_originalPlacementController != null)
            {
                _wasOriginalBuildingMode = _originalPlacementController.IsBuildingMode;
                _originalPlacementController.ToggleBuildMode(false);
                _originalPlacementController.enabled = false;
            }

            if (ghostRenderer != null)
            {
                ghostRenderer.gameObject.SetActive(true);
                ghostRenderer.sprite = null; // Maybe a hammer icon if we had one
                ghostRenderer.color = Color.red;
            }
            Debug.Log("[InfrastructurePlacementCoordinator] Started Demolish mode.");
        }

        public void CancelPlacement()
        {
            if (!_isBuildingMode && !_isDemolishMode) return;

            _isBuildingMode = false;
            _isDemolishMode = false;
            _currentData = null;
            if (ghostRenderer != null) ghostRenderer.gameObject.SetActive(false);

            // 원래 PlacementController를 다시 활성화하여 도로/건물 건설이 가능하도록 복원
            if (_originalPlacementController != null)
            {
                _originalPlacementController.enabled = true;
                _originalPlacementController.ToggleBuildMode(_wasOriginalBuildingMode);
            }
            Debug.Log("[InfrastructurePlacementCoordinator] Cancelled placement mode. Original controller restored.");
        }

        private void Update()
        {
            if (!_isBuildingMode) return;
            if (!_isDemolishMode && _currentData == null) return;

            if (_uiRaycastBlocker.IsPointerOverBlockingUI())
            {
                if (ghostRenderer != null) ghostRenderer.gameObject.SetActive(false);
                return;
            }

            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            {
                Vector2Int clickedCoord = GetMouseGridCoordinate();
                if (_originalPlacementController != null)
                {
                    _originalPlacementController.TryDemolishAt(clickedCoord);
                }
                else
                {
                    TryDemolishInfrastructureAt(clickedCoord);
                }
                return;
            }

            if (ghostRenderer != null) ghostRenderer.gameObject.SetActive(true);

            Vector2Int gridCoord = GetMouseGridCoordinate();

            if (ghostRenderer != null)
            {
                if (_originalPlacementController != null)
                {
                    ghostRenderer.transform.position = _originalPlacementController.GetGhostPosition(gridCoord);
                }
                else
                {
                    ghostRenderer.transform.position = new Vector3(gridCoord.x, 0, gridCoord.y);
                }
                
                if (_isDemolishMode)
                {
                    ghostRenderer.color = Color.red; // Always red for demolish
                }
                else
                {
                    bool canPlace = CheckCanPlace(gridCoord, _currentData);
                    Color ghostColor = canPlace ? colorValid : colorInvalid;
                    ghostRenderer.color = _originalPlacementController != null
                        ? _originalPlacementController.GetVisibleGhostColor(ghostColor)
                        : ghostColor;
                }
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

        private Vector2Int GetMouseGridCoordinate()
        {
            if (_originalPlacementController != null)
            {
                return _originalPlacementController.GetMouseGridCoordinate();
            }

            // Fallback if no PlacementController exists (sandbox without it)
            Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            Ray ray = Camera.main.ScreenPointToRay(mousePos);
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
            if (_facilityService == null || _trafficRuleService == null)
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
                _ => false
            };
        }

        private void TryPurchaseAndPlace(Vector2Int coord)
        {
            if (!CheckCanPlace(coord, _currentData))
            {
                Debug.Log($"[InfrastructurePlacementCoordinator] Cannot place {_currentData.InfrastructureName} at {coord}");
                return;
            }

            long cost = _currentData.Cost;
            
            // Transaction: TrySpend first
            if (cost > 0)
            {
                if (_economy == null)
                {
                    Debug.LogWarning("[InfrastructurePlacementCoordinator] EconomyService is null, but cost > 0. Placement blocked to prevent free placement.");
                    return;
                }

                if (!_economy.TrySpend(cost))
                {
                    Debug.LogWarning("[InfrastructurePlacementCoordinator] Not enough coins.");
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
                return;
            }

            if (_services != null && _services.Events != null)
            {
                _services.Events.Publish(new InfrastructureChangedEvent(coord, _currentData.Kind, false));
            }

            Debug.Log($"[InfrastructurePlacementCoordinator] Successfully placed {_currentData.InfrastructureName} at {coord} for {cost} coins.");
            
        }

        // --- Demolish Logic (LIFO priority handling) ---
        public bool TryDemolishInfrastructureAt(Vector2Int coord)
        {
            if (_facilityService == null || _trafficRuleService == null) return false;

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
                _services.Events.Publish(new InfrastructureChangedEvent(coord, kind, true));
            }
            
            Debug.Log($"[InfrastructurePlacementCoordinator] Demolished {kind}. Refunded {refundAmount} coins (Original Cost: {originalCost}, Rate: {DEMOLISH_REFUND_RATE}).");
        }
    }
}
