using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Content
{
    public enum BuildingCategory
    {
        Residential,    // 주거: 집, 아파트
        Business,       // 업무: 회사, 오피스
        Education,      // 교육: 학교
        Commercial,     // 상업: 식당, 쇼핑몰
        Utility,        // 보조: 주유소, 공영주차장
        Finance,        // 금융: 은행
        Transit         // 대중교통: 버스정류장, 지하철역
    }

    [CreateAssetMenu(
        fileName = "BuildingDefinition",
        menuName = "CityFlow/Content/Building Definition"
    )]
    public class BuildingDefinitionSO : ScriptableObject
    {
        [Header("기본 정보")]
        public string buildingId;
        public string buildingName;
        public BuildingCategory category;

        [Tooltip("Sim과 배치 시스템에서 사용하는 실제 타일 타입")]
        public TileType tileType = TileType.Empty;

        [TextArea]
        public string description;

        [Header("UI 및 비주얼")]
        [Tooltip("빌드 슬롯과 툴팁에서 표시할 아이콘")]
        public Sprite buildingIcon;

        [Tooltip("맵에 배치할 건물 프리팹. 비어 있으면 MainCityView의 폴백 비주얼을 사용합니다.")]
        public GameObject buildingPrefab;

        [Header("교통 역할")]
        public bool canGenerateTraffic;
        public bool canReceiveTraffic;

        [Min(0)]
        [Tooltip("이 건물이 만들어내는 기본 이동량")]
        public int trafficGenerationAmount = 1;

        [Min(0f)]
        [Tooltip("이 건물에 도착했을 때 보상 배율")]
        public float destinationRewardMultiplier = 1f;

        [Header("경제")]
        [Min(0)]
        public int buildCost = 100;

        [Min(0)]
        [Tooltip("하루 정산 시 이 건물이 제공하는 기본 코인")]
        public int dailyCoinValue = 20;

        [Header("번성도")]
        [Min(0)]
        [Tooltip("건물을 배치했을 때 증가하는 번성도")]
        public int prosperityValue = 1;

        [Header("해금 및 제한")]
        public bool unlockedByDefault;

        [Min(0)]
        [Tooltip("해금에 필요한 누적 차량 도착 횟수. 기본 해금 건물은 0을 사용합니다.")]
        public long requiredTotalArrivals;

        [Min(0)]
        [Tooltip("도시에 배치할 수 있는 최대 개수. 0이면 제한 없음")]
        public int maxPlacementCount;

        public bool IsPlacementLimited => maxPlacementCount > 0;

        public bool IsUnlocked(long lifetimeDeliveredTotal)
        {
            if (unlockedByDefault)
            {
                return true;
            }

            return lifetimeDeliveredTotal >= requiredTotalArrivals;
        }

        public bool CanPlaceAnother(int currentPlacedCount)
        {
            return !IsPlacementLimited || currentPlacedCount < maxPlacementCount;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            buildingId = buildingId?.Trim();
            buildingName = buildingName?.Trim();

            trafficGenerationAmount = Mathf.Max(0, trafficGenerationAmount);
            destinationRewardMultiplier = Mathf.Max(0f, destinationRewardMultiplier);
            buildCost = Mathf.Max(0, buildCost);
            dailyCoinValue = Mathf.Max(0, dailyCoinValue);
            prosperityValue = Mathf.Max(0, prosperityValue);
            requiredTotalArrivals = System.Math.Max(0L, requiredTotalArrivals);
            maxPlacementCount = Mathf.Max(0, maxPlacementCount);

            if (unlockedByDefault)
            {
                requiredTotalArrivals = 0L;
            }
        }
#endif
    }
}
