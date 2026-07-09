using System;

namespace GreenLight.Radio.Save
{
    [Serializable]
    public sealed class RadioSaveData
    {
        public RadioSlotSaveData[] Slots;
    }
}
