using NUnit.Framework;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.TestTools;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class SimEventBufferTests
    {
        [Test]
        public void Queue_DoesNotPublish_UntilDrain()
        {
            var hub = new SimEventHub();
            int calls = 0;
            hub.Arrival += _ => calls++;
            var buf = new SimEventBuffer(hub);

            buf.QueueArrival(new ArrivalEvent(new Vector2Int(1, 2), 3));
            buf.QueueArrival(new ArrivalEvent(new Vector2Int(0, 0), 1));

            Assert.AreEqual(0, calls); // 계산 중엔 발행 안 됨
        }

        [Test]
        public void Drain_PublishesAllInOrder()
        {
            var hub = new SimEventHub();
            var got = new List<int>();
            hub.Arrival += e => got.Add(e.Coins);
            var buf = new SimEventBuffer(hub);

            buf.QueueArrival(new ArrivalEvent(Vector2Int.zero, 10));
            buf.QueueArrival(new ArrivalEvent(Vector2Int.zero, 20));
            buf.QueueArrival(new ArrivalEvent(Vector2Int.zero, 30));
            buf.Drain();

            Assert.AreEqual(new[] { 10, 20, 30 }, got); // 큐 넣은 순서대로
        }

        [Test]
        public void Drain_ClearsQueue_NoRepublish()
        {
            var hub = new SimEventHub();
            int calls = 0;
            hub.Arrival += _ => calls++;
            var buf = new SimEventBuffer(hub);

            buf.QueueArrival(new ArrivalEvent(Vector2Int.zero, 1));
            buf.Drain();
            Assert.AreEqual(1, calls);

            buf.Drain(); // 큐 비었으니 추가 발행 없음
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void Drain_SubscriberException_IsolatesOtherEventCategories_NoDoublePublishNextDrain()
        {
            // 구독자(뷰/UI) 예외가 시뮬 틱과 다른 구독자를 죽이지 않게 격리 —
            // 이벤트 유실·이중발행 방지(감사 2026-07-12). Arrival 구독자가 죽어도 같은 Drain의
            // Burst 구독자는 정상 수신해야 하고, 큐는 예외와 무관하게 비워져야 한다.
            var hub = new SimEventHub();
            hub.Arrival += _ => throw new System.Exception("Arrival 구독자 고장");
            int bursts = 0;
            hub.FlowBurst += _ => bursts++;
            var buf = new SimEventBuffer(hub);

            buf.QueueArrival(new ArrivalEvent(Vector2Int.zero, 1));
            buf.QueueBurst(new FlowBurstEvent(Vector2Int.zero, 5));
            LogAssert.Expect(LogType.Exception, new Regex(".*"));
            Assert.DoesNotThrow(() => buf.Drain());
            Assert.AreEqual(1, bursts);   // Arrival 구독자가 죽어도 Burst 구독자는 이벤트를 받음

            buf.QueueArrival(new ArrivalEvent(Vector2Int.zero, 2));
            buf.QueueBurst(new FlowBurstEvent(Vector2Int.zero, 7));
            LogAssert.Expect(LogType.Exception, new Regex(".*"));
            buf.Drain();
            Assert.AreEqual(2, bursts);   // 다음 틱에 이중 발행 없음(정상적으로 1회만 증가)
        }
    }
}
