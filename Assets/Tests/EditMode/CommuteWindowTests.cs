using NUnit.Framework;

namespace CityFlow.Sim.Tests
{
    public class CommuteWindowTests
    {
        [Test]
        public void InWindow_NormalRange_IsHalfOpen()
        {
            // [6, 15) — 시작 포함, 끝 배타
            Assert.IsTrue(CommuteWindow.InWindow(6f, 6f, 15f), "시작 시각 포함");
            Assert.IsTrue(CommuteWindow.InWindow(10f, 6f, 15f));
            Assert.IsTrue(CommuteWindow.InWindow(14.99f, 6f, 15f));
            Assert.IsFalse(CommuteWindow.InWindow(5.99f, 6f, 15f));
            Assert.IsFalse(CommuteWindow.InWindow(15f, 6f, 15f), "끝 시각은 배타");
            Assert.IsFalse(CommuteWindow.InWindow(20f, 6f, 15f));
        }

        [Test]
        public void InWindow_WrapsMidnight_WhenStartGreaterThanEnd()
        {
            // [20, 5) — 자정을 넘는 구간
            Assert.IsTrue(CommuteWindow.InWindow(20f, 20f, 5f), "시작 시각 포함");
            Assert.IsTrue(CommuteWindow.InWindow(23f, 20f, 5f));
            Assert.IsTrue(CommuteWindow.InWindow(0f, 20f, 5f), "자정 통과");
            Assert.IsTrue(CommuteWindow.InWindow(4.99f, 20f, 5f));
            Assert.IsFalse(CommuteWindow.InWindow(5f, 20f, 5f), "끝 시각은 배타");
            Assert.IsFalse(CommuteWindow.InWindow(10f, 20f, 5f));
            Assert.IsFalse(CommuteWindow.InWindow(19.99f, 20f, 5f));
        }

        [Test]
        public void InWindow_ZeroLength_IsAlwaysFalse()
        {
            // start == end 는 빈 구간으로 해석한다(통상 구간의 반개 규칙을 따름)
            Assert.IsFalse(CommuteWindow.InWindow(8f, 8f, 8f));
            Assert.IsFalse(CommuteWindow.InWindow(0f, 8f, 8f));
        }
    }
}
