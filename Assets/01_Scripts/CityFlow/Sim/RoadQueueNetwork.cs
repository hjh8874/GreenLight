using System;
using UnityEngine;

namespace CityFlow.Sim
{
    internal interface ICarRouteProvider
    {
        bool TryGetNextTile(
            int carId,
            Vector2Int current,
            out Vector2Int next,
            out Dir entryDirAtNext);

        bool IsDestination(int carId, Vector2Int tile);
    }

    internal interface ISignalGate
    {
        bool IsServiceOpen(Vector2Int tile, Dir entryDir, int tick);
    }

    public struct StepResult
    {
        public int Arrivals;
        public int ValveActivations;
    }

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
        private const int NoNode = -1;

        private readonly int _width;
        private readonly int _height;
        private readonly int _capacity;
        private readonly int _servicePerTick;
        private readonly int _gridlockValveTicks;

        // 모든 차 토큰 노드를 생성자에서 한 번만 할당한다. 큐는 노드 인덱스로 FIFO를
        // 연결하므로 탈출 밸브가 만석 큐로 강제 이동해도 새 메모리나 차 복제가 필요 없다.
        private readonly int[] _cars;
        private readonly int[] _nextNodes;
        private readonly bool[] _movedThisTick;
        private readonly int[] _blockedTicks;

        private readonly int[] _heads;
        private readonly int[] _tails;
        private readonly int[] _counts;
        private readonly bool[] _intersections;
        private int _freeHead;

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
            _servicePerTick = Math.Max(1, cfg.QueueServicePerTick);
            _gridlockValveTicks = Math.Max(1, cfg.GridlockValveTicks);

            int queueCount = checked(width * height * DirectionCount);
            int maxCars = checked(queueCount * _capacity);
            _cars = new int[maxCars];
            _nextNodes = new int[maxCars];
            _movedThisTick = new bool[maxCars];
            _blockedTicks = new int[maxCars];
            _heads = new int[queueCount];
            _tails = new int[queueCount];
            _counts = new int[queueCount];
            _intersections = new bool[width * height];

            for (int queue = 0; queue < queueCount; queue++)
            {
                _heads[queue] = NoNode;
                _tails[queue] = NoNode;
            }

            for (int node = 0; node < maxCars; node++)
            {
                _cars[node] = NoNode;
                _nextNodes[node] = node + 1 < maxCars ? node + 1 : NoNode;
            }

            _freeHead = maxCars > 0 ? 0 : NoNode;

