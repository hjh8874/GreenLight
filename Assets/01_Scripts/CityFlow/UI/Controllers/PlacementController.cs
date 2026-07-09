using System;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;
using UnityEngine.EventSystems; // UI 클릭 감지용
using UnityEngine.InputSystem;

namespace CityFlow.UI
{
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
        
        private CityFlowServices _services;
        private bool _isBuildingMode = false;
        
        public bool IsBuildingMode => _isBuildingMode;
        
        private TileType _currentType = TileType.Road; 
        private Vector2Int? _lastPlacedCoord = null;

        /// <summary>
        /// 건설 패널(BuildPanelController) 등에서 타일 타입을 변경할 때 호출합니다.
        /// </summary>
        public void SetBuildType(TileType type)
        {
            _currentType = type;
            Debug.Log($"[PlacementController] 건설 모드 변경됨: {_currentType}");
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

        /// <summary>
        /// 도크의 '건설' 버튼 등을 눌렀을 때 외부(UIDockController)에서 호출하여 모드를 켭니다.
        /// </summary>
        public void ToggleBuildMode(bool isOn)
        {
            _isBuildingMode = isOn;
            if (ghostRenderer != null) ghostRenderer.gameObject.SetActive(isOn);
        }

        private void Update()
        {
            if (!_isBuildingMode || ghostRenderer == null) return;

            // 1. 방어 로직: 마우스가 UI(버튼, 패널) 위에 있으면 바닥 클릭(건설)을 방지합니다.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                ghostRenderer.gameObject.SetActive(false);
                return;
            }
            ghostRenderer.gameObject.SetActive(true);

            // 2. 마우스 좌표 -> 20x20 월드 그리드(Vector2Int) 맵핑
            Vector2Int gridCoord = GetMouseGridCoordinate();
            
            // 3. 고스트 위치 스냅 (일단 바닥이 없으므로 허공(XZ평면 혹은 XY평면)에 딱딱 맞춰 이동시킵니다)
            // 3D 쿼터뷰(Y=0 바닥) 기준 맵핑. 만약 2D 게임이라면 y대신 z를 0으로 주고 세팅합니다.
            ghostRenderer.transform.position = new Vector3(gridCoord.x, 0, gridCoord.y);

            // 4. 건설 유효성 검증 (엔진 디커플링 통신)
            bool canPlace = CheckCanPlace(gridCoord);
            ghostRenderer.color = canPlace ? colorValid : colorInvalid;

            // 5. 마우스 좌클릭 시 최종 건설 명령 하달 (드래그 연속 건설 지원)
            if (Mouse.current != null)
            {
                if (Mouse.current.leftButton.isPressed && canPlace)
                {
                    // 동일 타일에 중복 건설을 막기 위한 방어 로직
                    if (_lastPlacedCoord == null || _lastPlacedCoord.Value != gridCoord)
                    {
                        PlaceInfrastructure(gridCoord);
                        _lastPlacedCoord = gridCoord;
                    }
                }

                // 마우스를 떼면 마지막 설치 좌표 초기화
                if (Mouse.current.leftButton.wasReleasedThisFrame)
                {
                    _lastPlacedCoord = null;
                }
            }
        }

        private Vector2Int GetMouseGridCoordinate()
        {
            Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            // 화면 좌표를 평면(Y=0)에 투사하여 정수형 그리드 좌표로 뽑아냅니다.
            Ray ray = Camera.main.ScreenPointToRay(mousePos);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero); // Y가 0인 가상의 바닥 평면
            
            if (groundPlane.Raycast(ray, out float enter))
            {
                Vector3 hitPoint = ray.GetPoint(enter);
                return new Vector2Int(Mathf.RoundToInt(hitPoint.x), Mathf.RoundToInt(hitPoint.z));
            }
            
            // (Fallback) 만약 2D 세팅일 경우를 위한 안전장치
            Vector3 worldPos = Camera.main.ScreenToWorldPoint(mousePos);
            return new Vector2Int(Mathf.RoundToInt(worldPos.x), Mathf.RoundToInt(worldPos.y));
        }

        private bool CheckCanPlace(Vector2Int coord)
        {
            if (useFakeMode) return true; // UI 독립 테스트를 위해 무조건 초록색(건설 가능) 반환
            
            if (_services != null && _services.Placement != null)
            {
                if (!GridUtil.IsInside(coord)) return false; // 맵 밖은 건설/선택 불가

                // 실제 연산은 코어 모듈(IPlacementService)에 완벽히 위임합니다!
                // 철거 모드(Empty)일 때는 유효성 검사 생략(항상 true) 하거나 별도 로직 태움
                return _currentType == TileType.Empty ? true : _services.Placement.CanPlace(coord, _currentType); 
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

            if (_services != null && _services.Placement != null)
            {
                if (_currentType == TileType.Empty)
                {
                    // 철거
                    _services.Placement.Remove(coord);
                    Debug.Log($"[Real Mode] 코어 엔진에 {coord} 위치 철거 명령 전달 완료.");
                }
                else
                {
                    // 건설
                    _services.Placement.Place(coord, _currentType);
                    Debug.Log($"[Real Mode] 코어 엔진에 {coord} 위치 {_currentType} 건설 명령 전달 완료.");
                }
            }
        }
    }
}
