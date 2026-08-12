using System;
using CityFlow.Contracts;

namespace CityFlow.Sim
{
    internal sealed class RoundaboutTrafficState
    {
        private const int CellCount = 4;
        private const int NoNode = -1;

        private readonly int[] _nodes = new int[CellCount];
        private readonly bool[] _reservations = new bool[CellCount];
        private readonly bool[] _entryDemand = new bool[CellCount];
        private Dir _preferredEntry = Dir.E;
        private Dir _servingEntrySide;
        private Dir _selectedEntry;
        private bool _hasServingEntrySide;
        private bool _hasSelectedEntry;
        private bool _admittedThisTick;
        private int _activeEntryNode = NoNode;
        private Dir _activeEntrySide;
        private bool _hasBlockedEntryException;
        private Dir _blockedEntryException;

        public int OccupiedCount { get; private set; }
        public bool EntriesBlocked { get; private set; }
        public bool HasActiveEntry => _activeEntryNode != NoNode;

        public RoundaboutTrafficState()
        {
            Array.Fill(_nodes, NoNode);
        }

        public void BeginTick()
        {
            Array.Clear(_reservations, 0, _reservations.Length);
            Array.Clear(_entryDemand, 0, _entryDemand.Length);
            EntriesBlocked = false;
            _hasSelectedEntry = false;
            _admittedThisTick = false;
            _hasBlockedEntryException = false;
        }

        public int NodeAt(Dir cell) => IsValid(cell) ? _nodes[(int)cell] : NoNode;

        public void RegisterEntryDemand(Dir approachSide)
        {
            if (IsValid(approachSide)) _entryDemand[(int)approachSide] = true;
        }

        public void SelectEntrySide()
        {
            if (_activeEntryNode != NoNode)
            {
                _hasSelectedEntry = false;
                return;
            }

            if (_hasServingEntrySide)
            {
                if (_entryDemand[(int)_servingEntrySide])
                {
                    if (CanReserveMergeCells(_servingEntrySide))
                    {
                        SelectEntry(_servingEntrySide);
                    }
                    else
                    {
                        _hasSelectedEntry = false;
                    }
                    return;
                }

                _preferredEntry = NextCounterClockwise(_servingEntrySide);
                _hasServingEntrySide = false;
            }

            Dir candidate = _preferredEntry;
            for (int offset = 0; offset < CellCount; offset++)
            {
                if (_entryDemand[(int)candidate])
                {
                    _servingEntrySide = candidate;
                    _hasServingEntrySide = true;
                    if (CanReserveMergeCells(candidate))
                    {
                        SelectEntry(candidate);
                    }
                    else
                    {
                        _hasSelectedEntry = false;
                    }
                    return;
                }

                candidate = NextCounterClockwise(candidate);
            }

            _hasSelectedEntry = false;
        }

        public bool TryReserveApproach(Dir approachSide)
        {
            if (!CanReserveSelectedSide(approachSide)) return false;

            ReserveMergeCells(approachSide);
            _admittedThisTick = true;
            return true;
        }

        public bool CommitApproach(Dir approachSide, int node)
        {
            if (node < 0
                || _activeEntryNode != NoNode
                || !_admittedThisTick
                || !_hasSelectedEntry
                || approachSide != _selectedEntry)
            {
                return false;
            }

            _activeEntryNode = node;
            _activeEntrySide = approachSide;
            return true;
        }

        public bool TryReserveRingEntry(Dir approachSide, int node, out Dir target)
        {
            target = approachSide;
            bool ownsEntry = _activeEntryNode == node && _activeEntrySide == approachSide;
            bool canAdoptLegacyArm = _activeEntryNode == NoNode
                && _hasSelectedEntry
                && approachSide == _selectedEntry;
            if (node < 0
                || (!ownsEntry && !canAdoptLegacyArm)
                || !CanReserveMergeCells(approachSide))
            {
                return false;
            }

            ReserveMergeCells(approachSide);
            _admittedThisTick = true;
            return true;
        }

