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
        // 자동 감지된 교차로(신호) 타일들. UI가 조작 대상을 여기서 고른다.
        IReadOnlyList<Vector2Int> SignalTiles { get; }

        // 오프셋 레버: 인접 신호 타이밍을 밀어 그린웨이브를 맞춘다. 값은 랩어라운드(주기 등가).
        int GetSignalOffsetSlots(Vector2Int tile);
        bool TrySetSignalOffsetSlots(Vector2Int tile, int slots);

        // 초록 길이 레버: 그 교차로가 차를 통과시키는 시간 비율(유효 용량). [0, 주기]로 클램프.
        int GetSignalGreenSlots(Vector2Int tile);
        bool TrySetSignalGreenSlots(Vector2Int tile, int slots);
    }
}
