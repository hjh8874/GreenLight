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
    /// 정류장 사이 도로 경로를 계산하고 버스의 가상 위치를 갱신합니다.
    ///
    /// 일반 버스는 LoopRoute를 활성화하여
    /// 마지막 정류장 다음에 첫 정류장으로 돌아갑니다.
    ///
    /// 실제 버스 프리팹은 CurrentTile과
    /// CurrentRoadPathIndex를 읽어 View에서 표시할 수 있습니다.
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
        [Min(1)]
        [SerializeField]
        private int gridWidth = GridUtil.DefaultWidth;

        [Min(1)]
        [SerializeField]
        private int gridHeight = GridUtil.DefaultHeight;

        [Header("운행 설정")]
        [Min(0.01f)]
        [Tooltip("도로 타일 한 칸을 이동하는 데 필요한 시간입니다.")]
        [SerializeField]
        private float secondsPerTile = 0.2f;

        [Min(0f)]
        [Tooltip("각 정류장에서 기다리는 시간입니다.")]
        [SerializeField]
        private float stopWaitSeconds = 2f;

        [Tooltip("마지막 정류장 도착 후 첫 정류장으로 돌아갑니다.")]
        [SerializeField]
        private bool loopRoute = true;

        [Tooltip("컴포넌트 초기화 후 자동 운행합니다.")]
        [SerializeField]
        private bool autoStart;

        private readonly List<Vector2Int> stops = new();
        private readonly List<Vector2Int> currentRoadPath = new();

        private readonly Queue<Vector2Int> searchQueue = new();
        private readonly Dictionary<Vector2Int, Vector2Int> cameFrom = new();
        private readonly HashSet<Vector2Int> visited = new();

        private CityFlowServices services;
        private IReadOnlyTileData tileData;

        private int currentStopIndex;
        private int currentRoadPathIndex;

        private float moveTimer;
        private float waitTimer;

        private bool isInitialized;
        private bool routeRequested;

        public IReadOnlyList<Vector2Int> Stops => stops;
        public IReadOnlyList<Vector2Int> CurrentRoadPath => currentRoadPath;

        public BusRouteState State { get; private set; } =
            BusRouteState.Idle;

        public Vector2Int CurrentTile { get; private set; }
        public Vector2Int CurrentStop => GetCurrentStop();
        public Vector2Int NextStop => GetNextStop();

        public int CurrentStopIndex => currentStopIndex;
        public int CurrentRoadPathIndex => currentRoadPathIndex;

        public bool LoopRoute
        {
            get => loopRoute;
            set => loopRoute = value;
        }

        public float StopWaitSeconds
        {
            get => stopWaitSeconds;
            set => stopWaitSeconds = Mathf.Max(0f, value);
        }

        public event Action<Vector2Int> TileChanged;
        public event Action<Vector2Int, int> StopArrived;
        public event Action RouteCompleted;
        public event Action RouteUnavailable;

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (isInitialized)
            {
                return;
            }

            if (services == null || services.TileData == null)
            {
                Debug.LogError(
                    "[BusRoute] CityFlowServices 또는 TileData가 없습니다.",
                    this
                );
                return;
            }

            this.services = services;
            tileData = services.TileData;
            isInitialized = true;

            if (autoStart && stops.Count >= 2)
            {
                StartRoute();
            }
        }

        private void Start()
        {
            if (isInitialized)
            {
                return;
            }

            CityBootstrap bootstrap =
                FindAnyObjectByType<CityBootstrap>();

            if (bootstrap?.Services == null)
            {
                Debug.LogWarning(
                    "[BusRoute] CityBootstrap 또는 Services를 찾지 못했습니다.",
                    this
                );
                return;
            }

            Initialize(bootstrap.Services);
        }

        private void Update()
        {
            if (!isInitialized || !routeRequested)
            {
                return;
            }

            switch (State)
            {
                case BusRouteState.Moving:
                    UpdateMoving(Time.deltaTime);
                    break;

                case BusRouteState.WaitingAtStop:
                    UpdateWaiting(Time.deltaTime);
                    break;
            }
        }

        public bool ConfigureRoute(
            IReadOnlyList<Vector2Int> newStops,
            bool shouldLoop
        )
        {
            StopRoute();

            stops.Clear();

            if (newStops == null)
            {
                return false;
            }

            for (int i = 0; i < newStops.Count; i++)
            {
                Vector2Int stop = newStops[i];

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

            CurrentTile = stops[0];
            currentStopIndex = 0;
            currentRoadPathIndex = 0;

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
                    this
                );
                return false;
            }

            if (stops.Count < 2)
            {
                Debug.LogWarning(
                    "[BusRoute] 정류장이 최소 2개 필요합니다.",
                    this
                );
                return false;
            }

            routeRequested = true;
            currentStopIndex = 0;
            CurrentTile = stops[0];

            TileChanged?.Invoke(CurrentTile);

            return BuildPathToNextStop();
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

        public bool RebuildCurrentSegment()
        {
            if (!routeRequested || stops.Count < 2)
            {
                return false;
            }

            return BuildPathToNextStop();
        }

        private void UpdateMoving(float deltaTime)
        {
            moveTimer += Mathf.Max(0f, deltaTime);

            float safeSecondsPerTile =
                Mathf.Max(0.01f, secondsPerTile);

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
                currentRoadPath[currentRoadPathIndex];

            TileChanged?.Invoke(CurrentTile);

            if (currentRoadPathIndex >=
                currentRoadPath.Count - 1)
            {
                ArriveAtNextStop();
            }
        }

        private void ArriveAtNextStop()
        {
            int nextIndex = GetNextStopIndex();

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
            StopArrived?.Invoke(CurrentTile, currentStopIndex);

            waitTimer = stopWaitSeconds;
            State = BusRouteState.WaitingAtStop;

            if (waitTimer <= 0f)
            {
                ContinueAfterWait();
            }
        }

        private void UpdateWaiting(float deltaTime)
        {
            waitTimer -= Mathf.Max(0f, deltaTime);

            if (waitTimer <= 0f)
            {
                ContinueAfterWait();
            }
        }

        private void ContinueAfterWait()
        {
            int nextIndex = GetNextStopIndex();

            if (nextIndex < 0)
            {
                CompleteRoute();
                return;
            }

            BuildPathToNextStop();
        }

        private bool BuildPathToNextStop()
        {
            int nextStopIndex = GetNextStopIndex();

            if (nextStopIndex < 0)
            {
                CompleteRoute();
                return false;
            }

            Vector2Int startStop = stops[currentStopIndex];
            Vector2Int destinationStop = stops[nextStopIndex];

            if (!TryFindAccessRoad(startStop, out Vector2Int startRoad) ||
                !TryFindAccessRoad(destinationStop, out Vector2Int endRoad))
            {
                Debug.LogWarning(
                    $"[BusRoute] 정류장 근처 도로를 찾지 못했습니다. " +
                    $"Start: {startStop}, Destination: {destinationStop}",
                    this
                );

                SetRouteUnavailable();
                return false;
            }

            if (!TryFindRoadPath(
                    startRoad,
                    endRoad,
                    currentRoadPath
                ))
            {
                Debug.LogWarning(
                    $"[BusRoute] 연결된 도로 경로를 찾지 못했습니다. " +
                    $"StartRoad: {startRoad}, EndRoad: {endRoad}",
                    this
                );

                SetRouteUnavailable();
                return false;
            }

            /*
             * 정류장 타일 자체는 도로가 아닐 수 있으므로
             * 정류장 → 접근 도로 → 도로 경로 → 정류장 순서로 구성합니다.
             */
            if (currentRoadPath.Count == 0 ||
                currentRoadPath[0] != startStop)
            {
                currentRoadPath.Insert(0, startStop);
            }

            if (currentRoadPath[currentRoadPath.Count - 1] !=
                destinationStop)
            {
                currentRoadPath.Add(destinationStop);
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
            out Vector2Int roadTile
        )
        {
            if (IsRoad(stopTile))
            {
                roadTile = stopTile;
                return true;
            }

            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int candidate =
                    stopTile + Directions[i];

                if (IsRoad(candidate))
                {
                    roadTile = candidate;
                    return true;
                }
            }

            roadTile = default;
            return false;
        }

        private bool TryFindRoadPath(
            Vector2Int start,
            Vector2Int destination,
            List<Vector2Int> result
        )
        {
            result.Clear();
            searchQueue.Clear();
            cameFrom.Clear();
            visited.Clear();

            if (!IsRoad(start) || !IsRoad(destination))
            {
                return false;
            }

            searchQueue.Enqueue(start);
            visited.Add(start);

            bool found = start == destination;

            while (searchQueue.Count > 0 && !found)
            {
                Vector2Int current = searchQueue.Dequeue();

                for (int i = 0; i < Directions.Length; i++)
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

            Vector2Int pathTile = destination;
            result.Add(pathTile);

            while (pathTile != start)
            {
                if (!cameFrom.TryGetValue(
                        pathTile,
                        out Vector2Int previous
                    ))
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
            if (!IsInsideGrid(tile) || tileData == null)
            {
                return false;
            }

            return tileData.GetTileType(tile) ==
                   TileType.Road;
        }

        private bool IsInsideGrid(Vector2Int tile)
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

            int nextIndex = currentStopIndex + 1;

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
            int nextIndex = GetNextStopIndex();

            return nextIndex >= 0
                ? stops[nextIndex]
                : default;
        }

        private void CompleteRoute()
        {
            routeRequested = false;
            State = BusRouteState.Completed;

            RouteCompleted?.Invoke();
        }

        private void SetRouteUnavailable()
        {
            routeRequested = false;
            State = BusRouteState.RouteUnavailable;

            RouteUnavailable?.Invoke();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            gridWidth = Mathf.Max(1, gridWidth);
            gridHeight = Mathf.Max(1, gridHeight);

            secondsPerTile =
                Mathf.Max(0.01f, secondsPerTile);

            stopWaitSeconds =
                Mathf.Max(0f, stopWaitSeconds);
        }
#endif
    }
}