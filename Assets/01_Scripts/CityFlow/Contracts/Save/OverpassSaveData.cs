using System;

namespace CityFlow.Contracts.Save
{
    // 입체교차는 좌표만 — 조율값·계수 없음(스펙 2026-07-12). RoundaboutSaveData의 자매.
    [Serializable]
    public sealed class OverpassSaveData
    {
        public int X;
        public int Y;
    }
}
