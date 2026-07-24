namespace CityFlow.Content
{
    /// <summary>
    /// 상점 상품의 가격 계산 방식을 구분합니다.
    /// </summary>
    public enum ShopPriceMode
    {
        /// <summary>
        /// 건물이나 시설처럼 한 개를 배치할 때
        /// 가격을 한 번만 지불합니다.
        /// </summary>
        Fixed = 0,

        /// <summary>
        /// 도로처럼 배치한 타일 수만큼
        /// 가격을 계산합니다.
        /// </summary>
        PerTile = 1
    }
}