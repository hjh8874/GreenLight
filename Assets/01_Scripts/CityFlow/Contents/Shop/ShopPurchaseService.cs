using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Content
{
    /// <summary>
    /// 상점 상품의 구매 가능 여부와
    /// 실제 코인 차감을 담당합니다.
    ///
    /// 상품 UI 생성이나 실제 건물 배치는 담당하지 않습니다.
    /// </summary>
    public sealed class ShopPurchaseService :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        private CityFlowServices services;

        /// <summary>
        /// 경제 서비스가 정상적으로 연결되었는지 확인합니다.
        /// </summary>
        public bool IsReady =>
            services != null &&
            services.Economy != null;

        /// <summary>
        /// 현재 보유 중인 코인을 반환합니다.
        /// </summary>
        public long CurrentCoins =>
            IsReady
                ? services.Economy.Coins
                : 0L;

        public void Initialize(
            CityFlowServices services
        )
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (services == null)
            {
                Debug.LogError(
                    "[ShopPurchaseService] " +
                    "CityFlowServices가 없습니다.",
                    this
                );

                return;
            }

            this.services = services;

            if (services.Economy == null)
            {
                Debug.LogWarning(
                    "[ShopPurchaseService] " +
                    "EconomyService가 아직 등록되지 않았습니다.",
                    this
                );

                return;
            }

            Debug.Log(
                "[ShopPurchaseService] " +
                "상점 구매 서비스 초기화 완료.",
                this
            );
        }

        /// <summary>
        /// 상품의 총가격을 반환합니다.
        ///
        /// 도로나 연속 배치 상품은 placementCount를 사용하여
        /// 타일 수에 따른 가격을 계산합니다.
        /// </summary>
        public int GetPurchasePrice(
            ShopItemDataSO item,
            int placementCount = 1
        )
        {
            if (item == null)
            {
                return 0;
            }

            return item.GetTotalPrice(
                placementCount
            );
        }

        /// <summary>
        /// 현재 도시 조건에서 상품이 해금되었는지 확인합니다.
        /// </summary>
        public bool IsUnlocked(
            ShopItemDataSO item,
            CityTier currentCityTier,
            int currentPopulation,
            long totalArrivals
        )
        {
            if (item == null)
            {
                return false;
            }

            return item.IsUnlocked(
                currentCityTier,
                currentPopulation,
                totalArrivals
            );
        }

        /// <summary>
        /// 현재 코인으로 상품을 구매할 수 있는지 확인합니다.
        ///
        /// 이 함수에서는 실제 코인을 차감하지 않습니다.
        /// </summary>
        public bool CanAfford(
            ShopItemDataSO item,
            int placementCount = 1
        )
        {
            if (!IsReady || item == null)
            {
                return false;
            }

            int price =
                item.GetTotalPrice(
                    placementCount
                );

            if (price < 0)
            {
                return false;
            }

            return services.Economy.Coins >=
                   price;
        }

        /// <summary>
        /// 상품 구매를 시도합니다.
        ///
        /// 해금 조건과 보유 코인을 확인한 후
        /// EconomyService에서 실제 코인을 차감합니다.
        /// </summary>
        public ShopPurchaseResult TryPurchase(
            ShopItemDataSO item,
            CityTier currentCityTier,
            int currentPopulation,
            long totalArrivals,
            int placementCount = 1
        )
        {
            if (!IsReady)
            {
                Debug.LogWarning(
                    "[ShopPurchaseService] " +
                    "EconomyService를 사용할 수 없습니다.",
                    this
                );

                return
                    ShopPurchaseResult
                        .ServiceUnavailable;
            }

            if (item == null)
            {
                Debug.LogWarning(
                    "[ShopPurchaseService] " +
                    "구매할 상품 데이터가 없습니다.",
                    this
                );

                return
                    ShopPurchaseResult
                        .InvalidItem;
            }

            if (!item.IsUnlocked(
                    currentCityTier,
                    currentPopulation,
                    totalArrivals
                ))
            {
                Debug.Log(
                    $"[ShopPurchaseService] " +
                    $"잠긴 상품입니다: " +
                    $"{item.DisplayName}",
                    this
                );

                return
                    ShopPurchaseResult
                        .ItemLocked;
            }

            int price =
                item.GetTotalPrice(
                    placementCount
                );

            if (price < 0)
            {
                Debug.LogWarning(
                    $"[ShopPurchaseService] " +
                    $"잘못된 상품 가격입니다. " +
                    $"상품: {item.DisplayName}, " +
                    $"가격: {price}",
                    this
                );

                return
                    ShopPurchaseResult
                        .InvalidPrice;
            }

            // 가격이 0인 무료 상품은
            // 별도의 차감 없이 성공 처리합니다.
            if (price == 0)
            {
                Debug.Log(
                    $"[ShopPurchaseService] " +
                    $"무료 상품 구매 성공: " +
                    $"{item.DisplayName}",
                    this
                );

                return
                    ShopPurchaseResult
                        .Success;
            }

            if (!services.Economy.TrySpend(
                    price
                ))
            {
                Debug.Log(
                    $"[ShopPurchaseService] " +
                    $"코인이 부족합니다. " +
                    $"상품: {item.DisplayName}, " +
                    $"필요 코인: {price}, " +
                    $"현재 코인: " +
                    $"{services.Economy.Coins}",
                    this
                );

                return
                    ShopPurchaseResult
                        .InsufficientCoins;
            }

            Debug.Log(
                $"[ShopPurchaseService] " +
                $"상품 구매 성공. " +
                $"상품: {item.DisplayName}, " +
                $"사용 코인: {price}, " +
                $"남은 코인: " +
                $"{services.Economy.Coins}",
                this
            );

            return
                ShopPurchaseResult
                    .Success;
        }

        /// <summary>
        /// 배치 실패 또는 구매 취소 시
        /// 지불한 코인을 환불합니다.
        /// </summary>
        public bool RefundPurchase(
            ShopItemDataSO item,
            int placementCount = 1,
            string reason = "shop purchase refund"
        )
        {
            if (!IsReady || item == null)
            {
                return false;
            }

            int refundAmount =
                item.GetTotalPrice(
                    placementCount
                );

            if (refundAmount <= 0)
            {
                return false;
            }

            services.Economy.AddCoins(
                refundAmount,
                reason
            );

            Debug.Log(
                $"[ShopPurchaseService] " +
                $"상품 구매 환불 완료. " +
                $"상품: {item.DisplayName}, " +
                $"환불 코인: {refundAmount}",
                this
            );

            return true;
        }
    }
}