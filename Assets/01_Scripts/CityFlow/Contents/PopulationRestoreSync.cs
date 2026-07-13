using UnityEngine;

namespace CityFlow.Content
{
    [DefaultExecutionOrder(1100)]
    [RequireComponent(typeof(PopulationSystem))]
    public sealed class PopulationRestoreSync : MonoBehaviour
    {
        private PopulationSystem populationSystem;

        private void Awake()
        {
            populationSystem = GetComponent<PopulationSystem>();
        }

        private void Start()
        {
            populationSystem.RebuildPopulationFromTileData();
        }
    }
}
