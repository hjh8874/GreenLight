using CityFlow.Bootstrap;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Content
{
    [DefaultExecutionOrder(1100)]
    [RequireComponent(typeof(PopulationSystem))]
    public sealed class PopulationRestoreSync : MonoBehaviour, ICityFlowServiceConsumer
    {
        private PopulationSystem populationSystem;
        private CityFlowServices services;

        private void Awake()
        {
            populationSystem = GetComponent<PopulationSystem>();
        }

        public void Initialize(CityFlowServices cityFlowServices)
        {
            services = cityFlowServices;

            if (services?.Save != null)
            {
                services.Save.RestoreCompleted += OnRestoreCompleted;
            }
        }

        private void OnDestroy()
        {
            if (services?.Save != null)
            {
                services.Save.RestoreCompleted -= OnRestoreCompleted;
            }
        }

        private void OnRestoreCompleted(RestoreCompletedEvent _)
        {
            populationSystem.RebuildPopulationFromTileData();
        }
    }
}
