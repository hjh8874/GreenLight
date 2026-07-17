using NUnit.Framework;
using UnityEngine;
using CityFlow.Sim;

namespace CityFlow.Sim.Tests
{
    public class RoadQueueNetworkTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static SimConfig Cfg()
        {
            var c = SimConfig.Default();
            c.QueueCapacityPerTile = 4;
            return c;
        }

        [Test]
        public void Enqueue_UpToCapacity_ThenRejects()
        {
            var q = new RoadQueueNetwork(5, 5, Cfg());
            for (int i = 0; i < 4; i++)
            {
                Assert.IsTrue(
                    q.TryEnqueue(V(2, 2), Dir.E, carId: i),
                    $"{i}번째 인큐");
            }

            Assert.IsFalse(
                q.TryEnqueue(V(2, 2), Dir.E, 99),
                "용량 초과 거부");
            Assert.AreEqual(4, q.QueueCount(V(2, 2), Dir.E));
            Assert.AreEqual(0, q.CarAtHead(V(2, 2), Dir.E), "FIFO 머리");
        }

        [Test]
        public void Occupancy_IsMaxOverDirections()
        {
            var q = new RoadQueueNetwork(5, 5, Cfg());
            q.TryEnqueue(V(1, 1), Dir.N, 0);
            q.TryEnqueue(V(1, 1), Dir.N, 1);
            q.TryEnqueue(V(1, 1), Dir.E, 2);

            Assert.AreEqual(
                0.5f,
                q.MaxOccupancy01(V(1, 1)),
                1e-4f,
                "N큐 2/4가 최대");
        }

        [Test]
        public void DirectionQueues_AreIndependent()
        {
            var q = new RoadQueueNetwork(5, 5, Cfg());
            for (int i = 0; i < 4; i++)
            {
                q.TryEnqueue(V(3, 3), Dir.N, i);
            }

            Assert.IsTrue(
                q.TryEnqueue(V(3, 3), Dir.S, 10),
                "다른 방향 큐는 독립");
        }
    }
}
