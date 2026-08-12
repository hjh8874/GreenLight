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
        [SerializeField] private GameObject roundaboutPrefab;

        [Header("Parking Prefab")]
        [SerializeField] private GameObject drivewayPrefab;

        public GameObject DrivewayPrefab => drivewayPrefab;
        public GameObject RoadSurfacePrefab => roadSurfacePrefab;
        public GameObject RoundaboutPrefab => roundaboutPrefab;

        public GameObject GetRoadPrefab(SimpleTownRoadShape _)
        {
            return roadSurfacePrefab;
        }
    }
}
