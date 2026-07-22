using UnityEngine;
using UnityEngine.EventSystems;

namespace CityFlow.UI.Feed
{
    public sealed class GreenFeedHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private GreenFeedPanelController controller;

        public GreenFeedPanelController Controller => controller;

        public void Configure(GreenFeedPanelController targetController)
        {
            controller = targetController;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (controller != null)
            {
                controller.NotifyPointerEntered();
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (controller != null)
            {
                controller.NotifyPointerExited();
            }
        }

        // Unity setup: Attach this relay to the hover handle and feed panel, then assign one controller.
    }
}
