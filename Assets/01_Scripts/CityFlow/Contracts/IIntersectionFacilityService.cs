using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Contracts
{
    // 교차로 시설 배치 계약(설계 2026-07-13 §분리 — ISignalControl 3분할). 돈 내고 짓는 물리
    // 인프라(신호함·로터리섬·입체데크) — ISignalControl(조율, 공짜 실시간)과 트러스트 경계가
    // 다르다(배치는 상점·경제 통과가 전제). 김건 상점 UI가 이 계약에 붙는다.
    public interface IIntersectionFacilityService
    {
        // 신호 배치(구매 피벗 2단계, 스펙 2026-07-11): AutoDetectSignals=false 모드에서만 유효.
        // greenSlots = 구매 시 정하는 "방향+초"(가로 초록 슬롯 — 주기 절반 초과 = 가로 우선).
        // 가격 검증은 상점(UI+경제)이 호출 전에 — 엔진은 배치 규칙(교차로·중복)만 지킨다.
        bool CanPlaceSignal(Vector2Int tile);
        bool TryPlaceSignal(Vector2Int tile, int greenSlots);
        bool TryRemoveSignal(Vector2Int tile);

        // 신호 존재 조회(철거·점유 표시용) — ISignalControl과 동일 프로퍼티를 시설 계약에도 노출
        IReadOnlyList<Vector2Int> SignalTiles { get; }

        // 회전교차로 배치(스펙 2026-07-11): 신호와 배타(한 타일 한 장치). 배치 모드 전용.
        // 조율값 없음 — "조율 안 해도 흐르는 것"이 정체성. 수식(λ 0.25·용량 ×0.7)은 엔진 소관.
        IReadOnlyList<Vector2Int> RoundaboutTiles { get; }
        bool CanPlaceRoundabout(Vector2Int tile);
        bool TryPlaceRoundabout(Vector2Int tile);
        bool TryRemoveRoundabout(Vector2Int tile);

        // 입체교차 배치(스펙 2026-07-12): 축 분리로 간섭 소멸 — 교차로 4형제의 넷째(엔드게임 천장).
        // 신호·로터리와 3자 배타. 조율값·계수 없음(간섭 0·페널티 0이 정체성). 수식은 엔진 소관.
        IReadOnlyList<Vector2Int> OverpassTiles { get; }
        bool CanPlaceOverpass(Vector2Int tile);
        bool TryPlaceOverpass(Vector2Int tile);
        bool TryRemoveOverpass(Vector2Int tile);

        // 우선도로 배치(스펙 2026-07-13): 무신호 교차로에 우선축 지정 — 메인축 무정차·곁길 양보.
        // 신호·로터리·입체와 4자 배타(한 교차로 한 장치). 축 값(H/V) 보유 — 로터리와 달리 방향성 있음.
        IReadOnlyList<Vector2Int> PriorityRoadTiles { get; }
        Axis GetPriorityAxis(Vector2Int tile);
        bool CanPlacePriorityRoad(Vector2Int tile);
        bool TryPlacePriorityRoad(Vector2Int tile, Axis mainAxis);
        bool TryRemovePriorityRoad(Vector2Int tile);
    }
}
