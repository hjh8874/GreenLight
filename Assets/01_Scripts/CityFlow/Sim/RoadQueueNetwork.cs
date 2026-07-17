using System;
using UnityEngine;

namespace CityFlow.Sim
{
    public enum Dir
    {
        N = 0,
        E = 1,
        S = 2,
        W = 3
    }

    internal sealed class RoadQueueNetwork
    {
        private const int DirectionCount = 4;

        private readonly int _width;
        private readonly int _height;
        private readonly int _capacity;
        private readonly int[] _cars;
        private readonly int[] _heads;
        private readonly int[] _counts;

        public RoadQueueNetwork(int width, int height, in SimConfig cfg)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            _width = width;
            _height = height;
            _capacity = Math.Max(1, cfg.QueueCapacityPerTile);

            int queueCount = checked(width * height * DirectionCount);
            _cars = new int[checked(queueCount * _capacity)];
            _heads = new int[queueCount];
            _counts = new int[queueCount];

            // ponytail: 큐마다 고정 길이 링버퍼를 한 flat 배열에 배치해 틱 중 할당과
            // 컬렉션 순회 변동을 없앤다. 방향 순서는 enum N,E,S,W의 정수값으로 고정된다.
        }

        public void RebuildTopology(CityGrid grid)
        {
            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            // Task 4~5에서 도로 방향과 장치 상태를 주입해 활성 큐를 재구성한다.
        }

        public bool TryEnqueue(Vector2Int tile, Dir entryDir, int carId)
        {
            if (carId < 0 || !TryQueueIndex(tile, entryDir, out int queueIndex))
            {
                return false;
            }

            int count = _counts[queueIndex];
            if (count >= _capacity)
            {
                return false;
            }

            int tail = (_heads[queueIndex] + count) % _capacity;
            _cars[(queueIndex * _capacity) + tail] = carId;
            _counts[queueIndex] = count + 1;
            return true;
        }

        public int QueueCount(Vector2Int tile, Dir entryDir)
        {
            return TryQueueIndex(tile, entryDir, out int queueIndex)
                ? _counts[queueIndex]
                : 0;
        }

        public float MaxOccupancy01(Vector2Int tile)
        {
            if (!InBounds(tile))
            {
                return 0f;
            }

            int firstQueue = TileIndex(tile) * DirectionCount;
            int maxCount = 0;
            for (int direction = 0; direction < DirectionCount; direction++)
            {
                maxCount = Math.Max(maxCount, _counts[firstQueue + direction]);
            }

            return (float)maxCount / _capacity;
        }

        public void Step()
        {
            // Task 2에서 ICarRouteProvider를 받아 FIFO 전이를 수행한다.
        }

        public int CarAtHead(Vector2Int tile, Dir entryDir)
        {
            if (!TryQueueIndex(tile, entryDir, out int queueIndex)
                || _counts[queueIndex] == 0)
            {
                return -1;
            }

            return _cars[(queueIndex * _capacity) + _heads[queueIndex]];
        }

        private bool TryQueueIndex(
            Vector2Int tile,
            Dir entryDir,
            out int queueIndex)
        {
            int direction = (int)entryDir;
            if (!InBounds(tile)
                || direction < 0
                || direction >= DirectionCount)
            {
                queueIndex = -1;
                return false;
            }

            queueIndex = (TileIndex(tile) * DirectionCount) + direction;
            return true;
        }

        private int TileIndex(Vector2Int tile) =>
            (tile.y * _width) + tile.x;

        private bool InBounds(Vector2Int tile) =>
            tile.x >= 0
            && tile.x < _width
            && tile.y >= 0
            && tile.y < _height;
    }
}
