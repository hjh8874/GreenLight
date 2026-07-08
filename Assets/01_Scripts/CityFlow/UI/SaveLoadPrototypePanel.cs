using CityFlow.Bootstrap;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public sealed class SaveLoadPrototypePanel : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private Button saveButton;
        [SerializeField] private Button loadButton;
        [SerializeField] private TMP_Text statusText;

        private CityFlowServices services;

        private void Awake()
        {
            BindButtons();
            SetStatus("Save system waiting for CityBootstrap.");
        }

        private void Start()
        {
            if (services != null)
            {
                return;
            }

            CityBootstrap bootstrap = FindAnyObjectByType<CityBootstrap>();

            if (bootstrap?.Services != null)
            {
                Initialize(bootstrap.Services);
            }
        }

        private void OnDestroy()
        {
            if (saveButton != null)
            {
                saveButton.onClick.RemoveListener(OnSaveClicked);
            }

            if (loadButton != null)
            {
                loadButton.onClick.RemoveListener(OnLoadClicked);
            }
        }

        public void Initialize(CityFlowServices services)
        {
            this.services = services;
            RefreshInteractable();
        }

        public void OnSaveClicked()
        {
            if (!CanUseSaveService())
            {
                return;
            }

            bool saved = services.Save.Save();
            SetStatus(saved ? "Save completed." : "Save failed.");
        }

        public void OnLoadClicked()
        {
            if (!CanUseSaveService())
            {
                return;
            }

            bool loaded = services.Save.TryLoadAndRestore();
            SetStatus(loaded ? "Load request completed." : "No save file found.");
        }

        private void BindButtons()
        {
            if (saveButton != null)
            {
                saveButton.onClick.RemoveListener(OnSaveClicked);
                saveButton.onClick.AddListener(OnSaveClicked);
            }

            if (loadButton != null)
            {
                loadButton.onClick.RemoveListener(OnLoadClicked);
                loadButton.onClick.AddListener(OnLoadClicked);
            }
        }

        private bool CanUseSaveService()
        {
            if (services?.Save == null)
            {
                SetStatus("SaveService is not connected.");
                return false;
            }

            if (services.Save.SimSaveSource == null)
            {
                SetStatus("ISimSaveSource is not implemented yet.");
                return false;
            }

            return true;
        }

        private void RefreshInteractable()
        {
            bool canUse = services?.Save?.SimSaveSource != null;

            if (saveButton != null)
            {
                saveButton.interactable = canUse;
            }

            if (loadButton != null)
            {
                loadButton.interactable = canUse;
            }

            SetStatus(canUse
                ? "Save system ready."
                : "Waiting for SimEngine : ISimSaveSource.");
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }

            Debug.Log(message);
        }

        // Unity setup: Attach this to the prototype save panel and assign Save/Load buttons plus a status TMP text.
    }
}
