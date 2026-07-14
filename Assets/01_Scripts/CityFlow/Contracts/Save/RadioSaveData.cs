using System;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class RadioSaveData
    {
        public RadioSlotSaveData[] Slots;
        public int CurrentSlotIndex = -1;
    }
}
