using UnityEngine;

namespace CityFlow.View
{
    [CreateAssetMenu(
        fileName = "BuildingVisualCatalog",
        menuName = "CityFlow/View/Building Visual Catalog")]
    public sealed class BuildingVisualCatalogSO :
        ScriptableObject
    {
        [SerializeField] private GameObject housePrefab;
        [SerializeField] private GameObject officePrefab;
        [SerializeField] private GameObject schoolPrefab;
        [SerializeField] private GameObject hospitalPrefab;
        [SerializeField] private GameObject foundationPrefab;

        public GameObject HousePrefab => housePrefab;
        public GameObject OfficePrefab => officePrefab;
        public GameObject SchoolPrefab => schoolPrefab;
        public GameObject HospitalPrefab => hospitalPrefab;
        public GameObject FoundationPrefab => foundationPrefab;
    }
}
