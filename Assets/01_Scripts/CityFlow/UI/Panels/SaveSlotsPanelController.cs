using System.Collections;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Save;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public sealed class SaveSlotsPanelController : MonoBehaviour, ICityFlowServiceConsumer
    {
        private const int PreviewWidth = 480;
        private const int PreviewHeight = 270;

        [Header("Panel")]
        [SerializeField] private Button openPanelButton;
        [SerializeField] private Button loadPanelButton;
        [SerializeField] private Button closePanelButton;
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private CanvasGroup panelCanvasGroup;
        [SerializeField] private CanvasGroup settingsCanvasGroup;
        [SerializeField] private TMP_Text panelTitleText;
        [SerializeField] private TMP_Text panelSubtitleText;
        [SerializeField] private UIDockController dockController;

        [Header("Save Slots")]
        [SerializeField] private Button createSaveButton;
        [SerializeField] private Transform slotContainer;
        [SerializeField] private SaveSlotView slotTemplate;
        [SerializeField] private TMP_Text slotCountText;
        [SerializeField] private TMP_Text emptyStateText;
        [SerializeField] private TMP_Text statusText;

        [Header("Save Name Dialog")]
        [SerializeField] private GameObject nameDialog;
        [SerializeField] private TMP_InputField nameInput;
        [SerializeField] private Button confirmNameButton;
        [SerializeField] private Button cancelNameButton;
        [SerializeField] private ConfirmPopupController confirmPopup;

        private readonly Queue<string> automaticPreviewQueue = new Queue<string>();
        private CityFlowServices services;
        private Coroutine automaticPreviewCoroutine;
        private bool isCapturing;
        private bool isBound;
        private PanelMode panelMode = PanelMode.Save;

        private enum PanelMode
        {
            Save,
            Load
        }

        public void Initialize(CityFlowServices cityFlowServices)
        {
            UnsubscribeFromSaveService();
            services = cityFlowServices;

            if (services?.Save != null)
            {
                services.Save.AutomaticSaveSlotCreated += OnAutomaticSaveSlotCreated;
            }

            RefreshSlots();
        }

        private void Awake()
        {
            EnsureLoadPanelButton();
            ResolveLegacyPanelReferences();
            BindButtons();

            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }

            if (nameDialog != null)
            {
                nameDialog.SetActive(false);
            }
        }

        private void ResolveLegacyPanelReferences()
        {
            if (panelRoot == null)
            {
                return;
            }

            dockController ??= FindAnyObjectByType<UIDockController>(FindObjectsInactive.Include);
            Transform card = panelRoot.transform.Find("SaveSlotsCard");

            if (card == null)
            {
                return;
            }

            panelTitleText ??= card.Find("Title")?.GetComponent<TMP_Text>();
            panelSubtitleText ??= card.Find("Subtitle")?.GetComponent<TMP_Text>();
        }

        private void EnsureLoadPanelButton()
        {
            if (loadPanelButton != null || openPanelButton == null)
            {
                return;
            }

            loadPanelButton = Instantiate(openPanelButton, openPanelButton.transform.parent);
            loadPanelButton.name = "OpenLoadSlotsButton";
            loadPanelButton.onClick.RemoveAllListeners();

            if (loadPanelButton.transform is RectTransform loadRect)
            {
                loadRect.anchoredPosition =
                    openPanelButton.GetComponent<RectTransform>().anchoredPosition
                    + new Vector2(0f, 40f);
            }

            TMP_Text label = loadPanelButton.GetComponentInChildren<TMP_Text>(true);

            if (label != null)
            {
                label.text = "LOAD SLOTS";
            }

            Image background = loadPanelButton.GetComponent<Image>();

            if (background != null)
            {
                background.color = new Color(0.18f, 0.36f, 0.58f, 1f);
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null
                && panelRoot != null
                && panelRoot.activeSelf
                && keyboard.escapeKey.wasPressedThisFrame)
            {
                ClosePanel();
            }
        }

        private void OnDestroy()
        {
            UnsubscribeFromSaveService();
            UnbindButtons();
        }

        private void BindButtons()
        {
            if (isBound)
            {
                return;
            }

            openPanelButton?.onClick.AddListener(OpenSavePanel);
            loadPanelButton?.onClick.AddListener(OpenLoadPanel);
            closePanelButton?.onClick.AddListener(ClosePanel);
            createSaveButton?.onClick.AddListener(OpenNameDialog);
            confirmNameButton?.onClick.AddListener(ConfirmManualSave);
            cancelNameButton?.onClick.AddListener(CloseNameDialog);
            isBound = true;
        }

        private void UnbindButtons()
        {
            if (!isBound)
            {
                return;
            }

            openPanelButton?.onClick.RemoveListener(OpenSavePanel);
            loadPanelButton?.onClick.RemoveListener(OpenLoadPanel);
            closePanelButton?.onClick.RemoveListener(ClosePanel);
            createSaveButton?.onClick.RemoveListener(OpenNameDialog);
            confirmNameButton?.onClick.RemoveListener(ConfirmManualSave);
            cancelNameButton?.onClick.RemoveListener(CloseNameDialog);
            isBound = false;
        }

        private void OpenSavePanel()
        {
            OpenPanel(PanelMode.Save);
        }

        private void OpenLoadPanel()
        {
            OpenPanel(PanelMode.Load);
        }

        private void OpenPanel(PanelMode mode)
        {
            if (panelRoot == null)
            {
                return;
            }

            panelMode = mode;
            transform.SetAsLastSibling();
            panelRoot.SetActive(true);
            panelRoot.transform.SetAsLastSibling();
            SetStatus(string.Empty);
            RefreshSlots();
        }

        private void ClosePanel()
        {
            if (isCapturing)
            {
                return;
            }

            CloseNameDialog();
            panelRoot?.SetActive(false);
        }

        private void OpenNameDialog()
        {
            if (!CanCreateManualSave())
            {
                return;
            }

            if (nameInput != null)
            {
                nameInput.text = string.Empty;
            }

            nameDialog?.SetActive(true);
            nameDialog?.transform.SetAsLastSibling();
            nameInput?.ActivateInputField();
        }

        private void CloseNameDialog()
        {
            if (isCapturing)
            {
                return;
            }

            nameDialog?.SetActive(false);
        }

        private void ConfirmManualSave()
        {
            if (!CanCreateManualSave() || isCapturing)
            {
                return;
            }

            StartCoroutine(CreateManualSaveAfterCapture());
        }

        private IEnumerator CreateManualSaveAfterCapture()
        {
            while (isCapturing)
            {
                yield return null;
            }

            isCapturing = true;
            SetCreationControlsInteractable(false);
            SetStatus("Capturing current city view...");

            float panelAlpha = HideForCapture(panelCanvasGroup);
            float settingsAlpha = HideForCapture(settingsCanvasGroup);
            yield return new WaitForEndOfFrame();

            byte[] previewPng = CapturePreviewPng();
            RestoreAfterCapture(panelCanvasGroup, panelAlpha);
            RestoreAfterCapture(settingsCanvasGroup, settingsAlpha);

            string displayName = nameInput != null ? nameInput.text : string.Empty;
            bool saved = services.Save.TryCreateManualSave(
                displayName,
                previewPng,
                out SaveSlotMetadata metadata);

            isCapturing = false;
            SetCreationControlsInteractable(true);

            if (saved)
            {
                nameDialog?.SetActive(false);
                SetStatus($"Saved: {metadata.DisplayName}");
                Debug.Log($"[SaveSlotsPanel] Manual save completed: {metadata.DisplayName}");
            }
            else
            {
                SetStatus("Manual save failed or the 25-slot limit was reached.");
            }

            RefreshSlots();
        }

        private void OnAutomaticSaveSlotCreated(SaveSlotMetadata metadata)
        {
            if (metadata == null)
            {
                return;
            }

            automaticPreviewQueue.Enqueue(metadata.SlotId);

            if (automaticPreviewCoroutine == null)
            {
                automaticPreviewCoroutine = StartCoroutine(CaptureAutomaticPreviews());
            }

            if (panelRoot != null && panelRoot.activeSelf)
            {
                RefreshSlots();
            }
        }

        private IEnumerator CaptureAutomaticPreviews()
        {
            while (automaticPreviewQueue.Count > 0)
            {
                while (isCapturing)
                {
                    yield return null;
                }

                isCapturing = true;
                float panelAlpha = HideForCapture(panelCanvasGroup);
                float settingsAlpha = HideForCapture(settingsCanvasGroup);
                yield return new WaitForEndOfFrame();

                byte[] previewPng = CapturePreviewPng();
                RestoreAfterCapture(panelCanvasGroup, panelAlpha);
                RestoreAfterCapture(settingsCanvasGroup, settingsAlpha);

                while (automaticPreviewQueue.Count > 0)
                {
                    services?.Save?.SaveSlots.TryWritePreview(
                        automaticPreviewQueue.Dequeue(),
                        previewPng);
                }

                isCapturing = false;
            }

            automaticPreviewCoroutine = null;

            if (panelRoot != null && panelRoot.activeSelf)
            {
                RefreshSlots();
            }
        }

        private void RefreshSlots()
        {
            if (slotContainer == null || slotTemplate == null)
            {
                return;
            }

            for (int index = slotContainer.childCount - 1; index >= 0; index--)
            {
                Transform child = slotContainer.GetChild(index);

                if (child != slotTemplate.transform)
                {
                    Destroy(child.gameObject);
                }
            }

            IReadOnlyList<SaveSlotMetadata> slots = services?.Save?.SaveSlots?.Slots;
            int slotCount = slots?.Count ?? 0;
            int manualCount = services?.Save?.SaveSlots?.ManualSlotCount ?? 0;

            if (slots != null)
            {
                foreach (SaveSlotMetadata metadata in slots)
                {
                    CreateSlotView(metadata);
                }
            }

            if (slotCountText != null)
            {
                slotCountText.text =
                    $"{slotCount}/{SaveSlotRepository.MaximumTotalSlots}  " +
                    $"MANUAL {manualCount}/{SaveSlotRepository.MaximumManualSlots}";
            }

            if (emptyStateText != null)
            {
                emptyStateText.gameObject.SetActive(slotCount == 0);
            }

            if (createSaveButton != null)
            {
                createSaveButton.gameObject.SetActive(panelMode == PanelMode.Save);
                createSaveButton.interactable =
                    services?.Save != null
                    && manualCount < SaveSlotRepository.MaximumManualSlots;
            }

            if (panelTitleText != null)
            {
                panelTitleText.text = panelMode == PanelMode.Save
                    ? "SAVE SLOTS"
                    : "LOAD SLOTS";
            }

            if (panelSubtitleText != null)
            {
                panelSubtitleText.text = panelMode == PanelMode.Save
                    ? "MANUAL 25  |  AUTO 5"
                    : "SELECT A SAVE TO RESTORE";
            }
        }

        private void CreateSlotView(SaveSlotMetadata metadata)
        {
            SaveSlotView view = Instantiate(slotTemplate, slotContainer);
            view.gameObject.SetActive(true);

            Texture2D previewTexture = LoadPreviewTexture(metadata.SlotId);
            view.Configure(
                metadata,
                previewTexture,
                panelMode == PanelMode.Load,
                () => LoadSlot(metadata),
                () => RequestDeleteSlot(metadata));
        }

        private void LoadSlot(SaveSlotMetadata metadata)
        {
            if (services?.Save == null)
            {
                SetStatus("Save service is not connected.");
                return;
            }

            bool loaded = services.Save.TryLoadSaveSlot(metadata.SlotId);

            if (loaded)
            {
                Debug.Log($"[SaveSlotsPanel] Loaded: {metadata.DisplayName}");
                ClosePanelAfterLoad();
                dockController?.CloseAllPanels();
                return;
            }

            SetStatus($"Could not load: {metadata.DisplayName}");
            RefreshSlots();
        }

        private void ClosePanelAfterLoad()
        {
            nameDialog?.SetActive(false);
            panelRoot?.SetActive(false);
        }

        private void RequestDeleteSlot(SaveSlotMetadata metadata)
        {
            if (confirmPopup == null)
            {
                DeleteSlot(metadata);
                return;
            }

            confirmPopup.transform.SetAsLastSibling();
            confirmPopup.Show(
                $"Delete '{metadata.DisplayName}'?",
                () => DeleteSlot(metadata));
        }

        private void DeleteSlot(SaveSlotMetadata metadata)
        {
            bool deleted = services?.Save?.TryDeleteSaveSlot(metadata.SlotId) == true;
            SetStatus(deleted
                ? $"Deleted: {metadata.DisplayName}"
                : $"Could not delete: {metadata.DisplayName}");
            RefreshSlots();
        }

        private bool CanCreateManualSave()
        {
            if (services?.Save == null)
            {
                SetStatus("Save service is not connected.");
                return false;
            }

            if (services.Save.SaveSlots.ManualSlotCount >= SaveSlotRepository.MaximumManualSlots)
            {
                SetStatus("Manual save slots are full. Delete a slot before saving.");
                return false;
            }

            return true;
        }

        private Texture2D LoadPreviewTexture(string slotId)
        {
            if (services?.Save?.SaveSlots.TryReadPreview(slotId, out byte[] previewPng) != true)
            {
                return null;
            }

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGB24, false);

            if (ImageConversion.LoadImage(texture, previewPng, false))
            {
                return texture;
            }

            Destroy(texture);
            return null;
        }

        private static byte[] CapturePreviewPng()
        {
            Texture2D source = ScreenCapture.CaptureScreenshotAsTexture();

            if (source == null)
            {
                return null;
            }

            RenderTexture target = RenderTexture.GetTemporary(
                PreviewWidth,
                PreviewHeight,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(source, target);
            RenderTexture.active = target;

            Texture2D preview = new Texture2D(
                PreviewWidth,
                PreviewHeight,
                TextureFormat.RGB24,
                false);
            preview.ReadPixels(new Rect(0f, 0f, PreviewWidth, PreviewHeight), 0, 0);
            preview.Apply(false, false);
            byte[] png = preview.EncodeToPNG();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Destroy(source);
            Destroy(preview);
            return png;
        }

        private static float HideForCapture(CanvasGroup canvasGroup)
        {
            if (canvasGroup == null)
            {
                return 0f;
            }

            float previousAlpha = canvasGroup.alpha;
            canvasGroup.alpha = 0f;
            return previousAlpha;
        }

        private static void RestoreAfterCapture(CanvasGroup canvasGroup, float previousAlpha)
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = previousAlpha;
            }
        }

        private void SetCreationControlsInteractable(bool interactable)
        {
            if (confirmNameButton != null)
            {
                confirmNameButton.interactable = interactable;
            }

            if (cancelNameButton != null)
            {
                cancelNameButton.interactable = interactable;
            }
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }

        private void UnsubscribeFromSaveService()
        {
            if (services?.Save != null)
            {
                services.Save.AutomaticSaveSlotCreated -= OnAutomaticSaveSlotCreated;
            }
        }

        // Unity setup: Bake this UI from Tools > GreenLight > UI > Bake Save Slots Panel.
    }
}
