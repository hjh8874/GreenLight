namespace CityFlow.Contracts
{
    public interface IReadOnlyCityStats
    {
        int ActiveVehicleCount { get; }

        // 도로 예산제(스펙 2026-07-17): UI 카운터("도로 N/M")용 읽기 창구. N = 현재 도로 수, M = 상한.
        int RoadTileCount { get; }
        int MaxRoadTiles { get; }
    }
}
