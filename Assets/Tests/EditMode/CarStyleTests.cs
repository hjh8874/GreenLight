using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CityFlow.ViewKit;

namespace CityFlow.Sim.Tests
{
    public class CarStyleTests
    {
        // 결정론: 같은 입력 = 항상 같은 프로파일.
        [Test]
        public void FromHash_Deterministic()
        {
            var a = CarStyle.FromHash(new Vector2Int(3, 7), 1);
            var b = CarStyle.FromHash(new Vector2Int(3, 7), 1);
            Assert.AreEqual(a.LengthScale, b.LengthScale);
            Assert.AreEqual(a.SpeedMul, b.SpeedMul);
            Assert.AreEqual(a.DepartDelaySec, b.DepartDelaySec);
            Assert.AreEqual(a.ColorIndex, b.ColorIndex);
        }

        // 범위: 100개 샘플 전 필드가 스펙 범위 안.
        [Test]
        public void FromHash_AllFieldsInRange()
        {
            for (int i = 0; i < 100; i++)
            {
                var s = CarStyle.FromHash(new Vector2Int(i % 20, i / 20), i % 3);
                Assert.That(s.LengthScale, Is.InRange(0.85f, 1.15f));
                Assert.That(s.WidthScale, Is.InRange(0.9f, 1.1f));
                Assert.That(s.SpeedMul, Is.InRange(0.9f, 1.1f));
                Assert.That(s.AccelMul, Is.InRange(0.9f, 1.1f));
                Assert.That(s.DepartDelaySec, Is.InRange(0.1f, 0.4f));
                Assert.That(s.ColorIndex, Is.InRange(0, CarStyle.Palette.Length - 1));
            }
        }

        // 구별성: 서로 다른 집 16개 → 최소 8개 구별 프로파일(길이·색 조합 기준).
        [Test]
        public void FromHash_ProfilesAreDiverse()
        {
            var seen = new HashSet<(float, int)>();
            for (int i = 0; i < 16; i++)
                { var s = CarStyle.FromHash(new Vector2Int(i, i * 3), 0); seen.Add((s.LengthScale, s.ColorIndex)); }
            Assert.GreaterOrEqual(seen.Count, 8, "프로파일 다양성 부족");
        }
    }
}
