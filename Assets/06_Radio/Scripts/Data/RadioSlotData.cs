using System;
using UnityEngine;

namespace GreenLight.Radio.Data
{
    [Serializable]
    public sealed class RadioSlotData
    {
        [SerializeField] private int slotIndex;
        [SerializeField] private bool isUnlocked;
        [SerializeField] private RadioSourceType sourceType = RadioSourceType.None;
        [SerializeField] private string youtubeVideoId;
        [SerializeField] private string displayName;
        [SerializeField] private string themeId;

        public int SlotIndex => slotIndex;
        public bool IsUnlocked => isUnlocked;
        public RadioSourceType SourceType => sourceType;
        public string YoutubeVideoId => youtubeVideoId;
        public string DisplayName => displayName;
        public string ThemeId => themeId;

        public RadioSlotData(int slotIndex)
        {
            this.slotIndex = Mathf.Max(0, slotIndex);
        }

        public void Unlock()
        {
            isUnlocked = true;
        }

        public void Lock()
        {
            isUnlocked = false;
            Clear();
        }

        public void SetYouTubeVideo(string videoId, string displayName, string themeId = "")
        {
            TrySetYouTubeVideo(videoId, displayName, themeId);
        }

        public bool TrySetYouTubeVideo(string videoId, string displayName, string themeId = "")
        {
            if (!isUnlocked)
            {
                Debug.LogWarning($"[RadioSlotData] Slot {slotIndex} is locked and cannot register a YouTube video.");
                return false;
            }

            youtubeVideoId = NormalizeVideoId(videoId);
            sourceType = string.IsNullOrWhiteSpace(youtubeVideoId)
                ? RadioSourceType.None
                : RadioSourceType.YouTube;
            this.displayName = displayName ?? string.Empty;
            this.themeId = themeId ?? string.Empty;
            return sourceType == RadioSourceType.YouTube;
        }

        public void Clear()
        {
            sourceType = RadioSourceType.None;
            youtubeVideoId = string.Empty;
            displayName = string.Empty;
            themeId = string.Empty;
        }

        public void Restore(
            bool isUnlocked,
            RadioSourceType sourceType,
            string youtubeVideoId,
            string displayName,
            string themeId)
        {
            this.isUnlocked = isUnlocked;
            this.sourceType = sourceType;
            this.youtubeVideoId = youtubeVideoId ?? string.Empty;
            this.displayName = displayName ?? string.Empty;
            this.themeId = themeId ?? string.Empty;
        }

        private static string NormalizeVideoId(string videoId)
        {
            if (string.IsNullOrWhiteSpace(videoId))
            {
                return string.Empty;
            }

            string trimmed = videoId.Trim();

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri uri))
            {
                return CleanVideoId(trimmed);
            }

            string queryVideoId = ExtractQueryValue(uri.Query, "v");

            if (!string.IsNullOrWhiteSpace(queryVideoId))
            {
                return CleanVideoId(queryVideoId);
            }

            string[] segments = uri.AbsolutePath.Split(
                new[] { '/' },
                StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0)
            {
                return string.Empty;
            }

            if (uri.Host.Contains("youtu.be"))
            {
                return CleanVideoId(segments[0]);
            }

            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (segments[i] == "embed" || segments[i] == "shorts")
                {
                    return CleanVideoId(segments[i + 1]);
                }
            }

            return string.Empty;
        }

        private static string CleanVideoId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string cleaned = value.Trim();
            int queryStart = cleaned.IndexOfAny(new[] { '?', '&', '#' });

            if (queryStart >= 0)
            {
                cleaned = cleaned.Substring(0, queryStart);
            }

            return Uri.UnescapeDataString(cleaned);
        }

        private static string ExtractQueryValue(string query, string key)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return string.Empty;
            }

            string trimmedQuery = query.TrimStart('?');
            string[] pairs = trimmedQuery.Split('&');

            for (int i = 0; i < pairs.Length; i++)
            {
                string[] parts = pairs[i].Split(new[] { '=' }, 2);

                if (parts.Length != 2 || parts[0] != key)
                {
                    continue;
                }

                return Uri.UnescapeDataString(parts[1]);
            }

            return string.Empty;
        }
    }
}
