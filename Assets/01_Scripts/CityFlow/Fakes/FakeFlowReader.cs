using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Fakes
{
    public sealed class FakeFlowReader : IReadOnlyTileData
    {
        private readonly int width;
        private readonly int height;
        private float lastBurstTime;
        private float lastStabilityTime;

        public float Stability01 { get; private set; } = 0.75f;

        public FakeFlowReader(int width, int height)
        {
            this.width = Mathf.Max(1, width);
            this.height = Mathf.Max(1, height);
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

        public int GetQueueCount(Vector2Int tile, Dir entryDir)
        {
            return Mathf.RoundToInt(GetDensity01(tile) * 10f);
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
