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

    public sealed class BusRoute : MonoBehaviour, ICityFlowServiceConsumer
    {
        private static readonly Vector2Int[] Directions =
        {
            Vector2Int.up,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.left
        };

        [Header("그리드 크기")]
        [SerializeField, Min(1)] private int gridWidth = GridUtil.DefaultWidth;
        [SerializeField, Min(1)] private int gridHeight = GridUtil.DefaultHeight;

        [Header("운행 설정")]
        [SerializeField, Min(0.01f)] private float secondsPerTile = 0.2f;
        [SerializeField, Min(0f)] private float stopWaitSeconds = 2f;
        [SerializeField] private bool loopRoute = true;
        [SerializeField] private bool autoStart;

        [Header("시간 처리")]
        [SerializeField] private bool useUnityUpdate = true;

        private readonly List<Vector2Int> stops = new();
        private readonly List<Vector2Int> currentRoadPath = new();
        private readonly Queue<Vector2Int> searchQueue = new();
        private readonly Dictionary<Vector2Int, Vector2Int> cameFrom = new();
        private readonly HashSet<Vector2Int> visited = new();

        private IReadOnlyTileData tileData;
        private int currentStopIndex;
        private int currentRoadPathIndex;
        private float moveTimer;
        private float waitTimer;
        private bool isInitialized;
        private bool routeRequested;

        public IReadOnlyList<Vector2Int> Stops => stops;
        public IReadOnlyList<Vector2Int> CurrentRoadPath => currentRoadPath;
        public BusRouteState State { get; private set; } = BusRouteState.Idle;
        public Vector2Int CurrentTile { get; private set; }
        public Vector2Int CurrentStop => GetCurrentStop();
        public Vector2Int NextStop => GetNextStop();
        public int CurrentStopIndex => currentStopIndex;
        public int CurrentRoadPathIndex => currentRoadPathIndex;
        public bool IsInitialized => isInitialized;
        public bool IsOperating => State == BusRouteState.Moving || State == BusRouteState.WaitingAtStop;
        public float WaitRemaining => Mathf.Max(0f, waitTimer);

        public bool LoopRoute
        {
            get => loopRoute;
            set => loopRoute = value;
        }

        public float SecondsPerTile
        {
            get => secondsPerTile;
            set => secondsPerTile = Mathf.Max(0.01f, value);
        }

        public float StopWaitSeconds
        {
            get => stopWaitSeconds;
            set => stopWaitSeconds = Mathf.Max(0f, value);
        }

        public event Action<Vector2Int> TileChanged;
        public event Action<Vector2Int, int> StopArrived;
        public event Action<BusRouteState> StateChanged;
        public event Action RouteStarted;
        public event Action RouteCompleted;
        public event Action RouteUnavailable;

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled || isInitialized)
            {
                return;
            }

            if (services == null || services.TileData == null)
            {
                Debug.LogError("[BusRoute] CityFlowServices 또는 TileData가 없습니다.", this);
                return;
            }

            tileData = services.TileData;
            isInitialized = true;

            if (autoStart && stops.Count >= 2)
            {
                StartRoute();
            }
        }

        private void Update()
        {
            if (useUnityUpdate)
            {
                Tick(Time.deltaTime);
            }
        }

        public void Tick(float deltaTime)
        {
            if (!isInitialized || !routeRequested || deltaTime <= 0f)
            {
                return;
            }

            if (State == BusRouteState.Moving)
            {
                TickMoving(deltaTime);
            }
            else if (State == BusRouteState.WaitingAtStop)
            {
                TickWaiting(deltaTime);
            }
        }

        public bool ConfigureRoute(IReadOnlyList<Vector2Int> newStops, bool shouldLoop)
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

                if (stops.Count == 0 || stops[stops.Count - 1] != stop)
                {
                    stops.Add(stop);
                }
            }

            loopRoute = shouldLoop;

            if (stops.Count == 0)
            {
                return false;
            }

            currentStopIndex = 0;
            currentRoadPathIndex = 0;
            CurrentTile = stops[0];
            SetState(BusRouteState.Idle);
            TileChanged?.Invoke(CurrentTile);

            return stops.Count >= 2;
        }

        public bool StartRoute()
        {
            if (!isInitialized || stops.Count < 2)
            {
                Debug.LogWarning("[BusRoute] 초기화 또는 정류장 설정을 확인해 주세요.", this);
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
            SetState(BusRouteState.Idle);
        }

        public bool RestartRoute()
        {
            StopRoute();
            return StartRoute();
        }

        public bool RebuildCurrentSegment()
        {
            return routeRequested && stops.Count >= 2 && BuildPathToNextStop();
        }

        private void TickMoving(float deltaTime)
        {
            moveTimer += Mathf.Max(0f, deltaTime);
            float interval = Mathf.Max(0.01f, secondsPerTile);

            while (moveTimer >= interval && State == BusRouteState.Moving)
            {
                moveTimer -= interval;
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

            if (currentRoadPathIndex >= currentRoadPath.Count - 1)
            {
                ArriveAtNextStop();
                return;
            }

            currentRoadPathIndex++;
            CurrentTile = currentRoadPath[currentRoadPathIndex];
            TileChanged?.Invoke(CurrentTile);

            if (currentRoadPathIndex >= currentRoadPath.Count - 1)
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
            waitTimer = Mathf.Max(0f, stopWaitSeconds);

            // StopArrived보다 먼저 정차 상태를 확정합니다.
            SetState(BusRouteState.WaitingAtStop);

            TileChanged?.Invoke(CurrentTile);
            StopArrived?.Invoke(CurrentTile, currentStopIndex);

            if (waitTimer <= 0f)
            {
                ContinueAfterWait();
            }
        }

        private void TickWaiting(float deltaTime)
        {
            waitTimer -= Mathf.Max(0f, deltaTime);

            if (waitTimer <= 0f)
            {
                ContinueAfterWait();
            }
        }

        private void ContinueAfterWait()
        {
            waitTimer = 0f;

            if (GetNextStopIndex() < 0)
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
                !TryFindAccessRoad(destinationStop, out Vector2Int destinationRoad))
            {
                SetRouteUnavailable();
                return false;
            }

            if (!TryFindRoadPath(startRoad, destinationRoad, currentRoadPath))
            {
                SetRouteUnavailable();
                return false;
            }

            if (currentRoadPath.Count == 0 || currentRoadPath[0] != startStop)
            {
                currentRoadPath.Insert(0, startStop);
            }

            if (currentRoadPath[currentRoadPath.Count - 1] != destinationStop)
            {
                currentRoadPath.Add(destinationStop);
            }

            currentRoadPathIndex = 0;
            CurrentTile = currentRoadPath[0];
            moveTimer = 0f;

            SetState(BusRouteState.Moving);
            TileChanged?.Invoke(CurrentTile);

            return true;
        }

        private bool TryFindAccessRoad(Vector2Int stopTile, out Vector2Int roadTile)
        {
            if (IsRoad(stopTile))
            {
                roadTile = stopTile;
                return true;
            }

            TileType stopType = tileData.GetTileType(stopTile);
            Vector2Int footprint = TileFootprint.IsBuilding(stopType)
                ? tileData.GetFootprintSize(stopType)
                : Vector2Int.one;

            for (int y = 0; y < footprint.y; y++)
            {
                for (int x = 0; x < footprint.x; x++)
                {
                    Vector2Int footprintTile = stopTile + new Vector2Int(x, y);

                    for (int i = 0; i < Directions.Length; i++)
                    {
                        Vector2Int candidate = footprintTile + Directions[i];

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
                    Vector2Int next = current + Directions[i];

                    if (!IsRoad(next) || !visited.Add(next))
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

            Vector2Int tile = destination;
            result.Add(tile);

            while (tile != start)
            {
                if (!cameFrom.TryGetValue(tile, out Vector2Int previous))
                {
                    result.Clear();
                    return false;
                }

                tile = previous;
                result.Add(tile);
            }

            result.Reverse();
            return true;
        }

        private bool IsRoad(Vector2Int tile)
        {
            return IsInsideGrid(tile) &&
                   tileData != null &&
                   tileData.GetTileType(tile) == TileType.Road;
        }

        private bool IsInsideGrid(Vector2Int tile)
        {
            return tile.x >= 0 &&
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

            int next = currentStopIndex + 1;

            if (next < stops.Count)
            {
                return next;
            }

            return loopRoute ? 0 : -1;
        }

        private Vector2Int GetCurrentStop()
        {
            return stops.Count > 0 &&
                   currentStopIndex >= 0 &&
                   currentStopIndex < stops.Count
                ? stops[currentStopIndex]
                : default;
        }

        private Vector2Int GetNextStop()
        {
            int index = GetNextStopIndex();
            return index >= 0 ? stops[index] : default;
        }

        private void CompleteRoute()
        {
            routeRequested = false;
            currentRoadPath.Clear();
            SetState(BusRouteState.Completed);
            RouteCompleted?.Invoke();
        }

        private void SetRouteUnavailable()
        {
            routeRequested = false;
            currentRoadPath.Clear();
            SetState(BusRouteState.RouteUnavailable);
            RouteUnavailable?.Invoke();
        }

        private void SetState(BusRouteState newState)
        {
            if (State == newState)
            {
                return;
            }

            State = newState;
            StateChanged?.Invoke(State);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            gridWidth = Mathf.Max(1, gridWidth);
            gridHeight = Mathf.Max(1, gridHeight);
            secondsPerTile = Mathf.Max(0.01f, secondsPerTile);
            stopWaitSeconds = Mathf.Max(0f, stopWaitSeconds);
        }
#endif
    }
}
