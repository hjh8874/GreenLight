namespace CityFlow.Content.Transit
{
    /// <summary>
    /// UI와 운행 시스템이 공통으로 사용하는 버스 상태입니다.
    /// </summary>
    public enum BusOperatingState
    {
        Locked = 0,
        Idle = 1,
        Departing = 2,
        Moving = 3,
        WaitingAtStop = 4,
        Returning = 5,
        RouteUnavailable = 6,
        OutOfService = 7
    }
}