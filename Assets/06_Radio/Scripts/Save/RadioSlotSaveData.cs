using System;

namespace GreenLight.Radio.Save
{
    [Serializable]
    public sealed class RadioSlotSaveData
    {
        public int SlotIndex;
        public bool IsUnlocked;
        public string SourceType;
        public string YoutubeVideoId;
        public string DisplayName;
        public string ThemeId;
    }
}
