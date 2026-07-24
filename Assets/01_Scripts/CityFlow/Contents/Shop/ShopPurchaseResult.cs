namespace CityFlow.Content
{
    /// <summary>
    /// 상점 구매 요청의 처리 결과입니다.
    ///
    /// UI에서는 이 값을 확인하여
    /// 구매 성공, 코인 부족, 잠금 상태 등을 표시할 수 있습니다.
    /// </summary>
    public enum ShopPurchaseResult
    {
        Success = 0,
        ServiceUnavailable = 1,
        InvalidItem = 2,
        ItemLocked = 3,
        InvalidPrice = 4,
        InsufficientCoins = 5
    }
}