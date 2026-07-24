using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Content.Transit
{
    public enum CityBusState
    {
        Idle = 0,
        Operating = 1,
        WaitingAtStop = 2,
        RouteUnavailable = 3
    }

    [RequireComponent(typeof(BusRoute))]
    public sealed class CityBusService :
        MonoBehaviour,
        ICityFlowServiceConsumer,
        IBusRuntimeProvider
    {
        [Header("버스 데이터")]
        [SerializeField]
        private BusDefinitionSO busDefinition;

        [Header("필수 참조")]
        [SerializeField]
        private BusStopRegistry stopRegistry;

        [SerializeField]
        private BusRoute busRoute;

        [Header("노선 설정")]
        [SerializeField, Min(2)]
        private int maximumStops = 10;

        [SerializeField, Min(0)]
        [Tooltip("학생/인구 시스템 연동 전 임시 하차 인원입니다.")]
        private int fallbackPassengersLeavingPerStop = 1;

        [SerializeField, Min(0)]
        [Tooltip("학생/인구 시스템 연동 전 임시 탑승 인원입니다.")]
        private int fallbackPassengersBoardingPerStop = 2;

        [SerializeField]
        private bool autoStart = true;

        [Header("디버그")]
        [SerializeField]
        private bool verboseLogging;

        private readonly List<Vector2Int> routeStops = new();

        private BusRuntime runtime;
        private bool isInitialized;
        private bool isSubscribed;
        private bool wantsToOperate;
        private bool routeRebuildRequested;

        public CityBusState State { get; private set; } =
            CityBusState.Idle;

        public BusRuntime Runtime => runtime;
        public BusRoute Route => busRoute;
        public bool IsInitialized => isInitialized;

        public bool IsOperating =>
            busRoute != null &&
            busRoute.IsOperating;

        public IReadOnlyList<Vector2Int> RouteStops =>
            routeStops;

        public event Action RouteStarted;
        public event Action<Vector2Int, int, int> StopServed;
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

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled || isInitialized)
            {
                return;
            }

            if (services == null)
            {
                Debug.LogError(
                    "[CityBusService] CityFlowServices가 없습니다.",
                    this);
                return;
            }

            if (busDefinition == null)
            {
                Debug.LogError(
                    "[CityBusService] BusDefinitionSO가 연결되지 않았습니다.",
                    this);
                return;
            }

            if (!busDefinition.IsCityBus)
            {
                Debug.LogError(
                    "[CityBusService] CityBus 타입의 데이터를 연결해야 합니다.",
                    this);
                return;
            }

            if (stopRegistry == null)
            {
                Debug.LogError(
                    "[CityBusService] BusStopRegistry가 연결되지 않았습니다.",
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
                    "[CityBusService] BusRoute가 없습니다.",
                    this);
                return;
            }

            stopRegistry.Initialize(services);
            busRoute.Initialize(services);

            if (!stopRegistry.IsInitialized ||
                !busRoute.IsInitialized)
            {
                Debug.LogError(
                    "[CityBusService] 버스 의존성 초기화에 실패했습니다.",
                    this);
                return;
            }

            runtime = new BusRuntime(busDefinition);

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
                runtime.SetServiceEnabled(true);
                TryStartRoute();
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
            if (!isInitialized ||
                !routeRebuildRequested ||
                IsOperating)
            {
                return;
            }

            routeRebuildRequested = false;

            if (wantsToOperate)
            {
                TryStartRoute();
            }
        }

        private void Subscribe()
        {
            if (isSubscribed ||
                stopRegistry == null ||
                busRoute == null)
            {
                return;
            }

            stopRegistry.RegistryChanged +=
                OnRegistryChanged;

            busRoute.TileChanged +=
                OnTileChanged;

            busRoute.StopArrived +=
                OnStopArrived;

            busRoute.StateChanged +=
                OnRouteStateChanged;

            busRoute.RouteUnavailable +=
                OnRouteUnavailable;

            busRoute.RouteCompleted +=
                OnRouteCompleted;

            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (stopRegistry != null)
            {
                stopRegistry.RegistryChanged -=
                    OnRegistryChanged;
            }

            if (busRoute != null)
            {
                busRoute.TileChanged -=
                    OnTileChanged;

                busRoute.StopArrived -=
                    OnStopArrived;

                busRoute.StateChanged -=
                    OnRouteStateChanged;

                busRoute.RouteUnavailable -=
                    OnRouteUnavailable;

                busRoute.RouteCompleted -=
                    OnRouteCompleted;
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

            return TryStartRoute();
        }

        public void StopService()
        {
            wantsToOperate = false;
            routeRebuildRequested = false;

            busRoute?.StopRoute();

            State = CityBusState.Idle;

            runtime?.UnloadAllPassengers();
            runtime?.SetServiceEnabled(false);
            runtime?.SetState(BusOperatingState.Idle);
        }

        public bool TryStartRoute()
        {
            if (!isInitialized ||
                runtime == null ||
                !runtime.IsUnlocked ||
                !runtime.IsServiceEnabled ||
                IsOperating)
            {
                return false;
            }

            BuildRoute();

            if (routeStops.Count < 2)
            {
                SetRouteUnavailable(
                    "[CityBusService] 일반 버스 정류장이 최소 2개 필요합니다.");
                return false;
            }

            bool configured =
                busRoute.ConfigureRoute(
                    routeStops,
                    true);

            if (!configured)
            {
                SetRouteUnavailable(
                    "[CityBusService] 노선 설정에 실패했습니다.");
                return false;
            }

            runtime.SetState(
                BusOperatingState.Departing);

            if (!busRoute.StartRoute())
            {
                SetRouteUnavailable(
                    "[CityBusService] 도로 경로를 생성하지 못했습니다.");
                return false;
            }

            runtime.SetCurrentTile(
                busRoute.CurrentTile);

            runtime.SetNextStop(
                busRoute.NextStop,
                busRoute.CurrentStopIndex);

            RouteStarted?.Invoke();

            if (verboseLogging)
            {
                Debug.Log(
                    $"[CityBusService] 시내버스 운행 시작. " +
                    $"정류장 수: {routeStops.Count}",
                    this);
            }

            return true;
        }

        private void BuildRoute()
        {
            routeStops.Clear();

            IReadOnlyList<Vector2Int> registeredStops =
                stopRegistry.BusStops;

            int stopCount =
                Mathf.Min(
                    maximumStops,
                    registeredStops.Count);

            for (int i = 0; i < stopCount; i++)
            {
                routeStops.Add(
                    registeredStops[i]);
            }
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
            int leaving =
                runtime?.LeavePassengers(
                    fallbackPassengersLeavingPerStop)
                ?? 0;

            int boarding =
                runtime?.BoardPassengers(
                    fallbackPassengersBoardingPerStop)
                ?? 0;

            runtime?.SetCurrentTile(stopTile);

            runtime?.SetNextStop(
                busRoute.NextStop,
                stopIndex);

            StopServed?.Invoke(
                stopTile,
                boarding,
                leaving);

            if (verboseLogging)
            {
                Debug.Log(
                    $"[CityBusService] 정류장 도착. " +
                    $"Tile: {stopTile}, " +
                    $"탑승: {boarding}, " +
                    $"하차: {leaving}, " +
                    $"현재 승객: " +
                    $"{runtime?.CurrentPassengers ?? 0}/" +
                    $"{runtime?.PassengerCapacity ?? 0}",
                    this);
            }
        }

        private void OnRouteStateChanged(
            BusRouteState routeState)
        {
            switch (routeState)
            {
                case BusRouteState.Moving:
                    State = CityBusState.Operating;

                    runtime?.SetState(
                        BusOperatingState.Moving);
                    break;

                case BusRouteState.WaitingAtStop:
                    State = CityBusState.WaitingAtStop;

                    runtime?.SetState(
                        BusOperatingState.WaitingAtStop);
                    break;

                case BusRouteState.Completed:
                case BusRouteState.Idle:
                    State = CityBusState.Idle;

                    runtime?.SetState(
                        BusOperatingState.Idle);
                    break;

                case BusRouteState.RouteUnavailable:
                    State = CityBusState.RouteUnavailable;

                    runtime?.SetState(
                        BusOperatingState.RouteUnavailable);
                    break;
            }
        }

        private void OnRouteCompleted()
        {
            State = CityBusState.Idle;

            runtime?.SetState(
                BusOperatingState.Idle);

            if (wantsToOperate)
            {
                routeRebuildRequested = true;
            }
        }

        private void OnRouteUnavailable()
        {
            SetRouteUnavailable(
                "[CityBusService] 도로 연결이 끊겨 운행을 중단했습니다.");
        }

        private void OnRegistryChanged()
        {
            routeRebuildRequested = true;
        }

        private void SetRouteUnavailable(
            string message)
        {
            State = CityBusState.RouteUnavailable;

            runtime?.SetState(
                BusOperatingState.RouteUnavailable);

            Debug.LogWarning(
                message,
                this);

            RouteUnavailable?.Invoke();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            maximumStops =
                Mathf.Max(
                    2,
                    maximumStops);

            fallbackPassengersLeavingPerStop =
                Mathf.Max(
                    0,
                    fallbackPassengersLeavingPerStop);

            fallbackPassengersBoardingPerStop =
                Mathf.Max(
                    0,
                    fallbackPassengersBoardingPerStop);
        }
#endif
    }
}
