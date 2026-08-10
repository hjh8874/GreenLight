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
            bool hasTopBar = rect != null && topBar != null;
            if (hasTopBar)
            {
                if (rect.parent != topBar)
                {
                    rect.SetParent(topBar, false);
                }

                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 0f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(
                    HudTopBarLayout.ActionDockLeftInset,
                    -HudTopBarLayout.ActionDockTopGap);
                rect.sizeDelta = new Vector2(
                    HudTopBarLayout.ActionDockWidth,
                    HudTopBarLayout.ActionDockHeight);
            }

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
                layout.padding = hasTopBar
                    ? new RectOffset(6, 6, 6, 6)
                    : new RectOffset(4, 4, 4, 4);
                layout.spacing = hasTopBar ? 6f : 8f;
                layout.childAlignment = hasTopBar
                    ? TextAnchor.MiddleCenter
                    : TextAnchor.UpperCenter;
            }

            Button[] buttons = GetComponentsInChildren<Button>(true);
            Sprite cameraButtonSprite = FindCameraButtonSprite();
            for (int index = 0; index < buttons.Length; index++)
            {
                if (hasTopBar && buttons[index].transform.parent == transform)
                {
                    ApplyButtonLayout(buttons[index]);
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

            ApplyFloatingButtonSprite(cameraButtonSprite);

            if (topBar != null)
            {
                GreenFeedPanelController feed =
                    FindAnyObjectByType<GreenFeedPanelController>(
                        FindObjectsInactive.Include);
                feed?.AttachTickerToTopBar(topBar);
            }
        }

        private static void ApplyButtonLayout(Button button)
        {
            RectTransform buttonRect = button.transform as RectTransform;
            if (buttonRect != null)
            {
                buttonRect.sizeDelta = new Vector2(
                    HudTopBarLayout.ActionButtonWidth,
                    HudTopBarLayout.ActionButtonHeight);
            }

            LayoutElement layoutElement =
                button.GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                return;
            }

            layoutElement.minWidth = HudTopBarLayout.ActionButtonWidth;
            layoutElement.preferredWidth = HudTopBarLayout.ActionButtonWidth;
            layoutElement.minHeight = HudTopBarLayout.ActionButtonHeight;
            layoutElement.preferredHeight = HudTopBarLayout.ActionButtonHeight;
        }

        private RectTransform FindTopBar()
        {
            if (transform.parent is RectTransform parent &&
                parent.name == "HUD_TopBar")
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

        private Sprite FindCameraButtonSprite()
        {
            Transform cameraGroup = transform.Find("CameraRotateButton");
            Button cameraButton = cameraGroup != null
                ? cameraGroup.GetComponentInChildren<Button>(true)
                : null;
            Image image = cameraButton != null
                ? cameraButton.targetGraphic as Image
                : null;
            return image != null ? image.sprite : null;
        }

        private void ApplyFloatingButtonSprite(Sprite sprite)
        {
            if (sprite == null)
            {
                return;
            }

            Transform floating = transform.Find("Btn_Floating");
            Button button = floating != null
                ? floating.GetComponent<Button>()
                : null;
            Image image = button != null
                ? button.targetGraphic as Image
                : null;
            if (image == null)
            {
                return;
            }

            image.sprite = sprite;
            image.type = Image.Type.Sliced;
        }
    }
}

