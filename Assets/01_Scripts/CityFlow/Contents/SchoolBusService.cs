using System;
using System.Collections.Generic;
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
    /// 학교에서 출발해 주거지역을 순서대로 방문한 후
    /// 다시 학교로 돌아오는 스쿨버스 운행을 관리합니다.
    ///
    /// 운행 순서:
    /// 학교 → 주거지역 1 → 주거지역 2 → ... → 학교
    /// </summary>
    [RequireComponent(typeof(BusRoute))]
    public sealed class SchoolBusService : MonoBehaviour
    {
        [Header("필수 참조")]
        [SerializeField]
        private BusStopRegistry stopRegistry;

        [SerializeField]
        private BusRoute busRoute;

        [Header("운행 설정")]
        [Min(1)]
        [Tooltip("한 번 운행에서 방문할 최대 주거지역 수입니다.")]
        [SerializeField]
        private int maxResidentialStopsPerTrip = 5;

        [Min(0f)]
        [Tooltip("학교로 복귀한 후 다음 운행까지 기다리는 시간입니다.")]
        [SerializeField]
        private float schoolWaitSeconds = 5f;

        [Tooltip(
            "학교에서 가까운 주거지역부터 방문합니다. " +
            "현재는 맨해튼 거리 기준입니다."
        )]
        [SerializeField]
        private bool sortResidentialStopsByDistance = true;

        [Tooltip("시작 시 자동으로 운행을 시도합니다.")]
        [SerializeField]
        private bool autoStart = true;

        private readonly List<Vector2Int> schoolRouteStops = new();
        private readonly List<Vector2Int> residentialBuffer = new();

        private float schoolWaitTimer;
        private bool isSubscribed;
        private bool wantsToOperate;

        public SchoolBusState State { get; private set; } =
            SchoolBusState.Idle;

        public Vector2Int SchoolTile { get; private set; }

        public int VisitedResidentialCount { get; private set; }
        public int CurrentPassengers { get; private set; }

        public bool IsOperating =>
            busRoute != null &&
            (busRoute.State == BusRouteState.Moving ||
             busRoute.State == BusRouteState.WaitingAtStop);

        public event Action RouteStarted;
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
            Subscribe();

            wantsToOperate = autoStart;

            if (wantsToOperate)
            {
                TryStartSchoolRoute();
            }
        }

        private void OnEnable()
        {
            Subscribe();
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
            if (State != SchoolBusState.WaitingAtSchool ||
                !wantsToOperate)
            {
                return;
            }

            schoolWaitTimer -= Time.deltaTime;

            if (schoolWaitTimer <= 0f)
            {
                TryStartSchoolRoute();
            }
        }

        private void Subscribe()
        {
            if (isSubscribed || busRoute == null)
            {
                return;
            }

            busRoute.StopArrived += OnStopArrived;
            busRoute.RouteUnavailable += OnRouteUnavailable;
            busRoute.RouteCompleted += OnRouteCompleted;

            if (stopRegistry != null)
            {
                stopRegistry.RegistryChanged +=
                    OnRegistryChanged;
            }

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
                busRoute.StopArrived -= OnStopArrived;
                busRoute.RouteUnavailable -= OnRouteUnavailable;
                busRoute.RouteCompleted -= OnRouteCompleted;
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
            wantsToOperate = true;
            return TryStartSchoolRoute();
        }

        public void StopService()
        {
            wantsToOperate = false;

            busRoute?.StopRoute();

            State = SchoolBusState.Idle;
            schoolWaitTimer = 0f;
            CurrentPassengers = 0;
            VisitedResidentialCount = 0;
        }

        public bool TryStartSchoolRoute()
        {
            if (stopRegistry == null || busRoute == null)
            {
                Debug.LogWarning(
                    "[SchoolBusService] 필수 참조가 없습니다.",
                    this
                );
                return false;
            }

            if (!stopRegistry.TryGetFirstSchool(
                    out Vector2Int schoolTile
                ))
            {
                Debug.LogWarning(
                    "[SchoolBusService] 배치된 학교가 없습니다.",
                    this
                );
                return false;
            }

            if (stopRegistry.ResidentialStopCount == 0)
            {
                Debug.LogWarning(
                    "[SchoolBusService] 방문할 주거지역이 없습니다.",
                    this
                );
                return false;
            }

            SchoolTile = schoolTile;

            BuildSchoolRoute();

            if (schoolRouteStops.Count < 3)
            {
                Debug.LogWarning(
                    "[SchoolBusService] 유효한 스쿨버스 노선을 만들 수 없습니다.",
                    this
                );
                return false;
            }

            /*
             * 노선 마지막에 학교를 직접 넣었으므로
             * BusRoute 자체 반복은 사용하지 않습니다.
             */
            bool configured =
                busRoute.ConfigureRoute(
                    schoolRouteStops,
                    false
                );

            if (!configured)
            {
                return false;
            }

            VisitedResidentialCount = 0;
            CurrentPassengers = 0;
            schoolWaitTimer = 0f;

            State =
                SchoolBusState.DrivingToResidentialArea;

            if (!busRoute.StartRoute())
            {
                State = SchoolBusState.RouteUnavailable;
                return false;
            }

            RouteStarted?.Invoke();

            Debug.Log(
                $"[SchoolBusService] 스쿨버스 출발. " +
                $"학교: {SchoolTile}, " +
                $"방문 주거지역: {schoolRouteStops.Count - 2}",
                this
            );

            return true;
        }

        private void BuildSchoolRoute()
        {
            schoolRouteStops.Clear();
            residentialBuffer.Clear();

            schoolRouteStops.Add(SchoolTile);

            IReadOnlyList<Vector2Int> residentialStops =
                stopRegistry.ResidentialStops;

            for (int i = 0;
                 i < residentialStops.Count;
                 i++)
            {
                Vector2Int residentialTile =
                    residentialStops[i];

                if (residentialTile != SchoolTile)
                {
                    residentialBuffer.Add(
                        residentialTile
                    );
                }
            }

            if (sortResidentialStopsByDistance)
            {
                residentialBuffer.Sort(
                    CompareResidentialDistance
                );
            }

            int visitCount =
                Mathf.Min(
                    maxResidentialStopsPerTrip,
                    residentialBuffer.Count
                );

            for (int i = 0; i < visitCount; i++)
            {
                schoolRouteStops.Add(
                    residentialBuffer[i]
                );
            }

            /*
             * 마지막 정류장을 학교로 지정하여
             * 반드시 학교로 복귀하도록 합니다.
             */
            schoolRouteStops.Add(SchoolTile);
        }

        private int CompareResidentialDistance(
            Vector2Int left,
            Vector2Int right
        )
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

            int yCompare =
                left.y.CompareTo(right.y);

            return yCompare != 0
                ? yCompare
                : left.x.CompareTo(right.x);
        }

        private void OnStopArrived(
            Vector2Int stopTile,
            int stopIndex
        )
        {
            int finalIndex =
                schoolRouteStops.Count - 1;

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
                SchoolBusState.WaitingAtResidentialArea;

            VisitedResidentialCount++;

            /*
             * 현재 데이터 구조에 주거지역별 학생 수가 따로 없으므로
             * 우선 주거지역 한 곳당 학생 1명을 탑승 처리합니다.
             *
             * 이후 PopulationSystem.GetPopulationAt(tile)을
             * 공개 계약으로 연결하면 실제 인구 기준으로 교체할 수 있습니다.
             */
            CurrentPassengers++;

            ResidentialStopVisited?.Invoke(
                stopTile,
                CurrentPassengers
            );

            Debug.Log(
                $"[SchoolBusService] 주거지역 정차. " +
                $"Tile: {stopTile}, " +
                $"누적 탑승 학생: {CurrentPassengers}",
                this
            );

            State =
                SchoolBusState.DrivingToResidentialArea;
        }

        private void HandleSchoolReturn()
        {
            State = SchoolBusState.WaitingAtSchool;

            int deliveredStudents =
                CurrentPassengers;

            ReturnedToSchool?.Invoke(
                SchoolTile,
                deliveredStudents
            );

            Debug.Log(
                $"[SchoolBusService] 학교 복귀. " +
                $"방문 주거지역: {VisitedResidentialCount}, " +
                $"등교 학생: {deliveredStudents}",
                this
            );

            /*
             * BuildingEffectService와 실제 인구 상한 변경 API는
             * 현재 develop 소스에서 확인되지 않으므로 직접 호출하지 않습니다.
             *
             * 해당 서비스가 복구되면 ReturnedToSchool 이벤트를 구독하거나
             * 이 위치에서 학생 수를 전달하면 됩니다.
             */

            CurrentPassengers = 0;
            VisitedResidentialCount = 0;

            schoolWaitTimer =
                Mathf.Max(0f, schoolWaitSeconds);

            if (schoolWaitTimer <= 0f &&
                wantsToOperate)
            {
                TryStartSchoolRoute();
            }
        }

        private void OnRouteCompleted()
        {
            /*
             * 정상적인 스쿨버스 노선은 마지막 학교 정류장에서
             * HandleSchoolReturn이 먼저 호출됩니다.
             */
            if (State != SchoolBusState.WaitingAtSchool)
            {
                State = SchoolBusState.Idle;
            }
        }

        private void OnRouteUnavailable()
        {
            State = SchoolBusState.RouteUnavailable;

            RouteUnavailable?.Invoke();

            Debug.LogWarning(
                "[SchoolBusService] 도로가 연결되지 않아 운행을 중단했습니다.",
                this
            );
        }

        private void OnRegistryChanged()
        {
            /*
             * 운행 중에는 기존 노선을 즉시 바꾸지 않습니다.
             * 학교로 돌아온 뒤 다음 운행에서 최신 건물 목록을 사용합니다.
             */
            if (!IsOperating &&
                wantsToOperate &&
                State != SchoolBusState.WaitingAtSchool)
            {
                TryStartSchoolRoute();
            }
        }

        private static int ManhattanDistance(
            Vector2Int first,
            Vector2Int second
        )
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
                    maxResidentialStopsPerTrip
                );

            schoolWaitSeconds =
                Mathf.Max(
                    0f,
                    schoolWaitSeconds
                );
        }
#endif
    }
}