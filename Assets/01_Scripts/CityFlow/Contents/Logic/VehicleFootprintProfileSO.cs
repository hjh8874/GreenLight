using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Content
{
    [CreateAssetMenu(
        fileName = "VehicleFootprintProfile",
        menuName = "CityFlow/Traffic/Vehicle Footprint Profile")]
    public sealed class VehicleFootprintProfileSO : ScriptableObject
    {
        [SerializeField]
        private VehicleSizeClass sizeClass = VehicleSizeClass.Standard;

        [SerializeField, Min(0.1f)]
        private float lengthTiles = 0.44f;

        [SerializeField, Min(0.08f)]
        private float widthTiles = 0.2f;

        [SerializeField, Min(0f)]
        private float minimumGapTiles = 0.11f;

        public VehicleSizeClass SizeClass => sizeClass;
        public float LengthTiles => Mathf.Max(0.1f, lengthTiles);
        public float WidthTiles => Mathf.Max(0.08f, widthTiles);
        public float MinimumGapTiles => Mathf.Max(0f, minimumGapTiles);
        public VehicleFootprint Footprint => new(
            SizeClass,
            LengthTiles,
            WidthTiles,
            MinimumGapTiles);

#if UNITY_EDITOR
        private void OnValidate()
        {
            lengthTiles = Mathf.Max(0.1f, lengthTiles);
            widthTiles = Mathf.Max(0.08f, widthTiles);
            minimumGapTiles = Mathf.Max(0f, minimumGapTiles);
        }
#endif

        // Unity setup: create this asset from CityFlow > Traffic and assign it to vehicle definitions.
    }
}
