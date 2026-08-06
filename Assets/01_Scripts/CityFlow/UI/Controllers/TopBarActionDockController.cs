using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI.Controllers
{
    public sealed class TopBarActionDockController : MonoBehaviour
    {
        private const float RightInset = 8f;
        private const float DefaultTopBarHeight = 60f;
        private const float ButtonAlpha = 0.62f;

        private void Awake()
        {
            ApplyLayout();
        }

        private void ApplyLayout()
        {
            RectTransform rect = transform as RectTransform;
            RectTransform topBar = transform.parent != null
                ? transform.parent.Find("HUD_TopBar") as RectTransform
                : null;
            float topBarHeight = topBar != null && topBar.rect.height > 0f
                ? topBar.rect.height
                : DefaultTopBarHeight;

            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-RightInset, 0f);
            rect.sizeDelta = new Vector2(204f, topBarHeight);

            Image background = GetComponent<Image>();
            if (background != null)
            {
                Color color = background.color;
                color.a = 0.42f;
                background.color = color;
            }

            HorizontalLayoutGroup layout =
                GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
            {
                layout.padding = new RectOffset(4, 4, 4, 4);
                layout.spacing = 8f;
                layout.childAlignment = TextAnchor.MiddleCenter;
            }

            Button[] buttons = GetComponentsInChildren<Button>(true);
            for (int index = 0; index < buttons.Length; index++)
            {
                Image image = buttons[index].targetGraphic as Image;
                if (image == null)
                {
                    image = buttons[index].GetComponent<Image>();
                }

                if (image == null)
                {
                    continue;
                }

                Color color = image.color;
                color.a = ButtonAlpha;
                image.color = color;
            }
        }
    }
}
