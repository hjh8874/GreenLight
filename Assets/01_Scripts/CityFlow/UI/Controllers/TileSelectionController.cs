using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using CityFlow.Contracts;
using CityFlow.UI.Controllers;
using CityFlow.Bootstrap;

namespace CityFlow.UI
{
    public class TileSelectionController : MonoBehaviour, ICityFlowServiceConsumer
    {
        private CityFlowServices _services;

        public void Initialize(CityFlowServices services)
        {
            _services = services;
        }
        [Header("References")]
        [SerializeField] private AnalysisCardController analysisCard;
        [SerializeField] private BuildingInfoCardController buildingInfoCard;
        [SerializeField] private PlacementController placementController;
        private InfrastructurePlacementCoordinator _infraCoordinator;

        private Vector2Int? _lastHoveredBuildingCoord;

        [Header("Visuals")]
        [Tooltip("타일을 선택했을 때 바닥에 표시될 강조(하이라이트) 박스")]
        [SerializeField] private GameObject highlightBox; 
        [SerializeField] private bool useXYPlane = false;
        private Vector3 _highlightBaseScale = Vector3.one;
        private bool _highlightScaleInitialized;

        public void Configure(
            AnalysisCardController analysis,
            PlacementController placement,
            GameObject highlight)
        {
            analysisCard = analysis;
            placementController = placement;
            highlightBox = highlight;
            _highlightScaleInitialized = false;
            CacheHighlightScale();
        }

        public void SetUseXYPlane(bool isOn)
        {
            useXYPlane = isOn;
        }

        private void Start()
        {
            if (placementController == null)
            {
                placementController = FindFirstObjectByType<PlacementController>(FindObjectsInactive.Include);
            }
            if (_infraCoordinator == null)
            {
                _infraCoordinator = FindFirstObjectByType<InfrastructurePlacementCoordinator>(FindObjectsInactive.Include);
            }
            if (buildingInfoCard == null)
            {
                buildingInfoCard = FindFirstObjectByType<BuildingInfoCardController>(FindObjectsInactive.Include);
            }
            // 시작 시 상세 카드와 하이라이트 박스는 숨겨둡니다.
            CacheHighlightScale();
            DeselectTile();
        }

        private void Update()
        {
            if (OfflineSettlementPopup.IsInteractionBlocked)
            {
                return;
            }

            // 동적 생성되는 컨트롤러들을 위해 Update에서 지연 검색 지원
            if (placementController == null)
            {
                placementController = FindFirstObjectByType<PlacementController>(FindObjectsInactive.Include);
            }
            if (_infraCoordinator == null)
            {
                _infraCoordinator = FindFirstObjectByType<InfrastructurePlacementCoordinator>(FindObjectsInactive.Include);
            }

            bool isBuilding = (placementController != null && placementController.IsBuildingMode) || 
                              (_infraCoordinator != null && _infraCoordinator.IsBuildingMode);

            if (isBuilding)
            {
                DeselectTile(); // 건설 모드 켜지면 분석 카드도 바로 닫음
                if (buildingInfoCard != null && buildingInfoCard.IsOpen)
                {
                    buildingInfoCard.CloseCard();
                    _lastHoveredBuildingCoord = null;
                }
                return;
            }

            // --- 2. Hover 감지 (방치형 건물 정보 팝업용) ---
            HandleHover();

            // 3. 마우스 좌클릭 감지 (New Input System)
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                // UI(버튼 등) 위를 클릭했다면 무시
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                    return;

                Vector2Int? gridCoord = TryGetGridCoordinate();
                
                if (gridCoord.HasValue && IsAccessible(gridCoord.Value))
                {
                    bool isEmpty = false;
                    if (_services != null && _services.TileData != null)
                    {
                        if (_services.TileData.TryGetFootprintAnchor(gridCoord.Value, out Vector2Int anchor))
                        {
                            gridCoord = anchor;
                        }

                        isEmpty = _services.TileData.GetTileType(gridCoord.Value) == TileType.Empty;
                    }

                    if (!isEmpty)
                    {
                        SelectTile(gridCoord.Value);
                    }
                    else
                    {
                        DeselectTile();
                    }
                }
                else
                {
                    DeselectTile(); // 맵 바깥이나 허공 클릭 시 해제
                }
            }
        }

        private void HandleHover()
        {
            if (buildingInfoCard == null) return;

            // UI 위에 있으면 호버 해제
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                if (buildingInfoCard.IsOpen) buildingInfoCard.CloseCard();
                _lastHoveredBuildingCoord = null;
                return;
            }

