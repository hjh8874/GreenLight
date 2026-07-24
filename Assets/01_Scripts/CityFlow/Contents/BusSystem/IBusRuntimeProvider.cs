namespace CityFlow.Content.Transit
{
    /// <summary>
    /// 버스 UI가 운행 시스템 종류와 무관하게
    /// 동일한 방식으로 버스 상태를 조회하기 위한 계약입니다.
    /// </summary>
    public interface IBusRuntimeProvider
    {
        BusRuntime Runtime { get; }

        BusRoute Route { get; }

        bool StartService();

        void StopService();
    }
}