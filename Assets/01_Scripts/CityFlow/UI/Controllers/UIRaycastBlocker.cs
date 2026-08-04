using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CityFlow.UI.Controllers
{
    internal sealed class UIRaycastBlocker
    {
        private readonly List<RaycastResult> _raycastResults = new List<RaycastResult>();
        private EventSystem _eventSystem;
        private PointerEventData _pointerEventData;

        public bool IsPointerOverBlockingUI()
        {
            if (CityFlow.UI.WeeklySettlementPopup.IsInteractionBlocked)
            {
                return true;
            }

            EventSystem currentEventSystem = EventSystem.current;
            if (currentEventSystem == null || Mouse.current == null)
            {
                return false;
            }

            if (_pointerEventData == null || _eventSystem != currentEventSystem)
            {
                _eventSystem = currentEventSystem;
                _pointerEventData = new PointerEventData(currentEventSystem);
            }

            _pointerEventData.position = Mouse.current.position.ReadValue();
            _raycastResults.Clear();
            currentEventSystem.RaycastAll(_pointerEventData, _raycastResults);

            for (int i = 0; i < _raycastResults.Count; i++)
            {
                GameObject hitObject = _raycastResults[i].gameObject;
                if (hitObject.GetComponentInParent<Selectable>() != null ||
                    hitObject.GetComponentInParent<CityFlow.UI.BuildPanelController>() != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
