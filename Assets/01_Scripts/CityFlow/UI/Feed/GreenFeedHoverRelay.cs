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
            None = 0,
            Toggle = 1,
            Close = 2,
            // 글이 가리키는 타일을 선택한다. 기존 숫자는 씬에 직렬화돼 있으므로
            // 뒤에 잇기만 한다.
            Locate = 3
        }

        [SerializeField] private GreenFeedPanelController controller;
        [SerializeField] private ClickAction clickAction = ClickAction.None;
        // 패널처럼 스크롤되는 곳만 휠 가드를 잡는다.
        [SerializeField] private bool capturesPointer = true;
        // Locate에만 쓴다. 없으면 클릭이 아무 일도 하지 않을 뿐 다른 기능은 멀쩡하다.
        [SerializeField] private TileSelectionController tileSelection;

        public GreenFeedPanelController Controller => controller;

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

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (controller != null && capturesPointer)
            {
                controller.NotifyPointerEntered();
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (controller != null && capturesPointer)
            {
                controller.NotifyPointerExited();
            }
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
