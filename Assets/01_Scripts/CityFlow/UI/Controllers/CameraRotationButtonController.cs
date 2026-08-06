using CityFlow.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI.Controllers
{
    public sealed class CameraRotationButtonController : MonoBehaviour
    {
        [SerializeField] private Button rotateButton;
        [SerializeField] private int stepDirection = 1;

        private ICameraRotationController cameraRotation;
        private bool isBound;

        public void Configure(Button button, int direction)
        {
            Unbind();
            rotateButton = button;
            stepDirection = direction < 0 ? -1 : 1;
            Bind();
        }

        private void Awake()
        {
            ResolveCameraRotationController();
            Bind();
        }

        private void OnEnable()
        {
            Bind();
        }

        private void OnDisable()
        {
            Unbind();
        }

        private void Bind()
        {
            if (isBound || rotateButton == null)
            {
                return;
            }

            rotateButton.onClick.AddListener(OnRotateClicked);
            isBound = true;
        }

        private void Unbind()
        {
            if (!isBound || rotateButton == null)
            {
                return;
            }

            rotateButton.onClick.RemoveListener(OnRotateClicked);
            isBound = false;
        }

        private void OnRotateClicked()
        {
            ResolveCameraRotationController();
            cameraRotation?.TryRotateCamera(stepDirection);
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
