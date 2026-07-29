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
                simEngine = new SimEngine(config, hub, worldGridAccess);
                Services = new CityFlowServices(
                    hub,
                    simEngine,
                    simEngine,
                    CreateSaveService(simEngine),
                    stats: simEngine);
            }

            worldGridConsumer?.Initialize(Services);
        }

        private void Start()
        {
            InstallServices();
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
                    simEngine.SetGameHour(Services.GameCalendar.Hour);
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
