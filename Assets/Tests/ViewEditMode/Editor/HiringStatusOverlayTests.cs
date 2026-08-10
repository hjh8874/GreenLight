using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// 이름 필터로 돌린다: run_tests(group_names=[".*HiringStatusOverlayTests.*"])
public class HiringStatusOverlayTests
{
    [Test]
    public void Initialize_RegistersExistingUnderstaffedCompany_WithoutPlacedEvent()
    {
        RunWithOverlay((overlay, tiles, stats, services) =>
        {
            var anchor = new Vector2Int(2, 3);
            tiles.AddOffice(anchor);
            stats.SetStaffing(anchor, 1, 3);
            services.RegisterWorldGrid(new FakeWorldGrid(8, 8));

            overlay.Initialize(services);

            Assert.AreEqual(1, LabelCount(overlay),
                "씬 진입 시 이미 존재하는 정원 미달 회사도 이벤트 없이 라벨을 받아야 한다");
        });
    }

    [Test]
    public void Initialize_CreatesOneLabelPerFootprint_NotPerTile()
    {
        RunWithOverlay((overlay, tiles, stats, services) =>
        {
            var anchor = Vector2Int.zero;
            tiles.AddOffice(anchor);
            stats.SetStaffing(anchor, 2, 4);
            services.RegisterWorldGrid(new FakeWorldGrid(4, 4));

            overlay.Initialize(services);

            Assert.AreEqual(1, LabelCount(overlay),
                "2x2 회사도 앵커에만 라벨 하나를 만들어야 한다");
        });
    }

    [Test]
    public void Initialize_DoesNotCreateLabelForFullyStaffedCompany()
    {
        RunWithOverlay((overlay, tiles, stats, services) =>
        {
            var anchor = new Vector2Int(1, 1);
            tiles.AddOffice(anchor);
            stats.SetStaffing(anchor, 3, 3);
            services.RegisterWorldGrid(new FakeWorldGrid(4, 4));

            overlay.Initialize(services);

            Assert.AreEqual(0, LabelCount(overlay),
                "정원이 찬 회사에는 채용 라벨이 없어야 한다");
        });
    }

