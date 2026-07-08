using System;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class SignalSaveData
    {
        public int X;
        public int Y;
        public int OffsetSlots;
    }
}
