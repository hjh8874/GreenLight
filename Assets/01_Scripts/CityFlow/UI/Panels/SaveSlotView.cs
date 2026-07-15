using System;
using CityFlow.Save;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public sealed class SaveSlotView : MonoBehaviour
    {
        [Header("Slot UI")]
        [SerializeField] private Image background;
        [SerializeField] private Image typeAccent;
        [SerializeField] private RawImage previewImage;
        [SerializeField] private TMP_Text typeText;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text savedAtText;
        [SerializeField] private Button loadButton;
        [SerializeField] private Button deleteButton;

        private Texture2D ownedPreviewTexture;

        public void Configure(
            SaveSlotMetadata metadata,
            Texture2D previewTexture,
            bool showLoadAction,
            Action loadAction,
            Action deleteAction)
        {
            ReleasePreviewTexture();
            ownedPreviewTexture = previewTexture;

            bool isAutomatic = metadata.IsAutomatic;
            Color accentColor = isAutomatic
                ? new Color(0.94f, 0.66f, 0.20f, 1f)
                : new Color(0.18f, 0.72f, 0.66f, 1f);

            if (background != null)
            {
                background.color = isAutomatic
                    ? new Color(0.16f, 0.14f, 0.10f, 0.98f)
                    : new Color(0.08f, 0.14f, 0.15f, 0.98f);
            }

            if (typeAccent != null)
            {
                typeAccent.color = accentColor;
            }

            if (typeText != null)
            {
                typeText.text = isAutomatic ? "AUTO SAVE" : "MANUAL SAVE";
                typeText.color = accentColor;
            }

            if (nameText != null)
            {
                nameText.text = metadata.DisplayName;
            }

            if (savedAtText != null)
            {
                savedAtText.text = FormatSavedAt(metadata.SavedAtUtcTicks);
            }

            if (previewImage != null)
            {
                previewImage.texture = ownedPreviewTexture;
                previewImage.color = ownedPreviewTexture != null
                    ? Color.white
                    : new Color(0.15f, 0.17f, 0.18f, 1f);
            }

            BindButton(loadButton, loadAction);
            BindButton(deleteButton, deleteAction);

            if (loadButton != null)
            {
                loadButton.gameObject.SetActive(showLoadAction);
            }
        }

        private void OnDestroy()
        {
            ReleasePreviewTexture();
        }

        private static void BindButton(Button button, Action action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action?.Invoke());
        }

        private static string FormatSavedAt(long utcTicks)
        {
            try
            {
                DateTime utc = new DateTime(utcTicks, DateTimeKind.Utc);
                return utc.ToLocalTime().ToString("yyyy-MM-dd  HH:mm:ss");
            }
            catch (ArgumentOutOfRangeException)
            {
                return "Unknown save time";
            }
        }

        private void ReleasePreviewTexture()
        {
            if (ownedPreviewTexture == null)
            {
                return;
            }

            Destroy(ownedPreviewTexture);
            ownedPreviewTexture = null;
        }

        // Unity setup: Use this component on the inactive slot template baked by SaveSlotsPanelBaker.
    }
}
