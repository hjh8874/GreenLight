using CityFlow.Contracts;
using CityFlow.Fakes;
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

        public CityFlowServices Services { get; private set; }

        private FakeFlowReader fakeFlowReader;
        private FakePlacementService fakePlacementService;
        private SimEngine simEngine;

        private void Awake()
        {
            if (useFakeServices)
            {
                fakeFlowReader = new FakeFlowReader(mapWidth, mapHeight);
                fakePlacementService = new FakePlacementService(mapWidth, mapHeight);

                Services = new CityFlowServices(
                    new SimEventHub(),
                    fakeFlowReader,
                    fakePlacementService);

                fakePlacementService.Initialize(Services);
            }
            else
            {
                // 진짜 엔진: SimEngine이 TileData·Placement를 동시에 구현.
                // ponytail: 밸런스는 SimConfig.Default() — 이진우 EconomyConfig(SO) 주입은 D7.
                var config = SimConfig.Default();
                config.GridWidth = mapWidth;
                config.GridHeight = mapHeight;

                var hub = new SimEventHub();
                simEngine = new SimEngine(config, hub);
                Services = new CityFlowServices(hub, simEngine, simEngine);
            }

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
        }
    }
}
