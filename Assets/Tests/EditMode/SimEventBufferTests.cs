using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
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
    }
}