    [Test]
    public void Initialize_WithoutWorldServices_UsesGridUtilFallback()
    {
        RunWithOverlay((overlay, tiles, stats, services) =>
        {
            var anchor = new Vector2Int(2, 3);
            tiles.AddOffice(anchor);
            stats.SetStaffing(anchor, 2, 6);

            overlay.Initialize(services);

            HiringStatusIndicatorView indicator =
                IndicatorAt(overlay, anchor);
            Vector3 expectedPosition =
                (GridUtil.GridToWorld(anchor) +
                 GridUtil.GridToWorld(anchor + Vector2Int.one)) * 0.5f +
                Vector3.back * 1.2f;
            Camera cam = Camera.main;
            RectTransform canvasRect = OverlayCanvasRect(overlay);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                cam.WorldToScreenPoint(expectedPosition),
                null,
                out Vector2 expectedLocalPosition);

            Assert.AreEqual(1, LabelCount(overlay));
            Assert.AreEqual(6, indicator.SegmentCount);
            Assert.AreEqual(2, indicator.FilledSegmentCount);
            Assert.That(
                ((RectTransform)indicator.transform).anchoredPosition,
                Is.EqualTo(expectedLocalPosition));
            Assert.AreEqual(
                RenderMode.ScreenSpaceOverlay,
                canvasRect.GetComponent<Canvas>().renderMode);
        });
    }

    [Test]
    public void Update_HidesIndicator_WhenBuildingIsBehindCamera()
    {
        RunWithOverlay((overlay, tiles, stats, services) =>
        {
            var anchor = new Vector2Int(2, 3);
            tiles.AddOffice(anchor);
            stats.SetStaffing(anchor, 2, 6);
            overlay.Initialize(services);

            HiringStatusIndicatorView indicator =
                IndicatorAt(overlay, anchor);
            Assert.IsTrue(indicator.gameObject.activeSelf);

            Camera cam = Camera.main;
            Vector3 previousPosition = cam.transform.position;
            Quaternion previousRotation = cam.transform.rotation;
            try
            {
                cam.transform.SetPositionAndRotation(
                    new Vector3(0f, 0f, 10f),
                    Quaternion.identity);
                InvokeScreenPositionUpdate(overlay, cam);

                Assert.IsFalse(indicator.gameObject.activeSelf);
            }
            finally
            {
                cam.transform.SetPositionAndRotation(
                    previousPosition,
                    previousRotation);
            }
        });
    }

    [Test]
    public void Initialize_ConfiguresSixSegments_FromOfficeStaffing()
    {
        RunWithOverlay((overlay, tiles, stats, services) =>
        {
            var anchor = new Vector2Int(2, 2);
            tiles.AddOffice(anchor);
            stats.SetStaffing(anchor, 1, 6);
            services.RegisterWorldGrid(new FakeWorldGrid(6, 6));

            overlay.Initialize(services);

            HiringStatusIndicatorView indicator =
                IndicatorAt(overlay, anchor);
            Assert.AreEqual(6, indicator.SegmentCount);
            Assert.AreEqual(1, indicator.FilledSegmentCount);
            Assert.AreEqual("채용 중", indicator.StatusText);

            List<Image> slots = IndicatorSlots(indicator);
            Assert.That(
                slots,
                Has.All.Matches<Image>(slot =>
                    slot.type == Image.Type.Simple));
            Assert.That(
                Mathf.Abs(Mathf.DeltaAngle(
                    slots[0].rectTransform.localEulerAngles.z,
                    slots[1].rectTransform.localEulerAngles.z)),
                Is.EqualTo(60f).Within(0.01f));
        });
    }

    [TestCase(2, 4)]
    [TestCase(3, 5)]
    public void Initialize_UsesActualCompanyCapacity(
        int filled,
        int capacity)
    {
        RunWithOverlay((overlay, tiles, stats, services) =>
        {
            var anchor = new Vector2Int(1, 1);
            tiles.AddOffice(anchor);
            stats.SetStaffing(anchor, filled, capacity);
            services.RegisterWorldGrid(new FakeWorldGrid(4, 4));

            overlay.Initialize(services);

            HiringStatusIndicatorView indicator =
                IndicatorAt(overlay, anchor);
            Assert.AreEqual(capacity, indicator.SegmentCount);
            Assert.AreEqual(filled, indicator.FilledSegmentCount);
            Assert.AreEqual("채용 중", indicator.StatusText);
        });
    }

    [Test]
    public void BuildingInfoVisibility_HidesOnlyMatchingCompanyIndicator()
    {
        RunWithOverlay((overlay, tiles, stats, services) =>
        {
            var first = new Vector2Int(0, 0);
            var second = new Vector2Int(4, 4);
            tiles.AddOffice(first);
            tiles.AddOffice(second);
            stats.SetStaffing(first, 1, 6);
            stats.SetStaffing(second, 2, 6);
            services.RegisterWorldGrid(new FakeWorldGrid(8, 8));
            overlay.Initialize(services);

            InvokeBuildingInfoVisibility(overlay, first, true);

            Assert.IsFalse(IndicatorAt(overlay, first).gameObject.activeSelf);
            Assert.IsTrue(IndicatorAt(overlay, second).gameObject.activeSelf);

            InvokeBuildingInfoVisibility(overlay, first, false);

            Assert.IsTrue(IndicatorAt(overlay, first).gameObject.activeSelf);
            Assert.IsTrue(IndicatorAt(overlay, second).gameObject.activeSelf);
        });
    }

    [Test]
    public void BuildingInfoClose_DoesNotRestoreFullyStaffedIndicator()
    {
        RunWithOverlay((overlay, tiles, stats, services) =>
        {
            var anchor = new Vector2Int(2, 2);
            tiles.AddOffice(anchor);
            stats.SetStaffing(anchor, 1, 6);
            services.RegisterWorldGrid(new FakeWorldGrid(6, 6));
            overlay.Initialize(services);

            InvokeBuildingInfoVisibility(overlay, anchor, true);
            stats.SetStaffing(anchor, 6, 6);
            InvokeBuildingInfoVisibility(overlay, anchor, false);

            Assert.AreEqual(0, LabelCount(overlay));
        });
    }

    [Test]
    public void Update_RemovesLabel_WhenStaffingLookupFails()
    {
        RunWithOverlay((overlay, tiles, stats, services) =>
        {
            var anchor = new Vector2Int(1, 2);
            tiles.AddOffice(anchor);
            stats.SetStaffing(anchor, 1, 2);
            services.RegisterWorldGrid(new FakeWorldGrid(5, 5));
            overlay.Initialize(services);
            Assert.AreEqual(1, LabelCount(overlay));

            stats.Remove(anchor);
            InvokeRefresh(overlay);

            Assert.AreEqual(0, LabelCount(overlay),
                "철거로 staffing 조회가 실패하면 라벨을 제거해야 한다");
            Assert.AreEqual(0, TrackedAnchorCount(overlay),
                "철거로 staffing 조회가 실패하면 앵커 추적도 해제해야 한다");
        });
    }

    [Test]
    public void Update_RecreatesLabel_WhenFullyStaffedCompanyBecomesUnderstaffed()
    {
        RunWithOverlay((overlay, tiles, stats, services) =>
        {
            var anchor = new Vector2Int(1, 2);
            tiles.AddOffice(anchor);
            stats.SetStaffing(anchor, 1, 2);
            services.RegisterWorldGrid(new FakeWorldGrid(5, 5));
            overlay.Initialize(services);
            Assert.AreEqual(1, LabelCount(overlay));

            stats.SetStaffing(anchor, 2, 2);
            InvokeRefresh(overlay);
            Assert.AreEqual(0, LabelCount(overlay),
                "정원이 차면 라벨만 제거하고 회사 앵커는 계속 추적해야 한다");

            stats.SetStaffing(anchor, 1, 2);
            InvokeRefresh(overlay);
            Assert.AreEqual(1, LabelCount(overlay),
                "추적 중인 회사의 인력이 줄면 다음 폴링에서 라벨을 재생성해야 한다");
        });
    }

    static void RunWithOverlay(
        Action<HiringStatusOverlay, FakeTileData, FakeStats, CityFlowServices> test)
    {
        var owner = new GameObject("overlay");
        var templateOwner = new GameObject(
            "template",
            typeof(RectTransform));
        var cameraOwner = new GameObject("camera", typeof(Camera));
        try
        {
            cameraOwner.tag = "MainCamera";
            cameraOwner.transform.position = new Vector3(0f, 0f, -10f);

            HiringStatusIndicatorView template =
                templateOwner.AddComponent<HiringStatusIndicatorView>();
            templateOwner.SetActive(false);

            var overlay = owner.AddComponent<HiringStatusOverlay>();
            SetPrivate(overlay, "indicatorTemplate", template);

            var tiles = new FakeTileData();
            var stats = new FakeStats();
            var services = new CityFlowServices(
                new SimEventHub(), tiles, null, stats: stats);
            test(overlay, tiles, stats, services);
        }
        finally
        {
            HiringStatusIndicatorView[] indicators =
                Object.FindObjectsByType<HiringStatusIndicatorView>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            for (int index = 0; index < indicators.Length; index++)
            {
                if (indicators[index] != null)
                {
                    Object.DestroyImmediate(indicators[index].gameObject);
                }
            }

            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(cameraOwner);
        }
    }

    static RectTransform OverlayCanvasRect(HiringStatusOverlay overlay)
    {
        var field = typeof(HiringStatusOverlay).GetField(
            "_overlayCanvasRect",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        return (RectTransform)field.GetValue(overlay);
    }

    static int LabelCount(HiringStatusOverlay overlay)
    {
        var field = typeof(HiringStatusOverlay).GetField(
            "_indicators",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        var indicators =
            (Dictionary<Vector2Int, HiringStatusIndicatorView>)
            field.GetValue(overlay);
        return indicators.Count;
    }

    static HiringStatusIndicatorView IndicatorAt(
        HiringStatusOverlay overlay,
        Vector2Int anchor)
    {
        var field = typeof(HiringStatusOverlay).GetField(
            "_indicators",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        var indicators =
            (Dictionary<Vector2Int, HiringStatusIndicatorView>)
            field.GetValue(overlay);
        return indicators[anchor];
    }

    static List<Image> IndicatorSlots(
        HiringStatusIndicatorView indicator)
    {
        var field = typeof(HiringStatusIndicatorView).GetField(
            "_segments",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        return (List<Image>)field.GetValue(indicator);
    }

    static int TrackedAnchorCount(HiringStatusOverlay overlay)
    {
        var field = typeof(HiringStatusOverlay).GetField(
            "_trackedAnchors",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        var anchors = (HashSet<Vector2Int>)field.GetValue(overlay);
        return anchors.Count;
    }

    static void InvokeRefresh(HiringStatusOverlay overlay)
    {
        typeof(HiringStatusOverlay).GetMethod(
            "RefreshTrackedCompanies",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)
            .Invoke(overlay, null);
    }

    static void InvokeBuildingInfoVisibility(
        HiringStatusOverlay overlay,
        Vector2Int anchor,
        bool visible)
    {
        typeof(HiringStatusOverlay).GetMethod(
            "OnBuildingInfoVisibilityChanged",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)
            .Invoke(overlay, new object[] { anchor, visible });
    }

    static void InvokeScreenPositionUpdate(
        HiringStatusOverlay overlay,
        Camera cam)
    {
        typeof(HiringStatusOverlay).GetMethod(
            "UpdateIndicatorScreenPositions",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)
            .Invoke(overlay, new object[] { cam });
    }

    static void SetPrivate(object target, string field, object value)
    {
        typeof(HiringStatusOverlay).GetField(
            field,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)
            .SetValue(target, value);
    }

    sealed class FakeStats : IReadOnlyCityStats
    {
        readonly Dictionary<Vector2Int, CompanyStaffing> _staffing = new();

        public int ActiveVehicleCount => 0;
        public int LastDayArrivalCount => 0;

        public bool TryGetCompanyTypeId(Vector2Int tile, out string companyTypeId)
        {
            companyTypeId = null;
            return false;
        }

        public System.Collections.Generic.IReadOnlyList<CityFlow.Contracts.CommuterHomeCount>
            GetCompanyCommuterHomes(Vector2Int tile) =>
            System.Array.Empty<CityFlow.Contracts.CommuterHomeCount>();

        public void SetStaffing(Vector2Int anchor, int filled, int capacity) =>
            _staffing[anchor] = new CompanyStaffing(filled, capacity);

        public void Remove(Vector2Int anchor) => _staffing.Remove(anchor);

        public bool TryGetCompanyStaffing(
            Vector2Int tile,
            out CompanyStaffing staffing) =>
            _staffing.TryGetValue(tile, out staffing);
    }

    sealed class FakeTileData : IReadOnlyTileData
    {
        readonly HashSet<Vector2Int> _anchors = new();

        public void AddOffice(Vector2Int anchor) => _anchors.Add(anchor);

        public CongestionLevel GetCongestion(Vector2Int tile) => CongestionLevel.Free;
        public float GetDensity01(Vector2Int tile) => 0f;
        public int GetQueueCount(Vector2Int tile, Dir entryDir) => 0;

        public TileType GetTileType(Vector2Int tile) =>
            TryGetFootprintAnchor(tile, out _) ? TileType.Office : TileType.Empty;

        public PlacementDirection GetDirection(Vector2Int tile) =>
            PlacementDirection.North;

        public Vector2Int GetFootprintSize(TileType type) =>
            type == TileType.Office ? new Vector2Int(2, 2) : Vector2Int.one;

        public bool TryGetFootprintAnchor(Vector2Int tile, out Vector2Int anchor)
        {
            foreach (Vector2Int office in _anchors)
            {
                if (tile.x >= office.x && tile.x <= office.x + 1 &&
                    tile.y >= office.y && tile.y <= office.y + 1)
                {
                    anchor = office;
                    return true;
                }
            }

            anchor = default;
            return false;
        }

        public bool IsFootprintAnchor(Vector2Int tile) => _anchors.Contains(tile);

        public bool TryGetConstructionProgress01(Vector2Int tile, out float progress01)
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

    sealed class FakeWorldGrid : IWorldGridService
    {
        public FakeWorldGrid(int width, int height)
        {
            WorldWidth = width;
            WorldHeight = height;
        }

        public int WorldWidth { get; }
        public int WorldHeight { get; }
        public int ChunkSize => 8;
        public int ChunkColumns => 1;
        public int ChunkRows => 1;
        public Vector2Int InitialPlayableOrigin => Vector2Int.zero;
        public Vector2Int InitialPlayableSize => new(WorldWidth, WorldHeight);

        public bool IsInsideWorld(Vector2Int tile) =>
            tile.x >= 0 && tile.y >= 0 &&
            tile.x < WorldWidth && tile.y < WorldHeight;

        public bool IsTileUnlocked(Vector2Int tile) => IsInsideWorld(tile);
        public bool IsChunkUnlocked(GridChunkId chunk) => true;
        public bool IsAreaUnlocked(Vector2Int origin, Vector2Int size) => true;
        public bool TryUnlockChunk(GridChunkId chunk) => false;

        public bool TryGetChunkId(Vector2Int tile, out GridChunkId chunk)
        {
            chunk = default;
            return IsInsideWorld(tile);
        }

        public event Action<GridChunkId> ChunkUnlocked;
        public event Action AccessRestored;
    }
}
