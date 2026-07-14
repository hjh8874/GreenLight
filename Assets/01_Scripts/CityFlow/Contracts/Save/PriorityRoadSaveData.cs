using System;

namespace CityFlow.Contracts.Save
{
    // 우선도로 세이브(스펙 2026-07-13): 좌표 + 축(int 캐스팅). RoundaboutSaveData(좌표만)와
    // OnewaySaveData(좌표+값)의 조합 — 배치 조건은 로터리형(교차로 전용), 값 하나만 얹는 점은 일방통행형.
    [Serializable]
    public sealed class PriorityRoadSaveData
    {
        public int X;
        public int Y;
        public int Axis;   // (int)CityFlow.Contracts.Axis — 0=Horizontal, 1=Vertical
    }
}
