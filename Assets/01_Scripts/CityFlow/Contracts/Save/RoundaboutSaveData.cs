using System;

namespace CityFlow.Contracts.Save
{
    // 회전교차로는 좌표만 — 조율값 없음(스펙 2026-07-11). SignalSaveData의 자매.
    [Serializable]
    public sealed class RoundaboutSaveData
    {
        public int X;
        public int Y;
    }
}
