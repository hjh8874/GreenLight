using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Sim;
using CityFlow.UI.Controllers.Placement;
using NUnit.Framework;
using UnityEngine;

public class CompanyTypePlacementFlowTests
{
    [Test]
    public void Dispatcher_SelectedCompanyTypes_ProduceDistinctStaffingCapacities()
    {
        SimConfig config = SimConfig.Default();
        config.GridWidth = 12;
        config.GridHeight = 4;
        config.ConstructionHoursOffice = 0f;
        var events = new SimEventHub();
        var engine = new SimEngine(config, events);
        engine.SetCompanyTypes(new[]
        {
            NewType("office", 6),
            NewType("warehouse", 4),
            NewType("factory", 10)
        });
        var services = new CityFlowServices(
            events, engine, engine, stats: engine);
        var dispatcher = new PlacementActionDispatcher(null, false);

        Place(dispatcher, services, new Vector2Int(0, 0), "office");
        Place(dispatcher, services, new Vector2Int(3, 0), "warehouse");
        Place(dispatcher, services, new Vector2Int(6, 0), "factory");
        Place(dispatcher, services, new Vector2Int(9, 0), null);

        AssertCapacity(engine, new Vector2Int(0, 0), 6);
        AssertCapacity(engine, new Vector2Int(3, 0), 4);
        AssertCapacity(engine, new Vector2Int(6, 0), 10);
        AssertCapacity(engine, new Vector2Int(9, 0), 6,
            "유형 미선택 Office는 office 폴백");
    }

    static CompanyTypeInfo NewType(string id, int capacity) =>
        new CompanyTypeInfo(
            new CommuteWindow(id, 6f, 4f, 17f, 4f),
            capacity);

    static void Place(
        PlacementActionDispatcher dispatcher,
        CityFlowServices services,
        Vector2Int tile,
        string companyTypeId) =>
        dispatcher.PlaceInfrastructure(
            tile,
            TileType.Office,
            PlacementDirection.North,
            services,
            specialBuildingId: null,
            companyTypeId: companyTypeId);

    static void AssertCapacity(
        SimEngine engine,
        Vector2Int tile,
        int expected,
        string message = null)
    {
        Assert.IsTrue(engine.TryGetCompanyStaffing(
            tile,
            out CompanyStaffing staffing));
        Assert.AreEqual(expected, staffing.Capacity, message);
    }
}
