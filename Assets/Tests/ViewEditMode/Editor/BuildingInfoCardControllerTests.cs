using System.Reflection;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.UI;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class BuildingInfoCardControllerTests
{
    private static readonly BindingFlags PrivateInstance =
        BindingFlags.Instance | BindingFlags.NonPublic;

    [TestCase(TileType.House)]
    [TestCase(TileType.Office)]
    [TestCase(TileType.SpecialBuilding)]
    public void RefreshCurrentTileState_UsesCompletedConstructionType(
        TileType completedType)
    {
        GameObject owner = new GameObject("BuildingInfoCardControllerTests");
        try
        {
            BuildingInfoCardController controller =
                owner.AddComponent<BuildingInfoCardController>();
            MutableTileData tiles = new MutableTileData
            {
                CurrentType = TileType.UnderConstruction,
                ConstructionTargetType = completedType,
                HasConstruction = true
            };
            CityFlowServices services = new CityFlowServices(
                new SimEventHub(),
                tiles,
                null);

            SetPrivate(controller, "services", services);
            SetPrivate(controller, "currentTile", new Vector2Int(4, 6));
            SetPrivate(controller, "currentType", TileType.UnderConstruction);

            Assert.IsTrue(RefreshCurrentTileState(controller));
            Assert.AreEqual(
                TileType.UnderConstruction,
                GetCurrentType(controller));

            tiles.CompleteConstruction(completedType);

            Assert.IsTrue(RefreshCurrentTileState(controller));
            Assert.AreEqual(completedType, GetCurrentType(controller));
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    private static bool RefreshCurrentTileState(
        BuildingInfoCardController controller)
    {
        MethodInfo method = typeof(BuildingInfoCardController).GetMethod(
            "RefreshCurrentTileState",
            PrivateInstance);
        Assert.IsNotNull(method);
        return (bool)method.Invoke(controller, null);
    }

    private static TileType GetCurrentType(
        BuildingInfoCardController controller)
    {
        FieldInfo field = typeof(BuildingInfoCardController).GetField(
            "currentType",
            PrivateInstance);
        Assert.IsNotNull(field);
        return (TileType)field.GetValue(controller);
    }

    private static void SetPrivate(
        BuildingInfoCardController controller,
        string fieldName,
        object value)
    {
        FieldInfo field = typeof(BuildingInfoCardController).GetField(
            fieldName,
            PrivateInstance);
        Assert.IsNotNull(field);
        field.SetValue(controller, value);
    }

    private sealed class MutableTileData : IReadOnlyTileData
    {
        public TileType CurrentType { get; set; }
        public TileType ConstructionTargetType { get; set; }
        public bool HasConstruction { get; set; }

        public void CompleteConstruction(TileType completedType)
        {
            CurrentType = completedType;
            HasConstruction = false;
        }

        public TileType GetTileType(Vector2Int tile) => CurrentType;

        public PlacementDirection GetDirection(Vector2Int tile) =>
            PlacementDirection.North;

        public CongestionLevel GetCongestion(Vector2Int tile) =>
            CongestionLevel.Free;

        public float GetDensity01(Vector2Int tile) => 0f;

        public int GetQueueCount(Vector2Int tile, Dir entryDir) => 0;

        public Vector2Int GetFootprintSize(TileType type) =>
            TileFootprint.GetSize(type);

        public bool TryGetFootprintAnchor(
            Vector2Int tile,
            out Vector2Int anchor)
        {
            anchor = tile;
            return TileFootprint.IsBuilding(CurrentType);
        }

        public bool IsFootprintAnchor(Vector2Int tile) =>
            TileFootprint.IsBuilding(CurrentType);

        public bool TryGetConstructionProgress01(
            Vector2Int tile,
            out float progress01)
        {
            progress01 = HasConstruction ? 0.5f : 0f;
            return HasConstruction;
        }

        public bool TryGetConstructionTargetType(
            Vector2Int tile,
            out TileType targetType)
        {
            targetType = ConstructionTargetType;
            return HasConstruction;
        }
    }
}
