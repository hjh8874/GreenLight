using GreenLight.Radio.Runtime;
using GreenLight.Radio.Save;
using GreenLight.Radio.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GreenLight.Radio.UI
{
    public sealed class RadioPrototypePanel : MonoBehaviour
    {
        [Header("Services")]
        [SerializeField] private RadioService radioService;
        [SerializeField] private RadioDummyPlayer dummyPlayer;

        [Header("Inputs")]
        [SerializeField] private TMP_InputField slotIndexInput;
        [SerializeField] private TMP_InputField youtubeInput;
        [SerializeField] private TMP_InputField displayNameInput;
        [SerializeField] private TMP_InputField themeIdInput;

        [Header("Buttons")]
        [SerializeField] private Button unlockButton;
        [SerializeField] private Button registerButton;
        [SerializeField] private Button playButton;
        [SerializeField] private Button pauseButton;
        [SerializeField] private Button snapshotButton;
        [SerializeField] private Button restoreButton;

        [Header("Output")]
        [SerializeField] private TMP_Text statusText;

        private RadioSaveData cachedSnapshot;

        private void Awake()
        {
            BindButtons();
            BindServiceEvents();
            RefreshStatus("Radio prototype panel ready.");
        }

        private void OnDestroy()
        {
            UnbindButtons();
            UnbindServiceEvents();
        }

        public void Configure(
            RadioService radioService,
            RadioDummyPlayer dummyPlayer,
            TMP_InputField slotIndexInput,
            TMP_InputField youtubeInput,
            TMP_InputField displayNameInput,
            TMP_InputField themeIdInput,
            Button unlockButton,
            Button registerButton,
            Button playButton,
            Button pauseButton,
            Button snapshotButton,
            Button restoreButton,
            TMP_Text statusText)
        {
            this.radioService = radioService;
            this.dummyPlayer = dummyPlayer;
            this.slotIndexInput = slotIndexInput;
            this.youtubeInput = youtubeInput;
            this.displayNameInput = displayNameInput;
            this.themeIdInput = themeIdInput;
            this.unlockButton = unlockButton;
            this.registerButton = registerButton;
            this.playButton = playButton;
            this.pauseButton = pauseButton;
            this.snapshotButton = snapshotButton;
            this.restoreButton = restoreButton;
            this.statusText = statusText;

            BindButtons();
            BindServiceEvents();
        }

        private void OnUnlockClicked()
        {
            if (!HasService())
            {
                return;
            }

            int slotIndex = ReadSlotIndex();
            bool unlocked = radioService.TryUnlockSlot(slotIndex);
            RefreshStatus(unlocked
                ? $"Slot {slotIndex} unlocked."
                : $"Slot {slotIndex} unlock failed.");
        }

        private void OnRegisterClicked()
        {
            if (!HasService())
            {
                return;
            }

            int slotIndex = ReadSlotIndex();
            string youtubeValue = youtubeInput != null ? youtubeInput.text : string.Empty;
            string displayName = displayNameInput != null ? displayNameInput.text : string.Empty;
            string themeId = themeIdInput != null ? themeIdInput.text : string.Empty;

            bool registered = radioService.TryRegisterYouTubeLink(slotIndex, youtubeValue, displayName, themeId);
            RefreshStatus(registered
                ? $"Slot {slotIndex} registered."
                : $"Slot {slotIndex} register failed.");
        }

        private void OnPlayClicked()
        {
            if (!HasService())
            {
                return;
            }

            int slotIndex = ReadSlotIndex();
            bool requested = radioService.RequestPlaySlot(slotIndex);
            RefreshStatus(requested
                ? $"Play requested for slot {slotIndex}."
                : $"Play request failed for slot {slotIndex}.");
        }

        private void OnPauseClicked()
        {
            if (!HasService())
            {
                return;
            }

            radioService.RequestPause();
            RefreshStatus("Pause requested.");
        }

        private void OnSnapshotClicked()
        {
            if (!HasService())
            {
                return;
            }

            cachedSnapshot = radioService.CreateSnapshot();
            int count = cachedSnapshot?.Slots?.Length ?? 0;
            RefreshStatus($"Radio snapshot cached. Slots: {count}");
        }

        private void OnRestoreClicked()
        {
            if (!HasService())
            {
                return;
            }

            if (cachedSnapshot == null)
            {
                RefreshStatus("Restore skipped. Cache a snapshot first.");
                return;
            }

            radioService.RestoreSnapshot(cachedSnapshot);
            RefreshStatus("Radio snapshot restored.");
        }

        private void OnDummyStatusChanged(string message)
        {
            RefreshStatus(message);
        }

        private void OnSlotUnlocked(RadioSlotData slot)
        {
            RefreshStatus($"Slot {slot.SlotIndex} unlocked.");
        }

        private void OnSlotRegistered(RadioSlotData slot)
        {
            RefreshStatus($"Slot {slot.SlotIndex}: {slot.YoutubeVideoId}");
        }

        private bool HasService()
        {
            if (radioService != null)
            {
                return true;
            }

            RefreshStatus("RadioService is missing.");
            return false;
        }

        private int ReadSlotIndex()
        {
            if (slotIndexInput == null || !int.TryParse(slotIndexInput.text, out int slotIndex))
            {
                return 0;
            }

            return Mathf.Max(0, slotIndex);
        }

        private void RefreshStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }

            Debug.Log($"[RadioPrototypePanel] {message}");
        }

        private void BindButtons()
        {
            UnbindButtons();

            unlockButton?.onClick.AddListener(OnUnlockClicked);
            registerButton?.onClick.AddListener(OnRegisterClicked);
            playButton?.onClick.AddListener(OnPlayClicked);
            pauseButton?.onClick.AddListener(OnPauseClicked);
            snapshotButton?.onClick.AddListener(OnSnapshotClicked);
            restoreButton?.onClick.AddListener(OnRestoreClicked);
        }

        private void UnbindButtons()
        {
            unlockButton?.onClick.RemoveListener(OnUnlockClicked);
            registerButton?.onClick.RemoveListener(OnRegisterClicked);
            playButton?.onClick.RemoveListener(OnPlayClicked);
            pauseButton?.onClick.RemoveListener(OnPauseClicked);
            snapshotButton?.onClick.RemoveListener(OnSnapshotClicked);
            restoreButton?.onClick.RemoveListener(OnRestoreClicked);
        }

        private void BindServiceEvents()
        {
            UnbindServiceEvents();

            if (radioService != null)
            {
                radioService.SlotUnlocked += OnSlotUnlocked;
                radioService.SlotRegistered += OnSlotRegistered;
            }

            if (dummyPlayer != null)
            {
                dummyPlayer.StatusChanged += OnDummyStatusChanged;
            }
        }

        private void UnbindServiceEvents()
        {
            if (radioService != null)
            {
                radioService.SlotUnlocked -= OnSlotUnlocked;
                radioService.SlotRegistered -= OnSlotRegistered;
            }

            if (dummyPlayer != null)
            {
                dummyPlayer.StatusChanged -= OnDummyStatusChanged;
            }
        }

        // Unity setup: Use Tools/GreenLight Radio/Bake Prototype Radio Rig to create and wire this panel.
    }
}
