using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
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
            LogAssert.Expect(
                LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    "HiringStatus_1_2: Destroy may not be called from edit mode!"));
            InvokeUpdate(overlay);

            Assert.AreEqual(0, LabelCount(overlay),
                "철거로 staffing 조회가 실패하면 라벨을 제거해야 한다");
        });
    }

    static void RunWithOverlay(
        Action<HiringStatusOverlay, FakeTileData, FakeStats, CityFlowServices> test)
    {
        var owner = new GameObject("overlay");
        var templateOwner = new GameObject("template");
        try
        {
            TextMeshPro template = templateOwner.AddComponent<TextMeshPro>();
            template.gameObject.SetActive(false);

            var overlay = owner.AddComponent<HiringStatusOverlay>();
            SetPrivate(overlay, "labelTemplate", template);

            var tiles = new FakeTileData();
            var stats = new FakeStats();
            var services = new CityFlowServices(
                new SimEventHub(), tiles, null, stats: stats);
            test(overlay, tiles, stats, services);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(templateOwner);
        }
    }

    static int LabelCount(HiringStatusOverlay overlay)
    {
        var field = typeof(HiringStatusOverlay).GetField(
            "_labels",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        var labels = (Dictionary<Vector2Int, TextMeshPro>)field.GetValue(overlay);
        return labels.Count;
    }

    static void InvokeUpdate(HiringStatusOverlay overlay)
    {
        typeof(HiringStatusOverlay).GetMethod(
            "Update",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)
            .Invoke(overlay, null);
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
