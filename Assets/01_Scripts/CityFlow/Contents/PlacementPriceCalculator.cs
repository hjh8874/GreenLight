using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Content
{
    /// <summary>
    /// 배치물 가격 조회와 구매 가능 여부를 계산합니다.
    ///
    /// 실제 코인은 IEconomyService가 관리하며,
    /// 이 클래스는 별도의 코인 값을 보유하지 않습니다.
    /// </summary>
    public sealed class PlacementPriceCalculator :
        MonoBehaviour
    {
        [Header("가격 설정")]
        [SerializeField]
        private PlacementPriceConfigSO priceConfig;

        public PlacementPriceConfigSO PriceConfig =>
            priceConfig;

        /// <summary>
        /// 지정한 배치물의 가격을 반환합니다.
        /// </summary>
        public bool TryGetCost(
            TileType tileType,
            out long cost
        )
        {
            cost = 0L;

            if (priceConfig == null)
            {
                Debug.LogError(
                    "[PlacementPriceCalculator] " +
                    "PlacementPriceConfigSO가 연결되지 않았습니다.",
                    this
                );

                return false;
            }

            if (!priceConfig.TryGetPlacementCost(
                    tileType,
                    out int configuredCost
                ))
            {
                Debug.LogWarning(
                    $"[PlacementPriceCalculator] " +
                    $"가격이 등록되지 않은 TileType입니다: " +
                    $"{tileType}",
                    this
                );

                return false;
            }

            if (configuredCost < 0)
            {
                Debug.LogError(
                    $"[PlacementPriceCalculator] " +
                    $"잘못된 배치 가격입니다. " +
                    $"Type: {tileType}, " +
                    $"Cost: {configuredCost}",
                    this
                );

                return false;
            }

            cost = configuredCost;
            return true;
        }

        /// <summary>
        /// 현재 보유 코인으로 해당 배치물을
        /// 구매할 수 있는지 확인합니다.
        ///
        /// 실제 코인은 차감하지 않습니다.
        /// </summary>
        public bool CanAfford(
            IEconomyService economy,
            TileType tileType
        )
        {
            if (economy == null)
            {
                return false;
            }

            if (!TryGetCost(
                    tileType,
                    out long cost
                ))
            {
                return false;
            }

            return economy.Coins >= cost;
        }

        /// <summary>
        /// 해당 배치물 가격만큼 코인 사용을 시도합니다.
        ///
        /// 실제 배치가 성공한 이후 호출하는 것을 권장합니다.
        /// </summary>
        public bool TryPurchase(
            IEconomyService economy,
            TileType tileType
        )
        {
            if (economy == null)
            {
                Debug.LogError(
                    "[PlacementPriceCalculator] " +
                    "IEconomyService가 없습니다.",
                    this
                );

                return false;
            }

            if (!TryGetCost(
                    tileType,
                    out long cost
                ))
            {
                return false;
            }

            if (cost == 0L)
            {
                Debug.Log(
                    $"[PlacementPriceCalculator] " +
                    $"무료 배치물입니다: {tileType}",
                    this
                );

                return true;
            }

            if (!economy.TrySpend(cost))
            {
                Debug.Log(
                    $"[PlacementPriceCalculator] " +
                    $"코인이 부족합니다. " +
                    $"Type: {tileType}, " +
                    $"Required: {cost}, " +
                    $"Current: {economy.Coins}",
                    this
                );

                return false;
            }

            Debug.Log(
                $"[PlacementPriceCalculator] " +
                $"구매 성공. " +
                $"Type: {tileType}, " +
                $"Spent: {cost}, " +
                $"Remaining: {economy.Coins}",
                this
            );

            return true;
        }
    }
}