using CityFlow.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI.Controllers
{
    public sealed class CameraRotationButtonController : MonoBehaviour
    {
        private const float ButtonAlpha = 0.62f;
        [SerializeField] private Button rotateLeftButton;
        [SerializeField] private Button rotateRightButton;

        private ICameraRotationController cameraRotation;
        private bool isBound;

        public void Configure(Button leftButton, Button rightButton)
        {
            Unbind();
            rotateLeftButton = leftButton;
            rotateRightButton = rightButton;
            ApplyDockVisualStyle();
            Bind();
        }

        private void Awake()
        {
            ResolveCameraRotationController();
            ApplyDockVisualStyle();
            Bind();
        }

        private void OnEnable()
        {
            ApplyDockVisualStyle();
            Bind();
        }

        private void Start()
        {
            // Scene layout components can apply serialized child sizes after
            // Awake/OnEnable. Re-apply once after initialization without
            // touching the parent dock's authored RectTransform values.
            ApplyDockVisualStyle();
        }

        private void OnDisable()
        {
            Unbind();
        }

        private void Bind()
        {
            if (isBound ||
                (rotateLeftButton == null && rotateRightButton == null))
            {
                return;
            }

            rotateLeftButton?.onClick.AddListener(OnRotateLeftClicked);
            rotateRightButton?.onClick.AddListener(OnRotateRightClicked);
            isBound = true;
        }

        private void Unbind()
        {
            if (!isBound)
            {
                return;
            }

            rotateLeftButton?.onClick.RemoveListener(OnRotateLeftClicked);
            rotateRightButton?.onClick.RemoveListener(OnRotateRightClicked);
            isBound = false;
        }

        private void OnRotateLeftClicked()
        {
            TryRotate(-1);
        }

        private void OnRotateRightClicked()
        {
            TryRotate(1);
        }

        private void TryRotate(int stepDirection)
        {
            ResolveCameraRotationController();
            cameraRotation?.TryRotateCamera(stepDirection);
        }

        private void ApplyDockVisualStyle()
        {
            Image cameraImage = rotateLeftButton != null
                ? rotateLeftButton.targetGraphic as Image
                : null;
            if (cameraImage == null)
            {
                return;
            }

            ApplyAlpha(cameraImage);
            Image rightImage = rotateRightButton != null
                ? rotateRightButton.targetGraphic as Image
                : null;
            ApplyAlpha(rightImage);

            Transform floating = transform.parent != null
                ? transform.parent.Find("Btn_Floating")
                : null;
            Button floatingButton = floating != null
                ? floating.GetComponent<Button>()
                : null;
            Image floatingImage = floatingButton != null
                ? floatingButton.targetGraphic as Image
                : null;
            if (floatingImage == null)
            {
                return;
            }

            floatingImage.sprite = cameraImage.sprite;
            floatingImage.type = cameraImage.type;
            ApplyAlpha(floatingImage);
            MatchActionCardSize(floating, transform);
        }

        private static void MatchActionCardSize(
            Transform target,
            Transform source)
        {
            RectTransform sourceRect = source as RectTransform;
            if (target == null || sourceRect == null)
            {
                return;
            }

            Vector2 cardSize = new Vector2(
                Mathf.Max(
                    LayoutUtility.GetPreferredWidth(sourceRect),
                    sourceRect.rect.width,
                    sourceRect.sizeDelta.x),
                Mathf.Max(
                    LayoutUtility.GetPreferredHeight(sourceRect),
                    sourceRect.rect.height,
                    sourceRect.sizeDelta.y));
            if (cardSize.x <= 0f || cardSize.y <= 0f)
            {
                return;
            }

            RectTransform rect = target as RectTransform;
            if (rect != null)
            {
                rect.sizeDelta = cardSize;
            }

            LayoutElement layout = target.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = target.gameObject.AddComponent<LayoutElement>();
            }

            layout.minWidth = cardSize.x;
            layout.preferredWidth = cardSize.x;
            layout.flexibleWidth = 0f;
            layout.minHeight = cardSize.y;
            layout.preferredHeight = cardSize.y;
            layout.flexibleHeight = 0f;
        }

        private static void ApplyAlpha(Image image)
        {
            if (image == null)
            {
                return;
            }

            Color color = image.color;
            color.a = ButtonAlpha;
            image.color = color;
        }

        private void ResolveCameraRotationController()
        {
            if (cameraRotation != null)
            {
                return;
            }

            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int index = 0; index < behaviours.Length; index++)
            {
                if (behaviours[index] is ICameraRotationController controller)
                {
                    cameraRotation = controller;
                    return;
                }
            }
        }
    }
}
