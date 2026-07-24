namespace CityFlow.Content.Transit
{
    /// <summary>
    /// 게임에서 운행할 수 있는 버스 종류입니다.
    /// </summary>
    public enum BusType
    {
        None = 0,

        /// <summary>
        /// 학교와 주거지역 사이를 운행하는 통학버스입니다.
        /// </summary>
        SchoolBus = 1,

        /// <summary>
        /// 일반 버스 정류장을 순환하는 시내버스입니다.
        /// </summary>
        CityBus = 2,

        /// <summary>
        /// 많은 승객을 수송하는 대형 버스입니다.
        /// </summary>
        ExpressBus = 3
    }
}