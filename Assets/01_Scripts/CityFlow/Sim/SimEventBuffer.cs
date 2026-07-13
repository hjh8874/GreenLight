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
        readonly List<FlowBurstEvent> _bursts = new(16);
        readonly List<PlacedEvent> _placed = new(16);
        readonly List<CongestionEvent> _congestion = new(64);
        readonly List<SettlementEvent> _settlements = new(2);
        private readonly List<StabilityEvent> _stability = new(2);

        public SimEventBuffer(SimEventHub hub)
        {
            _hub = hub;
        }

        // 계산 중: 발행하지 않고 큐에만.
        internal void QueueArrival(in ArrivalEvent e) => _arrivals.Add(e);
        internal void QueueBurst(in FlowBurstEvent e) => _bursts.Add(e);
        internal void QueuePlaced(in PlacedEvent e) => _placed.Add(e);
        internal void QueueCongestion(in CongestionEvent e) => _congestion.Add(e);
        internal void QueueStability(in StabilityEvent e) => _stability.Add(e);
        internal void QueueSettlement(in SettlementEvent e) => _settlements.Add(e);

        // 틱 끝: 큐에 쌓인 순서대로 SimEventHub에 일괄 발행하고 비운다.
        // 발행 순서: 배치(원인) → 혼잡(도로 상태) → 도착·버스트(결과) — 구독자가 인과 순서로 받게.
        // 구독자(뷰/UI) 예외가 시뮬 틱과 다른 구독자를 죽이지 않게 격리 — 이벤트 유실·이중발행 방지(감사 2026-07-12).
        internal void Drain()
        {
            for (int i = 0; i < _placed.Count; i++)
            {
                try { _hub.Publish(_placed[i]); }
                catch (System.Exception ex) { UnityEngine.Debug.LogException(ex); }
            }
            _placed.Clear();

            for (int i = 0; i < _congestion.Count; i++)
            {
                try { _hub.Publish(_congestion[i]); }
                catch (System.Exception ex) { UnityEngine.Debug.LogException(ex); }
            }
            _congestion.Clear();

            for (int i = 0; i < _arrivals.Count; i++)
            {
                try { _hub.Publish(_arrivals[i]); }
                catch (System.Exception ex) { UnityEngine.Debug.LogException(ex); }
            }
            _arrivals.Clear();

            for (int i = 0; i < _bursts.Count; i++)
            {
                try { _hub.Publish(_bursts[i]); }
                catch (System.Exception ex) { UnityEngine.Debug.LogException(ex); }
            }
            _bursts.Clear();

            for (int i = 0; i < _settlements.Count; i++)
            {
                try { _hub.Publish(_settlements[i]); }
                catch (System.Exception ex) { UnityEngine.Debug.LogException(ex); }
            }
            _settlements.Clear();
            for (int i = 0; i < _stability.Count; i++)
            {
                try { _hub.Publish(_stability[i]); }
                catch (System.Exception ex) { UnityEngine.Debug.LogException(ex); }
            }
            _stability.Clear();
        }
    }
}
