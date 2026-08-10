using UnityEngine;
using UnityEngine.EventSystems;

namespace CityFlow.UI.Feed
{
    /// <summary>
    /// 피드 UI의 포인터 입력을 컨트롤러로 넘긴다.
    /// 클릭은 Button.onClick(UnityEvent) 대신 여기서 처리한다 — 베이커가 심는
    /// 지속 리스너는 씬 직렬화에 실패해도 조용해서, 참조 필드가 더 안전하다.
    /// </summary>
    public sealed class GreenFeedHoverRelay :
        MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerClickHandler
    {
        public enum ClickAction
        {
            /// <summary>
            /// 리베이킹 전 씬의 값. 이 필드가 새로 생기기 전에 구워진 릴레이는 전부
            /// 0으로 로드되므로, 0을 **레거시 호버 개폐**로 해석해야 기존 씬에서
            /// 피드가 계속 열린다. "아무 동작 없음"이 필요하면 Passive를 쓸 것.
            /// </summary>
            None = 0,
            Toggle = 1,
            Close = 2,
            // 글이 가리키는 타일을 선택한다. 기존 숫자는 씬에 직렬화돼 있으므로
            // 뒤에 잇기만 한다.
            Locate = 3,
            /// <summary>
            /// 클릭에 반응하지 않는다. 새 베이커가 패널 본체에 쓴다 —
            /// None을 쓰면 레거시 호버로 해석돼 패널 위에서 개폐가 요동친다.
            /// </summary>
            Passive = 4
        }

        [SerializeField] private GreenFeedPanelController controller;
        [SerializeField] private ClickAction clickAction = ClickAction.None;
        // 패널처럼 스크롤되는 곳만 휠 가드를 잡는다.
        [SerializeField] private bool capturesPointer = true;
        // Locate에만 쓴다. 없으면 클릭이 아무 일도 하지 않을 뿐 다른 기능은 멀쩡하다.
        [SerializeField] private TileSelectionController tileSelection;

        public GreenFeedPanelController Controller => controller;
        public TileSelectionController TileSelection => tileSelection;
        public ClickAction Action => clickAction;

        public void RebindRuntimeReferences(
            GreenFeedPanelController targetController,
            TileSelectionController targetTileSelection)
        {
            if (targetController != null)
            {
                controller = targetController;
            }

            if (targetTileSelection != null)
            {
                tileSelection = targetTileSelection;
            }
        }

        public void Configure(
            GreenFeedPanelController targetController,
            ClickAction action = ClickAction.None,
            bool capturePointer = true,
            TileSelectionController targetTileSelection = null)
        {
            controller = targetController;
            clickAction = action;
            capturesPointer = capturePointer;
            tileSelection = targetTileSelection;
        }

        // 리베이킹 전 씬은 clickAction이 0(None)으로 로드된다. 그 씬에서는
        // 예전처럼 호버가 패널을 열어야 기능이 죽지 않는다.
        private bool IsLegacyHoverMode => clickAction == ClickAction.None;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (controller == null) return;

            if (IsLegacyHoverMode)
            {
                controller.NotifyLegacyHoverEntered();
                return;
            }

            if (capturesPointer) controller.NotifyPointerEntered();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (controller == null) return;

            if (IsLegacyHoverMode)
            {
                controller.NotifyLegacyHoverExited();
                return;
            }

            if (capturesPointer) controller.NotifyPointerExited();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            switch (clickAction)
            {
                case ClickAction.Toggle:
                    controller?.Toggle();
                    break;
                case ClickAction.Close:
                    controller?.Close();
                    break;
                case ClickAction.Locate:
                    LocateTile();
                    break;
            }
        }

        private void LocateTile()
        {
            // 좌표가 없는 글(시간대 훅·차량 급증 등)은 가리킬 곳이 없다.
            // 아무 일도 하지 않는 게 맞다 — (0,0)으로 보내면 엉뚱한 곳이 열린다.
            if (tileSelection == null ||
                !TryGetComponent(out GreenFeedPostView view) ||
                !view.TryGetTile(out Vector2Int target))
            {
                return;
            }

            tileSelection.SelectTile(target);
        }

        // Unity setup: Attach this relay to the hover handle and feed panel, then assign one controller.
    }
}
