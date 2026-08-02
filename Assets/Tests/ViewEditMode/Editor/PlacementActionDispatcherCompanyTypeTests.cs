using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.UI.Controllers.Placement;
using NUnit.Framework;
using UnityEngine;

// 기본 에디터 어셈블리. 실행:
// run_tests(group_names=[".*PlacementActionDispatcherCompanyTypeTests.*"])
public class PlacementActionDispatcherCompanyTypeTests
{
    [Test]
    public void PlaceInfrastructure_Office_PassesDefaultCompanyTypeId()
    {
        FakePlacement placement = Dispatch(TileType.Office);

        Assert.AreEqual(1, placement.FourArgumentCallCount,
            "Office도 공용 4인자 배치 계약을 사용해야 한다");
        Assert.AreEqual("office", placement.LastCompanyTypeId);
    }

    [Test]
    public void PlaceInfrastructure_House_PassesNullCompanyTypeId()
    {
        FakePlacement placement = Dispatch(TileType.House);

        Assert.AreEqual(1, placement.FourArgumentCallCount,
            "비회사도 같은 공용 계약을 쓰되 유형은 전달하지 않는다");
        Assert.IsNull(placement.LastCompanyTypeId);
    }

    static FakePlacement Dispatch(TileType type)
    {
        var placement = new FakePlacement();
        var services = new CityFlowServices(
            new SimEventHub(),
            new EmptyTileData(),
            placement);
        var dispatcher = new PlacementActionDispatcher(
            availableTiles: null,
            useFakeMode: false);

        dispatcher.PlaceInfrastructure(
            new Vector2Int(2, 3),
            type,
            PlacementDirection.North,
            services);
        return placement;
    }

    sealed class FakePlacement : IPlacementService
    {
        public int FourArgumentCallCount { get; private set; }
        public string LastCompanyTypeId { get; private set; }

        public bool CanPlace(
            Vector2Int tile,
            TileType type,
            PlacementDirection direction = PlacementDirection.North) => true;

        public bool Place(
            Vector2Int tile,
            TileType type,
            PlacementDirection direction = PlacementDirection.North) => true;

        public bool Place(
            Vector2Int tile,
            TileType type,
            PlacementDirection direction,
            string companyTypeId)
        {
            FourArgumentCallCount++;
            LastCompanyTypeId = companyTypeId;
            return true;
        }

        public bool Remove(Vector2Int tile) => true;
    }

    sealed class EmptyTileData : IReadOnlyTileData
    {
        public CongestionLevel GetCongestion(Vector2Int tile) =>
            CongestionLevel.Free;
        public float GetDensity01(Vector2Int tile) => 0f;
        public int GetQueueCount(Vector2Int tile, Dir entryDir) => 0;
        public TileType GetTileType(Vector2Int tile) => TileType.Empty;
        public PlacementDirection GetDirection(Vector2Int tile) =>
            PlacementDirection.North;
        public Vector2Int GetFootprintSize(TileType type) => Vector2Int.one;

        public bool TryGetFootprintAnchor(
            Vector2Int tile,
            out Vector2Int anchor)
        {
            anchor = default;
            return false;
        }

        public bool IsFootprintAnchor(Vector2Int tile) => false;

        public bool TryGetConstructionProgress01(
            Vector2Int tile,
            out float progress01)
        {
            progress01 = 0f;
            return false;
        }

        public bool TryGetConstructionTargetType(
            Vector2Int tile,
            out TileType targetType)
        {
            targetType = TileType.Empty;
            return false;
        }
    }
}