            // ponytail: 전역 고정 노드 풀은 큐별 고정 링버퍼와 같은 총 메모리를 쓰되,
            // 밸브 순간에 큐별 용량 분포만 바꿀 수 있어 GC 0과 차 수 보존을 함께 지킨다.
        }

        public void RebuildTopology(CityGrid grid)
        {
            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            if (grid.Width != _width || grid.Height != _height)
            {
                throw new ArgumentException(
                    "RoadQueueNetwork와 CityGrid 크기가 일치해야 합니다.",
                    nameof(grid));
            }

            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    var tile = new Vector2Int(x, y);
                    _intersections[TileIndex(tile)] = grid.IsIntersection(tile);
                }
            }

            // Task 4~5에서 도로 방향과 장치 상태를 추가 주입한다.
        }

        public bool TryEnqueue(Vector2Int tile, Dir entryDir, int carId)
        {
            if (carId < 0
                || !TryQueueIndex(tile, entryDir, out int queueIndex)
                || !CanAcceptNormally(queueIndex)
                || !TryAllocateNode(out int node))
            {
                return false;
            }

            _cars[node] = carId;
            _movedThisTick[node] = false;
            _blockedTicks[node] = 0;
            AppendNode(queueIndex, node);
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

            return Mathf.Clamp01((float)maxCount / _capacity);
        }

        public StepResult Step(ICarRouteProvider routes)
        {
            return Step(routes, signalGate: null, tick: 0);
        }

        public StepResult Step(
            ICarRouteProvider routes,
            ISignalGate signalGate,
            int tick)
        {
            if (routes == null)
            {
                throw new ArgumentNullException(nameof(routes));
            }

            Array.Clear(_movedThisTick, 0, _movedThisTick.Length);
            StepResult result = default;
            int tileCount = _width * _height;

            for (int tileIndex = 0; tileIndex < tileCount; tileIndex++)
            {
                Vector2Int tile = new Vector2Int(
                    tileIndex % _width,
                    tileIndex / _width);
                int firstQueue = tileIndex * DirectionCount;

                for (int direction = 0; direction < DirectionCount; direction++)
                {
                    int queueIndex = firstQueue + direction;
                    int serviced = 0;

                    while (serviced < _servicePerTick
                        && _heads[queueIndex] != NoNode)
                    {
                        int node = _heads[queueIndex];
                        if (_movedThisTick[node])
                        {
                            break;
                        }

                        if (signalGate != null
                            && !signalGate.IsServiceOpen(
                                tile,
                                (Dir)direction,
                                tick))
                        {
                            // 빨강은 정상 제어 대기다. Gridlock 밸브로 신호를 우회하지 않는다.
                            break;
                        }

                        int carId = _cars[node];
                        if (routes.IsDestination(carId, tile))
                        {
                            int arrivedNode = DetachHead(queueIndex);
                            ReleaseNode(arrivedNode);
                            result.Arrivals++;
                            serviced++;
                            continue;
                        }

                        if (!routes.TryGetNextTile(
                                carId,
                                tile,
                                out Vector2Int next,
                                out Dir entryDirAtNext)
                            || !TryQueueIndex(
                                next,
                                entryDirAtNext,
                                out int nextQueueIndex))
                        {
                            // 경로 부재는 교통 데드락이 아니므로 밸브 카운터를 올리지 않는다.
                            break;
                        }

                        bool blocked = !CanAcceptNormally(nextQueueIndex)
                            || IsIntersectionExitBlocked(
                                routes,
                                carId,
                                next,
                                tileIndex: TileIndex(next));

                        if (blocked)
                        {
                            _blockedTicks[node]++;
                            if (_blockedTicks[node] < _gridlockValveTicks)
                            {
                                // FIFO: 머리가 막히면 같은 큐의 뒤차도 이번 틱 대기한다.
                                break;
                            }

                            MoveHead(queueIndex, nextQueueIndex);
                            result.ValveActivations++;
                            serviced++;
                            continue;
                        }

                        MoveHead(queueIndex, nextQueueIndex);
                        serviced++;
                    }
                }
            }

            return result;
        }

        public int CarAtHead(Vector2Int tile, Dir entryDir)
        {
            if (!TryQueueIndex(tile, entryDir, out int queueIndex))
            {
                return NoNode;
            }

            int node = _heads[queueIndex];
            return node == NoNode ? NoNode : _cars[node];
        }

        private bool IsIntersectionExitBlocked(
            ICarRouteProvider routes,
            int carId,
            Vector2Int intersection,
            int tileIndex)
        {
            if (!_intersections[tileIndex]
                || routes.IsDestination(carId, intersection))
            {
                return false;
            }

            return !routes.TryGetNextTile(
                    carId,
                    intersection,
                    out Vector2Int exit,
                    out Dir entryDirAtExit)
                || !TryQueueIndex(exit, entryDirAtExit, out int exitQueueIndex)
                || !CanAcceptNormally(exitQueueIndex);
        }

        private bool CanAcceptNormally(int queueIndex) =>
            _counts[queueIndex] < _capacity;

        private bool TryAllocateNode(out int node)
        {
            node = _freeHead;
            if (node == NoNode)
            {
                return false;
            }

            _freeHead = _nextNodes[node];
            _nextNodes[node] = NoNode;
            return true;
        }

        private void ReleaseNode(int node)
        {
            _cars[node] = NoNode;
            _movedThisTick[node] = false;
            _blockedTicks[node] = 0;
            _nextNodes[node] = _freeHead;
            _freeHead = node;
        }

        private void AppendNode(int queueIndex, int node)
        {
            _nextNodes[node] = NoNode;
            if (_tails[queueIndex] == NoNode)
            {
                _heads[queueIndex] = node;
                _tails[queueIndex] = node;
            }
            else
            {
                _nextNodes[_tails[queueIndex]] = node;
                _tails[queueIndex] = node;
            }

            _counts[queueIndex]++;
        }

        private int DetachHead(int queueIndex)
        {
            int node = _heads[queueIndex];
            int next = _nextNodes[node];
            _heads[queueIndex] = next;
            _counts[queueIndex]--;

            if (next == NoNode)
            {
                _tails[queueIndex] = NoNode;
            }

            _nextNodes[node] = NoNode;
            return node;
        }

        private void MoveHead(int fromQueueIndex, int toQueueIndex)
        {
            int node = DetachHead(fromQueueIndex);
            _movedThisTick[node] = true;
            _blockedTicks[node] = 0;
            AppendNode(toQueueIndex, node);
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
                queueIndex = NoNode;
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
