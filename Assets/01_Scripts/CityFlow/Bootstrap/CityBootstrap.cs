using CityFlow.Contracts;
using CityFlow.Fakes;
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

        private void Awake()
        {
            if (!useFakeServices)
            {
                Debug.LogWarning("Real services are not connected yet. Falling back to fake W1 services.", this);
            }

            fakeFlowReader = new FakeFlowReader(mapWidth, mapHeight);
            fakePlacementService = new FakePlacementService(mapWidth, mapHeight);

            Services = new CityFlowServices(
                new SimEventHub(),
                fakeFlowReader,
                fakePlacementService);

            fakePlacementService.Initialize(Services);
            InstallServices();
        }

        private void Update()
        {
            fakeFlowReader?.Tick(Time.time, Services.Events);
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
