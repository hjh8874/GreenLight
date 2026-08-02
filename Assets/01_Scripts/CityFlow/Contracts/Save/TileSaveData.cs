using System;
using CityFlow.Contracts;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class TileSaveData
    {
        public int X;
        public int Y;
        public TileType Type;
        public PlacementDirection Direction;
        // Office 의 회사 유형 id(사무실·공장·물류창고). 구세이브 = null → 폴백 창(마이그레이션 공짜).
        // 없으면 로드가 RegisterRestoredCompany 로 회사를 다시 만들면서 유형이 조용히 사라진다.
        public string CompanyTypeId;
    }
}