            Vector2Int? gridCoord = TryGetGridCoordinate();
            if (gridCoord.HasValue && IsAccessible(gridCoord.Value))
            {
                Vector2Int coord = gridCoord.Value;
                if (_services != null && _services.TileData != null)
                {
                    if (_services.TileData.TryGetFootprintAnchor(coord, out Vector2Int anchor))
                    {
                        coord = anchor;
                    }

                    TileType type = _services.TileData.GetTileType(coord);
                    if (TileFootprint.IsBuilding(type))
                    {
                        // 새로운 건물 위에 올라갔을 때만 연다 (중복 호출 방지)
                        if (_lastHoveredBuildingCoord != coord || !buildingInfoCard.IsOpen)
                        {
                            buildingInfoCard.OpenCard(coord, type);
                            _lastHoveredBuildingCoord = coord;
                        }
                        return;
                    }
                }
            }

            // 건물이 아닌 곳이거나 허공이면 닫기
            if (buildingInfoCard.IsOpen)
            {
                buildingInfoCard.CloseCard();
            }
            _lastHoveredBuildingCoord = null;
        }

        private Vector2Int? TryGetGridCoordinate()
        {
            Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

            if (Camera.main == null)
            {
                return null;
            }

            Ray ray = Camera.main.ScreenPointToRay(mousePos);
            if (_services?.WorldCoordinates != null)
            {
                return _services.WorldCoordinates.TryRayToGrid(
                    ray,
                    out Vector2Int tile,
                    out _)
                    ? tile
                    : null;
            }

            if (useXYPlane)
            {
                Plane xyPlane = new Plane(Vector3.forward, Vector3.zero);
                if (xyPlane.Raycast(ray, out float xyEnter))
                {
                    Vector3 worldPos = ray.GetPoint(xyEnter);
                    return new Vector2Int(Mathf.FloorToInt(worldPos.x), Mathf.FloorToInt(worldPos.y));
                }

                return null;
            }

            Plane groundPlane = new Plane(Vector3.up, Vector3.zero); // Y=0 바닥
            
            if (groundPlane.Raycast(ray, out float enter))
            {
                Vector3 hitPoint = ray.GetPoint(enter);
                return new Vector2Int(Mathf.RoundToInt(hitPoint.x), Mathf.RoundToInt(hitPoint.z));
            }
            return null;
        }

        private bool IsAccessible(Vector2Int tile)
        {
            return _services?.WorldGrid != null
                ? _services.WorldGrid.IsTileUnlocked(tile)
                : GridUtil.IsInside(tile);
        }

        private void SelectTile(Vector2Int coord)
        {
            // 하이라이트 박스 이동
            if (highlightBox != null)
            {
                highlightBox.SetActive(true);
                TileType type = _services != null && _services.TileData != null
                    ? _services.TileData.GetTileType(coord)
                    : TileType.Empty;
                Vector2Int size = TileFootprint.GetSize(type);
                float offsetX = (size.x - 1) * 0.5f;
                float offsetY = (size.y - 1) * 0.5f;
                float markerZ = placementController != null
                    ? placementController.GetSurfaceMarkerZ(coord)
                    : -0.05f;
                IWorldCoordinateSpace coordinateSpace =
                    _services?.WorldCoordinates;
                if (coordinateSpace != null)
                {
                    highlightBox.transform.position =
                        coordinateSpace.GridPointToWorld(
                            new Vector2(
                                coord.x + 0.5f + offsetX,
                                coord.y + 0.5f + offsetY),
                            -markerZ);
                    highlightBox.transform.rotation =
                        coordinateSpace.CoordinateRotation;
                    highlightBox.transform.localScale = Vector3.Scale(
                        _highlightBaseScale,
                        new Vector3(size.x, size.y, 1f));
                }
                else
                {
                    highlightBox.transform.position = useXYPlane
                        ? new Vector3(
                            coord.x + 0.5f + offsetX,
                            coord.y + 0.5f + offsetY,
                            markerZ)
                        : new Vector3(
                            coord.x + offsetX,
                            0,
                            coord.y + offsetY);
                    highlightBox.transform.localScale = useXYPlane
                        ? Vector3.Scale(
                            _highlightBaseScale,
                            new Vector3(size.x, size.y, 1f))
                        : Vector3.Scale(
                            _highlightBaseScale,
                            new Vector3(size.x, 1f, size.y));
                }
            }

            // 상세 분석 카드 열기
            if (analysisCard != null)
            {
                analysisCard.OpenCard(coord);
            }
            
            Debug.Log($"[TileSelection] 타일 선택됨: {coord}");
        }

        private void DeselectTile()
        {
            if (highlightBox != null) highlightBox.SetActive(false);
            if (analysisCard != null) analysisCard.CloseCard();
        }

        private void CacheHighlightScale()
        {
            if (highlightBox == null || _highlightScaleInitialized)
            {
                return;
            }

            _highlightBaseScale = highlightBox.transform.localScale;
            _highlightScaleInitialized = true;
        }
    }
}
