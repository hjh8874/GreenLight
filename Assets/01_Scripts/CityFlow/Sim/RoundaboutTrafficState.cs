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
        private Dir _selectedEntry;
        private bool _hasSelectedEntry;
        private bool _admittedThisTick;
        private int _activeEntryNode = NoNode;
        private Dir _activeEntrySide;

        public int OccupiedCount { get; private set; }
        public bool EntriesBlocked { get; private set; }

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

            Dir candidate = _preferredEntry;
            for (int offset = 0; offset < CellCount; offset++)
            {
                if (_entryDemand[(int)candidate])
                {
                    _selectedEntry = candidate;
                    _hasSelectedEntry = true;
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
            _preferredEntry = NextCounterClockwise(approachSide);
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
            _preferredEntry = NextCounterClockwise(approachSide);
            _activeEntryNode = NoNode;
            OccupiedCount++;
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

        public void AdvanceCounterClockwise()
        {
            int north = _nodes[(int)Dir.N];
            int east = _nodes[(int)Dir.E];
            int south = _nodes[(int)Dir.S];
            int west = _nodes[(int)Dir.W];

            ReserveTransition(Dir.N, Dir.W, north);
            ReserveTransition(Dir.E, Dir.N, east);
            ReserveTransition(Dir.S, Dir.E, south);
            ReserveTransition(Dir.W, Dir.S, west);

            _nodes[(int)Dir.N] = east;
            _nodes[(int)Dir.W] = north;
            _nodes[(int)Dir.S] = west;
            _nodes[(int)Dir.E] = south;
        }

        public void BlockEntries() => EntriesBlocked = true;

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
            _hasSelectedEntry = false;
            _admittedThisTick = false;
            _activeEntryNode = NoNode;
            _activeEntrySide = default;
        }

        private bool CanReserveSelectedSide(Dir approachSide) =>
            _activeEntryNode == NoNode
            && _hasSelectedEntry
            && approachSide == _selectedEntry
            && CanReserveMergeCells(approachSide);

        private bool CanReserveMergeCells(Dir approachSide)
        {
            if (!IsValid(approachSide) || EntriesBlocked || _admittedThisTick) return false;
            Dir immediateUpstream = UpstreamOf(approachSide);
            Dir approachingUpstream = UpstreamOf(immediateUpstream);
            return IsAvailable(approachSide)
                && IsAvailable(immediateUpstream)
                && IsAvailable(approachingUpstream);
        }

        private void ReserveMergeCells(Dir approachSide)
        {
            Dir immediateUpstream = UpstreamOf(approachSide);
            Dir approachingUpstream = UpstreamOf(immediateUpstream);
            Reserve(approachSide);
            Reserve(immediateUpstream);
            Reserve(approachingUpstream);
        }

        private bool IsAvailable(Dir cell)
        {
            int index = (int)cell;
            return _nodes[index] == NoNode && !_reservations[index];
        }

        private void ReserveTransition(Dir from, Dir to, int node)
        {
            if (node == NoNode) return;
            Reserve(from);
            Reserve(to);
        }

        private void Reserve(Dir cell) => _reservations[(int)cell] = true;

        private static Dir Opposite(Dir direction) => (Dir)(((int)direction + 2) % CellCount);

        private static Dir UpstreamOf(Dir target) => (Dir)(((int)target + 1) % CellCount);

        private static Dir NextCounterClockwise(Dir direction) =>
            (Dir)(((int)direction + CellCount - 1) % CellCount);

        private static int CounterClockwiseSteps(Dir from, Dir to) =>
            ((int)from - (int)to + CellCount) % CellCount;

        private static bool IsValid(Dir cell) => (int)cell >= 0 && (int)cell < CellCount;
    }

    // Unity integration: RoadQueueNetwork owns this pure simulation state; no scene component is required.
}
