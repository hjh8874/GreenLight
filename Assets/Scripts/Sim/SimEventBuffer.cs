using System.Collections.Generic;
using CityFlow.Contracts;

namespace CityFlow.Sim
{
    // 이벤트 큐잉과 발행을 분리한다.
    // 계산 중엔 큐에만 담고(QueueArrival), 틱 끝에 Drain으로 주석님 SimEventHub에 일괄 발행
    // → 구독자가 계산 중인 어중간한 상태를 읽는 재진입 버그를 구조적으로 차단(blueprint §0).
    internal sealed class SimEventBuffer
    {
        readonly SimEventHub _hub;
        readonly List<ArrivalEvent> _arrivals = new(64);   // 선할당(GC 회피)

        public SimEventBuffer(SimEventHub hub)
        {
            _hub = hub;
        }

        // 계산 중: 발행하지 않고 큐에만.
        internal void QueueArrival(in ArrivalEvent e) => _arrivals.Add(e);

        // 틱 끝: 큐에 쌓인 순서대로 SimEventHub에 일괄 발행하고 비운다.
        internal void Drain()
        {
            for (int i = 0; i < _arrivals.Count; i++)
                _hub.Publish(_arrivals[i]);
            _arrivals.Clear();
        }
    }
}
