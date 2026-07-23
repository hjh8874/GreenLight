using UnityEngine;
using UnityEngine.InputSystem;

namespace CityFlow.UI
{
    /// <summary>
    /// ESC 키를 누르면 현재 열려있는 UI를 우선순위(스택) 순서대로 하나씩 닫는 중앙 컨트롤러.
    /// 우선순위: 팝업 > 분석 카드 > 도크 서브패널
    /// </summary>
    public sealed class EscapeUIController : MonoBehaviour
    {
        [Header("UI References (우선순위 순서)")]
        [Tooltip("1순위: 확인 팝업 (UI_ConfirmPopup)")]
        [SerializeField] private ConfirmPopupController confirmPopup;

        [Tooltip("2순위: 건물 정보 카드")]
        [SerializeField] private BuildingInfoCardController buildingInfoCard;

        [Tooltip("3순위: 좌측 하단 분석 카드")]
        [SerializeField] private AnalysisCardController analysisCard;

        [Tooltip("4순위: 우측 도크 컨트롤러")]
        [SerializeField] private UIDockController dockController;

        public void Configure(
            ConfirmPopupController popup,
            AnalysisCardController analysis,
            UIDockController dock)
        {
            confirmPopup = popup;
            analysisCard = analysis;
            dockController = dock;
        }

        private void Update()
        {
            if (Keyboard.current == null) return;
            if (!Keyboard.current.escapeKey.wasPressedThisFrame) return;

            // 1순위: 팝업이 열려있으면 팝업부터 닫기
            if (confirmPopup != null && confirmPopup.gameObject.activeSelf)
            {
                confirmPopup.Hide();
                return;
            }

            // 2순위: 건물 정보 카드가 열려있으면 카드 닫기
            if (buildingInfoCard != null && buildingInfoCard.IsOpen)
            {
                buildingInfoCard.CloseCard();
                return;
            }

            // 3순위: 분석 카드가 열려있으면 카드 닫기
            if (analysisCard != null && analysisCard.gameObject.activeSelf)
            {
                analysisCard.CloseCard();
                return;
            }

            // 4순위: 도크 서브패널이 열려있으면 전부 닫기
            if (dockController != null)
            {
                dockController.CloseAllPanels();
                return;
            }
        }
    }
}
