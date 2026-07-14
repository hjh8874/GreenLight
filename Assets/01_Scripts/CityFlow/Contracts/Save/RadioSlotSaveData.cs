using System;

namespace CityFlow.Contracts.Save
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
