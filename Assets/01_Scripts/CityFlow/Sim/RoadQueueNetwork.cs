using System;
using UnityEngine;

namespace CityFlow.Sim
{
    internal interface ICarRouteProvider
    {
        bool TryGetNextTile(int carId, Vector2Int current, out Vector2Int next, out Dir entryDirAtNext);
        bool IsDestination(int carId, Vector2Int tile);
    }

    internal interface ISignalGate
    {
        bool IsServiceOpen(Vector2Int tile, Dir entryDir, int tick);
    }

    internal interface IDeviceState
    {
        bool IsRoundabout(Vector2Int tile);
        bool IsOverpass(Vector2Int tile);
        RoadAxis PriorityAxis(Vector2Int tile);
        Vector2Int OnewayDir(Vector2Int tile);
        bool IsTurnAllowed(Vector2Int tile, Dir entry, Dir exit);
    }

    public struct StepResult
    {
        public int Arrivals;
        public int ValveActivations;
    }

    public struct ArrivalRecord
    {
        public int CarId;
        public Vector2Int Tile;
    }

    public enum Dir { N = 0, E = 1, S = 2, W = 3 }
    public enum RoadAxis { None = 0, Horizontal = 1, Vertical = 2 }

    internal sealed class RoadQueueNetwork
    {
        private const int DirectionCount = 4;
        private const int NoNode = -1;

        private enum IntentKind { Arrival, Move, RingEntry }

        private struct Intent
        {
            public IntentKind Kind;
            public int FromQueue;
            public int ToQueue;
            public int Node;
            public int TileIndex;
            public Dir Entry;
            public Dir Exit;
            public int RingIndex;
            public bool Force;
        }

        private readonly int _width;
        private readonly int _height;
        private readonly int _capacity;
        private readonly int _servicePerTick;
        private readonly int _gridlockValveTicks;
        private readonly int[] _cars;
        private readonly int[] _nextNodes;
        private readonly bool[] _movedThisTick;
        private readonly int[] _blockedTicks;
        private readonly int[] _heads;
        private readonly int[] _tails;
        private readonly int[] _counts;
        private readonly bool[] _intersections;
        private readonly bool[] _roundabouts;
        private readonly bool[] _overpasses;
        private readonly RoadAxis[] _priorityAxes;
        private readonly bool[] _queueActive;
        private readonly bool[] _turnAllowed;
        private readonly int[] _ringNodes;
        private readonly Intent[] _intents;
        private readonly ArrivalRecord[] _arrivals;
        private int _freeHead;

        public int TurnRestrictionBlockCount { get; private set; }
        public int ArrivalCount { get; private set; }

        public RoadQueueNetwork(int width, int height, in SimConfig cfg)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            _width = width;
            _height = height;
            _capacity = Math.Max(1, cfg.QueueCapacityPerTile);
            _servicePerTick = Math.Max(1, cfg.QueueServicePerTick);
            _gridlockValveTicks = Math.Max(1, cfg.GridlockValveTicks);

            int tileCount = checked(width * height);
            int queueCount = checked(tileCount * DirectionCount);
            int maxCars = checked(queueCount * _capacity);
            _cars = new int[maxCars];
            _nextNodes = new int[maxCars];
            _movedThisTick = new bool[maxCars];
            _blockedTicks = new int[maxCars];
            _heads = new int[queueCount];
            _tails = new int[queueCount];
            _counts = new int[queueCount];
            _intersections = new bool[tileCount];
            _roundabouts = new bool[tileCount];
            _overpasses = new bool[tileCount];
            _priorityAxes = new RoadAxis[tileCount];
            _queueActive = new bool[queueCount];
            _turnAllowed = new bool[tileCount * DirectionCount * DirectionCount];
            _ringNodes = new int[tileCount * DirectionCount];
            _intents = new Intent[queueCount];
            _arrivals = new ArrivalRecord[maxCars];

            Array.Fill(_heads, NoNode);
            Array.Fill(_tails, NoNode);
            Array.Fill(_ringNodes, NoNode);
            Array.Fill(_queueActive, true);
            Array.Fill(_turnAllowed, true);
            for (int node = 0; node < maxCars; node++)
            {
                _cars[node] = NoNode;
                _nextNodes[node] = node + 1 < maxCars ? node + 1 : NoNode;
            }
            _freeHead = maxCars > 0 ? 0 : NoNode;
        }

