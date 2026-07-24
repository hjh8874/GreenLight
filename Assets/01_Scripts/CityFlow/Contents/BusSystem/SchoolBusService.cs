using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Content.Transit
{
    public enum SchoolBusState
    {
        Idle = 0,
        DrivingToResidentialArea = 1,
        WaitingAtResidentialArea = 2,
        ReturningToSchool = 3,
        WaitingAtSchool = 4,
        RouteUnavailable = 5
    }

    /// <summary>
    /// 학교에서 출발하여 주거지역을 방문하고
    /// 학생을 태운 뒤 학교로 복귀하는 운행을 관리합니다.
    /// </summary>
    [RequireComponent(typeof(BusRoute))]
    public sealed class SchoolBusService :
        MonoBehaviour,
        ICityFlowServiceConsumer,
        IBusRuntimeProvider
    {
        [Header("버스 데이터")]

        [SerializeField]
        [Tooltip("SchoolBus 타입의 BusDefinitionSO를 연결합니다.")]
        private BusDefinitionSO busDefinition;

        [Header("필수 참조")]

        [SerializeField]
        private BusStopRegistry stopRegistry;

        [SerializeField]
        private BusRoute busRoute;

        [Header("노선 설정")]

        [SerializeField]
        [Min(1)]
        [Tooltip("한 번 운행에서 방문할 최대 주거지역 수입니다.")]
        private int maxResidentialStopsPerTrip = 5;

        [SerializeField]
        [Min(1)]
        [Tooltip(
            "실제 학생 수 연동 전까지 " +
            "주거지역 한 곳당 탑승시키는 학생 수입니다.")]
        private int fallbackStudentsPerResidential = 1;

        [SerializeField]
        [Min(0f)]
        [Tooltip("학교 복귀 후 다음 운행까지 기다리는 시간입니다.")]
        private float schoolWaitSeconds = 5f;

        [SerializeField]
        [Tooltip("학교에서 가까운 주거지역부터 방문합니다.")]
        private bool sortResidentialStopsByDistance = true;

        [SerializeField]
        [Tooltip("초기화 후 자동으로 운행을 시작합니다.")]
        private bool autoStart = true;

        private readonly List<Vector2Int>
            schoolRouteStops = new();

        private readonly List<Vector2Int>
            residentialBuffer = new();

        private BusRuntime runtime;

        private float schoolWaitTimer;

        private bool isInitialized;
        private bool isSubscribed;
        private bool wantsToOperate;
        private bool routeRebuildRequested;

        public SchoolBusState State { get; private set; } =
            SchoolBusState.Idle;

        public BusRuntime Runtime => runtime;
        public BusRoute Route => busRoute;

        public Vector2Int SchoolTile { get; private set; }

        public int VisitedResidentialCount { get; private set; }

        public int CurrentPassengers =>
            runtime?.CurrentPassengers ?? 0;

        public int PassengerCapacity =>
            runtime?.PassengerCapacity ?? 0;

        public bool IsInitialized =>
            isInitialized;

        public bool IsOperating =>
            busRoute != null &&
            busRoute.IsOperating;

        public IReadOnlyList<Vector2Int> CurrentRouteStops =>
            schoolRouteStops;

        public event Action RouteStarted;

        public event Action<Vector2Int, int>
            ResidentialStopVisited;

        public event Action<Vector2Int, int>
            ReturnedToSchool;

        public event Action RouteUnavailable;

        private void Reset()
        {
            busRoute = GetComponent<BusRoute>();
        }

        private void Awake()
        {
            if (busRoute == null)
            {
                busRoute = GetComponent<BusRoute>();
            }
        }

        public void Initialize(
            CityFlowServices services)
        {
            if (!isActiveAndEnabled ||
                isInitialized)
            {
                return;
            }

            if (services == null)
            {
                Debug.LogError(
                    "[SchoolBusService] CityFlowServices가 없습니다.",
                    this);

                return;
            }

            if (busDefinition == null)
            {
                Debug.LogError(
                    "[SchoolBusService] BusDefinitionSO가 연결되지 않았습니다.",
                    this);

                return;
            }

            if (!busDefinition.IsSchoolBus)
            {
                Debug.LogError(
                    "[SchoolBusService] SchoolBus 타입의 데이터를 연결해야 합니다.",
                    this);

                return;
            }

            if (stopRegistry == null)
            {
                Debug.LogError(
                    "[SchoolBusService] BusStopRegistry가 연결되지 않았습니다.",
                    this);

                return;
            }

            if (busRoute == null)
            {
                busRoute = GetComponent<BusRoute>();
            }

            if (busRoute == null)
            {
                Debug.LogError(
                    "[SchoolBusService] BusRoute가 없습니다.",
                    this);

                return;
            }

            stopRegistry.Initialize(services);
            busRoute.Initialize(services);

            if (!stopRegistry.IsInitialized ||
                !busRoute.IsInitialized)
            {
                Debug.LogError(
                    "[SchoolBusService] 버스 의존성 초기화에 실패했습니다.",
                    this);

                return;
            }

            runtime =
                new BusRuntime(busDefinition);

            busRoute.SecondsPerTile =
                busDefinition.SecondsPerTile;

            busRoute.StopWaitSeconds =
                busDefinition.StopWaitSeconds;

            isInitialized = true;

            Subscribe();

            wantsToOperate =
                autoStart &&
                runtime.IsUnlocked;

            if (wantsToOperate)
            {
                TryStartSchoolRoute();
            }
        }

        private void OnEnable()
        {
            if (isInitialized)
            {
                Subscribe();
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();

            isInitialized = false;
        }

        private void Update()
        {
            if (!isInitialized)
            {
                return;
            }

            if (routeRebuildRequested &&
                !IsOperating &&
                State != SchoolBusState.WaitingAtSchool)
            {
                routeRebuildRequested = false;

                if (wantsToOperate)
                {
                    TryStartSchoolRoute();
                }
            }

            if (State !=
                    SchoolBusState.WaitingAtSchool ||
                !wantsToOperate)
            {
                return;
            }

            schoolWaitTimer -=
                Time.deltaTime;

            if (schoolWaitTimer <= 0f)
            {
                TryStartSchoolRoute();
            }
        }

        private void Subscribe()
        {
            if (isSubscribed ||
                busRoute == null ||
                stopRegistry == null)
            {
                return;
            }

            busRoute.TileChanged +=
                OnTileChanged;

            busRoute.StopArrived +=
                OnStopArrived;

            busRoute.RouteUnavailable +=
                OnRouteUnavailable;

            busRoute.RouteCompleted +=
                OnRouteCompleted;

            stopRegistry.RegistryChanged +=
                OnRegistryChanged;

            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (busRoute != null)
            {
                busRoute.TileChanged -=
                    OnTileChanged;

                busRoute.StopArrived -=
                    OnStopArrived;

                busRoute.RouteUnavailable -=
                    OnRouteUnavailable;

                busRoute.RouteCompleted -=
                    OnRouteCompleted;
            }

            if (stopRegistry != null)
            {
                stopRegistry.RegistryChanged -=
                    OnRegistryChanged;
            }

            isSubscribed = false;
        }

        public bool StartService()
        {
            if (runtime == null ||
                !runtime.IsUnlocked)
            {
                return false;
            }

            runtime.SetServiceEnabled(true);

            wantsToOperate = true;

            return TryStartSchoolRoute();
        }

        public void StopService()
        {
            wantsToOperate = false;
            routeRebuildRequested = false;

            busRoute?.StopRoute();

            State = SchoolBusState.Idle;

            schoolWaitTimer = 0f;
            VisitedResidentialCount = 0;

            runtime?.UnloadAllPassengers();
            runtime?.SetServiceEnabled(false);
        }

        public bool TryStartSchoolRoute()
        {
            if (!isInitialized ||
                runtime == null ||
                !runtime.IsUnlocked ||
                !runtime.IsServiceEnabled ||
                IsOperating)
            {
                return false;
            }

            if (!stopRegistry.TryGetFirstSchool(
                    out Vector2Int schoolTile))
            {
                SetRouteUnavailable(
                    "[SchoolBusService] 배치된 학교가 없습니다.");

                return false;
            }

            if (stopRegistry.ResidentialStopCount == 0)
            {
                SetRouteUnavailable(
                    "[SchoolBusService] 방문할 주거지역이 없습니다.");

                return false;
            }

            SchoolTile = schoolTile;

            BuildSchoolRoute();

            if (schoolRouteStops.Count < 3)
            {
                SetRouteUnavailable(
                    "[SchoolBusService] 유효한 노선을 만들 수 없습니다.");

                return false;
            }

            bool configured =
                busRoute.ConfigureRoute(
                    schoolRouteStops,
                    false);

            if (!configured)
            {
                SetRouteUnavailable(
                    "[SchoolBusService] 노선 설정에 실패했습니다.");

                return false;
            }

            VisitedResidentialCount = 0;
            schoolWaitTimer = 0f;

            runtime.UnloadAllPassengers();
            runtime.SetState(
                BusOperatingState.Departing);

            if (!busRoute.StartRoute())
            {
                SetRouteUnavailable(
                    "[SchoolBusService] 도로 경로를 생성하지 못했습니다.");

                return false;
            }

            State =
                SchoolBusState
                    .DrivingToResidentialArea;

            runtime.SetState(
                BusOperatingState.Moving);

            runtime.SetCurrentTile(
                busRoute.CurrentTile);

            runtime.SetNextStop(
                busRoute.NextStop,
                busRoute.CurrentStopIndex);

            RouteStarted?.Invoke();

            Debug.Log(
                $"[SchoolBusService] 스쿨버스 출발. " +
                $"학교: {SchoolTile}, " +
                $"방문 주거지역: {schoolRouteStops.Count - 2}",
                this);

            return true;
        }

        private void BuildSchoolRoute()
        {
            schoolRouteStops.Clear();
            residentialBuffer.Clear();

            schoolRouteStops.Add(SchoolTile);

            IReadOnlyList<Vector2Int>
                registeredResidentialStops =
                    stopRegistry.ResidentialStops;

            for (int i = 0;
                 i < registeredResidentialStops.Count;
                 i++)
            {
                Vector2Int residentialTile =
                    registeredResidentialStops[i];

                if (residentialTile != SchoolTile)
                {
                    residentialBuffer.Add(
                        residentialTile);
                }
            }

            if (sortResidentialStopsByDistance)
            {
                residentialBuffer.Sort(
                    CompareResidentialDistance);
            }

            int visitCount =
                Mathf.Min(
                    maxResidentialStopsPerTrip,
                    residentialBuffer.Count);

            for (int i = 0;
                 i < visitCount;
                 i++)
            {
                schoolRouteStops.Add(
                    residentialBuffer[i]);
            }

            schoolRouteStops.Add(SchoolTile);
        }

        private int CompareResidentialDistance(
            Vector2Int left,
            Vector2Int right)
        {
            int leftDistance =
                ManhattanDistance(
                    SchoolTile,
                    left);

            int rightDistance =
                ManhattanDistance(
                    SchoolTile,
                    right);

            int distanceCompare =
                leftDistance.CompareTo(
                    rightDistance);

            if (distanceCompare != 0)
            {
                return distanceCompare;
            }

            int yCompare =
                left.y.CompareTo(right.y);

            return yCompare != 0
                ? yCompare
                : left.x.CompareTo(right.x);
        }

        private void OnTileChanged(
            Vector2Int tile)
        {
            runtime?.SetCurrentTile(tile);

            if (busRoute != null)
            {
                runtime?.SetNextStop(
                    busRoute.NextStop,
                    busRoute.CurrentStopIndex);
            }
        }

        private void OnStopArrived(
            Vector2Int stopTile,
            int stopIndex)
        {
            int finalIndex =
                schoolRouteStops.Count - 1;

            runtime?.SetCurrentTile(stopTile);
            runtime?.SetNextStop(
                busRoute.NextStop,
                stopIndex);

            if (stopIndex == finalIndex &&
                stopTile == SchoolTile)
            {
                HandleSchoolReturn();
                return;
            }

            if (stopIndex <= 0 ||
                stopIndex >= finalIndex)
            {
                return;
            }

            State =
                SchoolBusState
                    .WaitingAtResidentialArea;

            runtime?.SetState(
                BusOperatingState
                    .WaitingAtStop);

            VisitedResidentialCount++;

            int boardedStudents =
                runtime?.BoardPassengers(
                    fallbackStudentsPerResidential)
                ?? 0;

            ResidentialStopVisited?.Invoke(
                stopTile,
                CurrentPassengers);

            Debug.Log(
                $"[SchoolBusService] 주거지역 정차. " +
                $"Tile: {stopTile}, " +
                $"탑승: {boardedStudents}, " +
                $"현재 탑승: " +
                $"{CurrentPassengers}/{PassengerCapacity}",
                this);

            int nextIndex =
                stopIndex + 1;

            if (nextIndex >= finalIndex)
            {
                State =
                    SchoolBusState
                        .ReturningToSchool;

                runtime?.SetState(
                    BusOperatingState.Returning);
            }
            else
            {
                State =
                    SchoolBusState
                        .DrivingToResidentialArea;

                runtime?.SetState(
                    BusOperatingState.Moving);
            }
        }

        private void HandleSchoolReturn()
        {
            State =
                SchoolBusState.WaitingAtSchool;

            runtime?.SetState(
                BusOperatingState.WaitingAtStop);

            int deliveredStudents =
                runtime?.UnloadAllPassengers()
                ?? 0;

            runtime?.CompleteTrip(
                deliveredStudents);

            ReturnedToSchool?.Invoke(
                SchoolTile,
                deliveredStudents);

            Debug.Log(
                $"[SchoolBusService] 학교 복귀. " +
                $"방문 주거지역: {VisitedResidentialCount}, " +
                $"등교 학생: {deliveredStudents}",
                this);

            VisitedResidentialCount = 0;

            schoolWaitTimer =
                Mathf.Max(
                    0f,
                    schoolWaitSeconds);

            if (schoolWaitTimer <= 0f &&
                wantsToOperate)
            {
                TryStartSchoolRoute();
            }
        }

        private void OnRouteCompleted()
        {
            if (State !=
                SchoolBusState.WaitingAtSchool)
            {
                State =
                    SchoolBusState.Idle;

                runtime?.SetState(
                    BusOperatingState.Idle);
            }
        }

        private void OnRouteUnavailable()
        {
            SetRouteUnavailable(
                "[SchoolBusService] 도로가 연결되지 않아 운행을 중단했습니다.");
        }

        private void OnRegistryChanged()
        {
            routeRebuildRequested = true;

            if (!IsOperating &&
                wantsToOperate &&
                State !=
                    SchoolBusState.WaitingAtSchool)
            {
                routeRebuildRequested = false;

                TryStartSchoolRoute();
            }
        }

        private void SetRouteUnavailable(
            string message)
        {
            State =
                SchoolBusState.RouteUnavailable;

            runtime?.SetState(
                BusOperatingState.RouteUnavailable);

            Debug.LogWarning(
                message,
                this);

            RouteUnavailable?.Invoke();
        }

        private static int ManhattanDistance(
            Vector2Int first,
            Vector2Int second)
        {
            return
                Mathf.Abs(first.x - second.x) +
                Mathf.Abs(first.y - second.y);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            maxResidentialStopsPerTrip =
                Mathf.Max(
                    1,
                    maxResidentialStopsPerTrip);

            fallbackStudentsPerResidential =
                Mathf.Max(
                    1,
                    fallbackStudentsPerResidential);

            schoolWaitSeconds =
                Mathf.Max(
                    0f,
                    schoolWaitSeconds);
        }
#endif
    }
}