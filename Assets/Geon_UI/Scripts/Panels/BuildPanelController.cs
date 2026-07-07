using UnityEngine;
using UnityEngine.UI;
using CityFlow.Contracts;

namespace CityFlow.UI.Geon
{
    public class BuildPanelController : MonoBehaviour
    {
        [Header("System References")]
        [Tooltip("씬에 배치된 PlacementManager (PlacementController) 오브젝트 연결")]
        [SerializeField] private PlacementController placementController;

        [Header("Menu Buttons")]
        [SerializeField] private Button btnRoad;
        [SerializeField] private Button btnHouse;
        [SerializeField] private Button btnOffice;
        [SerializeField] private Button btnRemove; // 철거 버튼

        private void Start()
        {
            if (placementController == null)
            {
                Debug.LogError("[BuildPanelController] PlacementController가 할당되지 않았습니다. 인스펙터를 확인해주세요.");
                return;
            }

            // 버튼 클릭 시 타일 타입 변경 함수 자동 바인딩
            if (btnRoad != null) btnRoad.onClick.AddListener(() => placementController.SetBuildType(TileType.Road));
            if (btnHouse != null) btnHouse.onClick.AddListener(() => placementController.SetBuildType(TileType.House));
            if (btnOffice != null) btnOffice.onClick.AddListener(() => placementController.SetBuildType(TileType.Office));
            
            // 철거 기능은 Empty 타일 타입으로 전달
            if (btnRemove != null) btnRemove.onClick.AddListener(() => placementController.SetBuildType(TileType.Empty)); 
        }
    }
}
