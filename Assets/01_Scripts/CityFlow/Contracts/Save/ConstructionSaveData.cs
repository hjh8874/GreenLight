using System;

namespace CityFlow.Contracts.Save
{
    // 공사 중인 건물 1건. 절대 완료시각이 아니라 잔여시간으로 저장한다 —
    // _simTime은 로드 시 리셋되지 않으므로 절대시각으로 저장하면 즉시 완성되거나 영원히 안 끝난다.
    [Serializable]
    public sealed class ConstructionSaveData
    {
        public int X;
        public int Y;
        public TileType TargetType;
        public PlacementDirection Direction;
        public float RemainingSimSeconds;
        // 공사 중인 회사의 유형 id. 없으면 완성 시 전부 사무실이 된다(설계 결정 ④). 구세이브 = null.
        public string CompanyTypeId;
    }
}
