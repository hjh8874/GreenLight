namespace CityFlow.Contracts
{
    // 턴 제한 표지판(스펙 2026-07-12): 교차로 방향 배분 — 신호(시간 배분)와 직교, 같은 교차로에 공존.
    // v0 최소 2종("좌회전만"/"우회전만"). 직진만·좌회전금지는 후보(비목표, 확장 여지로 enum 유지).
    // Contracts 소속(계약 표면에 노출) — SimEngine.TryPlaceTurnSign/GetTurnMode가 이 타입을 주고받는다.
    public enum TurnMode
    {
        LeftOnly,
        RightOnly
    }
}
