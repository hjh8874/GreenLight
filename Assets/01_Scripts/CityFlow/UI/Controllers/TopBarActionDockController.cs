using CityFlow.UI.Feed;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI.Controllers
{
    public sealed class TopBarActionDockController : MonoBehaviour
    {
        private const float ButtonAlpha = 0.62f;

        private void Awake()
        {
            ApplyLayout();
        }

        private void Start()
        {
            ApplyLayout();
        }

        internal void ApplyLayout()
        {
            RectTransform rect = transform as RectTransform;
            RectTransform topBar = FindTopBar();
            if (rect == null || topBar == null)
            {
                return;
            }

            if (rect.parent != topBar)
            {
                rect.SetParent(topBar, false);
            }

            float topBarHeight = topBar != null && topBar.rect.height > 0f
                ? topBar.rect.height
                : HudTopBarLayout.TopBarHeight;

            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(
                -HudTopBarLayout.ActionDockRightInset,
                0f);
            rect.sizeDelta = new Vector2(
                HudTopBarLayout.ActionDockWidth,
                Mathf.Max(
                    1f,
                    topBarHeight - HudTopBarLayout.VerticalInset * 2f));

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
                layout.padding = new RectOffset(6, 6, 6, 6);
                layout.spacing = 6f;
                layout.childAlignment = TextAnchor.MiddleCenter;
            }

            Button[] buttons = GetComponentsInChildren<Button>(true);
            for (int index = 0; index < buttons.Length; index++)
            {
                RectTransform buttonRect =
                    buttons[index].transform as RectTransform;
                if (buttonRect != null)
                {
                    buttonRect.sizeDelta =
                        new Vector2(
                            HudTopBarLayout.ActionButtonWidth,
                            HudTopBarLayout.ActionButtonHeight);
                }

                LayoutElement layoutElement =
                    buttons[index].GetComponent<LayoutElement>();
                if (layoutElement != null)
                {
                    layoutElement.minWidth =
                        HudTopBarLayout.ActionButtonWidth;
                    layoutElement.preferredWidth =
                        HudTopBarLayout.ActionButtonWidth;
                    layoutElement.minHeight =
                        HudTopBarLayout.ActionButtonHeight;
                    layoutElement.preferredHeight =
                        HudTopBarLayout.ActionButtonHeight;
                }

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

            GreenFeedPanelController feed =
                FindAnyObjectByType<GreenFeedPanelController>(
                    FindObjectsInactive.Include);
            feed?.AttachTickerToTopBar(topBar);
        }

        private RectTransform FindTopBar()
        {
            if (transform.parent is RectTransform parent
                && parent.name == "HUD_TopBar")
            {
                return parent;
            }

            Transform current = transform.parent;
            while (current != null)
            {
                Transform directChild = current.Find("HUD_TopBar");
                if (directChild is RectTransform topBar)
                {
                    return topBar;
                }

                current = current.parent;
            }

            return null;
        }
    }
}
