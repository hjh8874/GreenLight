using UnityEngine;

namespace CityFlow.View
{
    public sealed partial class MainCityView
    {
        private const string VehicleViewRecoveryProfileResourcePath =
            "CityFlow/VehicleViewRecoveryProfile";

        [SerializeField]
        private VehicleViewRecoveryProfileSO vehicleViewRecoveryProfile;

        private bool vehicleViewRecoveryProfileResolved;

        internal VehicleViewRecoveryProfileSO VehicleViewRecoveryProfile
        {
            get
            {
                if (!vehicleViewRecoveryProfileResolved)
                {
                    vehicleViewRecoveryProfileResolved = true;
                    vehicleViewRecoveryProfile ??=
                        Resources.Load<VehicleViewRecoveryProfileSO>(
                            VehicleViewRecoveryProfileResourcePath);
                }

                return vehicleViewRecoveryProfile;
            }
        }

        // Unity setup: MainCityView loads the Resources profile automatically.
    }
}
