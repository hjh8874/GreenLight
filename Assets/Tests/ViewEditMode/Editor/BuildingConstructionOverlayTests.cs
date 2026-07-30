using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

// 세이브에서 복원된 공사장에도 라벨이 붙는지 고정한다.
// SimEngine.RestoreSnapshot 은 "복원은 '건설'이 아니다"라며 PlacedEvent 를 쏘지 않으므로,
// Placed 이벤트만 구독하면 공사 중 저장 → 로드 시 진행도는 도는데 표시가 영구히 없다
// (리뷰 지적 2026-07-30). 이 테스트는 이벤트를 한 번도 쏘지 않고 라벨을 요구한다.
//
// 이름 필터로 돌린다: run_tests(group_names=[".*BuildingConstructionOverlayTests.*"])
public class BuildingConstructionOverlayTests
{
    [Test]
    public void Initialize_RegistersExistingConstructionSites_WithoutPlacedEvent()
    {
        var owner = new GameObject("overlay");
        var templateOwner = new GameObject("template");
        try
        {
            TextMeshPro template = templateOwner.AddComponent<TextMeshPro>();
            template.gameObject.SetActive(false);

            var overlay = owner.AddComponent<BuildingConstructionOverlay>();
            SetPrivate(overlay, "labelTemplate", template);

            var tiles = new FakeTileData();
            tiles.AddConstruction(new Vector2Int(2, 3), 0.5f);   // 2x2 앵커 하나
            var services = new CityFlowServices(new SimEventHub(), tiles, null);
            Assert.IsTrue(services.RegisterWorldGrid(new FakeWorldGrid(8, 8)));

            overlay.Initialize(services);   // Placed 이벤트는 쏘지 않는다

            Assert.AreEqual(1, LabelCount(overlay),
                "복원된 공사장은 이벤트 없이도 라벨을 받아야 한다");
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(templateOwner);
        }
    }

    [Test]
    public void Initialize_CreatesOneLabelPerFootprint_NotPerTile()
    {
        var owner = new GameObject("overlay");
        var templateOwner = new GameObject("template");
        try
        {
            TextMeshPro template = templateOwner.AddComponent<TextMeshPro>();
            template.gameObject.SetActive(false);

            var overlay = owner.AddComponent<BuildingConstructionOverlay>();
            SetPrivate(overlay, "labelTemplate", template);

            // 2x2 공사장: 진행도 조회는 네 타일 모두에 답한다(앵커로 환산).
            var tiles = new FakeTileData();
            tiles.AddConstruction(new Vector2Int(0, 0), 0.25f);
            var services = new CityFlowServices(new SimEventHub(), tiles, null);
            services.RegisterWorldGrid(new FakeWorldGrid(4, 4));

            overlay.Initialize(services);

            Assert.AreEqual(1, LabelCount(overlay),
                "앵커에만 하나 — 앵커 필터가 없으면 2x2 에 라벨이 4개 생긴다");
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(templateOwner);
        }
    }

    static int LabelCount(BuildingConstructionOverlay overlay)
    {
        var field = typeof(BuildingConstructionOverlay).GetField(
            "_labels",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        var labels = (Dictionary<Vector2Int, TextMeshPro>)field.GetValue(overlay);
        return labels.Count;
    }

    static void SetPrivate(object target, string field, object value)
    {
        typeof(BuildingConstructionOverlay).GetField(
            field,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)
            .SetValue(target, value);
    }

    // 공사장 2x2 하나만 아는 최소 타일 데이터.
    sealed class FakeTileData : IReadOnlyTileData
    {
        readonly Dictionary<Vector2Int, float> _sites = new();

        public void AddConstruction(Vector2Int anchor, float progress01) =>
            _sites[anchor] = progress01;

        public CongestionLevel GetCongestion(Vector2Int tile) => CongestionLevel.Free;
        public float GetDensity01(Vector2Int tile) => 0f;
        public int GetQueueCount(Vector2Int tile, Dir entryDir) => 0;

        public TileType GetTileType(Vector2Int tile) =>
            TryGetFootprintAnchor(tile, out _)
                ? TileType.UnderConstruction
                : TileType.Empty;

        public PlacementDirection GetDirection(Vector2Int tile) =>
            PlacementDirection.North;

        public Vector2Int GetFootprintSize(TileType type) =>
            type == TileType.UnderConstruction ? new Vector2Int(2, 2) : Vector2Int.one;

        public bool TryGetFootprintAnchor(Vector2Int tile, out Vector2Int anchor)
        {
            foreach (Vector2Int site in _sites.Keys)
            {
                if (tile.x >= site.x && tile.x <= site.x + 1 &&
                    tile.y >= site.y && tile.y <= site.y + 1)
                {
                    anchor = site;
                    return true;
                }
            }

            anchor = default;
            return false;
        }

        public bool IsFootprintAnchor(Vector2Int tile) => _sites.ContainsKey(tile);

        public bool TryGetConstructionProgress01(Vector2Int tile, out float progress01)
        {
            progress01 = 0f;
            if (!TryGetFootprintAnchor(tile, out Vector2Int anchor)) return false;
            progress01 = _sites[anchor];
            return true;
        }

        public bool TryGetConstructionTargetType(Vector2Int tile, out TileType targetType)
        {
            targetType = TileType.House;
            return TryGetFootprintAnchor(tile, out _);
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
        public Vector2Int InitialPlayableSize => new Vector2Int(WorldWidth, WorldHeight);

        public bool IsInsideWorld(Vector2Int tile) =>
            tile.x >= 0 && tile.y >= 0 && tile.x < WorldWidth && tile.y < WorldHeight;

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
