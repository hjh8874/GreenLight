using UnityEngine;

namespace CityFlow.View
{
    [CreateAssetMenu(
        fileName = "SimpleTownRoadVisualSet",
        menuName = "CityFlow/Visuals/SimpleTown Road Visual Set")]
    public sealed class SimpleTownRoadVisualSetSO : ScriptableObject
    {
        [Header("Road Prefabs")]
        [SerializeField] private GameObject roadSurfacePrefab;

        [Header("Parking Prefab")]
        [SerializeField] private GameObject drivewayPrefab;

        public GameObject DrivewayPrefab => drivewayPrefab;
        public GameObject RoadSurfacePrefab => roadSurfacePrefab;

        public GameObject GetRoadPrefab(SimpleTownRoadShape _)
        {
            return roadSurfacePrefab;
        }
    }
}
