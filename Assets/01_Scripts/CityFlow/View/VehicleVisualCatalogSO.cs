using UnityEngine;

namespace CityFlow.View
{
    [CreateAssetMenu(
        fileName = "VehicleVisualCatalog",
        menuName = "CityFlow/View/Vehicle Visual Catalog")]
    public sealed class VehicleVisualCatalogSO : ScriptableObject
    {
        [SerializeField] private GameObject[] normalVehiclePrefabs;
        [SerializeField] private GameObject schoolBusPrefab;
        [SerializeField] private GameObject ambulancePrefab;
        [SerializeField] private GameObject[] cityBusPrefabs;
        [SerializeField, HideInInspector]
        private int generatedVersion;

        public GameObject[] NormalVehiclePrefabs =>
            normalVehiclePrefabs;
        public GameObject SchoolBusPrefab =>
            schoolBusPrefab;
        public GameObject AmbulancePrefab =>
            ambulancePrefab;
        public GameObject[] CityBusPrefabs =>
            cityBusPrefabs;
        public int GeneratedVersion =>
            generatedVersion;
    }
}
