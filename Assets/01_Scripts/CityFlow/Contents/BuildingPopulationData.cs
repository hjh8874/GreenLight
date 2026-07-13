using UnityEngine;

namespace CityFlow.Content
{
    /// <summary>
    /// 건물의 용도를 구분하는 열거형입니다.
    /// </summary>
    public enum BuildingUseType
    {
        Residential,
        Commercial,
        Public,
        Infrastructure
    }

    /// <summary>
    /// 각 건물이 제공하는 인구 관련 정보를 관리합니다.
    ///
    /// 주거 건물 프리팹에 이 컴포넌트를 추가하고,
    /// Population Value에 증가시킬 인구를 입력합니다.
    /// </summary>
    public class BuildingPopulationData : MonoBehaviour
    {
        [Header("건물 용도")]
        [Tooltip(
            "건물의 용도를 설정합니다. " +
            "인구가 증가하는 건물은 Residential로 설정합니다."
        )]
        [SerializeField]
        private BuildingUseType buildingUseType =
            BuildingUseType.Residential;

        [Header("인구 설정")]
        [Tooltip(
            "이 건물이 건설되었을 때 증가하는 인구입니다. " +
            "주거 건물이 아닌 경우 0으로 설정합니다."
        )]
        [Min(0)]
        [SerializeField]
        private int populationValue = 5;

        [Header("런타임 상태")]
        [Tooltip(
            "현재 인구 시스템에 등록된 건물인지 표시합니다. " +
            "플레이 중 확인용입니다."
        )]
        [SerializeField]
        private bool isRegistered;

        /// <summary>
        /// 건물의 용도입니다.
        /// </summary>
        public BuildingUseType BuildingUseType =>
            buildingUseType;

        /// <summary>
        /// 주거 건물인지 확인합니다.
        /// </summary>
        public bool IsResidential =>
            buildingUseType ==
            BuildingUseType.Residential;

        /// <summary>
        /// 건설 시 증가하는 인구입니다.
        /// </summary>
        public int PopulationValue =>
            populationValue;

        /// <summary>
        /// 인구 시스템에 등록된 상태인지 확인합니다.
        /// </summary>
        public bool IsRegistered =>
            isRegistered;

        /// <summary>
        /// PopulationSystem에서 건물을 등록한 후 호출합니다.
        /// 외부에서 직접 호출할 필요는 없습니다.
        /// </summary>
        public void MarkAsRegistered()
        {
            isRegistered = true;
        }

        /// <summary>
        /// PopulationSystem에서 건물을 제거한 후 호출합니다.
        /// 외부에서 직접 호출할 필요는 없습니다.
        /// </summary>
        public void MarkAsUnregistered()
        {
            isRegistered = false;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            populationValue =
                Mathf.Max(
                    0,
                    populationValue
                );

            /*
             * 주거 건물이 아닌데 인구가 설정된 경우
             * 실수일 가능성이 있으므로 경고만 표시합니다.
             */
            if (buildingUseType !=
                    BuildingUseType.Residential &&
                populationValue > 0)
            {
                Debug.LogWarning(
                    $"[BuildingPopulationData] " +
                    $"{name}은 주거 건물이 아니지만 " +
                    $"인구 증가량이 {populationValue}로 설정되어 있습니다.",
                    this
                );
            }
        }
#endif
    }
}