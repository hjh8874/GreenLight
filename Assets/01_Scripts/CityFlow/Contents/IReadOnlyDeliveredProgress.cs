using System;

namespace CityFlow.Content
{
    /// <summary>
    /// 외부 시스템이 누적 도착 진행도를
    /// 읽기 위한 읽기 전용 인터페이스입니다.
    ///
    /// 외부에서는 누적값을 직접 변경할 수 없으며,
    /// 현재 값 조회와 변경 이벤트만 사용할 수 있습니다.
    /// </summary>
    public interface IReadOnlyDeliveredProgress
    {
        /// <summary>
        /// 새 게임 시작 이후 누적된 전체 도착 횟수입니다.
        /// </summary>
        long LifetimeDeliveredTotal { get; }

        /// <summary>
        /// 누적 도착값이 변경되었을 때 발생합니다.
        /// 변경된 최신 누적값을 전달합니다.
        /// </summary>
        event Action<long> LifetimeDeliveredChanged;
    }
}