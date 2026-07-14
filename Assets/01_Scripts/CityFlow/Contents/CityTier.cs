namespace CityFlow.Content
{
    /// <summary>
    /// 도시의 성장 단계를 구분합니다.
    ///
    /// 도시 단계는 땅의 크기를 확장하는 용도가 아니라,
    /// 건물·도로·교통시설의 해금 조건으로 사용합니다.
    /// </summary>
    public enum CityTier
    {
        Village = 0,
        SmallCity = 1,
        MiddleCity = 2,
        BigCity = 3
    }
}