using System;
using UnityEngine;

namespace CityFlow.Save
{
    [Serializable]
    public sealed class SaveSlotMetadata
    {
        [SerializeField] private string slotId;
        [SerializeField] private string displayName;
        [SerializeField] private long savedAtUtcTicks;
        [SerializeField] private bool automatic;
        [SerializeField] private bool hasPreview;

        public string SlotId => slotId;
        public string DisplayName => displayName;
        public long SavedAtUtcTicks => savedAtUtcTicks;
        public bool IsAutomatic => automatic;
        public bool HasPreview => hasPreview;

        public SaveSlotMetadata(
            string slotId,
            string displayName,
            long savedAtUtcTicks,
            bool automatic,
            bool hasPreview)
        {
            this.slotId = slotId;
            this.displayName = displayName;
            this.savedAtUtcTicks = savedAtUtcTicks;
            this.automatic = automatic;
            this.hasPreview = hasPreview;
        }

        internal void MarkPreviewAvailable()
        {
            hasPreview = true;
        }
    }
}
