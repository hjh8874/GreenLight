using GreenLight.Radio.Data;

namespace GreenLight.Radio.Runtime
{
    public readonly struct RadioPlaybackRequest
    {
        public readonly int SlotIndex;
        public readonly RadioSourceType SourceType;
        public readonly string YoutubeVideoId;
        public readonly string DisplayName;
        public readonly string ThemeId;

        public RadioPlaybackRequest(RadioSlotData slot)
        {
            SlotIndex = slot?.SlotIndex ?? -1;
            SourceType = slot?.SourceType ?? RadioSourceType.None;
            YoutubeVideoId = slot?.YoutubeVideoId ?? string.Empty;
            DisplayName = slot?.DisplayName ?? string.Empty;
            ThemeId = slot?.ThemeId ?? string.Empty;
        }
    }
}
