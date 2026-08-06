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
        // 한 줄 행·티커는 높이가 고정이다 — 본문 길이로 늘리면 줄이 어긋난다.
        [SerializeField] private bool compactRow;
        // 글이 가리키는 도시 좌표. CitizenFeedPost가 원래 들고 있었으나
        // UI로 넘어오는 과정에서 버려지고 있었다.
        [SerializeField] private Vector2Int tile;
        [SerializeField] private bool hasTile;

        public void BindTile(Vector2Int value)
        {
            tile = value;
            hasTile = true;
        }

        public bool TryGetTile(out Vector2Int value)
        {
            value = tile;
            return hasTile;
        }

        public void Configure(
            Image accent,
            Image avatar,
            TMP_Text avatarInitial,
            TMP_Text authorName,
            TMP_Text occupation,
            TMP_Text message,
            TMP_Text timestamp,
            LayoutElement layout,
            bool compact = false)
        {
            compactRow = compact;
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

            if (compactRow || messageText == null || layoutElement == null)
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
