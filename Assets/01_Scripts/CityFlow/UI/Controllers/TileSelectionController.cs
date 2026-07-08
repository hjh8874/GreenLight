using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using CityFlow.Contracts;

namespace CityFlow.UI
{
    public class TileSelectionController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private AnalysisCardController analysisCard;
        [SerializeField] private PlacementController placementController;

        [Header("Visuals")]
        [Tooltip("타일을 선택했을 때 바닥에 표시될 강조(하이라이트) 박스")]
        [SerializeField] private GameObject highlightBox; 

        private void Start()
        {
            // 시작 시 상세 카드와 하이라이트 박스는 숨겨둡니다.
            DeselectTile();
        }

        private void Update()
        {
            // 1. 방어 로직: 현재 건설 모드(고스트가 떠다니는 상태)라면 타일 선택을 무시합니다.
            if (placementController != null && placementController.IsBuildingMode)
            {
                DeselectTile(); // 건설 모드 켜지면 분석 카드도 바로 닫음
                return;
            }

            // 2. 마우스 좌클릭 감지 (New Input System)
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                // UI(버튼 등) 위를 클릭했다면 무시
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                    return;

                Vector2Int? gridCoord = TryGetGridCoordinate();
                
                if (gridCoord.HasValue && GridUtil.IsInside(gridCoord.Value))
                {
                    SelectTile(gridCoord.Value);
                }
                else
                {
                    DeselectTile(); // 맵 바깥이나 허공 클릭 시 해제
                }
            }
        }

        private Vector2Int? TryGetGridCoordinate()
        {
            Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            Ray ray = Camera.main.ScreenPointToRay(mousePos);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero); // Y=0 바닥
            
            if (groundPlane.Raycast(ray, out float enter))
            {
                Vector3 hitPoint = ray.GetPoint(enter);
                return new Vector2Int(Mathf.RoundToInt(hitPoint.x), Mathf.RoundToInt(hitPoint.z));
            }
            return null;
        }

        private void SelectTile(Vector2Int coord)
        {
            // 하이라이트 박스 이동
            if (highlightBox != null)
            {
                highlightBox.SetActive(true);
                highlightBox.transform.position = new Vector3(coord.x, 0, coord.y);
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
    }
}
