namespace CityFlow.UI.Data
{
    public enum InfrastructureKind
    {
        Signal,             // 신호등
        Roundabout,         // 회전교차로
        Overpass,           // 입체교차로
        Oneway,             // 일방통행
        TurnRestriction,    // 턴 제한
        PriorityRoad        // 우선도로 (무신호 교차로 λ 비대칭 — 주축 무정차, 곁길 양보)
    }
}
