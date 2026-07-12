using System;

namespace CityFlow.Contracts.Save
{
    // 일방통행은 좌표+방향(단위벡터) — 배치물 세이브 첫 방향 필드(스펙 2026-07-12).
    // RoundaboutSaveData/OverpassSaveData의 자매지만 조율값 대신 방향값을 들고 있는 점이 다르다.
    [Serializable]
    public sealed class OnewaySaveData
    {
        public int X;
        public int Y;
        public int DirX;
        public int DirY;
    }
}
