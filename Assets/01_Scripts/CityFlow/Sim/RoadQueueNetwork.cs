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
        bool TryGetHighwayPartner(Vector2Int ramp, out Vector2Int partner);
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

        private enum IntentKind { Arrival, Move, RingEntry, HighwayEntry }

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
            public int HighwayLane;
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
        private readonly int[] _highwayPartners;
        private readonly System.Collections.Generic.List<HighwayState> _highways = new();
        private System.Collections.Generic.List<LinkCar>[] _highwayCars = Array.Empty<System.Collections.Generic.List<LinkCar>>();
        private readonly Intent[] _intents;
        private readonly ArrivalRecord[] _arrivals;
        private int _freeHead;

        private struct HighwayState
        {
            public int A;
            public int B;
            public int TransitTicks;
            public int Capacity;
        }

        private struct LinkCar
        {
            public int Node;
            public int ExitTick;
        }

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
            _highwayPartners = new int[tileCount];
            _intents = new Intent[queueCount];
            _arrivals = new ArrivalRecord[maxCars];

            Array.Fill(_heads, NoNode);
            Array.Fill(_tails, NoNode);
            Array.Fill(_ringNodes, NoNode);
            Array.Fill(_highwayPartners, NoNode);
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

            ReturnHighwayCarsToNearestRamp();
            TurnRestrictionBlockCount = 0;
            _highways.Clear();
            Array.Fill(_highwayPartners, NoNode);
            for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++)
            {
                var tile = new Vector2Int(x, y);
                int tileIndex = TileIndex(tile);
                _intersections[tileIndex] = grid.IsIntersection(tile);
                _roundabouts[tileIndex] = devices?.IsRoundabout(tile) ?? false;
                _overpasses[tileIndex] = devices?.IsOverpass(tile) ?? false;
                _priorityAxes[tileIndex] = devices?.PriorityAxis(tile) ?? RoadAxis.None;

                if (devices != null && devices.TryGetHighwayPartner(tile, out Vector2Int partner)
                    && InBounds(partner))
                {
                    int partnerIndex = TileIndex(partner);
                    _highwayPartners[tileIndex] = partnerIndex;
                    if (tileIndex < partnerIndex)
                    {
                        int distance = Mathf.Abs(tile.x - partner.x) + Mathf.Abs(tile.y - partner.y);
                        _highways.Add(new HighwayState
                        {
                            A = tileIndex,
                            B = partnerIndex,
                            TransitTicks = Mathf.Max(1, Mathf.CeilToInt(distance / 2f)),
                            Capacity = Mathf.Max(1, distance)
                        });
                    }
                }

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
            _highwayCars = new System.Collections.Generic.List<LinkCar>[_highways.Count * 2];
            for (int i = 0; i < _highwayCars.Length; i++)
                _highwayCars[i] = new System.Collections.Generic.List<LinkCar>();
        }

        private void ReturnHighwayCarsToNearestRamp()
        {
            for (int lane = 0; lane < _highwayCars.Length; lane++)
            {
                HighwayState link = _highways[lane / 2];
                bool reverse = (lane & 1) != 0;
                int start = reverse ? link.B : link.A;
                int end = reverse ? link.A : link.B;
                Dir direction = DirectionBetween(TileAt(start), TileAt(end));
                var cars = _highwayCars[lane];
                for (int i = 0; i < cars.Count; i++)
                {
                    float progress = 1f - (cars[i].ExitTick - _currentTick) / (float)link.TransitTicks;
                    int ramp = progress < 0.5f ? start : end;
                    AppendNode(ramp * DirectionCount + (int)direction, cars[i].Node);
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

        internal bool TryLocateCar(int carId, out Vector2Int tile, out Dir direction, out int slot, out float linkProgress01)
        {
            if (TryLocateCar(carId, out tile, out direction, out slot))
            {
                linkProgress01 = 0f;
                return true;
            }
            for (int lane = 0; lane < _highwayCars.Length; lane++)
            {
                var cars = _highwayCars[lane];
                for (int i = 0; i < cars.Count; i++)
                {
                    if (_cars[cars[i].Node] != carId) continue;
                    HighwayState link = _highways[lane / 2];
                    bool reverse = (lane & 1) != 0;
                    tile = TileAt(reverse ? link.B : link.A);
                    direction = DirectionBetween(TileAt(reverse ? link.B : link.A), TileAt(reverse ? link.A : link.B));
                    slot = 0;
                    linkProgress01 = Mathf.Clamp01(1f - (cars[i].ExitTick - _currentTick) / (float)link.TransitTicks);
                    return true;
                }
            }
            linkProgress01 = 0f;
            return false;
        }

        private int _currentTick;

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
            for (int i = 0; i < _highwayCars.Length; i++) _highwayCars[i].Clear();
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
            _currentTick = tick;
            ServiceHighwayLinks(ref result);

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

                int carId = _cars[node];
                if (routes.IsDestination(carId, tile))
                {
                    _intents[count++] = NewIntent(IntentKind.Arrival, queue, node, tileIndex, entry, entry);
                    continue;
                }
                if (!routes.TryGetNextTile(carId, tile, out Vector2Int next, out Dir exit)
                    ) continue;

                int partnerIndex = _highwayPartners[tileIndex];
                if (partnerIndex != NoNode && partnerIndex == TileIndex(next))
                {
                    int linkIndex = FindHighway(tileIndex, partnerIndex, out bool reverse);
                    if (linkIndex >= 0)
                    {
                        int lane = linkIndex * 2 + (reverse ? 1 : 0);
                        if (_highwayCars[lane].Count < _highways[linkIndex].Capacity)
                        {
                            Intent highway = NewIntent(IntentKind.HighwayEntry, queue, node, tileIndex, entry, exit);
                            highway.HighwayLane = lane;
                            _intents[count++] = highway;
                        }
                        else _blockedTicks[node]++;
                    }
                    continue;
                }

                if (!TryQueueIndex(next, exit, out int nextQueue)) continue;

                // 신호는 '진입'을 게이트한다(2026-07-21, 환 결정). 예전엔 차가 밟고 있는
                // 타일의 신호를 봤는데, 신호는 교차로 타일에만 있으므로 접근 도로에선 검사가
                // 무사통과됐고 적색은 차를 정지선 앞이 아니라 **교차로 안에** 쌓았다 —
                // 화면에선 원인이 안 보이는 "이유 없는 멈춤"(환 라이브 2026-07-21).
                // 이제 다음 타일의 신호를 본다: 차는 접근 타일(정지선)에서 기다리고,
                // 교차로 위의 차는 적색이어도 빠져나간다(교차로 비우기).
                // 신호 대기는 시간이 반드시 풀어주므로 교착 밸브를 무장시키지 않는다(기존 동일).
                // exit = 다음 타일 기준 진입방향 → 어댑터의 축 판정(E/W=수평)이 그대로 맞다.
                if (signalGate != null && !signalGate.IsServiceOpen(next, exit, tick)) continue;

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
                ExecuteIntent(_intents[winner], ref result);
                // 승자와 충돌하지 않는 이동은 같은 틱에 함께 통과시킨다. 예전엔 교차로당
                // 1대만 통과해, 마주보는 직진처럼 서로 방해하지 않는 차까지 순번을 기다렸다
                // (환 라이브 2026-07-20: "앞에 차가 없는데 교차로에서 멈췄다 간다").
                for (int i = 0; i < intentCount; i++)
                {
                    if (i == winner || _intents[i].TileIndex != tile) continue;
                    if (Compatible(_intents[winner], _intents[i]))
                    {
                        // 정원 미달로 못 가면 ExecuteIntent가 스스로 _blockedTicks를 올린다.
                        ExecuteIntent(_intents[i], ref result);
                        continue;
                    }
                    int node = _intents[i].Node;
                    if (node != NoNode) _blockedTicks[node]++;
                }
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
                case IntentKind.HighwayEntry:
                    int linkIndex = intent.HighwayLane / 2;
                    if (_highwayCars[intent.HighwayLane].Count >= _highways[linkIndex].Capacity)
                    {
                        _blockedTicks[intent.Node]++;
                        return;
                    }
                    DetachHead(intent.FromQueue);
                    _highwayCars[intent.HighwayLane].Add(new LinkCar
                    {
                        Node = intent.Node,
                        ExitTick = _currentTick + _highways[linkIndex].TransitTicks
                    });
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

        private void ServiceHighwayLinks(ref StepResult result)
        {
            for (int lane = 0; lane < _highwayCars.Length; lane++)
            {
                var cars = _highwayCars[lane];
                if (cars.Count == 0 || cars[0].ExitTick > _currentTick) continue;
                HighwayState link = _highways[lane / 2];
                bool reverse = (lane & 1) != 0;
                int destination = reverse ? link.A : link.B;
                Dir entry = DirectionBetween(TileAt(reverse ? link.B : link.A), TileAt(destination));
                int queue = destination * DirectionCount + (int)entry;
                if (!CanAcceptNormally(queue))
                {
                    _blockedTicks[cars[0].Node]++;
                    continue;
                }
                int node = cars[0].Node;
                cars.RemoveAt(0);
                _movedThisTick[node] = true;
                _blockedTicks[node] = 0;
                AppendNode(queue, node);
            }
        }

        private int FindHighway(int from, int to, out bool reverse)
        {
            for (int i = 0; i < _highways.Count; i++)
            {
                if (_highways[i].A == from && _highways[i].B == to) { reverse = false; return i; }
                if (_highways[i].B == from && _highways[i].A == to) { reverse = true; return i; }
            }
            reverse = false;
            return -1;
        }

        private static Dir DirectionBetween(Vector2Int from, Vector2Int to)
        {
            Vector2Int delta = to - from;
            if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)) return delta.x >= 0 ? Dir.E : Dir.W;
            return delta.y >= 0 ? Dir.N : Dir.S;
        }

        private Intent NewIntent(IntentKind kind, int queue, int node, int tile, Dir entry, Dir exit) =>
            new Intent { Kind = kind, FromQueue = queue, ToQueue = NoNode, Node = node,
                TileIndex = tile, Entry = entry, Exit = exit, RingIndex = NoNode, HighwayLane = NoNode };

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

        // 교차로 동시 통과 판정.
        // 한 타일의 인텐트는 진입 방향이 서로 다르므로(큐가 방향별로 하나), 진입축이 같다는 건
        // 두 차가 마주보고 들어온다는 뜻이다. 거기서 이탈축까지 같고 합류(같은 ToQueue)만
        // 아니면 실제로 통과 가능한 조합은 셋뿐이다:
        //   마주보는 직진(E→E, W→W) · 동시 좌회전(E→N, W→S) · 동시 우회전(E→S, W→N)
        // 셋 다 경로가 교차하지 않는다. 단 **동시 U턴**(E→W, W→E)은 이탈축도 같고 ToQueue도
        // 달라 위 조건을 통과하지만 서로를 정면으로 가로지르므로 명시적으로 배제한다.
        private static bool Compatible(Intent winner, Intent other) =>
            winner.Kind == IntentKind.Move
            && other.Kind == IntentKind.Move
            && winner.ToQueue != other.ToQueue
            && Axis(winner.Entry) == Axis(other.Entry)
            && Axis(winner.Exit) == Axis(other.Exit)
            && winner.Exit != Opposite(winner.Entry)
            && other.Exit != Opposite(other.Entry);

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
