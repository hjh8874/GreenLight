using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI.Feed
{
    public sealed class GreenFeedPostView : MonoBehaviour
    {
        [SerializeField] private Image accentImage;
        [SerializeField] private Image avatarImage;
        [SerializeField] private TMP_Text avatarInitialText;
        [SerializeField] private TMP_Text authorNameText;
        [SerializeField] private TMP_Text occupationText;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private TMP_Text timestampText;
        [SerializeField] private LayoutElement layoutElement;

        public void Configure(
            Image accent,
            Image avatar,
            TMP_Text avatarInitial,
            TMP_Text authorName,
            TMP_Text occupation,
            TMP_Text message,
            TMP_Text timestamp,
            LayoutElement layout)
        {
            accentImage = accent;
            avatarImage = avatar;
            avatarInitialText = avatarInitial;
            authorNameText = authorName;
            occupationText = occupation;
            messageText = message;
            timestampText = timestamp;
            layoutElement = layout;
        }

        public void Bind(
            string authorName,
            string occupation,
            string message,
            string timestamp,
            string avatarInitial,
            Color accentColor)
        {
            if (accentImage != null)
            {
                accentImage.color = accentColor;
            }

            if (avatarImage != null)
            {
                avatarImage.color = new Color(
                    accentColor.r * 0.32f,
                    accentColor.g * 0.32f,
                    accentColor.b * 0.32f,
                    1f);
            }

            SetText(avatarInitialText, avatarInitial);
            SetText(authorNameText, authorName);
            SetText(occupationText, occupation);
            SetText(messageText, message);
            SetText(timestampText, timestamp);

            if (messageText == null || layoutElement == null)
            {
                return;
            }

            messageText.ForceMeshUpdate();
            layoutElement.preferredHeight = Mathf.Clamp(72f + messageText.preferredHeight, 112f, 172f);
        }

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null)
            {
                target.text = value ?? string.Empty;
            }
        }

        // Unity setup: Use the baked inactive template or assign all visual references in the Inspector.
    }
}
