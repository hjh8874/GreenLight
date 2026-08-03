using System;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class BusStopSaveData
    {
        public int X;
        public int Y;
        public bool HasOppositePlatform;
        public int OppositeX;
        public int OppositeY;
    }
}
