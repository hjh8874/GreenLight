using CityFlow.Bootstrap;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public sealed class SaveDataSettingsController : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("Settings UI")]
        [SerializeField] private Button deleteSaveButton;
        [SerializeField] private ConfirmPopupController confirmPopup;

        private CityFlowServices services;
        private bool isBound;

        public void Initialize(CityFlowServices services)
        {
            this.services = services;
        }

        private void Start()
        {
            BindButton();
        }

        private void OnDestroy()
        {
            if (isBound && deleteSaveButton != null)
            {
                deleteSaveButton.onClick.RemoveListener(OnDeleteSaveClicked);
            }
        }

        private void BindButton()
        {
            if (isBound || deleteSaveButton == null)
            {
                return;
            }

            deleteSaveButton.onClick.AddListener(OnDeleteSaveClicked);
            isBound = true;
        }

        private void OnDeleteSaveClicked()
        {
            if (services?.Save == null)
            {
                Debug.LogWarning("[SaveDataSettingsController] SaveService is not connected.");
                return;
            }

            if (confirmPopup == null)
            {
                Debug.LogWarning("[SaveDataSettingsController] Delete confirmation popup is not connected.");
                return;
            }

            confirmPopup.Show(
                "Delete all save data? This cannot be undone.",
                DeleteSaveData);
        }

        private void DeleteSaveData()
        {
            bool deleted = services.Save.DeleteSaveAndSuspend();

            if (deleted && deleteSaveButton != null)
            {
                deleteSaveButton.interactable = false;
            }

            Debug.Log(deleted
                ? "[SaveDataSettingsController] Save data deleted. Restart Play Mode to begin a new game."
                : "[SaveDataSettingsController] Save data deletion failed.");
        }

        // Unity setup: Add this component to the Delete Save Data button in the settings panel.
        // Assign the button itself and the shared UI_ConfirmPopup object.
    }
}
