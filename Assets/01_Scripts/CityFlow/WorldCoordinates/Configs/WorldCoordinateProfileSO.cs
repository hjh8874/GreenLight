using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.WorldCoordinates
{
    [CreateAssetMenu(
        fileName = "WorldCoordinateProfile",
        menuName = "CityFlow/World Coordinate Profile")]
    public sealed class WorldCoordinateProfileSO : ScriptableObject
    {
        [SerializeField] private WorldCoordinatePlane plane =
            WorldCoordinatePlane.XY;
        [SerializeField, Min(0.01f)] private float tileSize = 1f;

        public WorldCoordinatePlane Plane => plane;
        public float TileSize => Mathf.Max(0.01f, tileSize);
        public Quaternion PlaneRotation => plane == WorldCoordinatePlane.XZ
            ? Quaternion.Euler(90f, 0f, 0f)
            : Quaternion.identity;

        private void OnValidate()
        {
            tileSize = Mathf.Max(0.01f, tileSize);
        }
    }
}

// Unity setup: Assign this asset to WorldCoordinateService on its system prefab.
