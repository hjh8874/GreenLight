using System;
using System.Collections.Generic;

namespace CityFlow.Sim
{
    // 이벤트의 큐잉과 발행을 분리한다.
    // 계산 중엔 큐에만 담고(Queue), 틱 끝에 한꺼번에 발행(Drain)
    // → 구독자가 계산 중인 어중간한 상태를 읽는 재진입 버그를 구조적으로 차단(blueprint §0).
    public sealed class SimEvents
    {
        // 외부(한주석 뷰·한준희 정산)가 구독. 내부만 발행하고 외부는 += 로 듣기만.
        public event Action<ArrivalEvent> OnArrival;

        // 선할당(64): 이벤트가 쌓여도 배열 재할당(GC 스파이크) 없이 재사용.
        readonly List<ArrivalEvent> _arrivalQueue = new(64);

        // 계산 중: 발행하지 않고 큐에만.
        internal void Queue(in ArrivalEvent e) => _arrivalQueue.Add(e);

        // 틱 끝: 큐에 쌓인 순서대로 일괄 발행하고 비운다.
        internal void Drain()
        {
            var handler = OnArrival;
            if (handler != null)
            {
                for (int i = 0; i < _arrivalQueue.Count; i++)
                    handler(_arrivalQueue[i]);
            }
            _arrivalQueue.Clear();   // 구독자 없어도 큐는 항상 비운다(무한 증식 방지).
        }
    }
}
