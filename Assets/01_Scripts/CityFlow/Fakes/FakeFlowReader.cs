using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Fakes
{
    public sealed class FakeFlowReader : IReadOnlyTileData
    {
        // 실 게임의 타일당 큐 상한과 같은 값을 받는다. 하드코딩하면 페이크 UI의 눈금이
        // 실제 범위와 어긋나 게이지 최댓값을 잘못 맞추게 된다(리뷰 지적 2026-07-22).
        private const int DefaultQueueCapacityPerTile = 4;   // SimConfig.Default()와 동일

        private readonly int width;
        private readonly int height;
        private readonly int queueCapacityPerTile;
        private float lastBurstTime;
        private float lastStabilityTime;

        public float Stability01 { get; private set; } = 0.75f;

        public FakeFlowReader(
            int width,
            int height,
            int queueCapacityPerTile = DefaultQueueCapacityPerTile)
        {
            this.width = Mathf.Max(1, width);
            this.height = Mathf.Max(1, height);
            this.queueCapacityPerTile = Mathf.Max(1, queueCapacityPerTile);
        }

        public CongestionLevel GetCongestion(Vector2Int tile)
        {
            float density = GetDensity01(tile);

            if (density >= 0.66f)
            {
                return CongestionLevel.Jam;
            }

            if (density >= 0.33f)
            {
                return CongestionLevel.Slow;
            }

            return CongestionLevel.Free;
        }

        public float GetDensity01(Vector2Int tile)
        {
            if (!GridUtil.IsInside(tile, width, height))
            {
                return 0f;
            }

            float wave = Mathf.Sin((tile.x * 0.73f) + (tile.y * 1.17f) + Time.time);
            return Mathf.Clamp01((wave + 1f) * 0.5f);
        }

        // 방향마다 위상을 다르게 준다. 방향별 API의 페이크가 방향을 구분하지 못하면
        // UI가 가로/세로를 바꿔 연결해도 화면상 정상으로 보여 오배선을 못 잡는다.
        public int GetQueueCount(Vector2Int tile, Dir entryDir)
        {
            if (!GridUtil.IsInside(tile, width, height))
            {
                return 0;
            }

            float wave = Mathf.Sin(
                (tile.x * 0.73f)
                + (tile.y * 1.17f)
                + ((int)entryDir * Mathf.PI * 0.5f)
                + Time.time);

            return Mathf.RoundToInt(
                Mathf.Clamp01((wave + 1f) * 0.5f) * queueCapacityPerTile);
        }

        public TileType GetTileType(Vector2Int tile)
        {
            if (!GridUtil.IsInside(tile, width, height))
            {
                return TileType.Empty;
            }

            if ((tile.x + tile.y) % 7 == 0)
            {
                return TileType.Office;
            }

            if ((tile.x + tile.y) % 5 == 0)
            {
                return TileType.House;
            }

            if (tile.x == width / 2 || tile.y == height / 2)
            {
                return TileType.Road;
            }

            return TileType.Empty;
        }

        public Vector2Int GetFootprintSize(TileType type) => TileFootprint.GetSize(type);

        public bool TryGetFootprintAnchor(Vector2Int tile, out Vector2Int anchor)
        {
            anchor = tile;
            return GetTileType(tile) != TileType.Empty;
        }

        public bool IsFootprintAnchor(Vector2Int tile) => GetTileType(tile) != TileType.Empty;

        public void Tick(float time, SimEventHub events)
        {
            if (events == null)
            {
                return;
            }

            if (time - lastStabilityTime >= 1f)
            {
                lastStabilityTime = time;
                Stability01 = Mathf.Clamp01(0.65f + Mathf.Sin(time * 0.4f) * 0.2f);
                events.Publish(new StabilityEvent(Stability01));
            }

            if (time - lastBurstTime >= 3f)
            {
                lastBurstTime = time;
                Vector2Int tile = new Vector2Int(
                    Mathf.Abs(Mathf.RoundToInt(Mathf.Sin(time) * 100f)) % width,
                    Mathf.Abs(Mathf.RoundToInt(Mathf.Cos(time) * 100f)) % height);

                events.Publish(new FlowBurstEvent(tile, 10));
            }
        }
    }
}
