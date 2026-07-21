using Kirurobo;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace CityFlow.View
{
    public sealed class FloatingWindowDragHandle : MonoBehaviour,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IPointerUpHandler
    {
        private UniWindowController windowController;
        private Vector2 dragOffset;
        private bool hitTestWasEnabled;

        public bool IsDragging { get; private set; }

        public void Initialize(UniWindowController controller)
        {
            windowController = controller;
        }

        private void Update()
        {
            if (!IsDragging || windowController == null)
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.isPressed)
            {
                EndDragging();
                return;
            }

            windowController.windowPosition = windowController.cursorPosition + dragOffset;
        }

        private void OnDisable()
        {
            EndDragging();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (windowController == null
                || eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            dragOffset = windowController.windowPosition - windowController.cursorPosition;
            hitTestWasEnabled = windowController.isHitTestEnabled;
            windowController.isHitTestEnabled = false;
            windowController.isClickThrough = false;
            IsDragging = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (IsDragging && windowController != null)
            {
                windowController.windowPosition = windowController.cursorPosition + dragOffset;
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            EndDragging();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            EndDragging();
        }

        private void EndDragging()
        {
            if (!IsDragging)
            {
                return;
            }

            if (windowController != null)
            {
                windowController.isHitTestEnabled = hitTestWasEnabled;
            }

            IsDragging = false;
        }

        // Unity setup: Added to the baked title bar drag area by FloatingWindowTitleBarBaker.
    }
}