        public bool CommitEntry(Dir approachSide, Dir target, int node)
        {
            if (node < 0
                || !IsValid(approachSide)
                || !IsValid(target)
                || target != approachSide
                || (_activeEntryNode != NoNode && _activeEntryNode != node)
                || _nodes[(int)target] != NoNode
                || !_reservations[(int)target])
            {
                return false;
            }

            _nodes[(int)target] = node;
            _activeEntryNode = NoNode;
            _activeEntrySide = default;
            OccupiedCount++;
            _preferredEntry = NextCounterClockwise(approachSide);
            _hasServingEntrySide = false;
            _hasSelectedEntry = false;
            return true;
        }

        public int Remove(Dir cell)
        {
            if (!IsValid(cell)) return NoNode;

            int index = (int)cell;
            int node = _nodes[index];
            if (node == NoNode) return NoNode;

            _nodes[index] = NoNode;
            OccupiedCount--;
            return node;
        }

        public bool RemoveNodeForRescue(int node)
        {
            bool removed = false;
            for (int cell = 0; cell < CellCount; cell++)
            {
                if (_nodes[cell] != node) continue;
                _nodes[cell] = NoNode;
                OccupiedCount--;
                removed = true;
                break;
            }

            if (_activeEntryNode == node)
            {
                _preferredEntry = NextCounterClockwise(_activeEntrySide);
                _activeEntryNode = NoNode;
                _activeEntrySide = default;
                _hasServingEntrySide = false;
                _hasSelectedEntry = false;
                removed = true;
            }

            if (!removed) return false;

            // Rescue runs after a completed service step. Clear every ephemeral
            // grant owned by the removed node so the next tick cannot inherit a
            // phantom mouth reservation or admission.
            Array.Clear(_reservations, 0, _reservations.Length);
            _admittedThisTick = false;
            _hasBlockedEntryException = false;
            return true;
        }

        public void AdvanceCounterClockwise()
        {
            int north = _nodes[(int)Dir.N];
            int east = _nodes[(int)Dir.E];
            int south = _nodes[(int)Dir.S];
            int west = _nodes[(int)Dir.W];

            ReserveTransition(Dir.W, north);
            ReserveTransition(Dir.N, east);
            ReserveTransition(Dir.E, south);
            ReserveTransition(Dir.S, west);

            _nodes[(int)Dir.N] = east;
            _nodes[(int)Dir.W] = north;
            _nodes[(int)Dir.S] = west;
            _nodes[(int)Dir.E] = south;
        }

        public void BlockEntries()
        {
            EntriesBlocked = true;
            _hasBlockedEntryException = false;
        }

        public void BlockEntriesExcept(Dir approachSide)
        {
            EntriesBlocked = true;
            _hasBlockedEntryException = IsValid(approachSide);
            _blockedEntryException = approachSide;
        }

        public bool TryPrepareBlockedExitHandoff(
            Dir exitSide,
            int exitingNode,
            int waitingEntryNode)
        {
            bool ownsEntry =
                _activeEntryNode == waitingEntryNode &&
                _activeEntrySide == exitSide;
            bool canAdoptArmOrigin =
                _activeEntryNode == NoNode &&
                waitingEntryNode != NoNode;
            if (!IsValid(exitSide)
                || exitingNode < 0
                || waitingEntryNode < 0
                || _nodes[(int)exitSide] != exitingNode
                || (!ownsEntry && !canAdoptArmOrigin)
                || EntriesBlocked
                || _admittedThisTick)
            {
                return false;
            }

            // The exiting car owns exitSide until RoadQueueNetwork moves it to
            // the arm. The adjacent upstream cell must still satisfy the normal
            // mouth-clearance rule for the waiting arm car to enter immediately.
            if (!IsAvailable(UpstreamOf(exitSide))
                || !IsAvailable(DownstreamOf(exitSide)))
            {
                return false;
            }

            if (canAdoptArmOrigin)
            {
                _activeEntryNode = waitingEntryNode;
                _activeEntrySide = exitSide;
            }

            return true;
        }

