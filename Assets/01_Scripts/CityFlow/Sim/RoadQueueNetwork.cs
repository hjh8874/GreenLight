using System;
using CityFlow.Contracts;
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

    public enum RoadAxis { None = 0, Horizontal = 1, Vertical = 2 }

    internal sealed class RoadQueueNetwork
    {
        private const int DirectionCount = 4;
        private const int NoNode = -1;
        private const int RoundaboutArmCapacity = 1;
        // MainCityView.vehicleMinHeadway(0.55)와 값 일치 계약: Sim 타일 용량과 View 차간격은
        // 같은 물리 공간을 표현한다. 승인 후 SimConfig 필드로 승격하면 이 임시 상수는 제거한다.
        private const float QueueHeadwayTiles = 0.55f;

        private enum IntentKind
        {
            Arrival,
            Move,
            RoundaboutArmEntry,
            RingEntry,
            IntersectionAdvance,
            HighwayEntry
        }

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
            public int ReservationTile;
            public Dir MovementEntry;
            public Dir MovementExit;
            public IntersectionCell CurrentReservationMask;
            public IntersectionCell ReservationMask;
            public bool CompleteIntersectionOnEntry;
        }

        private readonly int _width;
        private readonly int _height;
        private readonly int _capacity;
        private readonly int _effectiveCapacity;
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
        private readonly int[] _roundaboutCenters;
        private readonly int[] _roundaboutArmSides;
        private readonly bool[] _overpasses;
        private readonly RoadAxis[] _priorityAxes;
        private readonly bool[] _queueActive;
        private readonly bool[] _turnAllowed;
        private readonly RoundaboutTrafficState[] _roundaboutStates;
        private readonly int[] _highwayPartners;
        private readonly System.Collections.Generic.List<HighwayState> _highways = new();
        private System.Collections.Generic.List<LinkCar>[] _highwayCars = Array.Empty<System.Collections.Generic.List<LinkCar>>();
        private readonly Intent[] _intents;
        private readonly bool[] _intentHandled;
        private readonly IntersectionCell[] _intersectionOccupancy;
        private readonly IntersectionCell[] _intersectionRoundReservations;
        private readonly bool[] _approachingStraightThreats;
        private readonly IntersectionStage[] _intersectionStages;
        private readonly Dir[] _intersectionMovementExits;
        // Encodes the previous intersection and movement entry for one rear-clearance tick.
        // Pending values are negative so multiple service rounds in the exit tick still see them.
        private readonly int[] _clearingIntersection;
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
            _effectiveCapacity = Math.Min(
                _capacity,
                Math.Max(1, Mathf.CeilToInt(1f / QueueHeadwayTiles)));
            _servicePerTick = Math.Max(1, cfg.QueueServicePerTick);
            _gridlockValveTicks = Math.Max(1, cfg.GridlockValveTicks);

            int tileCount = checked(width * height);
            int queueCount = checked(tileCount * DirectionCount);
            // 노드 풀은 설정 용량을 메모리 상한으로 유지한다. 물리 수용 판정만 effectiveCapacity를 쓴다.
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
            _roundaboutCenters = new int[tileCount];
            _roundaboutArmSides = new int[tileCount];
            _overpasses = new bool[tileCount];
            _priorityAxes = new RoadAxis[tileCount];
            _queueActive = new bool[queueCount];
            _turnAllowed = new bool[tileCount * DirectionCount * DirectionCount];
            _roundaboutStates = new RoundaboutTrafficState[tileCount];
            _highwayPartners = new int[tileCount];
            _intents = new Intent[queueCount];
            _intentHandled = new bool[queueCount];
            _intersectionOccupancy = new IntersectionCell[tileCount];
            _intersectionRoundReservations = new IntersectionCell[tileCount];
            _approachingStraightThreats = new bool[queueCount];
            _intersectionStages = new IntersectionStage[maxCars];
            _intersectionMovementExits = new Dir[maxCars];
            _clearingIntersection = new int[maxCars];
            _arrivals = new ArrivalRecord[maxCars];

            Array.Fill(_heads, NoNode);
            Array.Fill(_tails, NoNode);
            Array.Fill(_highwayPartners, NoNode);
            Array.Fill(_roundaboutCenters, NoNode);
            Array.Fill(_roundaboutArmSides, NoNode);
            Array.Fill(_clearingIntersection, NoNode);
            Array.Fill(_queueActive, true);
            Array.Fill(_turnAllowed, true);
            for (int node = 0; node < maxCars; node++)
            {
                _cars[node] = NoNode;
                _nextNodes[node] = node + 1 < maxCars ? node + 1 : NoNode;
            }
            for (int tile = 0; tile < tileCount; tile++)
            {
                _roundaboutStates[tile] = new RoundaboutTrafficState();
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
            Array.Fill(_roundaboutCenters, NoNode);
            Array.Fill(_roundaboutArmSides, NoNode);
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
            RebuildRoundaboutFootprints();
            ClearStagesOutsideIntersections();
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
            int node = _roundaboutStates[TileIndex(tile)].NodeAt(cell);
            return node == NoNode ? NoNode : _cars[node];
        }

        internal bool IsSafeResumeTile(Vector2Int tile)
        {
            if (!InBounds(tile)) return false;
            int tileIndex = TileIndex(tile);
            // Rebuild enqueue has no intersection stage or roundabout ring reservation.
            // Resume only on an ordinary queue tile so those state machines admit the
            // vehicle through their normal entry paths.
            return !UsesSharedBudget(tileIndex)
                && !_roundabouts[tileIndex]
                && !IsRoundaboutArm(tileIndex);
        }

        internal bool TryLocateCar(int carId, out Vector2Int tile, out Dir direction, out int slot)
            => TryLocateCar(carId, out tile, out direction, out slot, out _, out _);

        internal bool TryLocateCar(
            int carId,
            out Vector2Int tile,
            out Dir direction,
            out int slot,
            out float progress)
        {
            bool found = TryLocateCar(
                carId,
                out tile,
                out direction,
                out slot,
                out float intersectionProgress,
                out float linkProgress,
                out _,
                out bool onHighway);
            progress = onHighway ? linkProgress : intersectionProgress;
            return found;
        }

        internal bool TryLocateCar(
            int carId,
            out Vector2Int tile,
            out Dir direction,
            out int slot,
            out float intersectionProgress,
            out float linkProgress01)
            => TryLocateCar(
                carId,
                out tile,
                out direction,
                out slot,
                out intersectionProgress,
                out linkProgress01,
                out _,
                out _);

        internal bool TryLocateCar(
            int carId,
            out Vector2Int tile,
            out Dir direction,
            out int slot,
            out float intersectionProgress,
            out float linkProgress01,
            out int roundaboutCell)
            => TryLocateCar(
                carId,
                out tile,
                out direction,
                out slot,
                out intersectionProgress,
                out linkProgress01,
                out roundaboutCell,
                out _);

        private bool TryLocateCar(
            int carId,
            out Vector2Int tile,
            out Dir direction,
            out int slot,
            out float intersectionProgress,
            out float linkProgress01,
            out int roundaboutCell,
            out bool onHighway)
        {
            for (int queue = 0; queue < _heads.Length; queue++)
            {
                int node = _heads[queue];
                int queueSlot = 0;
                while (node != NoNode)
                {
                    if (_cars[node] == carId)
                    {
                        int tileIndex = queue / DirectionCount;
                        tile = TileAt(tileIndex);
                        direction = (Dir)(queue % DirectionCount);
                        slot = queueSlot;
                        intersectionProgress = UsesSharedBudget(tileIndex)
                            ? IntersectionMicroGrid.Progress01(_intersectionStages[node])
                            : -1f;
                        linkProgress01 = 0f;
                        roundaboutCell = NoNode;
                        onHighway = false;
                        return true;
                    }
                    node = _nextNodes[node];
                    queueSlot++;
                }
            }
            for (int tileIndex = 0; tileIndex < _roundaboutStates.Length; tileIndex++)
            {
                RoundaboutTrafficState state = _roundaboutStates[tileIndex];
                for (int cell = 0; cell < DirectionCount; cell++)
                {
                    int node = state.NodeAt((Dir)cell);
                    if (node == NoNode || _cars[node] != carId) continue;
                    tile = TileAt(tileIndex);
                    direction = (Dir)cell;
                    slot = 0;
                    intersectionProgress = -1f;
                    linkProgress01 = 0f;
                    roundaboutCell = cell;
                    onHighway = false;
                    return true;
                }
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
                    intersectionProgress = -1f;
                    linkProgress01 = Mathf.Clamp01(1f - (cars[i].ExitTick - _currentTick) / (float)link.TransitTicks);
                    roundaboutCell = NoNode;
                    onHighway = true;
                    return true;
                }
            }
            tile = default;
            direction = default;
            slot = -1;
            intersectionProgress = -1f;
            linkProgress01 = 0f;
            roundaboutCell = NoNode;
            onHighway = false;
            return false;
        }

        private void ClearStagesOutsideIntersections()
        {
            for (int queue = 0; queue < _heads.Length; queue++)
            {
                int tileIndex = queue / DirectionCount;
                if (UsesSharedBudget(tileIndex)) continue;

                int node = _heads[queue];
                while (node != NoNode)
                {
                    if (TryDecodeClearingIntersection(
                            _clearingIntersection[node],
                            out int clearingTile,
                            out _)
                        && UsesSharedBudget(clearingTile))
                    {
                        node = _nextNodes[node];
                        continue;
                    }

                    _intersectionStages[node] = IntersectionStage.None;
                    _intersectionMovementExits[node] = default;
                    _clearingIntersection[node] = NoNode;
                    node = _nextNodes[node];
                }
            }
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
            for (int tile = 0; tile < _roundaboutStates.Length; tile++)
                _roundaboutStates[tile].Clear();
            for (int i = 0; i < _highwayCars.Length; i++) _highwayCars[i].Clear();
            for (int node = 0; node < _cars.Length; node++)
            {
                _cars[node] = NoNode;
                _movedThisTick[node] = false;
                _blockedTicks[node] = 0;
                _intersectionStages[node] = IntersectionStage.None;
                _intersectionMovementExits[node] = default;
                _clearingIntersection[node] = NoNode;
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
            int totalCount = 0;
            for (int d = 0; d < DirectionCount; d++)
            {
                int count = _counts[tileIndex * DirectionCount + d];
                maxCount = Math.Max(maxCount, count);
                totalCount += count;
            }
            if (IsRoundaboutArm(tileIndex)) return totalCount > 0 ? 1f : 0f;
            float approach = (float)maxCount / _effectiveCapacity;
            if (!_roundabouts[tileIndex]) return Mathf.Clamp01(approach);
            int ringCount = _roundaboutStates[tileIndex].OccupiedCount;
            return Mathf.Clamp01(Math.Max(approach, ringCount / 4f));
        }

        public StepResult Step(ICarRouteProvider routes) => Step(routes, null, 0);

        public StepResult Step(ICarRouteProvider routes, ISignalGate signalGate, int tick)
        {
            if (routes == null) throw new ArgumentNullException(nameof(routes));
            ArrivalCount = 0;
            AdvanceIntersectionClearanceWindows();
            Array.Clear(_movedThisTick, 0, _movedThisTick.Length);
            for (int tile = 0; tile < _roundaboutStates.Length; tile++)
                _roundaboutStates[tile].BeginTick();
            StepResult result = default;
            _currentTick = tick;
            ServiceHighwayLinks(ref result);
            PrepareRoundaboutEntrySides(routes);

            for (int serviceRound = 0; serviceRound < _servicePerTick; serviceRound++)
            {
                ServiceRoundaboutRings(routes, ref result);
                RebuildIntersectionOccupancy(routes);
                int intentCount = CollectIntents(routes, signalGate, tick);
                ResolveIntents(intentCount, ref result);
            }
            return result;
        }

        private int CollectIntents(ICarRouteProvider routes, ISignalGate signalGate, int tick)
        {
            Array.Clear(_approachingStraightThreats, 0, _approachingStraightThreats.Length);
            int count = 0;
            for (int queue = 0; queue < _heads.Length; queue++)
            {
                int node = _heads[queue];
                if (node == NoNode || _movedThisTick[node]) continue;
                int tileIndex = queue / DirectionCount;
                Dir entry = (Dir)(queue % DirectionCount);
                Vector2Int tile = TileAt(tileIndex);

                int carId = _cars[node];
                IntersectionStage intersectionStage = _intersectionStages[node];
                if (UsesSharedBudget(tileIndex)
                    && (intersectionStage == IntersectionStage.Entry
                        || intersectionStage == IntersectionStage.Conflict))
                {
                    IntersectionStage nextStage = IntersectionMicroGrid.NextStage(intersectionStage);
                    Dir stageMovementExit = _intersectionMovementExits[node];
                    Intent advance = NewIntent(
                        IntentKind.IntersectionAdvance,
                        queue,
                        node,
                        tileIndex,
                        entry,
                        stageMovementExit);
                    advance.ReservationTile = tileIndex;
                    advance.MovementEntry = entry;
                    advance.MovementExit = stageMovementExit;
                    advance.CurrentReservationMask = IntersectionMicroGrid.StageMask(
                        entry,
                        stageMovementExit,
                        intersectionStage);
                    advance.ReservationMask = IntersectionMicroGrid.StageMask(
                        entry,
                        stageMovementExit,
                        nextStage);
                    _intents[count++] = advance;
                    continue;
                }

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
                    Dir legacyApproachSide = Opposite(entry);
                    RoundaboutTrafficState state = _roundaboutStates[tileIndex];
                    if (state.TryReserveRingEntry(legacyApproachSide, node, out Dir target))
                    {
                        Intent intent = NewIntent(
                            IntentKind.RingEntry,
                            queue,
                            node,
                            tileIndex,
                            legacyApproachSide,
                            exit);
                        intent.RingIndex = (int)target;
                        _intents[count++] = intent;
                    }
                    else _blockedTicks[node]++;
                    continue;
                }

                if (TryGetRoundaboutEntry(tileIndex, TileIndex(next), out int centerIndex, out Dir approachSide))
                {
                    RoundaboutTrafficState state = _roundaboutStates[centerIndex];
                    if (state.TryReserveRingEntry(approachSide, node, out Dir target))
                    {
                        Intent intent = NewIntent(
                            IntentKind.RingEntry,
                            queue,
                            node,
                            centerIndex,
                            approachSide,
                            exit);
                        intent.RingIndex = (int)target;
                        _intents[count++] = intent;
                    }
                    else _blockedTicks[node]++;
                    continue;
                }

                int nextTileIndex = TileIndex(next);
                if (TryGetRoundaboutApproach(
                        routes,
                        carId,
                        tileIndex,
                        nextTileIndex,
                        out centerIndex,
                        out approachSide))
                {
                    RoundaboutTrafficState state = _roundaboutStates[centerIndex];
                    if (CanAcceptNormally(nextQueue) && state.TryReserveApproach(approachSide))
                    {
                        Intent intent = NewIntent(
                            IntentKind.RoundaboutArmEntry,
                            queue,
                            node,
                            centerIndex,
                            approachSide,
                            exit);
                        intent.ToQueue = nextQueue;
                        _intents[count++] = intent;
                    }
                    else _blockedTicks[node]++;
                    continue;
                }

                bool nextIsSharedIntersection = UsesSharedBudget(nextTileIndex);
                Dir movementExit = exit;
                bool hasMovementBeyondIntersection = false;
                if (nextIsSharedIntersection
                    && !routes.IsDestination(carId, next)
                    && routes.TryGetNextTile(carId, next, out _, out Dir plannedExit))
                {
                    movementExit = plannedExit;
                    hasMovementBeyondIntersection = true;
                }
                if (hasMovementBeyondIntersection && exit == movementExit)
                {
                    // Track only the queue head moving into this intersection.
                    _approachingStraightThreats[
                        nextTileIndex * DirectionCount + (int)exit] = true;
                }

                bool leavingIntersection = UsesSharedBudget(tileIndex)
                    && intersectionStage == IntersectionStage.Exit;
                bool blocked = (leavingIntersection
                        ? !HasClearIntersectionExit(nextQueue)
                        : !CanAcceptNormally(nextQueue))
                    || IsIntersectionExitBlocked(routes, carId, next, nextTileIndex);
                Intent move = NewIntent(IntentKind.Move, queue, node, tileIndex, entry, exit);
                move.ToQueue = nextQueue;

                if (UsesSharedBudget(tileIndex)
                    && intersectionStage == IntersectionStage.Exit)
                {
                    move.MovementEntry = entry;
                    move.MovementExit = _intersectionMovementExits[node];
                    move.CurrentReservationMask = IntersectionMicroGrid.OccupancyMask(
                        entry,
                        move.MovementExit,
                        IntersectionStage.Exit);
                }

                if (nextIsSharedIntersection)
                {
                    bool straightMovement = exit == movementExit;
                    IntersectionCell reservationMask = straightMovement
                        ? IntersectionMicroGrid.MovementMask(exit, movementExit)
                        : IntersectionMicroGrid.StageMask(
                            exit,
                            movementExit,
                            IntersectionStage.Entry);

                    move.ReservationTile = nextTileIndex;
                    move.MovementEntry = exit;
                    move.MovementExit = movementExit;
                    move.ReservationMask = reservationMask;
                }

                if (blocked)
                {
                    _blockedTicks[node]++;
                    // Never force a car into an intersection with a blocked exit.
                    // Every movement on a roundabout arm shares one physical tile. Forced entry
                    // would bypass its single-car safety invariant, including for tangent traffic.
                    if (move.ReservationTile != NoNode
                        || IsRoundaboutArm(nextTileIndex)
                        || _blockedTicks[node] < _gridlockValveTicks) continue;
                    move.Force = true;
                }
                _intents[count++] = move;
            }
            return count;
        }

        private void PrepareRoundaboutEntrySides(ICarRouteProvider routes)
        {
            // Demand is observed on the outer approach, before a vehicle occupies an arm.
            // The admitted node keeps ownership while moving approach -> arm -> central ring.
            for (int queue = 0; queue < _heads.Length; queue++)
            {
                int node = _heads[queue];
                if (node == NoNode || _movedThisTick[node]) continue;

                int tileIndex = queue / DirectionCount;
                Dir queueEntry = (Dir)(queue % DirectionCount);
                Vector2Int tile = TileAt(tileIndex);
                int carId = _cars[node];
                if (routes.IsDestination(carId, tile)) continue;

                if (_roundabouts[tileIndex])
                {
                    _roundaboutStates[tileIndex].RegisterEntryDemand(Opposite(queueEntry));
                    continue;
                }

                if (!routes.TryGetNextTile(carId, tile, out Vector2Int next, out Dir entryAtNext)
                    || !InBounds(next))
                {
                    continue;
                }

                int nextTileIndex = TileIndex(next);
                if (TryGetRoundaboutEntry(tileIndex, nextTileIndex, out int centerIndex, out Dir approachSide))
                {
                    _roundaboutStates[centerIndex].RegisterEntryDemand(approachSide);
                }
                else if (TryGetRoundaboutApproach(
                             routes,
                             carId,
                             tileIndex,
                             nextTileIndex,
                             out centerIndex,
                             out approachSide))
                {
                    _roundaboutStates[centerIndex].RegisterEntryDemand(approachSide);
                }
            }

            for (int tile = 0; tile < _roundabouts.Length; tile++)
            {
                if (_roundabouts[tile]) _roundaboutStates[tile].SelectEntrySide();
            }
        }

        private void RebuildIntersectionOccupancy(ICarRouteProvider routes)
        {
            Array.Clear(_intersectionOccupancy, 0, _intersectionOccupancy.Length);
            for (int tile = 0; tile < _intersections.Length; tile++)
            {
                if (!UsesSharedBudget(tile)) continue;
                Vector2Int position = TileAt(tile);
                int firstQueue = tile * DirectionCount;
                for (int direction = 0; direction < DirectionCount; direction++)
                {
                    int node = _heads[firstQueue + direction];
                    while (node != NoNode)
                    {
                        IntersectionCell mask = IntersectionCell.All;
                        IntersectionStage stage = _intersectionStages[node];
                        if (stage != IntersectionStage.None)
                        {
                            mask = IntersectionMicroGrid.OccupancyMask(
                                (Dir)direction,
                                _intersectionMovementExits[node],
                                stage);
                        }
                        else
                        {
                            int carId = _cars[node];
                            if (!routes.IsDestination(carId, position)
                                && routes.TryGetNextTile(carId, position, out _, out Dir exit))
                            {
                                mask = IntersectionMicroGrid.MovementMask((Dir)direction, exit);
                            }
                        }

                        _intersectionOccupancy[tile] |= mask;
                        node = _nextNodes[node];
                    }
                }
            }

            // A vehicle that just entered its exit tile still has its rear inside the
            // conflict zone for the following simulation tick. Keep the full movement
            // mask, not only the exit cell, so crossing paths cannot reuse it early.
            for (int queue = 0; queue < _heads.Length; queue++)
            {
                int node = _heads[queue];
                while (node != NoNode)
                {
                    if (TryDecodeClearingIntersection(
                            _clearingIntersection[node],
                            out int clearingTile,
                            out Dir movementEntry)
                        && UsesSharedBudget(clearingTile))
                    {
                        _intersectionOccupancy[clearingTile] |=
                            IntersectionMicroGrid.MovementMask(
                                movementEntry,
                                _intersectionMovementExits[node]);
                    }

                    node = _nextNodes[node];
                }
            }
        }

        private void ResolveIntents(int intentCount, ref StepResult result)
        {
            Array.Clear(_intentHandled, 0, intentCount);
            Array.Clear(
                _intersectionRoundReservations,
                0,
                _intersectionRoundReservations.Length);

            // Resolve vehicles already inside first. Entry is a valid waiting state for turns,
            // but new arrivals must not repeatedly take the cells needed to finish that turn.
            for (int tile = 0; tile < _intersections.Length; tile++)
            {
                if (UsesSharedBudget(tile))
                    ResolveIntersectionGroup(intentCount, tile, false, ref result);
            }

            // Admit new vehicles after internal transitions have reserved this round's cells.
            for (int tile = 0; tile < _intersections.Length; tile++)
            {
                if (UsesSharedBudget(tile))
                    ResolveIntersectionGroup(intentCount, tile, true, ref result);
            }

            for (int i = 0; i < intentCount; i++)
            {
                if (_intentHandled[i]) continue;
                _intentHandled[i] = true;
                ExecuteIntent(_intents[i], ref result);
            }
        }

        private void ResolveIntersectionGroup(
            int intentCount,
            int intersectionTile,
            bool useReservation,
            ref StepResult result)
        {
            IntersectionCell granted = useReservation
                ? _intersectionOccupancy[intersectionTile]
                    | _intersectionRoundReservations[intersectionTile]
                : _intersectionOccupancy[intersectionTile];

            while (true)
            {
                int winner = NoNode;
                for (int i = 0; i < intentCount; i++)
                {
                    if (_intentHandled[i] || !BelongsToIntersectionGroup(
                            _intents[i], intersectionTile, useReservation)) continue;

                    IntersectionCell blocking = granted & ~_intents[i].CurrentReservationMask;
                    IntersectionCell requested = GetRequestedMovementMask(
                        _intents[i],
                        intersectionTile,
                        useReservation,
                        blocking,
                        out _);
                    if (!useReservation
                        && _intents[i].Kind == IntentKind.IntersectionAdvance
                        && !IsStraightMovement(_intents[i])
                        && HasOpposingStraightThreat(
                            _intents[i].Node,
                            intersectionTile,
                            _intents[i].MovementEntry,
                            _intents[i].MovementExit))
                    {
                        continue;
                    }
                    if (IntersectionMicroGrid.Conflicts(blocking, requested)) continue;
                    if (winner == NoNode || IsBetterForIntersection(
                            _intents[i], _intents[winner], intersectionTile, useReservation))
                    {
                        winner = i;
                    }
                }

                if (winner == NoNode) break;
                _intentHandled[winner] = true;
                Intent winnerIntent = _intents[winner];
                IntersectionCell winnerBlocking =
                    granted & ~winnerIntent.CurrentReservationMask;
                IntersectionCell winnerMask = GetRequestedMovementMask(
                    winnerIntent,
                    intersectionTile,
                    useReservation,
                    winnerBlocking,
                    out bool completeIntersectionOnEntry);
                winnerIntent.CompleteIntersectionOnEntry = completeIntersectionOnEntry;
                _intents[winner] = winnerIntent;
                granted |= winnerMask;
                if (useReservation || _intents[winner].Kind == IntentKind.IntersectionAdvance)
                {
                    _intersectionRoundReservations[intersectionTile] |= winnerMask;
                }
                ExecuteIntent(_intents[winner], ref result);
            }

            for (int i = 0; i < intentCount; i++)
            {
                if (_intentHandled[i] || !BelongsToIntersectionGroup(
                        _intents[i], intersectionTile, useReservation)) continue;
                _intentHandled[i] = true;
                int node = _intents[i].Node;
                if (node != NoNode) _blockedTicks[node]++;
            }
        }

        private static bool BelongsToIntersectionGroup(
            Intent intent,
            int intersectionTile,
            bool useReservation)
        {
            if (useReservation)
            {
                return intent.Kind != IntentKind.IntersectionAdvance
                    && intent.ReservationTile == intersectionTile;
            }

            return intent.TileIndex == intersectionTile
                && (intent.Kind == IntentKind.IntersectionAdvance
                    || intent.ReservationTile == NoNode);
        }

        private static IntersectionCell GetIntentMovementMask(Intent intent, bool useReservation)
        {
            if (useReservation) return intent.ReservationMask;
            // Arrival removes the current node in place; it traverses no new cell.
            // In particular, an intersection destination must not request All and
            // conflict with the occupancy mask contributed by that same vehicle.
            if (intent.Kind == IntentKind.Arrival) return IntersectionCell.None;
            if (intent.Kind == IntentKind.IntersectionAdvance) return intent.ReservationMask;
            if (intent.Kind == IntentKind.Move)
                return IntersectionMicroGrid.MovementMask(intent.MovementEntry, intent.MovementExit);
            return IntersectionCell.All;
        }

        private IntersectionCell GetRequestedMovementMask(
            Intent intent,
            int intersectionTile,
            bool useReservation,
            IntersectionCell blocking,
            out bool completeIntersectionOnEntry)
        {
            completeIntersectionOnEntry = false;
            IntersectionCell requested = GetIntentMovementMask(intent, useReservation);
            if (!useReservation
                || intent.Kind != IntentKind.Move
                || IsStraightMovement(intent))
            {
                return requested;
            }

            IntersectionCell fullPath = IntersectionMicroGrid.MovementMask(
                intent.MovementEntry,
                intent.MovementExit);
            bool yieldsToStraight = HasOpposingStraightThreat(
                intent.Node,
                intersectionTile,
                intent.MovementEntry,
                intent.MovementExit);
            if (yieldsToStraight || IntersectionMicroGrid.Conflicts(blocking, fullPath))
            {
                return requested;
            }

            completeIntersectionOnEntry = true;
            return fullPath;
        }

        private static bool IsStraightMovement(Intent intent) =>
            intent.MovementEntry == intent.MovementExit;

        private bool HasOpposingStraightThreat(
            int turningNode,
            int intersectionTile,
            Dir turningEntry,
            Dir turningExit)
        {
            // 신호 게이트는 intent 생성 전에 continue하므로 그 대기는 _blockedTicks에
            // 포함되지 않는다. 실제 교차로 중재에서 임계까지 밀린 회전만 양보를 끝낸다.
            if (IsIntersectionStarved(turningNode)) return false;

            // The opposite tile may also contain traffic moving away. Its direction queue
            // is distinct, so only the queue entering this intersection can block the turn.
            Dir opposingEntry = Opposite(turningEntry);
            int threatIndex = intersectionTile * DirectionCount + (int)opposingEntry;
            if (!_approachingStraightThreats[threatIndex]) return false;

            IntersectionCell turningPath = IntersectionMicroGrid.MovementMask(
                turningEntry,
                turningExit);
            IntersectionCell opposingStraightPath = IntersectionMicroGrid.MovementMask(
                opposingEntry,
                opposingEntry);
            return IntersectionMicroGrid.Conflicts(turningPath, opposingStraightPath);
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
                    RoundaboutTrafficState state = _roundaboutStates[intent.TileIndex];
                    if (!state.CommitEntry(intent.Entry, (Dir)intent.RingIndex, intent.Node))
                    {
                        _blockedTicks[intent.Node]++;
                        return;
                    }
                    DetachHead(intent.FromQueue);
                    _movedThisTick[intent.Node] = true;
                    _blockedTicks[intent.Node] = 0;
                    return;
                case IntentKind.RoundaboutArmEntry:
                    if (!CanAcceptNormally(intent.ToQueue))
                    {
                        _blockedTicks[intent.Node]++;
                        return;
                    }
                    RoundaboutTrafficState armState = _roundaboutStates[intent.TileIndex];
                    if (!armState.CommitApproach(intent.Entry, intent.Node))
                    {
                        _blockedTicks[intent.Node]++;
                        return;
                    }
                    MoveHead(intent.FromQueue, intent.ToQueue);
                    return;
                case IntentKind.IntersectionAdvance:
                    // Conflict is the reservation mask used while crossing from the
                    // entry cell to the exit cell. It is not a persisted dwell stage.
                    _intersectionStages[intent.Node] = IntersectionStage.Exit;
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
                    int blockedTicksBeforeMove = _blockedTicks[intent.Node];
                    bool leavingIntersection = UsesSharedBudget(intent.TileIndex);
                    MoveHead(intent.FromQueue, intent.ToQueue);
                    if (leavingIntersection)
                    {
                        _clearingIntersection[intent.Node] = EncodePendingClearingIntersection(
                            intent.TileIndex,
                            intent.MovementEntry);
                    }
                    if (intent.ReservationTile != NoNode)
                    {
                        bool straightMovement = intent.MovementEntry == intent.MovementExit;
                        _intersectionStages[intent.Node] = straightMovement
                            || intent.CompleteIntersectionOnEntry
                            ? IntersectionStage.Exit
                            : IntersectionStage.Entry;
                        _intersectionMovementExits[intent.Node] = intent.MovementExit;
                        // 진입 셀은 아직 교차로 통과 완료가 아니다. 접근 중 쌓인 aging을
                        // 보존해야 starved 회전이 Entry에서 다시 임계만큼 굶지 않는다.
                        _blockedTicks[intent.Node] = blockedTicksBeforeMove;
                    }
                    else
                    {
                        _intersectionStages[intent.Node] = IntersectionStage.None;
                    }
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
                RoundaboutTrafficState state = _roundaboutStates[tile];
                int heldMask = 0;
                int handoffSide = NoNode;

                for (int cell = 0; cell < DirectionCount; cell++)
                {
                    int node = state.NodeAt((Dir)cell);
                    if (node == NoNode || _movedThisTick[node]) continue;
                    int carId = _cars[node];
                    if (routes.IsDestination(carId, position))
                    {
                        RecordArrival(carId, position);
                        state.Remove((Dir)cell);
                        ReleaseNode(node);
                        result.Arrivals++;
                        continue;
                    }
                    if (!routes.TryGetNextTile(carId, position, out Vector2Int next, out Dir exit)
                        || (int)exit != cell || !TryQueueIndex(next, exit, out int toQueue)) continue;
                    if (!CanAcceptNormally(toQueue))
                    {
                        bool sameArmHandoff = state.CanHandoffBlockedExit(
                                (Dir)cell,
                                node,
                                out int activeEntryNode)
                            && IsNodeAtTileHead(activeEntryNode, TileIndex(next));
                        if (!sameArmHandoff)
                        {
                            _blockedTicks[node]++;
                            heldMask |= 1 << cell;
                            continue;
                        }

                        // Temporarily append the exiting car behind the active arm car.
                        // CollectIntents immediately admits that exact head into the now
                        // empty ring cell, restoring the arm's physical capacity in-round.
                        handoffSide = cell;
                    }
                    state.Remove((Dir)cell);
                    _movedThisTick[node] = true;
                    _blockedTicks[node] = 0;
                    AppendNode(toQueue, node);
                }

                if (handoffSide != NoNode)
                {
                    // Do not advance the ring into the vacated mouth. Only the active
                    // entrant from the exit target arm may consume this exception.
                    state.BlockEntriesExcept((Dir)handoffSide);
                    continue;
                }

                // 이탈 대기차가 있으면 링 전체가 그 차의 점유를 존중해 정지한다.
                // 이동 중에는 출발·도착 셀을 모두 예약해 대각선 원호의 진입 공백을 막는다.
                if (heldMask != 0)
                {
                    state.BlockEntries();
                    continue;
                }

                bool alreadyMoved = false;
                for (int cell = 0; cell < DirectionCount; cell++)
                {
                    int node = state.NodeAt((Dir)cell);
                    if (node != NoNode && _movedThisTick[node])
                    {
                        alreadyMoved = true;
                        break;
                    }
                }
                if (alreadyMoved)
                {
                    state.BlockEntries();
                    continue;
                }

                state.AdvanceCounterClockwise();
                for (int cell = 0; cell < DirectionCount; cell++)
                {
                    int node = state.NodeAt((Dir)cell);
                    if (node != NoNode) _movedThisTick[node] = true;
                }
            }
        }

        private bool IsNodeAtTileHead(int node, int tileIndex)
        {
            if (node == NoNode || tileIndex < 0 || tileIndex >= _roundabouts.Length)
                return false;
            int firstQueue = tileIndex * DirectionCount;
            for (int direction = 0; direction < DirectionCount; direction++)
            {
                if (_heads[firstQueue + direction] == node) return true;
            }
            return false;
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
                TileIndex = tile, Entry = entry, Exit = exit, RingIndex = NoNode,
                HighwayLane = NoNode,
                ReservationTile = NoNode, MovementEntry = entry, MovementExit = exit,
                CurrentReservationMask = IntersectionCell.None,
                ReservationMask = IntersectionCell.None,
                CompleteIntersectionOnEntry = false };

        private void RecordArrival(int carId, Vector2Int tile)
        {
            _arrivals[ArrivalCount++] = new ArrivalRecord { CarId = carId, Tile = tile };
        }

        private bool UsesSharedBudget(int tile) =>
            _intersections[tile] && !_overpasses[tile] && !_roundabouts[tile];

        private bool IsBetterForIntersection(
            Intent candidate,
            Intent current,
            int intersectionTile,
            bool useReservation)
        {
            bool candidateInside = candidate.Kind == IntentKind.IntersectionAdvance;
            bool currentInside = current.Kind == IntentKind.IntersectionAdvance;
            if (candidateInside != currentInside) return candidateInside;

            bool candidateStarved = IsIntersectionStarved(candidate.Node);
            bool currentStarved = IsIntersectionStarved(current.Node);
            if (candidateStarved != currentStarved) return candidateStarved;

            Dir candidateEntry = useReservation ? candidate.MovementEntry : candidate.Entry;
            Dir candidateExit = useReservation ? candidate.MovementExit : candidate.Exit;
            Dir currentEntry = useReservation ? current.MovementEntry : current.Entry;
            Dir currentExit = useReservation ? current.MovementExit : current.Exit;
            bool candidateStraight = candidateEntry == candidateExit;
            bool currentStraight = currentEntry == currentExit;
            if (candidateStraight != currentStraight) return candidateStraight;
            RoadAxis priority = _priorityAxes[intersectionTile];
            bool candidatePriority = priority != RoadAxis.None && Axis(candidateEntry) == priority;
            bool currentPriority = priority != RoadAxis.None && Axis(currentEntry) == priority;
            if (candidatePriority != currentPriority) return candidatePriority;
            int candidateTurn = TurnRank(candidateEntry, candidateExit);
            int currentTurn = TurnRank(currentEntry, currentExit);
            return candidateTurn != currentTurn
                ? candidateTurn < currentTurn
                : candidateEntry < currentEntry;
        }

        private bool IsIntersectionStarved(int node) =>
            node != NoNode && _blockedTicks[node] >= _gridlockValveTicks;

        private bool IsIntersectionExitBlocked(ICarRouteProvider routes, int carId, Vector2Int intersection, int tileIndex)
        {
            if (!_intersections[tileIndex] || _overpasses[tileIndex] || _roundabouts[tileIndex]
                || routes.IsDestination(carId, intersection)) return false;
            return !routes.TryGetNextTile(carId, intersection, out Vector2Int exit, out Dir exitDir)
                || !TryQueueIndex(exit, exitDir, out int exitQueue)
                || !HasClearIntersectionExit(exitQueue);
        }

        private bool HasClearIntersectionExit(int queue) =>
            _queueActive[queue] && _counts[queue] == 0;

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
        private bool CanAcceptNormally(int queue)
        {
            if (!_queueActive[queue]) return false;
            int tileIndex = queue / DirectionCount;
            if (!IsRoundaboutArm(tileIndex)) return _counts[queue] < _effectiveCapacity;

            int occupied = 0;
            int firstQueue = tileIndex * DirectionCount;
            for (int direction = 0; direction < DirectionCount; direction++)
                occupied += _counts[firstQueue + direction];
            // All directional queues share the same physical arm cell.
            return occupied < RoundaboutArmCapacity;
        }

        private void RebuildRoundaboutFootprints()
        {
            for (int centerIndex = 0; centerIndex < _roundabouts.Length; centerIndex++)
            {
                if (!_roundabouts[centerIndex]) continue;
                _roundaboutCenters[centerIndex] = centerIndex;
                Vector2Int center = TileAt(centerIndex);
                for (int side = 0; side < DirectionCount; side++)
                {
                    Vector2Int arm = center + DirectionVector((Dir)side);
                    if (!InBounds(arm)) continue;
                    int armIndex = TileIndex(arm);
                    // The whole adjacent tile is an arm footprint. Tangent traffic still shares
                    // this physical space and therefore follows the same capacity rule.
                    _roundaboutCenters[armIndex] = centerIndex;
                    _roundaboutArmSides[armIndex] = side;
                }
            }
        }

        private bool TryGetRoundaboutEntry(
            int tileIndex,
            int nextTileIndex,
            out int centerIndex,
            out Dir approachSide)
        {
            centerIndex = _roundaboutCenters[tileIndex];
            int side = _roundaboutArmSides[tileIndex];
            if (centerIndex == NoNode
                || centerIndex != nextTileIndex
                || side < 0
                || side >= DirectionCount)
            {
                approachSide = default;
                return false;
            }

            approachSide = (Dir)side;
            return true;
        }

        private bool TryGetRoundaboutApproach(
            ICarRouteProvider routes,
            int carId,
            int tileIndex,
            int nextTileIndex,
            out int centerIndex,
            out Dir approachSide)
        {
            centerIndex = nextTileIndex >= 0 && nextTileIndex < _roundaboutCenters.Length
                ? _roundaboutCenters[nextTileIndex]
                : NoNode;
            int side = nextTileIndex >= 0 && nextTileIndex < _roundaboutArmSides.Length
                ? _roundaboutArmSides[nextTileIndex]
                : NoNode;
            if (centerIndex == NoNode || side < 0 || side >= DirectionCount)
            {
                approachSide = default;
                return false;
            }

            approachSide = (Dir)side;
            Vector2Int expectedApproach = TileAt(centerIndex)
                + DirectionVector(approachSide) * 2;
            Vector2Int arm = TileAt(nextTileIndex);
            return TileAt(tileIndex) == expectedApproach
                && routes.TryGetNextTile(carId, arm, out Vector2Int afterArm, out _)
                && InBounds(afterArm)
                && TileIndex(afterArm) == centerIndex;
        }

        private bool IsRoundaboutArm(int tileIndex) =>
            tileIndex >= 0
            && tileIndex < _roundaboutArmSides.Length
            && _roundaboutArmSides[tileIndex] != NoNode;

        private static Vector2Int DirectionVector(Dir direction)
        {
            switch (direction)
            {
                case Dir.N: return Vector2Int.up;
                case Dir.E: return Vector2Int.right;
                case Dir.S: return Vector2Int.down;
                default: return Vector2Int.left;
            }
        }

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
            _intersectionStages[node] = IntersectionStage.None;
            _intersectionMovementExits[node] = default;
            _clearingIntersection[node] = NoNode;
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

        private void AdvanceIntersectionClearanceWindows()
        {
            for (int node = 0; node < _clearingIntersection.Length; node++)
            {
                int encoded = _clearingIntersection[node];
                if (encoded == NoNode) continue;
                _clearingIntersection[node] = encoded < NoNode
                    ? DecodePendingClearingIntersection(encoded)
                    : NoNode;
            }
        }

        private static int EncodePendingClearingIntersection(int tile, Dir movementEntry)
        {
            int encoded = checked(tile * DirectionCount + (int)movementEntry);
            return checked(-encoded - 2);
        }

        private static int DecodePendingClearingIntersection(int encoded) => -encoded - 2;

        private static bool TryDecodeClearingIntersection(
            int encoded,
            out int tile,
            out Dir movementEntry)
        {
            if (encoded == NoNode)
            {
                tile = NoNode;
                movementEntry = default;
                return false;
            }

            int decoded = encoded < NoNode
                ? DecodePendingClearingIntersection(encoded)
                : encoded;
            tile = decoded / DirectionCount;
            movementEntry = (Dir)(decoded % DirectionCount);
            return true;
        }
    }
}
