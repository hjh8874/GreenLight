using System.Reflection;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.UI;
using CityFlow.UI.Data;
using NUnit.Framework;
using TMPro;
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

    [Test]
    public void NormalBuildingMetrics_RemoveTestLabelAndCloseHiddenRowGap()
    {
        GameObject owner = new GameObject(
            "BuildingInfoCardLayoutTest",
            typeof(RectTransform));
        GameObject incomeRow = new GameObject(
            "IncomeRow",
            typeof(RectTransform));
        GameObject delayRow = new GameObject(
            "DelayRow",
            typeof(RectTransform));
        GameObject incomeValue = new GameObject(
            "IncomeValue",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        GameObject delayValue = new GameObject(
            "DelayValue",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));

        try
        {
            incomeRow.transform.SetParent(owner.transform, false);
            delayRow.transform.SetParent(owner.transform, false);
            incomeValue.transform.SetParent(incomeRow.transform, false);
            delayValue.transform.SetParent(delayRow.transform, false);

            RectTransform incomeRect =
                incomeRow.GetComponent<RectTransform>();
            RectTransform delayRect =
                delayRow.GetComponent<RectTransform>();
            RectTransform cardRect =
                owner.GetComponent<RectTransform>();
            cardRect.sizeDelta = new Vector2(430f, 286f);
            incomeRect.anchoredPosition = new Vector2(0f, -181f);
            delayRect.anchoredPosition = new Vector2(0f, -219f);

            BuildingInfoCardController controller =
                owner.AddComponent<BuildingInfoCardController>();
            TMP_Text incomeText =
                incomeValue.GetComponent<TextMeshProUGUI>();
            TMP_Text delayText =
                delayValue.GetComponent<TextMeshProUGUI>();
            SetPrivate(controller, "txtIncomePerMin", incomeText);
            SetPrivate(controller, "txtDelaySeconds", delayText);

            InvokePrivate(
                controller,
                "BindDataToUI",
                new BuildingStoryData(
                    "테스트 건물",
                    "테스트 설명",
                    6,
                    2,
                    0.7f),
                0f,
                CongestionLevel.Free);

            Assert.IsFalse(incomeRow.activeSelf);
            Assert.That(
                delayRect.anchoredPosition.y,
                Is.EqualTo(-181f).Within(0.001f));
            Assert.That(
                cardRect.sizeDelta.y,
                Is.EqualTo(248f).Within(0.001f));
            Assert.AreEqual("지연: +0.7초", delayText.text);
            StringAssert.DoesNotContain("(테스트)", delayText.text);

            InvokePrivate(
                controller,
                "SetIncomeMetricRowVisible",
                true);
            Assert.IsTrue(incomeRow.activeSelf);
            Assert.That(
                delayRect.anchoredPosition.y,
                Is.EqualTo(-219f).Within(0.001f));
            Assert.That(
                cardRect.sizeDelta.y,
                Is.EqualTo(286f).Within(0.001f));
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

    private static object InvokePrivate(
        BuildingInfoCardController controller,
        string methodName,
        params object[] arguments)
    {
        MethodInfo method = typeof(BuildingInfoCardController).GetMethod(
            methodName,
            PrivateInstance);
        Assert.IsNotNull(method);
        return method.Invoke(controller, arguments);
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
