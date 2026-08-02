using UnityEngine;

namespace CityFlow.Buildings
{
    // Scene integration point: add beside SpecialBuildingVisitService. Runtime
    // service/scheduler wiring belongs here; pure planning lives in Contracts.
    [DisallowMultipleComponent]
    public sealed class LeisureTripSource : MonoBehaviour { }
}
