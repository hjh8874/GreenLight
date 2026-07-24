using UnityEngine;

namespace CityFlow.Content.Transit
{
    /// <summary>
    /// 버스 한 종류의 고정 데이터를 보관합니다.
    ///
    /// 이름, 아이콘, 정원, 이동 속도, 운행 비용처럼
    /// 게임 플레이 중 바뀌지 않는 기본값을 관리합니다.
    /// </summary>
    [CreateAssetMenu(
        fileName = "BusDefinition",
        menuName = "CityFlow/Transit/Bus Definition")]
    public sealed class BusDefinitionSO : ScriptableObject
    {
        [Header("기본 정보")]

        [SerializeField]
        [Tooltip("버스를 식별하는 고유 ID입니다.")]
        private string busId = "bus_001";

        [SerializeField]
        [Tooltip("UI에 표시할 버스 이름입니다.")]
        private string busName = "버스";

        [SerializeField]
        [TextArea]
        [Tooltip("UI에 표시할 버스 설명입니다.")]
        private string description;

        [SerializeField]
        [Tooltip("버스 종류입니다.")]
        private BusType busType = BusType.CityBus;

        [Header("UI")]

        [SerializeField]
        [Tooltip("버스 목록 및 상세 UI에 표시할 아이콘입니다.")]
        private Sprite icon;

        [SerializeField]
        [Tooltip("미니맵이나 노선 UI에 사용할 색상입니다.")]
        private Color routeColor = Color.white;

        [Header("프리팹")]

        [SerializeField]
        [Tooltip("월드에 표시할 버스 프리팹입니다.")]
        private GameObject busPrefab;

        [Header("운행 능력")]

        [SerializeField]
        [Min(1)]
        [Tooltip("버스의 최대 승객 수입니다.")]
        private int passengerCapacity = 20;

        [SerializeField]
        [Min(0.01f)]
        [Tooltip("도로 타일 한 칸을 이동하는 데 걸리는 시간입니다.")]
        private float secondsPerTile = 0.2f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("정류장에서 기다리는 시간입니다.")]
        private float stopWaitSeconds = 2f;

        [Header("경제")]

        [SerializeField]
        [Min(0)]
        [Tooltip("버스를 구매하거나 해금할 때 필요한 비용입니다.")]
        private long purchaseCost = 1000;

        [SerializeField]
        [Min(0)]
        [Tooltip("한 번 운행할 때 차감되는 비용입니다.")]
        private long operatingCostPerTrip = 10;

        [SerializeField]
        [Min(0)]
        [Tooltip("승객 한 명을 목적지까지 수송했을 때의 기본 수익입니다.")]
        private long rewardPerPassenger = 1;

        [Header("해금")]

        [SerializeField]
        [Tooltip("게임 시작부터 사용할 수 있는 버스인지 여부입니다.")]
        private bool unlockedByDefault;

        [SerializeField]
        [Min(0)]
        [Tooltip("해금에 필요한 번성도입니다.")]
        private int requiredProsperity;

        public string BusId => busId;
        public string BusName => busName;
        public string Description => description;
        public BusType BusType => busType;

        public Sprite Icon => icon;
        public Color RouteColor => routeColor;

        public GameObject BusPrefab => busPrefab;

        public int PassengerCapacity =>
            passengerCapacity;

        public float SecondsPerTile =>
            secondsPerTile;

        public float StopWaitSeconds =>
            stopWaitSeconds;

        public long PurchaseCost =>
            purchaseCost;

        public long OperatingCostPerTrip =>
            operatingCostPerTrip;

        public long RewardPerPassenger =>
            rewardPerPassenger;

        public bool UnlockedByDefault =>
            unlockedByDefault;

        public int RequiredProsperity =>
            requiredProsperity;

        public bool IsSchoolBus =>
            busType == BusType.SchoolBus;

        public bool IsCityBus =>
            busType == BusType.CityBus;

#if UNITY_EDITOR
        private void OnValidate()
        {
            busId = busId?.Trim();
            busName = busName?.Trim();

            passengerCapacity =
                Mathf.Max(1, passengerCapacity);

            secondsPerTile =
                Mathf.Max(0.01f, secondsPerTile);

            stopWaitSeconds =
                Mathf.Max(0f, stopWaitSeconds);

            purchaseCost =
                System.Math.Max(0L, purchaseCost);

            operatingCostPerTrip =
                System.Math.Max(
                    0L,
                    operatingCostPerTrip);

            rewardPerPassenger =
                System.Math.Max(
                    0L,
                    rewardPerPassenger);

            requiredProsperity =
                Mathf.Max(0, requiredProsperity);
        }
#endif
    }
}