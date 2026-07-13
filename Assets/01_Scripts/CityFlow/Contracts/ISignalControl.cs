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
    }
}
