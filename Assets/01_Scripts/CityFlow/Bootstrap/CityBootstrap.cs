using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.Fakes;
using CityFlow.Save;
using CityFlow.Sim;
using UnityEngine;

namespace CityFlow.Bootstrap
{
    public sealed class CityBootstrap : MonoBehaviour
    {
        [Header("Fake W1 Services")]
        [SerializeField] private bool useFakeServices = true;
        [SerializeField] private int mapWidth = GridUtil.DefaultWidth;
        [SerializeField] private int mapHeight = GridUtil.DefaultHeight;

        [Header("Real Engine")]
        [SerializeField] private Configs.SimConfigAsset simConfig;   // 비우면 SimConfig.Default()

        public CityFlowServices Services { get; private set; }

        private FakeFlowReader fakeFlowReader;
        private FakePlacementService fakePlacementService;
        private SimEngine simEngine;
        private SimConfig engineConfig;
        private SaveService saveService;
        private IWorldGridAccess worldGridAccess;
        private ICityFlowServiceConsumer worldGridConsumer;
        private bool servicesInstalled;

        private void Awake()
        {
            FindWorldGridService();

            if (useFakeServices)
            {
                // 페이크 큐 상한을 실 설정과 같은 값으로 — 페이크 UI와 실 게임의 눈금을 맞춘다.
                var fakeCfg = simConfig != null ? simConfig.Value : SimConfig.Default();
                int fakeWidth = worldGridAccess?.WorldWidth ?? mapWidth;
                int fakeHeight = worldGridAccess?.WorldHeight ?? mapHeight;
                fakeFlowReader = new FakeFlowReader(
                    fakeWidth,
                    fakeHeight,
                    fakeCfg.QueueCapacityPerTile);
                fakePlacementService = new FakePlacementService(
                    fakeWidth,
                    fakeHeight);

                Services = new CityFlowServices(
                    new SimEventHub(),
                    fakeFlowReader,
                    fakePlacementService,
                    CreateSaveService(null));

                fakePlacementService.Initialize(Services);
            }
            else
            {
                // 진짜 엔진: SimEngine이 TileData·Placement를 동시에 구현.
                // 밸런스는 SO 에셋(인스펙터 튜닝) 우선, 비어 있으면 Default() 폴백.
                // WorldGrid가 있으면 월드 크기를 단일 원본으로 사용한다.
                var config = simConfig != null ? simConfig.Value : SimConfig.Default();
                config.GridWidth = worldGridAccess?.WorldWidth ?? mapWidth;
                config.GridHeight = worldGridAccess?.WorldHeight ?? mapHeight;

                var hub = new SimEventHub();
                engineConfig = config;   // 하루 길이 동기화에 재사용(Sim 의 config 는 internal 이라 못 읽는다)
                simEngine = new SimEngine(config, hub, worldGridAccess);
                Services = new CityFlowServices(
                    hub,
                    simEngine,
                    simEngine,
                    CreateSaveService(simEngine),
                    stats: simEngine);
                Services.RegisterVehicleTrips(simEngine);
            }

            worldGridConsumer?.Initialize(Services);
        }

        private void Start()
        {
            InstallServices();
            SyncSimDayLengthToCalendar();
        }

        // 하루 길이 단일 출처화의 마지막 조각. `GameTimeSettingsSO`(표시 시계)와
        // `SimConfig.DayLengthSeconds`(Sim 사본)는 어셈블리 경계 때문에 값을 복제해야 하는데,
        // **복제는 어긋난다.** 기본값을 맞춰놔도 씬이 다른 GameTimeSettings 에셋을 물리면 갈라진다
        // (리뷰 지적 2026-07-30: `CityFlowIntegrated_Lee`가 24초 디버그 설정 + 720초 SimConfig 를
        // 함께 참조 → 30배). 씬이 실제로 쓰는 캘린더 값을 런타임에 Sim 으로 밀어 불일치를
        // 구조적으로 불가능하게 만든다. 씬 편집도, 씬별 전용 SimConfig 도 필요 없다.
        private void SyncSimDayLengthToCalendar()
        {
            if (simEngine == null) return;

            IGameCalendarService calendar = Services?.GameCalendar;
            if (calendar == null) return;

            if (!TryResolveSimDayLength(
                    calendar.RealSecondsPerGameDay,
                    engineConfig.DayLengthSeconds,
                    out float resolved))
            {
                return;
            }

            engineConfig.DayLengthSeconds = resolved;
            if (!simEngine.ApplyConfig(engineConfig))
            {
                Debug.LogWarning(
                    "[CityBootstrap] 하루 길이 동기화가 거부됐다(퇴화 config). "
                    + $"Sim 은 {engineConfig.DayLengthSeconds}초를 유지한다.",
                    this);
            }
        }

        // 순수 판정 — 캘린더 값이 유효하고 Sim 사본과 다를 때만 동기화한다.
        // internal 이라 기본 에디터 어셈블리(테스트)에서 보인다(AssemblyInfo 의 InternalsVisibleTo).
        internal static bool TryResolveSimDayLength(
            float calendarSecondsPerDay,
            float currentSimDayLength,
            out float resolved)
        {
            resolved = currentSimDayLength;
            if (calendarSecondsPerDay <= 0f) return false;   // 미설정·퇴화 값은 무시
            if (Mathf.Approximately(calendarSecondsPerDay, currentSimDayLength)) return false;

            resolved = calendarSecondsPerDay;
            return true;
        }

        private void Update()
        {
            if (useFakeServices)
            {
                fakeFlowReader?.Tick(Time.time, Services.Events);
            }
            else
            {
                if (simEngine != null && Services?.GameCalendar != null)
                    simEngine.SetGameTime(
                        Services.GameCalendar.TotalDays,
                        Services.GameCalendar.Hour);
                simEngine?.Tick(Time.deltaTime);
            }
        }

        private void InstallServices()
        {
            if (servicesInstalled)
            {
                return;
            }

            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == this)
                {
                    continue;
                }

                if (behaviour is ICityFlowServiceConsumer consumer)
                {
                    consumer.Initialize(Services);
                }
            }

            servicesInstalled = true;
        }

        private SaveService CreateSaveService(ISimSaveSource simSaveSource)
        {
            saveService = new SaveService(
                simSaveSource,
                new JsonSaveRepository(),
                new SystemSaveClock(),
                worldGridAccess: worldGridAccess);

            return saveService;
        }

        private void FindWorldGridService()
        {
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int index = 0; index < behaviours.Length; index++)
            {
                MonoBehaviour behaviour = behaviours[index];
                if (behaviour == this ||
                    !behaviour.isActiveAndEnabled ||
                    behaviour is not IWorldGridAccess access)
                {
                    continue;
                }

                if (worldGridAccess != null)
                {
                    Debug.LogWarning(
                        "[CityBootstrap] Multiple world grid services were " +
                        "found. The first active service will be used.",
                        this);
                    continue;
                }

                worldGridAccess = access;
                worldGridConsumer = behaviour as ICityFlowServiceConsumer;
            }
        }
    }
}