        public void RebuildTopology(CityGrid grid) => RebuildTopology(grid, null);

        public void RebuildTopology(CityGrid grid, IDeviceState devices)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (grid.Width != _width || grid.Height != _height)
                throw new ArgumentException("RoadQueueNetwork와 CityGrid 크기가 일치해야 합니다.", nameof(grid));

            TurnRestrictionBlockCount = 0;
            for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++)
            {
                var tile = new Vector2Int(x, y);
                int tileIndex = TileIndex(tile);
                _intersections[tileIndex] = grid.IsIntersection(tile);
                _roundabouts[tileIndex] = devices?.IsRoundabout(tile) ?? false;
                _overpasses[tileIndex] = devices?.IsOverpass(tile) ?? false;
                _priorityAxes[tileIndex] = devices?.PriorityAxis(tile) ?? RoadAxis.None;

                Vector2Int oneway = devices?.OnewayDir(tile) ?? Vector2Int.zero;
                bool hasOneway = TryDir(oneway, out Dir allowedDir);
                for (int entry = 0; entry < DirectionCount; entry++)
                {
                    int queue = tileIndex * DirectionCount + entry;
                    _queueActive[queue] = !hasOneway || entry == (int)allowedDir;
                    for (int exit = 0; exit < DirectionCount; exit++)
                    {
                        _turnAllowed[TurnIndex(tileIndex, (Dir)entry, (Dir)exit)] =
                            devices?.IsTurnAllowed(tile, (Dir)entry, (Dir)exit) ?? true;
                    }
                }
            }
        }

        public bool TryEnqueue(Vector2Int tile, Dir entryDir, int carId)
        {
            if (carId < 0 || !TryQueueIndex(tile, entryDir, out int queue)
                || !CanAcceptNormally(queue) || !TryAllocateNode(out int node)) return false;
            _cars[node] = carId;
            _movedThisTick[node] = false;
            _blockedTicks[node] = 0;
            AppendNode(queue, node);
            return true;
        }

        public int QueueCount(Vector2Int tile, Dir entryDir) =>
            TryQueueIndex(tile, entryDir, out int queue) ? _counts[queue] : 0;

        public int CarAtHead(Vector2Int tile, Dir entryDir)
        {
            if (!TryQueueIndex(tile, entryDir, out int queue)) return NoNode;
            int node = _heads[queue];
            return node == NoNode ? NoNode : _cars[node];
        }

        public int RingCellCar(Vector2Int tile, Dir cell)
        {
            if (!InBounds(tile) || (int)cell < 0 || (int)cell >= DirectionCount) return NoNode;
            int node = _ringNodes[TileIndex(tile) * DirectionCount + (int)cell];
            return node == NoNode ? NoNode : _cars[node];
        }

        internal bool TryLocateCar(int carId, out Vector2Int tile, out Dir direction, out int slot)
        {
            for (int queue = 0; queue < _heads.Length; queue++)
            {
                int node = _heads[queue];
                int queueSlot = 0;
                while (node != NoNode)
                {
                    if (_cars[node] == carId)
                    {
                        tile = TileAt(queue / DirectionCount);
                        direction = (Dir)(queue % DirectionCount);
                        slot = queueSlot;
                        return true;
                    }
                    node = _nextNodes[node];
                    queueSlot++;
                }
            }
            for (int ring = 0; ring < _ringNodes.Length; ring++)
            {
                int node = _ringNodes[ring];
                if (node == NoNode || _cars[node] != carId) continue;
                tile = TileAt(ring / DirectionCount);
                direction = (Dir)(ring % DirectionCount);
                slot = 0;
                return true;
            }
            tile = default;
            direction = default;
            slot = -1;
            return false;
        }

        public ArrivalRecord GetArrival(int index)
        {
            if (index < 0 || index >= ArrivalCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _arrivals[index];
        }

        public void RemoveAllCars()
        {
            Array.Fill(_heads, NoNode);
            Array.Fill(_tails, NoNode);
            Array.Clear(_counts, 0, _counts.Length);
            Array.Fill(_ringNodes, NoNode);
            for (int node = 0; node < _cars.Length; node++)
            {
                _cars[node] = NoNode;
                _movedThisTick[node] = false;
                _blockedTicks[node] = 0;
                _nextNodes[node] = node + 1 < _cars.Length ? node + 1 : NoNode;
            }
            _freeHead = _cars.Length > 0 ? 0 : NoNode;
            ArrivalCount = 0;
        }

        public float MaxOccupancy01(Vector2Int tile)
        {
            if (!InBounds(tile)) return 0f;
            int tileIndex = TileIndex(tile);
            int maxCount = 0;
            for (int d = 0; d < DirectionCount; d++)
                maxCount = Math.Max(maxCount, _counts[tileIndex * DirectionCount + d]);
            float approach = (float)maxCount / _capacity;
            if (!_roundabouts[tileIndex]) return Mathf.Clamp01(approach);
            int ringCount = 0;
            for (int d = 0; d < DirectionCount; d++)
                if (_ringNodes[tileIndex * DirectionCount + d] != NoNode) ringCount++;
            return Mathf.Clamp01(Math.Max(approach, ringCount / 4f));
        }

        public StepResult Step(ICarRouteProvider routes) => Step(routes, null, 0);

        public StepResult Step(ICarRouteProvider routes, ISignalGate signalGate, int tick)
        {
            if (routes == null) throw new ArgumentNullException(nameof(routes));
            ArrivalCount = 0;
            Array.Clear(_movedThisTick, 0, _movedThisTick.Length);
            StepResult result = default;

            for (int serviceRound = 0; serviceRound < _servicePerTick; serviceRound++)
            {
                ServiceRoundaboutRings(routes, ref result);
                int intentCount = CollectIntents(routes, signalGate, tick);
                ResolveIntents(intentCount, ref result);
            }
            return result;
        }

        private int CollectIntents(ICarRouteProvider routes, ISignalGate signalGate, int tick)
        {
            int count = 0;
            for (int queue = 0; queue < _heads.Length; queue++)
            {
                int node = _heads[queue];
                if (node == NoNode || _movedThisTick[node]) continue;
                int tileIndex = queue / DirectionCount;
                Dir entry = (Dir)(queue % DirectionCount);
                Vector2Int tile = TileAt(tileIndex);
                if (signalGate != null && !signalGate.IsServiceOpen(tile, entry, tick)) continue;

                int carId = _cars[node];
                if (routes.IsDestination(carId, tile))
                {
                    _intents[count++] = NewIntent(IntentKind.Arrival, queue, node, tileIndex, entry, entry);
                    continue;
                }
                if (!routes.TryGetNextTile(carId, tile, out Vector2Int next, out Dir exit)
                    || !TryQueueIndex(next, exit, out int nextQueue)) continue;

                if (!_turnAllowed[TurnIndex(tileIndex, entry, exit)])
                {
                    TurnRestrictionBlockCount++;
                    continue;
                }

                if (_roundabouts[tileIndex])
                {
                    int ringIndex = tileIndex * DirectionCount + (int)Opposite(entry);
                    if (_ringNodes[ringIndex] == NoNode)
                    {
                        Intent intent = NewIntent(IntentKind.RingEntry, queue, node, tileIndex, entry, exit);
                        intent.RingIndex = ringIndex;
                        _intents[count++] = intent;
                    }
                    else _blockedTicks[node]++;
                    continue;
                }

                bool blocked = !CanAcceptNormally(nextQueue)
                    || IsIntersectionExitBlocked(routes, carId, next, TileIndex(next));
                Intent move = NewIntent(IntentKind.Move, queue, node, tileIndex, entry, exit);
                move.ToQueue = nextQueue;
                if (blocked)
                {
                    _blockedTicks[node]++;
                    if (_blockedTicks[node] < _gridlockValveTicks) continue;
                    move.Force = true;
                }
                _intents[count++] = move;
            }
            return count;
        }

        private void ResolveIntents(int intentCount, ref StepResult result)
        {
            for (int tile = 0; tile < _intersections.Length; tile++)
            {
                if (!UsesSharedBudget(tile)) continue;
                int winner = NoNode;
                for (int i = 0; i < intentCount; i++)
                {
                    if (_intents[i].TileIndex != tile) continue;
                    if (winner == NoNode || IsBetter(_intents[i], _intents[winner])) winner = i;
                }
                if (winner == NoNode) continue;
                for (int i = 0; i < intentCount; i++)
                {
                    if (i == winner || _intents[i].TileIndex != tile) continue;
                    int node = _intents[i].Node;
                    if (node != NoNode) _blockedTicks[node]++;
                }
                ExecuteIntent(_intents[winner], ref result);
            }

            for (int i = 0; i < intentCount; i++)
                if (!UsesSharedBudget(_intents[i].TileIndex)) ExecuteIntent(_intents[i], ref result);
        }

        private void ExecuteIntent(Intent intent, ref StepResult result)
        {
            if (_heads[intent.FromQueue] != intent.Node || _movedThisTick[intent.Node]) return;
            switch (intent.Kind)
            {
                case IntentKind.Arrival:
                    RecordArrival(_cars[intent.Node], TileAt(intent.TileIndex));
                    ReleaseNode(DetachHead(intent.FromQueue));
                    result.Arrivals++;
                    return;
                case IntentKind.RingEntry:
                    if (_ringNodes[intent.RingIndex] != NoNode)
                    {
                        _blockedTicks[intent.Node]++;
                        return;
                    }
                    _ringNodes[intent.RingIndex] = DetachHead(intent.FromQueue);
                    _movedThisTick[intent.Node] = true;
                    _blockedTicks[intent.Node] = 0;
                    return;
                case IntentKind.Move:
                    if (!intent.Force && !CanAcceptNormally(intent.ToQueue))
                    {
                        _blockedTicks[intent.Node]++;
                        return;
                    }
                    MoveHead(intent.FromQueue, intent.ToQueue);
                    if (intent.Force) result.ValveActivations++;
                    return;
            }
        }

        private void ServiceRoundaboutRings(ICarRouteProvider routes, ref StepResult result)
        {
            for (int tile = 0; tile < _roundabouts.Length; tile++)
            {
                if (!_roundabouts[tile]) continue;
                Vector2Int position = TileAt(tile);
                int first = tile * DirectionCount;
                int heldMask = 0;

                for (int cell = 0; cell < DirectionCount; cell++)
                {
                    int ring = first + cell;
                    int node = _ringNodes[ring];
                    if (node == NoNode || _movedThisTick[node]) continue;
                    int carId = _cars[node];
                    if (routes.IsDestination(carId, position))
                    {
                        RecordArrival(carId, position);
                        _ringNodes[ring] = NoNode;
                        ReleaseNode(node);
                        result.Arrivals++;
                        continue;
                    }
                    if (!routes.TryGetNextTile(carId, position, out Vector2Int next, out Dir exit)
                        || (int)exit != cell || !TryQueueIndex(next, exit, out int toQueue)) continue;
                    if (!CanAcceptNormally(toQueue))
                    {
                        _blockedTicks[node]++;
                        heldMask |= 1 << cell;
                        continue;
                    }
                    _ringNodes[ring] = NoNode;
                    _movedThisTick[node] = true;
                    _blockedTicks[node] = 0;
                    AppendNode(toQueue, node);
                }

                // 이탈 대기차가 있으면 링 전체가 그 차의 점유를 존중해 정지한다.
                // 그렇지 않으면 네 셀을 동시에 치환해 만석 링도 차 손실 없이 CCW 회전한다.
                if (heldMask != 0) continue;
                int north = _ringNodes[first + (int)Dir.N];
                int east = _ringNodes[first + (int)Dir.E];
                int south = _ringNodes[first + (int)Dir.S];
                int west = _ringNodes[first + (int)Dir.W];
                _ringNodes[first + (int)Dir.N] = east;
                _ringNodes[first + (int)Dir.W] = north;
                _ringNodes[first + (int)Dir.S] = west;
                _ringNodes[first + (int)Dir.E] = south;
                for (int cell = 0; cell < DirectionCount; cell++)
                {
                    int node = _ringNodes[first + cell];
                    if (node != NoNode) _movedThisTick[node] = true;
                }
            }
        }

        private Intent NewIntent(IntentKind kind, int queue, int node, int tile, Dir entry, Dir exit) =>
            new Intent { Kind = kind, FromQueue = queue, ToQueue = NoNode, Node = node,
                TileIndex = tile, Entry = entry, Exit = exit, RingIndex = NoNode };

        private void RecordArrival(int carId, Vector2Int tile)
        {
            _arrivals[ArrivalCount++] = new ArrivalRecord { CarId = carId, Tile = tile };
        }

        private bool UsesSharedBudget(int tile) =>
            _intersections[tile] && !_overpasses[tile] && !_roundabouts[tile];

        private bool IsBetter(Intent candidate, Intent current)
        {
            RoadAxis priority = _priorityAxes[candidate.TileIndex];
            bool candidatePriority = priority != RoadAxis.None && Axis(candidate.Entry) == priority;
            bool currentPriority = priority != RoadAxis.None && Axis(current.Entry) == priority;
            if (candidatePriority != currentPriority) return candidatePriority;
            int candidateTurn = TurnRank(candidate.Entry, candidate.Exit);
            int currentTurn = TurnRank(current.Entry, current.Exit);
            return candidateTurn != currentTurn
                ? candidateTurn < currentTurn
                : candidate.Entry < current.Entry;
        }

        private bool IsIntersectionExitBlocked(ICarRouteProvider routes, int carId, Vector2Int intersection, int tileIndex)
        {
            if (!_intersections[tileIndex] || _overpasses[tileIndex] || _roundabouts[tileIndex]
                || routes.IsDestination(carId, intersection)) return false;
            return !routes.TryGetNextTile(carId, intersection, out Vector2Int exit, out Dir exitDir)
                || !TryQueueIndex(exit, exitDir, out int exitQueue) || !CanAcceptNormally(exitQueue);
        }

        private static int TurnRank(Dir entry, Dir exit)
        {
            if (exit == entry) return 0;
            if ((int)exit == ((int)entry + 1) % DirectionCount) return 1;
            if ((int)exit == ((int)entry + 3) % DirectionCount) return 2;
            return 3;
        }

        private static RoadAxis Axis(Dir direction) =>
            direction == Dir.E || direction == Dir.W ? RoadAxis.Horizontal : RoadAxis.Vertical;
        private static Dir Opposite(Dir direction) => (Dir)(((int)direction + 2) % DirectionCount);
        private static bool TryDir(Vector2Int vector, out Dir direction)
        {
            if (vector == Vector2Int.up) direction = Dir.N;
            else if (vector == Vector2Int.right) direction = Dir.E;
            else if (vector == Vector2Int.down) direction = Dir.S;
            else if (vector == Vector2Int.left) direction = Dir.W;
            else { direction = default; return false; }
            return true;
        }

        private int TurnIndex(int tile, Dir entry, Dir exit) =>
            ((tile * DirectionCount + (int)entry) * DirectionCount) + (int)exit;
        private bool CanAcceptNormally(int queue) => _queueActive[queue] && _counts[queue] < _capacity;

        private bool TryAllocateNode(out int node)
        {
            node = _freeHead;
            if (node == NoNode) return false;
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

        private void AppendNode(int queue, int node)
        {
            _nextNodes[node] = NoNode;
            if (_tails[queue] == NoNode) _heads[queue] = _tails[queue] = node;
            else { _nextNodes[_tails[queue]] = node; _tails[queue] = node; }
            _counts[queue]++;
        }

        private int DetachHead(int queue)
        {
            int node = _heads[queue];
            int next = _nextNodes[node];
            _heads[queue] = next;
            _counts[queue]--;
            if (next == NoNode) _tails[queue] = NoNode;
            _nextNodes[node] = NoNode;
            return node;
        }

        private void MoveHead(int from, int to)
        {
            int node = DetachHead(from);
            _movedThisTick[node] = true;
            _blockedTicks[node] = 0;
            AppendNode(to, node);
        }

        private bool TryQueueIndex(Vector2Int tile, Dir direction, out int queue)
        {
            int d = (int)direction;
            if (!InBounds(tile) || d < 0 || d >= DirectionCount)
            {
                queue = NoNode;
                return false;
            }
            queue = TileIndex(tile) * DirectionCount + d;
            return true;
        }

        private Vector2Int TileAt(int index) => new Vector2Int(index % _width, index / _width);
        private int TileIndex(Vector2Int tile) => tile.y * _width + tile.x;
        private bool InBounds(Vector2Int tile) => tile.x >= 0 && tile.x < _width && tile.y >= 0 && tile.y < _height;
    }
}
