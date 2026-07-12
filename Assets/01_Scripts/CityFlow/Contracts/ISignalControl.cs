using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Contracts
{
    // 유저(UI)가 교차로 신호를 조율하는 유일한 창구 — 두 레버:
    //   오프셋(그린웨이브 타이밍)  ·  초록 길이(교차로 듀티 = 유효 용량)
    // 방치형 능동 코어: 심심할 때 신호를 탭해 이 값들을 미는 것 = 유일한 실시간 상호작용.
    // ponytail: 뷰용 페이즈 조회(IsSignalGreen·GetSignalPhase)는 여기 안 넣음 — 조작이 아니라 렌더 관심사.
    //           주기(CycleSlots) 노출은 UI가 요구하면 그때(초록을 %로 보여줄 때). YAGNI.
    // 제안(설계 §5): SimEngine에 흩어져 있던 신호 조작 메서드를 계약으로 승격. 최종 확정은 주석·김건 합의.
    public interface ISignalControl
    {
        // 존재하는 신호 타일들(자동 감지 또는 배치). UI가 조작 대상을 여기서 고른다.
        IReadOnlyList<Vector2Int> SignalTiles { get; }

        // 오프셋 레버: 인접 신호 타이밍을 밀어 그린웨이브를 맞춘다. 값은 랩어라운드(주기 등가).
        int GetSignalOffsetSlots(Vector2Int tile);
        bool TrySetSignalOffsetSlots(Vector2Int tile, int slots);

        // 초록 길이 레버: 그 교차로가 차를 통과시키는 시간 비율(유효 용량). [0, 주기]로 클램프.
        int GetSignalGreenSlots(Vector2Int tile);
        bool TrySetSignalGreenSlots(Vector2Int tile, int slots);

        // 오버라이드 스킬(기획 §2-D): duration초 양축 강제 초록(정령 마법 — 충돌 소멸) + 엔진 쿨다운.
        // horizontal은 초록 축이 아니라 **코리도어 걷기 방향**(그 라인의 신호들을 함께 발동).
        // 쿨다운을 엔진이 들고 있어 UI(트러스트 경계 밖)가 못 우회 → 조회만 계약으로 노출.
        // 제안(E-1): 튜너·통합뷰가 SimEngine 직접 캐스팅하던 걸 계약으로 승격. 최종 확정은 김건 합의.
        bool TryOverrideSignal(Vector2Int tile, bool horizontal);
        float GetOverrideSecondsLeft(Vector2Int tile);   // 0 = 비활성
        float GetOverrideCooldownLeft(Vector2Int tile);  // 0 = 사용 가능

        // 신호 배치(구매 피벗 2단계, 스펙 2026-07-11): AutoDetectSignals=false 모드에서만 유효.
        // greenSlots = 구매 시 정하는 "방향+초"(가로 초록 슬롯 — 주기 절반 초과 = 가로 우선).
        // 가격 검증은 상점(UI+경제)이 호출 전에 — 엔진은 배치 규칙(교차로·중복)만 지킨다.
        // 제안: 상점 UI가 붙을 창구. 최종 확정은 김건 합의.
        bool CanPlaceSignal(Vector2Int tile);
        bool TryPlaceSignal(Vector2Int tile, int greenSlots);
        bool TryRemoveSignal(Vector2Int tile);

        // 회전교차로 배치(스펙 2026-07-11): 신호와 배타(한 타일 한 장치). 배치 모드 전용.
        // 조율값 없음 — "조율 안 해도 흐르는 것"이 정체성. 수식(λ 0.25·용량 ×0.7)은 엔진 소관.
        // 제안: 상점 UI 창구(신호 3종의 자매). 최종 확정은 김건 합의.
        IReadOnlyList<Vector2Int> RoundaboutTiles { get; }
        bool CanPlaceRoundabout(Vector2Int tile);
        bool TryPlaceRoundabout(Vector2Int tile);
        bool TryRemoveRoundabout(Vector2Int tile);

        // 입체교차 배치(스펙 2026-07-12): 축 분리로 간섭 소멸 — 교차로 4형제의 넷째(엔드게임 천장).
        // 신호·로터리와 3자 배타. 조율값·계수 없음(간섭 0·페널티 0이 정체성). 수식은 엔진 소관.
        // 제안: 상점 UI 창구. 제안이 14종까지 누적(오버라이드3+신호배치3+로터리4+입체4) — 배치물 공통 계약 분리도 검토 대상. 최종 확정은 김건 합의.
        IReadOnlyList<Vector2Int> OverpassTiles { get; }
        bool CanPlaceOverpass(Vector2Int tile);
        bool TryPlaceOverpass(Vector2Int tile);
        bool TryRemoveOverpass(Vector2Int tile);

        // 일방통행 배치(스펙 2026-07-12): 교차로 3형제와 달리 일반 도로 전용(!IsIntersection) —
        // 배치 조건이 정반대라 별도 배타 검사가 필요 없다(자연 배타). 4번째 배치 가족의 첫째 다른 점:
        // 방향값을 들고 있다(좌표-전용 셋이 아니라 Dictionary). GetOnewayDir은 뷰·저장용 조회
        // (없으면 Vector2Int.zero) — "제안" 집계엔 포함하지 않음.
        // 제안이 18종 도달(오버라이드3+신호배치3+로터리4+입체4+일방통행4) — 배치물 공통 계약 분리를
        // 김건 안건으로 격상. 최종 확정은 김건 합의.
        IReadOnlyList<Vector2Int> OnewayTiles { get; }
        bool CanPlaceOneway(Vector2Int tile);
        bool TryPlaceOneway(Vector2Int tile, Vector2Int dir);
        bool TryRemoveOneway(Vector2Int tile);
        Vector2Int GetOnewayDir(Vector2Int tile);

        // 턴 제한 표지판 배치(스펙 2026-07-12): 5번째 배치 가족 — 교차로 전용이되 신호와 공존
        // (로터리·입체와만 배타). 신호(시간 배분)·표지판(방향 배분)은 직교 개념이라 CanPlaceSignal 등
        // 기존 3형제 API는 무수정 — 표지판 쪽에서만 로터리/입체를 검사한다.
        // 값은 방향 벡터 대신 TurnMode enum(일방통행과 동형 Dictionary 소유, 값 타입만 다름).
        // GetTurnMode는 뷰·저장용 조회(없으면 null) — "제안" 집계엔 포함하지 않음(GetOnewayDir과 동형).
        // 제안이 22종 도달(오버라이드3+신호배치3+로터리4+입체4+일방통행4+턴제한4) — 배치물 공통 계약
        // 분리 안건이 계속 누적. 최종 확정은 김건 합의.
        IReadOnlyList<Vector2Int> TurnSignTiles { get; }
        bool CanPlaceTurnSign(Vector2Int tile);
        bool TryPlaceTurnSign(Vector2Int tile, TurnMode mode);
        bool TryRemoveTurnSign(Vector2Int tile);
        TurnMode? GetTurnMode(Vector2Int tile);
    }
}
