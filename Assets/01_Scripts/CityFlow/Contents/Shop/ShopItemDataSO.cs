using UnityEngine;

namespace CityFlow.Content
{
    /// <summary>
    /// 상점에서 판매하는 건물, 도로,
    /// 교통시설 한 종류의 데이터를 저장합니다.
    ///
    /// 실제 코인을 차감하거나 건물을 배치하지 않고,
    /// 상점과 배치 시스템이 사용할 설정값만 제공합니다.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ShopItemData",
        menuName = "CityFlow/Content/Shop Item Data"
    )]
    public class ShopItemDataSO : ScriptableObject
    {
        [Header("기본 정보")]

        [Tooltip(
            "상품을 구분하기 위한 고유 ID입니다. " +
            "예: building_house_001"
        )]
        [SerializeField]
        private string itemId;

        [Tooltip("상점 UI에 표시할 상품 이름입니다.")]
        [SerializeField]
        private string displayName;

        [Tooltip("상점 UI에 표시할 상품 설명입니다.")]
        [TextArea(2, 5)]
        [SerializeField]
        private string description;

        [Tooltip("상점 UI에 표시할 상품 아이콘입니다.")]
        [SerializeField]
        private Sprite icon;

        [Header("상품 분류")]

        [Tooltip(
            "건물, 도로, 교통시설 중 하나를 선택합니다."
        )]
        [SerializeField]
        private ShopItemCategory category =
            ShopItemCategory.Building;

        [Header("가격")]

        [Tooltip(
            "상품의 기본 가격입니다. " +
            "코인은 소수 없이 정수만 사용합니다."
        )]
        [Min(0)]
        [SerializeField]
        private int basePrice = 100;

        [Tooltip(
            "Fixed는 한 개당 가격, " +
            "PerTile은 타일 1칸당 가격입니다."
        )]
        [SerializeField]
        private ShopPriceMode priceMode =
            ShopPriceMode.Fixed;

        [Header("배치 데이터")]

        [Tooltip(
            "실제 배치할 건물 또는 시설 프리팹입니다."
        )]
        [SerializeField]
        private GameObject placementPrefab;

        [Tooltip(
            "상품이 차지하는 그리드 가로·세로 크기입니다. " +
            "예: 단독주택 2×2, 학교 5×5"
        )]
        [SerializeField]
        private Vector2Int footprintSize =
            new Vector2Int(1, 1);

        [Header("해금 조건")]

        [Tooltip(
            "상품을 해금하기 위해 필요한 최소 도시 단계입니다."
        )]
        [SerializeField]
        private CityTier requiredCityTier =
            CityTier.Village;

        [Tooltip(
            "상품을 해금하기 위해 필요한 최소 인구입니다. " +
            "조건을 사용하지 않으면 0으로 설정합니다."
        )]
        [Min(0)]
        [SerializeField]
        private int requiredPopulation;

        [Tooltip(
            "상품을 해금하기 위해 필요한 누적 도착 횟수입니다. " +
            "조건을 사용하지 않으면 0으로 설정합니다."
        )]
        [Min(0)]
        [SerializeField]
        private long requiredTotalArrivals;

        public string ItemId =>
            itemId;

        public string DisplayName =>
            displayName;

        public string Description =>
            description;

        public Sprite Icon =>
            icon;

        public ShopItemCategory Category =>
            category;

        public int BasePrice =>
            basePrice;

        public ShopPriceMode PriceMode =>
            priceMode;

        public GameObject PlacementPrefab =>
            placementPrefab;

        public Vector2Int FootprintSize =>
            footprintSize;

        public CityTier RequiredCityTier =>
            requiredCityTier;

        public int RequiredPopulation =>
            requiredPopulation;

        public long RequiredTotalArrivals =>
            requiredTotalArrivals;

        /// <summary>
        /// 배치할 개수 또는 도로 타일 수를 기준으로
        /// 실제 필요한 총가격을 계산합니다.
        ///
        /// Fixed 상품은 개수와 관계없이 기본 가격을 반환합니다.
        /// PerTile 상품은 기본 가격 × 타일 수로 계산합니다.
        /// </summary>
        public int GetTotalPrice(
            int placementCount = 1
        )
        {
            int safeCount =
                Mathf.Max(1, placementCount);

            if (priceMode ==
                ShopPriceMode.PerTile)
            {
                long totalPrice =
                    (long)basePrice *
                    safeCount;

                if (totalPrice >
                    int.MaxValue)
                {
                    Debug.LogWarning(
                        $"[ShopItemDataSO] " +
                        $"{displayName}의 총가격이 " +
                        $"int 범위를 초과했습니다.",
                        this
                    );

                    return int.MaxValue;
                }

                return (int)totalPrice;
            }

            return basePrice;
        }

        /// <summary>
        /// 현재 도시 단계, 인구, 누적 도착 횟수를 기준으로
        /// 상품의 해금 여부를 확인합니다.
        ///
        /// 세 조건을 모두 만족해야 true를 반환합니다.
        /// </summary>
        public bool IsUnlocked(
            CityTier currentCityTier,
            int currentPopulation,
            long totalArrivals
        )
        {
            bool cityTierUnlocked =
                currentCityTier >=
                requiredCityTier;

            bool populationUnlocked =
                currentPopulation >=
                requiredPopulation;

            bool arrivalsUnlocked =
                totalArrivals >=
                requiredTotalArrivals;

            return
                cityTierUnlocked &&
                populationUnlocked &&
                arrivalsUnlocked;
        }

        /// <summary>
        /// 상품이 잠겨 있을 때
        /// 어떤 조건이 부족한지 설명 문자열을 반환합니다.
        ///
        /// 이후 상점 잠금 UI에 표시할 수 있습니다.
        /// </summary>
        public string GetUnlockDescription(
            CityTier currentCityTier,
            int currentPopulation,
            long totalArrivals
        )
        {
            if (IsUnlocked(
                currentCityTier,
                currentPopulation,
                totalArrivals
            ))
            {
                return "해금 완료";
            }

            string result =
                "해금 조건";

            if (currentCityTier <
                requiredCityTier)
            {
                result +=
                    $"\n- 도시 단계: " +
                    $"{GetCityTierDisplayName(requiredCityTier)}";
            }

            if (currentPopulation <
                requiredPopulation)
            {
                result +=
                    $"\n- 인구: " +
                    $"{currentPopulation} / " +
                    $"{requiredPopulation}";
            }

            if (totalArrivals <
                requiredTotalArrivals)
            {
                result +=
                    $"\n- 누적 도착: " +
                    $"{totalArrivals} / " +
                    $"{requiredTotalArrivals}";
            }

            return result;
        }

        /// <summary>
        /// 도시 단계 enum을
        /// 한글 이름으로 변환합니다.
        /// </summary>
        public static string GetCityTierDisplayName(
            CityTier cityTier
        )
        {
            switch (cityTier)
            {
                case CityTier.Village:
                    return "마을";

                case CityTier.SmallCity:
                    return "소도시";

                case CityTier.MiddleCity:
                    return "중도시";

                case CityTier.BigCity:
                    return "대도시";

                default:
                    return "마을";
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            basePrice =
                Mathf.Max(
                    0,
                    basePrice
                );

            requiredPopulation =
                Mathf.Max(
                    0,
                    requiredPopulation
                );

            requiredTotalArrivals =
                System.Math.Max(
                    0L,
                    requiredTotalArrivals
                );

            footprintSize.x =
                Mathf.Max(
                    1,
                    footprintSize.x
                );

            footprintSize.y =
                Mathf.Max(
                    1,
                    footprintSize.y
                );

            if (string.IsNullOrWhiteSpace(
                itemId
            ))
            {
                return;
            }

            itemId =
                itemId.Trim()
                    .ToLowerInvariant()
                    .Replace(" ", "_");
        }
#endif
    }
}