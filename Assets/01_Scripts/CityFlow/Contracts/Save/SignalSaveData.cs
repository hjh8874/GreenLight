using System;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class SignalSaveData
    {
        public int X;
        public int Y;
        public int OffsetSlots;
        // 초록 길이(듀티) 레버 — 처리량에 직접 영향. 저장 안 하면 로드 시 조율이 날아감.
        // 구세이브 호환: JSON에 이 필드 없으면 0으로 역직렬화 → 복원부가 0을 "미저장"으로 보고 기본 초록 유지.
        public int GreenSlots;
    }
}
