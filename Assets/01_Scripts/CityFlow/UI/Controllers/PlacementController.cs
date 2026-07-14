using System;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;
using UnityEngine.EventSystems; // UI 클릭 감지용
using UnityEngine.InputSystem;
using UnityEngine.UI;
using System.Collections.Generic;

namespace CityFlow.UI
{
    public enum PlacementActionType { Place, Remove }

    public struct PlacementAction
    {
        public string GroupId;
        public PlacementActionType ActionType;
        public Vector2Int Coord;
        public TileType PreviousType;
        public TileType NewType;
        public long Cost; // 추후 경제(환불) 시스템 연동을 위한 필드
    }

    public class PlacementController : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("Ghost Settings")]
        [Tooltip("마우스를 따라다닐 잔상(고스트) 프리팹 또는 스프라이트")]
        [SerializeField] private SpriteRenderer ghostRenderer;
        [SerializeField] private Color colorValid = new Color(0f, 1f, 0f, 0.5f);   // 초록색 (반투명)
        [SerializeField] private Color colorInvalid = new Color(1f, 0f, 0f, 0.5f); // 빨간색 (반투명)
        
        [Header("Debug / Testing")]
        [Tooltip("월~화 코어엔진 미연동 시 UI 단독 테스트를 위한 강제 성공 모드")]
        [SerializeField] private bool useFakeMode = false; // 코어 연동을 위해 끕니다.
        [SerializeField] private bool useXYPlane = false;
        
        [Header("Economy Data")]
        [Tooltip("비용(Cost)을 조회하기 위한 타일 데이터 모음")]
        [SerializeField] private CityFlow.Configs.TileDataSO[] availableTiles;
        
        [Header("UI References")]
        [SerializeField] private ConfirmPopupController confirmPopup;
        
        private CityFlowServices _services;
        private bool _isBuildingMode = false;
        
        private Stack<PlacementAction> _undoStack = new Stack<PlacementAction>();
        private readonly List<RaycastResult> _uiRaycastResults = new List<RaycastResult>();
        
        public bool IsBuildingMode => _isBuildingMode;
        
        private TileType _currentType = TileType.Road; 
        private Vector2Int? _lastPlacedCoord = null;
        private Vector2Int? _lastRemovedCoord = null;
        private Vector2Int? _rightClickStartCoord = null;
        private string _currentDragGroupId = null;

        /// <summary>
        /// 건설 패널(BuildPanelController) 등에서 타일 타입을 변경할 때 호출합니다.
        /// </summary>
        public void SetBuildType(TileType type)
        {
            _currentType = type;
            Debug.Log($"[PlacementController] 건설 모드 변경됨: {_currentType}");

            // 인프라 상점과 같은 패널에 탭으로 합쳐졌을 경우를 대비해, 일반 타일 선택 시 인프라 모드를 강제 취소합니다.
            var infraCoord = UnityEngine.Object.FindFirstObjectByType<CityFlow.UI.Controllers.InfrastructurePlacementCoordinator>();
            if (infraCoord != null && infraCoord.IsBuildingMode)
            {
                infraCoord.CancelPlacement();
            }

            // 도로/집 건설 모드를 확실하게 활성화합니다.
            enabled = true;
            ToggleBuildMode(true);
        }

        public void Initialize(CityFlowServices services)
        {
            _services = services;
        }

        public void ConfigureGhost(SpriteRenderer renderer)
        {
            ghostRenderer = renderer;
            if (ghostRenderer != null)
            {
                ghostRenderer.gameObject.SetActive(_isBuildingMode);
            }
        }

        public void SetFakeMode(bool isOn)
        {
            useFakeMode = isOn;
        }

        public void SetUseXYPlane(bool isOn)
        {
            useXYPlane = isOn;
        }

        /// <summary>
        /// 도크의 '건설' 버튼 등을 눌렀을 때 외부(UIDockController)에서 호출하여 모드를 켭니다.
        /// </summary>
        public void ToggleBuildMode(bool isOn)
        {
            _isBuildingMode = isOn;
            if (ghostRenderer != null) ghostRenderer.gameObject.SetActive(isOn);
        }

        private long GetTileCost(TileType type)
        {
            if (availableTiles == null) return 0;
            foreach (var t in availableTiles)
            {
                if (t != null && t.Category == type) return t.BuildCost;
            }
            return 0; // Default
        }

        public void UndoLastAction()
        {
            if (_undoStack.Count == 0) return;

            string targetGroupId = _undoStack.Peek().GroupId;

            while (_undoStack.Count > 0 && _undoStack.Peek().GroupId == targetGroupId)
            {
                var action = _undoStack.Peek();
                if (_services != null && _services.Placement != null)
                {
                    if (action.ActionType == PlacementActionType.Place)
                    {
                        // 덮어쓰기 취소 시, 기존에 차액을 환불받았다면(netCost < 0) 되돌릴 때 돈을 다시 지불해야 함
                        if (action.PreviousType != TileType.Empty && action.Cost < 0)
                        {
                            if (_services.Economy != null && _services.Economy.Coins < -action.Cost)
                            {
                                Debug.LogWarning("[Undo] 코인이 부족하여 덮어쓰기를 복구할 수 없습니다!");
                                return; // 그룹 내 실패 시 롤백 중단
                            }
                        }

                        // 현재 지어진 건물을 철거
                        if (_services.Placement.Remove(action.Coord))
                        {
                            if (action.PreviousType != TileType.Empty)
                            {
                                // 덮어쓰기 복구: 원래 건물 다시 짓기
                                _services.Placement.Place(action.Coord, action.PreviousType);
                                
                                if (_services.Economy != null)
                                {
                                    if (action.Cost > 0) _services.Economy.AddCoins(action.Cost, "Undo Overwrite Refund");
                                    else if (action.Cost < 0) _services.Economy.TrySpend(-action.Cost);
                                }
                                Debug.Log($"[Undo] 덮어쓰기 취소됨 (복구 완료 및 역연산): {action.Coord}");
                            }
                            else
                            {
                                // 순수 빈 땅에 지은 것 복구
                                if (_services.Economy != null && action.Cost > 0)
                                    _services.Economy.AddCoins(action.Cost, "Undo Build 100% Refund");
                                Debug.Log($"[Undo] Place 취소됨 (철거 수행 및 환불 {action.Cost}): {action.Coord}");
                            }
                            _undoStack.Pop(); // 성공적으로 복구 시 스택에서 제거
                        }
                        else return; // 엔진 롤백 실패 시 중단
                    }
                    else if (action.ActionType == PlacementActionType.Remove)
                    {
                        // 철거한 걸 되돌리기 -> 다시 원래 건물로 건설
                        if (_services.Economy != null && action.Cost > 0 && _services.Economy.Coins < action.Cost)
                        {
                            Debug.LogWarning("[Undo] 코인이 부족하여 철거를 복구할 수 없습니다!");
                            return; // 잔액 부족 시 롤백 중단
                        }

                        if (_services.Placement.Place(action.Coord, action.PreviousType))
                        {
                            if (_services.Economy != null && action.Cost > 0)
                                _services.Economy.TrySpend(action.Cost);
                            Debug.Log($"[Undo] Remove 취소됨 (복구 수행 및 {action.Cost} 차감): {action.Coord}");
                            _undoStack.Pop(); // 성공 시 스택에서 제거
                        }
                        else return; // 엔진 롤백 실패 시 중단
                    }
                }
            }
        }

        private void Update()
        {
            // 6. 마우스 우클릭 시 철거 확인창 호출 (도로는 드래그 즉시 철거 지원)
            if (Mouse.current != null)
            {
                bool rightPressed = Mouse.current.rightButton.isPressed;
                bool rightPressedThisFrame = Mouse.current.rightButton.wasPressedThisFrame;

                if (rightPressedThisFrame)
                {
                    if (!IsPointerOverBlockingUI())
                    {
                        _rightClickStartCoord = GetMouseGridCoordinate();
                        _currentDragGroupId = Guid.NewGuid().ToString();
                    }
                }

                if (rightPressed)
                {
                    // 마우스가 UI 패널 위에 있으면 씬 클릭 무시. (단, 맵에서 시작된 클릭만 인정하여 드래그 철거 방어)
                    if (!IsPointerOverBlockingUI() && _rightClickStartCoord.HasValue)
                    {
                        Vector2Int rightClickCoord = GetMouseGridCoordinate();

                        // 중복 철거(드래그 중 같은 타일 반복 철거) 방지
                        if (_lastRemovedCoord == null || _lastRemovedCoord.Value != rightClickCoord)
                        {
                            TileType currentTileType = TileType.Empty;
                            if (useFakeMode)
                            {
                                currentTileType = TileType.Road;
                            }
                            else if (_services != null && _services.TileData != null)
                            {
                                currentTileType = _services.TileData.GetTileType(rightClickCoord);
                            }

                            if (currentTileType != TileType.Empty)
                            {
                                // 모든 타일(도로/건물) 우클릭 드래그 시 즉시 철거
                                _lastRemovedCoord = rightClickCoord;
                                TileType oldType = _currentType;
                                _currentType = TileType.Empty; 
                                PlaceInfrastructure(rightClickCoord);
                                _currentType = oldType;
                            }
                        }
                    }
                }

                if (Mouse.current.rightButton.wasReleasedThisFrame)
                {
                    if (!IsPointerOverBlockingUI())
                    {
                        Vector2Int rightClickCoord = GetMouseGridCoordinate();
                        
                        // 단일 클릭 판정 (누른 곳과 뗀 곳이 같음)
                        if (_rightClickStartCoord != null && _rightClickStartCoord.Value == rightClickCoord)
                        {
                            // 드래그 중에 이미 철거 처리됨 — 추가 동작 불필요
                            TileType clickedType = TileType.Empty;
                            if (!useFakeMode && _services != null && _services.TileData != null)
                            {
                                clickedType = _services.TileData.GetTileType(rightClickCoord);
                            }

                            // 빈 타일(공터)에 우클릭 시 건설 모드 쾌적하게 종료
                            if (clickedType == TileType.Empty)
                            {
                                ToggleBuildMode(false);
                                UnityEngine.Object.FindFirstObjectByType<UIDockController>()?.CloseAllPanels();
                            }
                        }
                    }
                    _lastRemovedCoord = null;
                    _rightClickStartCoord = null;
                    _currentDragGroupId = null;
                }
            }
            if (!_isBuildingMode || ghostRenderer == null) return;

            // 1. 방어 로직: 마우스가 UI(버튼, 패널) 위에 있으면 바닥 클릭(건설)을 방지합니다.
            if (IsPointerOverBlockingUI())
            {
                ghostRenderer.gameObject.SetActive(false);
                _lastPlacedCoord = null;
                return;
            }
            ghostRenderer.gameObject.SetActive(true);

            // 2. 마우스 좌표 -> 20x20 월드 그리드(Vector2Int) 맵핑
            Vector2Int gridCoord = GetMouseGridCoordinate();
            
            // 3. 고스트 위치 스냅 (일단 바닥이 없으므로 허공(XZ평면 혹은 XY평면)에 딱딱 맞춰 이동시킵니다)
            // 3D 쿼터뷰(Y=0 바닥) 기준 맵핑. 만약 2D 게임이라면 y대신 z를 0으로 주고 세팅합니다.
            ghostRenderer.transform.position = GetGhostPosition(gridCoord);

            // 4. 건설 유효성 검증 (엔진 디커플링 통신)
            bool canPlace = CheckCanPlace(gridCoord);
            ghostRenderer.color = canPlace ? colorValid : colorInvalid;

            // 5. 마우스 좌클릭 시 최종 건설 명령 하달 (드래그 연속 건설 지원)
            if (Mouse.current != null)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame)
                {
                    _currentDragGroupId = Guid.NewGuid().ToString(); // 좌클릭 드래그 시작 시 새 Undo 그룹 생성
                }

                if (Mouse.current.leftButton.isPressed)
                {
                    if (_lastPlacedCoord == null)
                    {
                        if (canPlace)
                        {
                            PlaceInfrastructure(gridCoord);
                        }

                        _lastPlacedCoord = gridCoord;
                    }
                    else if (_lastPlacedCoord.Value != gridCoord)
                    {
                        PlaceDragPath(_lastPlacedCoord.Value, gridCoord);
                        _lastPlacedCoord = gridCoord;
                    }
                }

                // 마우스를 떼면 마지막 설치 좌표 초기화
                if (Mouse.current.leftButton.wasReleasedThisFrame)
                {
                    _lastPlacedCoord = null;
                    _currentDragGroupId = null;
                }
            }
        }

        private void PlaceDragPath(Vector2Int from, Vector2Int to)
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

        private bool IsPointerOverBlockingUI()
        {
            if (confirmPopup != null && confirmPopup.gameObject.activeInHierarchy)
            {
                return true;
            }

            if (EventSystem.current == null || Mouse.current == null)
            {
                return false;
            }

            PointerEventData eventData = new PointerEventData(EventSystem.current)
            {
                position = Mouse.current.position.ReadValue()
            };

            _uiRaycastResults.Clear();
            EventSystem.current.RaycastAll(eventData, _uiRaycastResults);

            for (int i = 0; i < _uiRaycastResults.Count; i++)
            {
                if (_uiRaycastResults[i].gameObject.GetComponentInParent<Selectable>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        private void TryPlaceDragTile(Vector2Int coord)
        {
            if (CheckCanPlace(coord))
            {
                PlaceInfrastructure(coord);
            }
        }

        public Vector3 GetGhostPosition(Vector2Int gridCoord)
        {
            return useXYPlane
                ? new Vector3(gridCoord.x + 0.5f, gridCoord.y + 0.5f, -0.6f)
                : new Vector3(gridCoord.x, 0, gridCoord.y);
        }

        public Vector2Int GetMouseGridCoordinate()
        {
            Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

            if (useXYPlane && Camera.main != null)
            {
                float distance = Mathf.Abs(Camera.main.transform.position.z);
                Vector3 worldPos = Camera.main.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, distance));
                return new Vector2Int(Mathf.FloorToInt(worldPos.x), Mathf.FloorToInt(worldPos.y));
            }
            // 화면 좌표를 평면(Y=0)에 투사하여 정수형 그리드 좌표로 뽑아냅니다.
            Ray ray = Camera.main.ScreenPointToRay(mousePos);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero); // Y가 0인 가상의 바닥 평면
            
            if (groundPlane.Raycast(ray, out float enter))
            {
                Vector3 hitPoint = ray.GetPoint(enter);
                return new Vector2Int(Mathf.RoundToInt(hitPoint.x), Mathf.RoundToInt(hitPoint.z));
            }
            
            // (Fallback) 만약 2D 세팅일 경우를 위한 안전장치
            Vector3 fallbackWorldPos = Camera.main.ScreenToWorldPoint(mousePos);
            return new Vector2Int(Mathf.RoundToInt(fallbackWorldPos.x), Mathf.RoundToInt(fallbackWorldPos.y));
        }

        private bool CheckCanPlace(Vector2Int coord)
        {
            if (useFakeMode) return true; // UI 독립 테스트를 위해 무조건 초록색(건설 가능) 반환
            
            if (_services != null && _services.Placement != null && _services.TileData != null)
            {
                if (!GridUtil.IsInside(coord)) return false; // 맵 밖은 건설/선택 불가

                if (_currentType == TileType.Empty) return true;

                // 덮어쓰기 허용 로직: 만약 현재 마우스 위치에 '다른 건물'이 있다면 건설(덮어쓰기)이 가능하다고 판단
                TileType previousType = _services.TileData.GetTileType(coord);
                if (previousType != TileType.Empty && previousType != _currentType)
                {
                    return true;
                }

                // 빈 땅이거나 그 외의 경우는 코어 엔진의 기본 룰(CanPlace)을 따름
                return _services.Placement.CanPlace(coord, _currentType); 
            }
            return false;
        }

        private void PlaceInfrastructure(Vector2Int coord)
        {
            if (useFakeMode)
            {
                Debug.Log($"[UI Fake Mode] 타일 {_currentType} 적용 성공! 위치: {coord}");
                return;
            }

            string groupId = string.IsNullOrEmpty(_currentDragGroupId) ? Guid.NewGuid().ToString() : _currentDragGroupId;

            if (_services != null && _services.Placement != null && _services.TileData != null)
            {
                TileType previousType = _services.TileData.GetTileType(coord);

                if (_currentType == TileType.Empty)
                {
                    long refundCost = GetTileCost(previousType);
                    // 철거 시도 및 성공 여부 확인
                    if (_services.Placement.Remove(coord))
                    {
                        if (_services.Economy != null && refundCost > 0)
                            _services.Economy.AddCoins(refundCost, "Demolish Refund");
                            
                        _undoStack.Push(new PlacementAction { GroupId = groupId, ActionType = PlacementActionType.Remove, Coord = coord, PreviousType = previousType, NewType = TileType.Empty, Cost = refundCost });
                        Debug.Log($"[Real Mode] 코어 엔진에 {coord} 위치 철거 명령 전달 (환불 {refundCost}) 및 Undo 기록 완료.");
                    }
                }
                else
                {
                    long buildCost = GetTileCost(_currentType);
                    
                    // 덮어쓰기 로직: 기존에 다른 건물이 있다면 차액 계산 후 덮어쓰기 시도
                    if (previousType != TileType.Empty && previousType != _currentType)
                    {
                        long refundCost = GetTileCost(previousType);
                        long netCost = buildCost - refundCost;
                        
                        if (netCost > 0 && _services.Economy != null && _services.Economy.Coins < netCost)
                        {
                            Debug.LogWarning("[UI] 코인이 부족하여 덮어쓰기를 할 수 없습니다!");
                            return;
                        }

                        // 먼저 기존 건물 철거
                        if (_services.Placement.Remove(coord))
                        {
                            if (_services.Placement.Place(coord, _currentType))
                            {
                                if (_services.Economy != null)
                                {
                                    if (netCost > 0) _services.Economy.TrySpend(netCost);
                                    else if (netCost < 0) _services.Economy.AddCoins(-netCost, "Overwrite Refund");
                                }
                                
                                // Undo 기록은 덮어쓰기(Place)로 기록하여 복구 시 previousType로 돌아가게 함
                                _undoStack.Push(new PlacementAction { GroupId = groupId, ActionType = PlacementActionType.Place, Coord = coord, PreviousType = previousType, NewType = _currentType, Cost = netCost });
                                Debug.Log($"[Real Mode] 덮어쓰기 성공! {previousType} -> {_currentType}. 차액: {netCost}");
                            }
                            else
                            {
                                // 만약 짓는 데 실패했다면 원복
                                _services.Placement.Place(coord, previousType);
                            }
                        }
                    }
                    else if (previousType == TileType.Empty)
                    {
                        if (_services.Economy != null && buildCost > 0 && _services.Economy.Coins < buildCost)
                        {
                            Debug.LogWarning("[UI] 코인이 부족하여 건설할 수 없습니다!");
                            return;
                        }

                        // 순수 빈땅 건설 시도
                        if (_services.Placement.Place(coord, _currentType))
                        {
                            if (_services.Economy != null && buildCost > 0)
                                _services.Economy.TrySpend(buildCost);
                                
                            _undoStack.Push(new PlacementAction { GroupId = groupId, ActionType = PlacementActionType.Place, Coord = coord, PreviousType = previousType, NewType = _currentType, Cost = buildCost });
                            Debug.Log($"[Real Mode] 코어 엔진에 {coord} 위치 {_currentType} 건설 명령 전달 (비용 {buildCost}) 및 Undo 기록 완료.");
                        }
                    }
                }
            }
        }
    }
}