        public bool IsReserved(Dir cell) => IsValid(cell) && _reservations[(int)cell];

        public static float Progress01(Dir entry, Dir exit, Dir currentCell)
        {
            Dir entryCell = Opposite(entry);
            int totalSteps = CounterClockwiseSteps(entryCell, exit);
            int currentSteps = CounterClockwiseSteps(entryCell, currentCell);
            return currentSteps <= totalSteps
                ? (currentSteps + 1f) / (totalSteps + 1f)
                : -1f;
        }

        public void Clear()
        {
            Array.Fill(_nodes, NoNode);
            Array.Clear(_reservations, 0, _reservations.Length);
            Array.Clear(_entryDemand, 0, _entryDemand.Length);
            OccupiedCount = 0;
            EntriesBlocked = false;
            _preferredEntry = Dir.E;
            _servingEntrySide = default;
            _hasServingEntrySide = false;
            _hasSelectedEntry = false;
            _admittedThisTick = false;
            _activeEntryNode = NoNode;
            _activeEntrySide = default;
            _hasBlockedEntryException = false;
            _blockedEntryException = default;
        }

        private void SelectEntry(Dir side)
        {
            _selectedEntry = side;
            _hasSelectedEntry = true;
        }

        private bool CanReserveSelectedSide(Dir approachSide) =>
            _activeEntryNode == NoNode
            && _hasSelectedEntry
            && approachSide == _selectedEntry
            && CanReserveMergeCells(approachSide);

        private bool CanReserveMergeCells(Dir approachSide)
        {
            bool blocked = EntriesBlocked
                && (!_hasBlockedEntryException || approachSide != _blockedEntryException);
            // 논리적으로 반대편인 두 셀도 실제 베지어 주행 경로에서는 서로 교차할 수 있다.
            // 차량 한 대가 출구 전이까지 끝내기 전에 다음 차량을 들이면 화면에서 관통하므로,
            // 로터리 내부 소유권은 중심당 한 대로 직렬화한다. 같은 팔 handoff는 기존 차를
            // 먼저 arm으로 이동시켜 OccupiedCount가 0이 된 뒤 이 예외를 소비한다.
            if (!IsValid(approachSide)
                || blocked
                || _admittedThisTick
                || OccupiedCount != 0)
            {
                return false;
            }
            Dir immediateUpstream = UpstreamOf(approachSide);
            Dir immediateDownstream = DownstreamOf(approachSide);
            return IsAvailable(approachSide)
                && IsAvailable(immediateUpstream)
                && IsAvailable(immediateDownstream);
        }

        private void ReserveMergeCells(Dir approachSide)
        {
            Dir immediateUpstream = UpstreamOf(approachSide);
            Dir immediateDownstream = DownstreamOf(approachSide);
            Reserve(approachSide);
            Reserve(immediateUpstream);
            Reserve(immediateDownstream);
        }

        private bool IsAvailable(Dir cell)
        {
            int index = (int)cell;
            return _nodes[index] == NoNode && !_reservations[index];
        }

        private void ReserveTransition(Dir to, int node)
        {
            if (node == NoNode) return;
            Reserve(to);
        }

        private void Reserve(Dir cell) => _reservations[(int)cell] = true;

        private static Dir Opposite(Dir direction) => (Dir)(((int)direction + 2) % CellCount);

        private static Dir UpstreamOf(Dir target) => (Dir)(((int)target + 1) % CellCount);

        private static Dir DownstreamOf(Dir target) =>
            (Dir)(((int)target + CellCount - 1) % CellCount);

        private static Dir NextCounterClockwise(Dir direction) =>
            (Dir)(((int)direction + CellCount - 1) % CellCount);

        private static int CounterClockwiseSteps(Dir from, Dir to) =>
            ((int)from - (int)to + CellCount) % CellCount;

        private static bool IsValid(Dir cell) => (int)cell >= 0 && (int)cell < CellCount;
    }

    // Unity integration: RoadQueueNetwork owns this pure simulation state; no scene component is required.
}
