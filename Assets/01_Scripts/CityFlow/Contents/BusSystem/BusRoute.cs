using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Content.Transit
{
    public enum BusRouteState
    {
        Idle = 0,
        Moving = 1,
        WaitingAtStop = 2,
        Completed = 3,
        RouteUnavailable = 4
    }

    /// <summary>
    /// 일반 버스와 스쿨버스가 공통으로 사용하는
    /// 도로 경로 탐색 및 운행 엔진입니다.
    /// </summary>
    public sealed class BusRoute :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        private static readonly Vector2Int[] Directions =
        {
            Vector2Int.up,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.left
        };

        [Header("그리드 크기")]

        [SerializeField]
        [Min(1)]
        private int gridWidth =
            GridUtil.DefaultWidth;

        [SerializeField]
        [Min(1)]
        private int gridHeight =
            GridUtil.DefaultHeight;

        [Header("운행 설정")]

        [SerializeField]
        [Min(0.01f)]
        [Tooltip("도로 타일 한 칸을 이동하는 데 걸리는 시간입니다.")]
        private float secondsPerTile = 0.2f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("정류장에서 대기하는 시간입니다.")]
        private float stopWaitSeconds = 2f;

        [SerializeField]
        [Tooltip("마지막 정류장 이후 첫 정류장으로 돌아갑니다.")]
        private bool loopRoute = true;

        [SerializeField]
        [Tooltip("초기화 후 설정된 노선이 있으면 자동 운행합니다.")]
        private bool autoStart;

        [Header("시간 처리")]

        [SerializeField]
        [Tooltip(
            "활성화하면 Unity Update에서 운행합니다. " +
            "공용 Simulation Tick에 연결할 경우 해제합니다.")]
        private bool useUnityUpdate = true;

        private readonly List<Vector2Int>
            stops = new();

        private readonly List<Vector2Int>
            currentRoadPath = new();

        private readonly Queue<Vector2Int>
            searchQueue = new();

        private readonly Dictionary<Vector2Int, Vector2Int>
            cameFrom = new();

        private readonly HashSet<Vector2Int>
            visited = new();

        private IReadOnlyTileData tileData;

        private int currentStopIndex;
        private int currentRoadPathIndex;

        private float moveTimer;
        private float waitTimer;

        private bool isInitialized;
        private bool routeRequested;

        public IReadOnlyList<Vector2Int> Stops =>
            stops;

        public IReadOnlyList<Vector2Int> CurrentRoadPath =>
            currentRoadPath;

        public BusRouteState State { get; private set; } =
            BusRouteState.Idle;

        public Vector2Int CurrentTile { get; private set; }

        public Vector2Int CurrentStop =>
            GetCurrentStop();

        public Vector2Int NextStop =>
            GetNextStop();

        public int CurrentStopIndex =>
            currentStopIndex;

        public int CurrentRoadPathIndex =>
            currentRoadPathIndex;

        public bool IsInitialized =>
            isInitialized;

        public bool IsOperating =>
            State == BusRouteState.Moving ||
            State == BusRouteState.WaitingAtStop;

        public float WaitRemaining =>
            Mathf.Max(0f, waitTimer);

        public bool LoopRoute
        {
            get => loopRoute;
            set => loopRoute = value;
        }

        public float SecondsPerTile
        {
            get => secondsPerTile;

            set => secondsPerTile =
                Mathf.Max(0.01f, value);
        }

        public float StopWaitSeconds
        {
            get => stopWaitSeconds;

            set => stopWaitSeconds =
                Mathf.Max(0f, value);
        }

        public event Action<Vector2Int> TileChanged;

        public event Action<Vector2Int, int>
            StopArrived;

        public event Action RouteStarted;
        public event Action RouteCompleted;
        public event Action RouteUnavailable;

        public void Initialize(
            CityFlowServices services)
        {
            if (!isActiveAndEnabled ||
                isInitialized)
            {
                return;
            }

            if (services == null ||
                services.TileData == null)
            {
                Debug.LogError(
                    "[BusRoute] CityFlowServices 또는 TileData가 없습니다.",
                    this);

                return;
            }

            tileData = services.TileData;
            isInitialized = true;

            if (autoStart &&
                stops.Count >= 2)
            {
                StartRoute();
            }
        }

        private void Update()
        {
            if (!useUnityUpdate)
            {
                return;
            }

            Tick(Time.deltaTime);
        }

        /// <summary>
        /// 공용 Simulation Tick에서도 호출할 수 있습니다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!isInitialized ||
                !routeRequested ||
                deltaTime <= 0f)
            {
                return;
            }

            switch (State)
            {
                case BusRouteState.Moving:
                    TickMoving(deltaTime);
                    break;

                case BusRouteState.WaitingAtStop:
                    TickWaiting(deltaTime);
                    break;
            }
        }

        public bool ConfigureRoute(
            IReadOnlyList<Vector2Int> newStops,
            bool shouldLoop)
        {
            StopRoute();

            stops.Clear();

            if (newStops == null)
            {
                return false;
            }

            for (int i = 0;
                 i < newStops.Count;
                 i++)
            {
                Vector2Int stop =
                    newStops[i];

                if (stops.Count > 0 &&
                    stops[stops.Count - 1] == stop)
                {
                    continue;
                }

                stops.Add(stop);
            }

            loopRoute = shouldLoop;

            if (stops.Count == 0)
            {
                return false;
            }

            currentStopIndex = 0;
            currentRoadPathIndex = 0;

            CurrentTile = stops[0];

            State = BusRouteState.Idle;

            TileChanged?.Invoke(CurrentTile);

            return stops.Count >= 2;
        }

        public bool StartRoute()
        {
            if (!isInitialized)
            {
                Debug.LogWarning(
                    "[BusRoute] 초기화되지 않아 운행을 시작할 수 없습니다.",
                    this);

                return false;
            }

            if (stops.Count < 2)
            {
                Debug.LogWarning(
                    "[BusRoute] 정류장이 최소 2개 필요합니다.",
                    this);

                return false;
            }

            routeRequested = true;

            currentStopIndex = 0;
            currentRoadPathIndex = 0;

            CurrentTile = stops[0];

            moveTimer = 0f;
            waitTimer = 0f;

            TileChanged?.Invoke(CurrentTile);

            if (!BuildPathToNextStop())
            {
                return false;
            }

            RouteStarted?.Invoke();

            return true;
        }

        public void StopRoute()
        {
            routeRequested = false;

            moveTimer = 0f;
            waitTimer = 0f;

            currentRoadPath.Clear();
            currentRoadPathIndex = 0;

            State = BusRouteState.Idle;
        }

        public bool RestartRoute()
        {
            StopRoute();

            return StartRoute();
        }

        public bool RebuildCurrentSegment()
        {
            if (!routeRequested ||
                stops.Count < 2)
            {
                return false;
            }

            return BuildPathToNextStop();
        }

        private void TickMoving(float deltaTime)
        {
            moveTimer +=
                Mathf.Max(0f, deltaTime);

            float safeSecondsPerTile =
                Mathf.Max(
                    0.01f,
                    secondsPerTile);

            while (moveTimer >= safeSecondsPerTile &&
                   State == BusRouteState.Moving)
            {
                moveTimer -= safeSecondsPerTile;

                MoveOneTile();
            }
        }

        private void MoveOneTile()
        {
            if (currentRoadPath.Count == 0)
            {
                SetRouteUnavailable();
                return;
            }

            if (currentRoadPathIndex >=
                currentRoadPath.Count - 1)
            {
                ArriveAtNextStop();
                return;
            }

            currentRoadPathIndex++;

            CurrentTile =
                currentRoadPath[
                    currentRoadPathIndex];

            TileChanged?.Invoke(CurrentTile);

            if (currentRoadPathIndex >=
                currentRoadPath.Count - 1)
            {
                ArriveAtNextStop();
            }
        }

        private void ArriveAtNextStop()
        {
            int nextIndex =
                GetNextStopIndex();

            if (nextIndex < 0)
            {
                CompleteRoute();
                return;
            }

            currentStopIndex = nextIndex;
            CurrentTile = stops[currentStopIndex];

            currentRoadPath.Clear();
            currentRoadPathIndex = 0;

            TileChanged?.Invoke(CurrentTile);

            StopArrived?.Invoke(
                CurrentTile,
                currentStopIndex);

            waitTimer =
                Mathf.Max(
                    0f,
                    stopWaitSeconds);

            State =
                BusRouteState.WaitingAtStop;

            if (waitTimer <= 0f)
            {
                ContinueAfterWait();
            }
        }

        private void TickWaiting(float deltaTime)
        {
            waitTimer -=
                Mathf.Max(0f, deltaTime);

            if (waitTimer <= 0f)
            {
                ContinueAfterWait();
            }
        }

        private void ContinueAfterWait()
        {
            waitTimer = 0f;

            int nextIndex =
                GetNextStopIndex();

            if (nextIndex < 0)
            {
                CompleteRoute();
                return;
            }

            BuildPathToNextStop();
        }

        private bool BuildPathToNextStop()
        {
            int nextStopIndex =
                GetNextStopIndex();

            if (nextStopIndex < 0)
            {
                CompleteRoute();
                return false;
            }

            Vector2Int startStop =
                stops[currentStopIndex];

            Vector2Int destinationStop =
                stops[nextStopIndex];

            if (!TryFindAccessRoad(
                    startStop,
                    out Vector2Int startRoad) ||
                !TryFindAccessRoad(
                    destinationStop,
                    out Vector2Int destinationRoad))
            {
                Debug.LogWarning(
                    $"[BusRoute] 정류장 근처 도로를 찾지 못했습니다. " +
                    $"출발: {startStop}, 목적지: {destinationStop}",
                    this);

                SetRouteUnavailable();

                return false;
            }

            if (!TryFindRoadPath(
                    startRoad,
                    destinationRoad,
                    currentRoadPath))
            {
                Debug.LogWarning(
                    $"[BusRoute] 연결된 도로 경로가 없습니다. " +
                    $"출발 도로: {startRoad}, " +
                    $"목적지 도로: {destinationRoad}",
                    this);

                SetRouteUnavailable();

                return false;
            }

            if (currentRoadPath.Count == 0 ||
                currentRoadPath[0] != startStop)
            {
                currentRoadPath.Insert(
                    0,
                    startStop);
            }

            if (currentRoadPath[
                    currentRoadPath.Count - 1] !=
                destinationStop)
            {
                currentRoadPath.Add(
                    destinationStop);
            }

            currentRoadPathIndex = 0;
            CurrentTile = currentRoadPath[0];

            moveTimer = 0f;

            State = BusRouteState.Moving;

            TileChanged?.Invoke(CurrentTile);

            return true;
        }

        private bool TryFindAccessRoad(
            Vector2Int stopTile,
            out Vector2Int roadTile)
        {
            if (IsRoad(stopTile))
            {
                roadTile = stopTile;
                return true;
            }

            TileType stopType =
                tileData.GetTileType(stopTile);

            Vector2Int footprint =
                TileFootprint.IsBuilding(stopType)
                    ? tileData.GetFootprintSize(stopType)
                    : Vector2Int.one;

            for (int y = 0;
                 y < footprint.y;
                 y++)
            {
                for (int x = 0;
                     x < footprint.x;
                     x++)
                {
                    Vector2Int footprintTile =
                        stopTile +
                        new Vector2Int(x, y);

                    for (int i = 0;
                         i < Directions.Length;
                         i++)
                    {
                        Vector2Int candidate =
                            footprintTile +
                            Directions[i];

                        if (IsRoad(candidate))
                        {
                            roadTile = candidate;
                            return true;
                        }
                    }
                }
            }

            roadTile = default;

            return false;
        }

        private bool TryFindRoadPath(
            Vector2Int start,
            Vector2Int destination,
            List<Vector2Int> result)
        {
            result.Clear();
            searchQueue.Clear();
            cameFrom.Clear();
            visited.Clear();

            if (!IsRoad(start) ||
                !IsRoad(destination))
            {
                return false;
            }

            searchQueue.Enqueue(start);
            visited.Add(start);

            bool found =
                start == destination;

            while (searchQueue.Count > 0 &&
                   !found)
            {
                Vector2Int current =
                    searchQueue.Dequeue();

                for (int i = 0;
                     i < Directions.Length;
                     i++)
                {
                    Vector2Int next =
                        current + Directions[i];

                    if (!IsRoad(next) ||
                        !visited.Add(next))
                    {
                        continue;
                    }

                    cameFrom[next] = current;

                    if (next == destination)
                    {
                        found = true;
                        break;
                    }

                    searchQueue.Enqueue(next);
                }
            }

            if (!found)
            {
                return false;
            }

            Vector2Int pathTile =
                destination;

            result.Add(pathTile);

            while (pathTile != start)
            {
                if (!cameFrom.TryGetValue(
                        pathTile,
                        out Vector2Int previous))
                {
                    result.Clear();

                    return false;
                }

                pathTile = previous;
                result.Add(pathTile);
            }

            result.Reverse();

            return true;
        }

        private bool IsRoad(Vector2Int tile)
        {
            if (!IsInsideGrid(tile) ||
                tileData == null)
            {
                return false;
            }

            return
                tileData.GetTileType(tile) ==
                TileType.Road;
        }

        private bool IsInsideGrid(
            Vector2Int tile)
        {
            return
                tile.x >= 0 &&
                tile.y >= 0 &&
                tile.x < gridWidth &&
                tile.y < gridHeight;
        }

        private int GetNextStopIndex()
        {
            if (stops.Count < 2)
            {
                return -1;
            }

            int nextIndex =
                currentStopIndex + 1;

            if (nextIndex < stops.Count)
            {
                return nextIndex;
            }

            return loopRoute
                ? 0
                : -1;
        }

        private Vector2Int GetCurrentStop()
        {
            if (currentStopIndex < 0 ||
                currentStopIndex >= stops.Count)
            {
                return default;
            }

            return stops[currentStopIndex];
        }

        private Vector2Int GetNextStop()
        {
            int nextIndex =
                GetNextStopIndex();

            return nextIndex >= 0
                ? stops[nextIndex]
                : default;
        }

        private void CompleteRoute()
        {
            routeRequested = false;
            waitTimer = 0f;

            State = BusRouteState.Completed;

            RouteCompleted?.Invoke();
        }

        private void SetRouteUnavailable()
        {
            routeRequested = false;

            moveTimer = 0f;
            waitTimer = 0f;

            State =
                BusRouteState.RouteUnavailable;

            RouteUnavailable?.Invoke();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            gridWidth =
                Mathf.Max(1, gridWidth);

            gridHeight =
                Mathf.Max(1, gridHeight);

            secondsPerTile =
                Mathf.Max(
                    0.01f,
                    secondsPerTile);

            stopWaitSeconds =
                Mathf.Max(
                    0f,
                    stopWaitSeconds);
        }
#endif
    }
}