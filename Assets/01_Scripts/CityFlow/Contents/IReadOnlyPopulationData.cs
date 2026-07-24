using System;

namespace CityFlow.Content
{
    /// <summary>
    /// 외부 시스템이 인구 값을 읽기 위한
    /// 읽기 전용 인터페이스입니다.
    ///
    /// 인구 값을 직접 변경하지 않고,
    /// 현재 인구 조회와 변경 이벤트만 제공합니다.
    /// </summary>
    public interface IReadOnlyPopulationData
    {
        int CurrentPopulation { get; }

        event Action<int> PopulationChanged;
    }
}