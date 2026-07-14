using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Contracts
{
    // 유저(UI)가 교차로 신호를 조율하는 유일한 창구 — 두 레버:
    //   오프셋(그린웨이브 타이밍)  ·  초록 길이(교차로 듀티 = 유효 용량)
    // 방치형 능동 코어: 심심할 때 신호를 탭해 이 값들을 미는 것 = 유일한 실시간 상호작용.
    // ponytail: 뷰용 페이즈 조회(IsSignalGreen·GetSignalPhase)는 여기 안 넣음 — 조작이 아니라 렌더 관심사.
    //           주기(CycleSlots) 노출은 UI가 요구하면 그때(초록을 %로 보여줄 때). YAGNI.
    // 조율 전용 계약(설계 2026-07-13 §분리 — 팀장 리뷰 #47/#54/#55 + 김건 상점 UI 착수 계기):
    //   배치물(신호·로터리·입체·일방통행·턴제한) 배치 계약은 IIntersectionFacilityService·
    //   ITrafficRuleService로 분리됨. 근거: 조율=공짜 실시간 vs 배치=상점·경제 통과 → 트러스트
    //   경계가 다르다(팀장 #46/#47 결제-원자성). 확정: 팀장 제안, 최종 합의는 김건.
    public interface ISignalControl
    {
        // 존재하는 신호 타일들(자동 감지 또는 배치). UI가 조작 대상을 여기서 고른다.
        IReadOnlyList<Vector2Int> SignalTiles { get; }

        // 전체 주기 (Max Value 설정을 위해 UI에서 사용)
        int GetSignalCycleSlots(Vector2Int tile);

        // 오프셋 레버: 인접 신호 타이밍을 밀어 그린웨이브를 맞춘다. 값은 랩어라운드(주기 등가).
        int GetSignalOffsetSlots(Vector2Int tile);
        bool TrySetSignalOffsetSlots(Vector2Int tile, int slots);

        // 초록 길이 레버: 그 교차로가 차를 통과시키는 시간 비율(유효 용량). [0, 주기]로 클램프.
        int GetSignalGreenSlots(Vector2Int tile);
        bool TrySetSignalGreenSlots(Vector2Int tile, int slots);

        // 오버라이드 스킬(기획 §2-D): duration초 양축 강제 초록(정령 마법 — 충돌 소멸) + 엔진 쿨다운.
        // horizontal은 초록 축이 아니라 **코리도어 걷기 방향**(그 라인의 신호들을 함께 발동).
        // 쿨다운을 엔진이 들고 있어 UI(트러스트 경계 밖)가 못 우회 → 조회만 계약으로 노출.
        bool TryOverrideSignal(Vector2Int tile, bool horizontal);
        float GetOverrideSecondsLeft(Vector2Int tile);   // 0 = 비활성
        float GetOverrideCooldownLeft(Vector2Int tile);  // 0 = 사용 가능

        // UI 쿨타임 애니메이션용 (타일, 가로여부, 오버라이드지속시간, 전체쿨타임시간)
        event System.Action<Vector2Int, bool, float, float> OnOverrideTriggered;
    }
}
