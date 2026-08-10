using CityFlow.Sim;
using UnityEngine;

namespace CityFlow.View
{
    public sealed partial class MainCityView
    {
        private BottleneckMarkerView bottleneckMarkerView;

        private void LateUpdate()
        {
            if (simEngine == null)
            {
                return;
            }

            if (bottleneckMarkerView == null)
            {
                bottleneckMarkerView =
                    gameObject.AddComponent<BottleneckMarkerView>();
                bottleneckMarkerView.Initialize(services);
            }

            for (int index = 0; index < carSimMirrors.Count; index++)
            {
                CommuteCar car = carSimMirrors[index];
                if (!carVehicles.TryGetValue(car, out RouteVehicle vehicle) ||
                    vehicle == null ||
                    vehicle.Object == null)
                {
                    continue;
                }

                FreeFlowStreakView streakView =
                    vehicle.Object.GetComponent<FreeFlowStreakView>();
                if (streakView == null)
                {
                    streakView =
                        vehicle.Object.AddComponent<FreeFlowStreakView>();
                }

                streakView.ApplySnapshot(
                    simEngine.GetCarSnapshot(index));
            }
        }
    }
}
