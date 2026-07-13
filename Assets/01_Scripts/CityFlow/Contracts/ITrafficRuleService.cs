using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Contracts
{
    // 도로 규칙 배치 계약(설계 2026-07-13 §분리 — ISignalControl 3분할). 방향 제약 표지판
    // (일방통행·턴제한) — 물리 인프라가 아니라 "규칙"이라 IIntersectionFacilityService와 성격이
    // 다르다(팀장 제안 확정). 배치는 상점 전제(김건 상점 UI가 이 계약에도 붙는다).
    public interface ITrafficRuleService
    {
        // 일방통행 배치(스펙 2026-07-12): 교차로 3형제와 달리 일반 도로 전용(!IsIntersection) —
        // 배치 조건이 정반대라 별도 배타 검사가 필요 없다(자연 배타). 방향값을 들고 있다
        // (좌표-전용 셋이 아니라 Dictionary). GetOnewayDir은 뷰·저장용 조회(없으면 Vector2Int.zero).
        IReadOnlyList<Vector2Int> OnewayTiles { get; }
        bool CanPlaceOneway(Vector2Int tile);
        bool TryPlaceOneway(Vector2Int tile, Vector2Int dir);
        bool TryRemoveOneway(Vector2Int tile);
        Vector2Int GetOnewayDir(Vector2Int tile);

        // 턴 제한 표지판 배치(스펙 2026-07-12): 교차로 전용이되 신호와 공존(로터리·입체와는 양방향
        // 배타 — 계획 정정 2026-07-12). 신호(시간 배분)·표지판(방향 배분)은 직교 개념이라
        // CanPlaceSignal만 무수정(공존) — CanPlaceRoundabout/CanPlaceOverpass는 표지판도 검사.
        // 값은 방향 벡터 대신 TurnMode enum(일방통행과 동형 Dictionary 소유, 값 타입만 다름).
        // GetTurnMode는 뷰·저장용 조회(없으면 null).
        IReadOnlyList<Vector2Int> TurnSignTiles { get; }
        bool CanPlaceTurnSign(Vector2Int tile);
        bool TryPlaceTurnSign(Vector2Int tile, TurnMode mode);
        bool TryRemoveTurnSign(Vector2Int tile);
        TurnMode? GetTurnMode(Vector2Int tile);
    }
}
