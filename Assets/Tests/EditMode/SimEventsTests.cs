using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public class SimEventsTests
    {
        [Test]
        public void Queue_DoesNotInvoke_UntilDrain()
        {
            var events = new SimEvents();
            int calls = 0;
            events.OnArrival += _ => calls++;

            events.Queue(new ArrivalEvent { Sink = new Vector2Int(1, 2), Count = 3 });
            events.Queue(new ArrivalEvent { Sink = new Vector2Int(0, 0), Count = 1 });
            events.Queue(new ArrivalEvent { Sink = new Vector2Int(4, 3), Count = 2 });

            Assert.AreEqual(0, calls); // 계산 중엔 발행 안 됨
        }

        [Test]
        public void Drain_InvokesAllInQueueOrder()
        {
            var events = new SimEvents();
            var got = new List<int>();
            events.OnArrival += e => got.Add(e.Count);

            events.Queue(new ArrivalEvent { Count = 10 });
            events.Queue(new ArrivalEvent { Count = 20 });
            events.Queue(new ArrivalEvent { Count = 30 });
            events.Drain();

            Assert.AreEqual(new[] { 10, 20, 30 }, got); // 큐 넣은 순서대로
        }

        [Test]
        public void Drain_ClearsQueue_NoRedelivery()
        {
            var events = new SimEvents();
            int calls = 0;
            events.OnArrival += _ => calls++;

            events.Queue(new ArrivalEvent { Count = 1 });
            events.Drain();
            Assert.AreEqual(1, calls);

            events.Drain(); // 큐 비었으니 추가 발행 없음
            Assert.AreEqual(1, calls);
        }
    }
}
