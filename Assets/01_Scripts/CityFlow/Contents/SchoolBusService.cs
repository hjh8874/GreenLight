using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
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
        RouteUnavailable = 5,
        WaitingForSchedule = 6
    }

    /// <summary>
    /// 학교와 주거지역을 연결하는 등교·하교 스쿨버스 운행을 관리합니다.
    /// 등교편은 주거지역에서 학생을 태워 학교로 이동하고,
    /// 하교편은 학교에서 학생을 태워 주거지역에 내려줍니다.
    /// </summary>
    [RequireComponent(typeof(BusRoute))]
    public sealed class SchoolBusService :
        MonoBehaviour,
        ICityFlowServiceConsumer,
        ISchoolBusSaveSource
    {
        [Header("필수 참조")]
        [SerializeField]
        private BusDefinitionSO definition;

        [Tooltip("한국 학교 기준 평일 등하교 운행 시간 설정입니다.")]
        [SerializeField]
        private SchoolBusScheduleSO schedule;

        [SerializeField]
        private BusStopRegistry stopRegistry;

        [SerializeField]
        private BusRoute busRoute;

        [Header("운행 설정")]
        [Min(1)]
        [SerializeField]
        private int maxResidentialStopsPerTrip = 5;

        [Min(0f)]
        [Tooltip("시간표가 없는 기존 씬에서만 사용하는 반복 대기 시간입니다.")]
        [SerializeField]
        private float schoolWaitSeconds = 5f;

        [SerializeField]
        private bool sortResidentialStopsByDistance = true;

        [SerializeField]
        private bool autoStart = true;

        private readonly List<Vector2Int> schoolRouteStops = new();
        private readonly List<Vector2Int> residentialBuffer = new();

        private CityFlowServices services;
        private IGameCalendarService calendar;
        private float schoolWaitTimer;
        private bool routeSubscribed;
        private bool registrySubscribed;
        private bool servicesSubscribed;
        private bool calendarSubscribed;
        private bool wantsToOperate;
        private bool isInitialized;
        private bool unavailableReported;
        private bool pendingScheduledRefresh;
        private long lastMorningTripDay = -1L;
        private long lastAfternoonTripDay = -1L;
        private int studentsServedThisTrip;

        public SchoolBusState State { get; private set; } =
            SchoolBusState.Idle;
        public SchoolBusTripKind CurrentTrip { get; private set; } =
            SchoolBusTripKind.None;
        public Vector2Int SchoolTile { get; private set; }
        public int VisitedResidentialCount { get; private set; }
        public int CurrentPassengers =>
            Runtime?.CurrentPassengers ?? 0;
        public BusRuntime Runtime { get; private set; }
        public IReadOnlyList<Vector2Int> RouteStops =>
            schoolRouteStops;
        public bool IsInitialized => isInitialized;
        public bool IsScheduled => schedule != null;

        public bool IsOperating =>
            busRoute != null &&
            (busRoute.State == BusRouteState.Moving ||
             busRoute.State == BusRouteState.WaitingAtStop);

        public event Action RouteStarted;
        public event Action<SchoolBusTripKind> ScheduledTripStarted;
        public event Action<Vector2Int, int> ResidentialStopVisited;
        public event Action<Vector2Int, int> ReturnedToSchool;
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

            if (stopRegistry == null)
            {
                stopRegistry =
                    FindAnyObjectByType<BusStopRegistry>();
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
            if (bootstrap?.Services != null)
            {
                Initialize(bootstrap.Services);
            }
        }

        public void Initialize(CityFlowServices cityServices)
        {
            if (isInitialized || cityServices == null)
            {
                return;
            }

            if (stopRegistry == null)
            {
                stopRegistry =
                    FindAnyObjectByType<BusStopRegistry>();
            }

            if (busRoute == null)
            {
                busRoute = GetComponent<BusRoute>();
            }

            if (cityServices.TileData == null ||
                stopRegistry == null ||
                busRoute == null)
            {
                Debug.LogError(
                    "[SchoolBusService] CityFlowServices, BusRoute, BusStopRegistry가 필요합니다.",
                    this);
                return;
            }

            if (definition != null &&
                definition.BusType != BusType.SchoolBus)
            {
                Debug.LogError(
                    "[SchoolBusService] SchoolBus 타입 BusDefinitionSO가 필요합니다.",
                    this);
                return;
            }

            services = cityServices;
            cityServices.RegisterSchoolBusSaveSource(this);
            stopRegistry.Initialize(cityServices);
            busRoute.UseRoadsideStopApproach = true;
            busRoute.RoadsideStopSetbackTiles = 1;
            busRoute.AllowUnscheduledStopArrival = false;
            busRoute.RoadsideStopFilter =
                IsResidentialStop;
            busRoute.Initialize(cityServices);

            int capacity = definition != null
                ? definition.PassengerCapacity
                : Mathf.Max(1, maxResidentialStopsPerTrip);
            Runtime = new BusRuntime(capacity);

            if (definition != null)
            {
                busRoute.SecondsPerTile =
                    definition.SecondsPerTile;
                busRoute.StopWaitSeconds =
                    definition.StopWaitSeconds;
            }

            isInitialized = true;
            Subscribe();
            BindCalendar(cityServices.GameCalendar);

            wantsToOperate = autoStart;
            if (!wantsToOperate)
            {
                State = SchoolBusState.Idle;
                return;
            }

            if (IsScheduled)
            {
                if (cityServices.Save != null)
                {
                    pendingScheduledRefresh = true;
                    SetWaitingForSchedule();
                }
                else
                {
                    RefreshScheduledOperation();
                }
            }
            else
            {
                TryStartRoute(SchoolBusTripKind.MorningCommute);
            }
        }

        private void OnEnable()
        {
            Subscribe();
            BindCalendar(services?.GameCalendar);
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (!isInitialized)
            {
                return;
            }

            if (pendingScheduledRefresh &&
                services?.Save?.IsRestoring != true)
            {
                pendingScheduledRefresh = false;
                RefreshScheduledOperation();
            }

            if (busRoute.State == BusRouteState.Moving)
            {
                int finalResidentialIndex =
                    schoolRouteStops.Count - 2;
                State = busRoute.CurrentStopIndex >=
                        finalResidentialIndex
                    ? SchoolBusState.ReturningToSchool
                    : SchoolBusState.DrivingToResidentialArea;
                Runtime?.SetState(BusOperatingState.Moving);
            }

            if (IsScheduled ||
                State != SchoolBusState.WaitingAtSchool ||
                !wantsToOperate)
            {
                return;
            }

            schoolWaitTimer -= Time.deltaTime;
            if (schoolWaitTimer <= 0f)
            {
                TryStartRoute(SchoolBusTripKind.MorningCommute);
            }
        }

        private void Subscribe()
        {
            if (!routeSubscribed && busRoute != null)
            {
                busRoute.StopArrived += OnStopArrived;
                busRoute.TileChanged += OnTileChanged;
                busRoute.RouteUnavailable += OnRouteUnavailable;
                busRoute.RouteCompleted += OnRouteCompleted;
                routeSubscribed = true;
            }

            if (!registrySubscribed && stopRegistry != null)
            {
                stopRegistry.RegistryChanged += OnRegistryChanged;
                registrySubscribed = true;
            }

            if (!servicesSubscribed && services != null)
            {
                services.GameCalendarRegistered +=
                    OnGameCalendarRegistered;
                servicesSubscribed = true;
            }
        }

        private void Unsubscribe()
        {
            if (routeSubscribed && busRoute != null)
            {
                busRoute.StopArrived -= OnStopArrived;
                busRoute.TileChanged -= OnTileChanged;
                busRoute.RouteUnavailable -= OnRouteUnavailable;
                busRoute.RouteCompleted -= OnRouteCompleted;
            }

            if (registrySubscribed && stopRegistry != null)
            {
                stopRegistry.RegistryChanged -= OnRegistryChanged;
            }

            if (servicesSubscribed && services != null)
            {
                services.GameCalendarRegistered -=
                    OnGameCalendarRegistered;
            }

            if (calendarSubscribed && calendar != null)
            {
                calendar.HourChanged -= OnHourChanged;
            }

            routeSubscribed = false;
            registrySubscribed = false;
            servicesSubscribed = false;
            calendarSubscribed = false;
        }

        private void BindCalendar(IGameCalendarService gameCalendar)
        {
            if (ReferenceEquals(calendar, gameCalendar) &&
                calendarSubscribed)
            {
                return;
            }

            if (calendarSubscribed && calendar != null)
            {
                calendar.HourChanged -= OnHourChanged;
            }

            calendar = gameCalendar;
            calendarSubscribed = false;

            if (calendar != null)
            {
                calendar.HourChanged += OnHourChanged;
                calendarSubscribed = true;
            }
        }

        public bool StartService()
        {
            wantsToOperate = true;
            pendingScheduledRefresh = false;
            return IsScheduled
                ? RefreshScheduledOperation()
                : TryStartRoute(
                    SchoolBusTripKind.MorningCommute);
        }

        public void StopService()
        {
            wantsToOperate = false;
            pendingScheduledRefresh = false;
            busRoute?.StopRoute();
            State = SchoolBusState.Idle;
            CurrentTrip = SchoolBusTripKind.None;
            schoolWaitTimer = 0f;
            VisitedResidentialCount = 0;
            studentsServedThisTrip = 0;
            Runtime?.ResetPassengers();
            Runtime?.SetState(BusOperatingState.OutOfService);
        }

        public bool TryStartSchoolRoute()
        {
            pendingScheduledRefresh = false;
            return IsScheduled
                ? RefreshScheduledOperation()
                : TryStartRoute(
                    SchoolBusTripKind.MorningCommute);
        }

        private bool RefreshScheduledOperation()
        {
            if (!wantsToOperate || IsOperating)
            {
                return false;
            }

            if (!HasRequiredBuildings())
            {
                return SetUnavailable();
            }

            if (calendar == null)
            {
                SetWaitingForSchedule();
                return false;
            }

            SchoolBusTripKind trip =
                schedule.GetEligibleTrip(
                    calendar.TotalDays,
                    calendar.Hour,
                    lastMorningTripDay,
                    lastAfternoonTripDay);

            if (trip == SchoolBusTripKind.None)
            {
                SetWaitingForSchedule();
                return false;
            }

            return TryStartRoute(trip);
        }

        private bool TryStartRoute(SchoolBusTripKind trip)
        {
            if (!HasRequiredBuildings())
            {
                return SetUnavailable();
            }

            stopRegistry.TryGetFirstSchool(out Vector2Int schoolTile);
            SchoolTile = schoolTile;
            BuildSchoolRoute();

            if (schoolRouteStops.Count < 3)
            {
                return SetUnavailable();
            }

            if (!busRoute.ConfigureRoute(
                    schoolRouteStops,
                    false))
            {
                return SetUnavailable();
            }

            VisitedResidentialCount = 0;
            studentsServedThisTrip = 0;
            Runtime?.ResetPassengers();
            schoolWaitTimer = 0f;
            unavailableReported = false;
            CurrentTrip = trip;

            if (trip == SchoolBusTripKind.AfternoonDismissal)
            {
                int demandPerStop = GetDemandPerStop();
                int studentDemand =
                    demandPerStop *
                    (schoolRouteStops.Count - 2);
                Runtime?.Board(studentDemand);
            }

            State = SchoolBusState.DrivingToResidentialArea;
            if (!busRoute.StartRoute())
            {
                CurrentTrip = SchoolBusTripKind.None;
                Runtime?.ResetPassengers();
                return SetUnavailable();
            }

            if (IsScheduled && calendar != null)
            {
                if (trip == SchoolBusTripKind.MorningCommute)
                {
                    lastMorningTripDay = calendar.TotalDays;
                }
                else if (trip ==
                         SchoolBusTripKind.AfternoonDismissal)
                {
                    lastAfternoonTripDay = calendar.TotalDays;
                }
            }

            Runtime?.SetState(BusOperatingState.Moving);
            RouteStarted?.Invoke();
            ScheduledTripStarted?.Invoke(trip);

            string tripLabel = trip ==
                SchoolBusTripKind.AfternoonDismissal
                ? "하교"
                : "등교";
            Debug.Log(
                $"[SchoolBusService] {tripLabel} 스쿨버스 출발. " +
                $"학교: {SchoolTile}, " +
                $"방문 주거지역: {schoolRouteStops.Count - 2}",
                this);
            return true;
        }

        private bool HasRequiredBuildings()
        {
            if (stopRegistry == null || busRoute == null)
            {
                return false;
            }

            return
                stopRegistry.TryGetFirstSchool(out _) &&
                stopRegistry.ResidentialStopCount > 0;
        }

        private void BuildSchoolRoute()
        {
            schoolRouteStops.Clear();
            residentialBuffer.Clear();
            schoolRouteStops.Add(SchoolTile);

            IReadOnlyList<Vector2Int> residentialStops =
                stopRegistry.ResidentialStops;
            for (int i = 0; i < residentialStops.Count; i++)
            {
                Vector2Int residentialTile =
                    residentialStops[i];
                if (residentialTile != SchoolTile)
                {
                    residentialBuffer.Add(residentialTile);
                }
            }

            if (sortResidentialStopsByDistance)
            {
                residentialBuffer.Sort(
                    CompareResidentialDistance);
            }

            int demandPerStop = GetDemandPerStop();
            int capacityStopLimit = demandPerStop > 0
                ? Mathf.CeilToInt(
                    (float)(Runtime?.PassengerCapacity ??
                            maxResidentialStopsPerTrip) /
                    demandPerStop)
                : maxResidentialStopsPerTrip;
            int visitLimit =
                Mathf.Min(
                    Mathf.Min(
                        maxResidentialStopsPerTrip,
                        Mathf.Max(1, capacityStopLimit)),
                    residentialBuffer.Count);

            if (!busRoute.TryGetAccessRoadForStop(
                    SchoolTile,
                    out Vector2Int currentRoad))
            {
                return;
            }

            int selectedStopCount = 0;
            bool preventImmediateReverse = false;
            Vector2Int forbiddenFirstStep = default;
            for (int i = 0;
                 i < residentialBuffer.Count &&
                 selectedStopCount < visitLimit;
                 i++)
            {
                Vector2Int residentialTile =
                    residentialBuffer[i];
                if (!busRoute.TryFindReachableRoadsideStop(
                        currentRoad,
                        residentialTile,
                        preventImmediateReverse,
                        forbiddenFirstStep,
                        out Vector2Int arrivalRoad,
                        out Vector2Int arrivalPreviousRoad) ||
                    !busRoute.CanReachStopFromRoad(
                        arrivalRoad,
                        SchoolTile,
                        true,
                        arrivalPreviousRoad))
                {
                    continue;
                }

                schoolRouteStops.Add(residentialTile);
                currentRoad = arrivalRoad;
                forbiddenFirstStep =
                    arrivalPreviousRoad;
                preventImmediateReverse = true;
                selectedStopCount++;
            }

            schoolRouteStops.Add(SchoolTile);
        }

        private int GetDemandPerStop()
        {
            return definition != null
                ? definition.BoardingDemandPerStop
                : 1;
        }

        private int CompareResidentialDistance(
            Vector2Int left,
            Vector2Int right)
        {
            int leftDistance =
                ManhattanDistance(SchoolTile, left);
            int rightDistance =
                ManhattanDistance(SchoolTile, right);
            int distanceCompare =
                leftDistance.CompareTo(rightDistance);
            if (distanceCompare != 0)
            {
                return distanceCompare;
            }

            int yCompare = left.y.CompareTo(right.y);
            return yCompare != 0
                ? yCompare
                : left.x.CompareTo(right.x);
        }

        private bool IsResidentialStop(
            Vector2Int stopTile)
        {
            return stopRegistry != null &&
                   stopRegistry.ContainsResidentialStop(
                       stopTile);
        }

        private void OnStopArrived(
            Vector2Int stopTile,
            int stopIndex)
        {
            int finalIndex = schoolRouteStops.Count - 1;
            if (stopIndex == finalIndex &&
                stopTile == SchoolTile)
            {
                HandleSchoolReturn();
                return;
            }

            if (stopIndex <= 0 || stopIndex >= finalIndex)
            {
                return;
            }

            State = SchoolBusState.WaitingAtResidentialArea;
            VisitedResidentialCount++;

            int changedStudents;
            if (CurrentTrip ==
                SchoolBusTripKind.AfternoonDismissal)
            {
                changedStudents =
                    Runtime?.Leave(GetDemandPerStop()) ?? 0;
            }
            else
            {
                changedStudents =
                    Runtime?.Board(GetDemandPerStop()) ?? 0;
            }

            studentsServedThisTrip += changedStudents;
            Runtime?.CompleteStop();
            Runtime?.SetState(BusOperatingState.WaitingAtStop);
            ResidentialStopVisited?.Invoke(
                stopTile,
                CurrentPassengers);

            string actionLabel = CurrentTrip ==
                SchoolBusTripKind.AfternoonDismissal
                ? "하차"
                : "탑승";
            Debug.Log(
                $"[SchoolBusService] 주거지역 정차. " +
                $"Tile: {stopTile}, 학생 {actionLabel}: " +
                $"{changedStudents}, 현재 탑승: {CurrentPassengers}",
                this);

            State = SchoolBusState.DrivingToResidentialArea;
        }

        private void HandleSchoolReturn()
        {
            SchoolBusTripKind completedTrip = CurrentTrip;
            State = SchoolBusState.WaitingAtSchool;

            if (completedTrip ==
                SchoolBusTripKind.MorningCommute)
            {
                Runtime?.Leave(CurrentPassengers);
            }
            else
            {
                Runtime?.Leave(CurrentPassengers);
            }

            Runtime?.CompleteStop();
            Runtime?.SetState(BusOperatingState.WaitingAtStop);
            ReturnedToSchool?.Invoke(
                SchoolTile,
                studentsServedThisTrip);

            string tripLabel = completedTrip ==
                SchoolBusTripKind.AfternoonDismissal
                ? "하교"
                : "등교";
            Debug.Log(
                $"[SchoolBusService] {tripLabel} 운행 완료. " +
                $"방문 주거지역: {VisitedResidentialCount}, " +
                $"수송 학생: {studentsServedThisTrip}",
                this);

            VisitedResidentialCount = 0;
            studentsServedThisTrip = 0;
            CurrentTrip = SchoolBusTripKind.None;

            if (IsScheduled)
            {
                SetWaitingForSchedule();
                return;
            }

            schoolWaitTimer =
                Mathf.Max(0f, schoolWaitSeconds);
            if (schoolWaitTimer <= 0f && wantsToOperate)
            {
                TryStartRoute(
                    SchoolBusTripKind.MorningCommute);
            }
        }

        private void OnRouteCompleted()
        {
            if (State == SchoolBusState.WaitingAtSchool ||
                State == SchoolBusState.WaitingForSchedule)
            {
                return;
            }

            State = IsScheduled
                ? SchoolBusState.WaitingForSchedule
                : SchoolBusState.Idle;
            CurrentTrip = SchoolBusTripKind.None;
            Runtime?.SetState(BusOperatingState.Idle);
        }

        private void OnRouteUnavailable()
        {
            SetUnavailable();
        }

        private bool SetUnavailable()
        {
            State = SchoolBusState.RouteUnavailable;
            CurrentTrip = SchoolBusTripKind.None;
            Runtime?.SetState(
                BusOperatingState.RouteUnavailable);

            if (!unavailableReported)
            {
                unavailableReported = true;
                RouteUnavailable?.Invoke();
            }

            return false;
        }

        private void SetWaitingForSchedule()
        {
            State = SchoolBusState.WaitingForSchedule;
            CurrentTrip = SchoolBusTripKind.None;
            unavailableReported = false;
            Runtime?.SetState(BusOperatingState.Idle);
        }

        private void OnTileChanged(Vector2Int tile)
        {
            Runtime?.SetRoutePosition(
                tile,
                busRoute != null
                    ? busRoute.NextStop
                    : default);
        }

        private void OnRegistryChanged()
        {
            if (IsOperating || !wantsToOperate)
            {
                return;
            }

            if (IsScheduled)
            {
                if (pendingScheduledRefresh ||
                    services?.Save?.IsRestoring == true)
                {
                    pendingScheduledRefresh = true;
                    return;
                }

                RefreshScheduledOperation();
            }
            else if (State == SchoolBusState.RouteUnavailable ||
                     State == SchoolBusState.Idle)
            {
                TryStartRoute(
                    SchoolBusTripKind.MorningCommute);
            }
        }

        private void OnGameCalendarRegistered(
            IGameCalendarService gameCalendar)
        {
            BindCalendar(gameCalendar);
            if (IsScheduled)
            {
                if (pendingScheduledRefresh ||
                    services?.Save?.IsRestoring == true)
                {
                    pendingScheduledRefresh = true;
                    return;
                }

                RefreshScheduledOperation();
            }
        }

        private void OnHourChanged(int hour)
        {
            if (IsScheduled)
            {
                if (pendingScheduledRefresh ||
                    services?.Save?.IsRestoring == true)
                {
                    pendingScheduledRefresh = true;
                    return;
                }

                RefreshScheduledOperation();
            }
        }

        public SchoolBusSaveData CreateSnapshot()
        {
            return new SchoolBusSaveData
            {
                HasTripHistory = true,
                LastMorningTripDay = lastMorningTripDay,
                LastAfternoonTripDay = lastAfternoonTripDay
            };
        }

        public void RestoreSnapshot(SchoolBusSaveData snapshot)
        {
            if (snapshot == null || !snapshot.HasTripHistory)
            {
                lastMorningTripDay = -1L;
                lastAfternoonTripDay = -1L;
                return;
            }

            lastMorningTripDay = snapshot.LastMorningTripDay;
            lastAfternoonTripDay = snapshot.LastAfternoonTripDay;
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
                Mathf.Max(1, maxResidentialStopsPerTrip);
            schoolWaitSeconds =
                Mathf.Max(0f, schoolWaitSeconds);
        }
#endif
    }
}
