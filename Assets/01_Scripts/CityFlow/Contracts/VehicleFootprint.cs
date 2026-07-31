using UnityEngine;

namespace CityFlow.Contracts
{
    public readonly struct VehicleFootprint
    {
        private const float MinimumLengthTiles = 0.1f;
        private const float MinimumWidthTiles = 0.08f;

        public static VehicleFootprint StandardDefault => new(
            VehicleSizeClass.Standard,
            0.44f,
            0.2f,
            0.11f);

        public VehicleFootprint(
            VehicleSizeClass sizeClass,
            float lengthTiles,
            float widthTiles,
            float minimumGapTiles)
        {
            SizeClass = sizeClass;
            LengthTiles = Mathf.Max(MinimumLengthTiles, lengthTiles);
            WidthTiles = Mathf.Max(MinimumWidthTiles, widthTiles);
            MinimumGapTiles = Mathf.Max(0f, minimumGapTiles);
        }

        public VehicleSizeClass SizeClass { get; }
        public float LengthTiles { get; }
        public float WidthTiles { get; }
        public float MinimumGapTiles { get; }
        public float HeadwayTiles => LengthTiles + MinimumGapTiles;

        // Unity setup: copy this value from a VehicleFootprintProfileSO when registering a vehicle.
    }
}
