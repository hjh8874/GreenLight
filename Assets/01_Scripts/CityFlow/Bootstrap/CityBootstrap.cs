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
        private bool servicesInstalled;

        private void Awake()
        {
            if (useFakeServices)
            {
                fakeFlowReader = new FakeFlowReader(mapWidth, mapHeight);
                fakePlacementService = new FakePlacementService(mapWidth, mapHeight);

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
                // 그리드 크기는 부트스트랩 필드가 계속 오너(이중 오너 충돌 방지).
                var config = simConfig != null ? simConfig.Value : SimConfig.Default();
                config.GridWidth = mapWidth;
                config.GridHeight = mapHeight;

                var hub = new SimEventHub();
                simEngine = new SimEngine(config, hub);
                Services = new CityFlowServices(
                    hub,
                    simEngine,
                    simEngine,
                    // TODO: This becomes non-null after SimEngine implements ISimSaveSource.
                    CreateSaveService((simEngine as object) as ISimSaveSource));
            }
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
                new SystemSaveClock());

            return saveService;
        }
    }
}
