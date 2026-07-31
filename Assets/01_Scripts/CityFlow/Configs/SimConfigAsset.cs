using UnityEngine;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Sim;

namespace CityFlow.Configs
{
    // 밸런스 수치의 에셋 그릇 — 코드 수정 없이 인스펙터에서 튜닝(이진우).
    // Bootstrap이 있으면 이걸 쓰고, 없으면 SimConfig.Default() 폴백.
    // ponytail: EconomyConfig 분리·수치 검증은 D7 — 지금은 SimConfig 통짜 노출만.
    [CreateAssetMenu(fileName = "SimConfig", menuName = "CityFlow/Sim Config")]
    public sealed class SimConfigAsset : ScriptableObject
    {
        public SimConfig Value = SimConfig.Default();

        [SerializeField]
        private VehicleFootprintProfileSO standardVehicleFootprint;

        public VehicleFootprint StandardVehicleFootprint =>
            standardVehicleFootprint != null
                ? standardVehicleFootprint.Footprint
                : VehicleFootprint.StandardDefault;

        // Unity setup: assign StandardVehicleFootprint to keep core car spacing configurable.
    }
}
