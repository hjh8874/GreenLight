using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public sealed class TitleSceneEnhancementsView : MonoBehaviour
    {
        [SerializeField] private GameObject logoBackdrop;
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private Button closeButton;

        private bool isBound;

        public bool IsLogoBackdropVisible =>
            logoBackdrop != null && logoBackdrop.activeSelf;
        public bool IsSettingsVisible =>
            settingsPanel != null && settingsPanel.activeSelf;

        public void Configure(
            GameObject backdrop,
            GameObject panel,
            Button close)
        {
            Unbind();
            logoBackdrop = backdrop;
            settingsPanel = panel;
            closeButton = close;
            Bind();
        }

        public void Initialize(bool showLogoBackdrop)
        {
            if (logoBackdrop != null)
            {
                logoBackdrop.SetActive(showLogoBackdrop);
            }

            SetSettingsVisible(false);
            Bind();
        }

        public void ToggleSettings()
        {
            SetSettingsVisible(!IsSettingsVisible);
        }

        public void SetSettingsVisible(bool visible)
        {
            if (settingsPanel == null)
            {
                return;
            }

            if (visible)
            {
                Transform canvas = transform.parent;
                if (canvas != null)
                {
                    settingsPanel.transform.SetParent(canvas, false);
                    settingsPanel.transform.SetAsLastSibling();
                }

                settingsPanel.SetActive(true);
            }
            else
            {
                settingsPanel.SetActive(visible);
                settingsPanel.transform.SetParent(transform, false);
                transform.SetAsFirstSibling();
            }
        }

        private void Awake()
        {
            Bind();
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void Bind()
        {
            if (isBound || closeButton == null)
            {
                return;
            }

            closeButton.onClick.AddListener(OnCloseClicked);
            isBound = true;
        }

        private void Unbind()
        {
            if (!isBound)
            {
                return;
            }

            closeButton?.onClick.RemoveListener(OnCloseClicked);
            isBound = false;
        }

        private void OnCloseClicked()
        {
            SetSettingsVisible(false);
        }
    }
}
