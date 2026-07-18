using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    // 배정 ↔ 경제 연결 프로브 (2026-07-18). 코드 무변경 특성화 테스트.
    // 목적: "회사 배정 용량"과 "실제 통근(=코인 지급 횟수)" 사이의 캡 결합을 수치로 고정한다.
    //
    // 발견: 배정 캡이 두 레이어에 나뉘어 있고 서로 대화하지 않는다.
    //  - DemandMap.AssignType : 사무실당 OfficeCapacity(기본 20)까지 (집,회사) 짝을 만든다.
    //  - CommuteScheduler.Rebuild : 그 짝들 중 OfficeParkingSlots(기본 6)만 WorkSlot을 얻어 실제 통근.
    // 코인 = 회사 도착 1회당 flat CoinPerTrip 이므로, 한 사무실의 하루 최대 코인 = (통근 차 수) × CoinPerTrip.
    // 결론: 코인의 실질 레버는 OfficeCapacity(배정 20)가 아니라 OfficeParkingSlots(통근 6) + 사무실 개수다.
    public class CommuteEconomyProbeTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static void Rebuild(CommuteScheduler sched, List<Vector2Int> homes, List<Vector2Int> works, in SimConfig cfg)
            => sched.Rebuild(homes, works,
                officeSlots: cfg.OfficeParkingSlots,
                homeSlots: cfg.CarsPerHouse,
                maxCars: cfg.MaxSimCars,
                morningStart: cfg.MorningStartHour, morningEnd: cfg.MorningEndHour,
                eveningStart: cfg.EveningStartHour, eveningEnd: cfg.EveningEndHour);

        // 한 사무실로 30집이 몰려도 실제 통근은 OfficeParkingSlots(6)로 캡.
        // → 사무실 배정 용량(OfficeCapacity)을 아무리 키워도 이 코인 상한은 안 움직인다.
        [Test]
        public void OneOffice_ManyHomes_CommutersCappedByParkingSlots()
        {
            var cfg = SimConfig.Default();
            Assert.AreEqual(6, cfg.OfficeParkingSlots, "가정: 주차 슬롯 6");
            Assert.AreEqual(20, cfg.OfficeCapacity, "가정: 배정 용량 20 (통근 캡과 3배 넘게 어긋남)");

            var office = V(50, 50);
            var homes = new List<Vector2Int>(30);
            var works = new List<Vector2Int>(30);
            for (int i = 0; i < 30; i++) { homes.Add(V(i, 0)); works.Add(office); }

            var sched = new CommuteScheduler();
            Rebuild(sched, homes, works, cfg);

            Assert.AreEqual(cfg.OfficeParkingSlots, sched.Cars.Count,
                "30집이 한 사무실에 붙어도 통근은 6대 — 24집은 그날 통근·수입 0");

            long maxDailyCoinPerOffice = (long)sched.Cars.Count * cfg.CoinPerTrip;
            Assert.AreEqual(60L, maxDailyCoinPerOffice,
                "사무실 1개 하루 최대 코인 = 6 × 10 = 60. 배정 용량 20은 코인에 무영향(inert).");
        }

        // 대조군: 통근(=코인)의 실질 레버는 '사무실 주차슬롯 총합'.
        // 사무실 3개 × 6슬롯 = 18 통근으로 선형 증가(같은 36집 후보라도 사무실 수가 좌우).
        [Test]
        public void CommuterCount_ScalesWithOfficeSlotSum_NotHomeCount()
        {
            var cfg = SimConfig.Default();
            var homes = new List<Vector2Int>();
            var works = new List<Vector2Int>();
            for (int o = 0; o < 3; o++)
                for (int s = 0; s < 12; s++)   // 사무실당 12집 후보(슬롯 6의 2배)
                { homes.Add(V(o * 20 + s, 0)); works.Add(V(o * 20, 50)); }

            var sched = new CommuteScheduler();
            Rebuild(sched, homes, works, cfg);

            Assert.AreEqual(3 * cfg.OfficeParkingSlots, sched.Cars.Count,
                "36집 후보라도 통근은 3사무실 × 6슬롯 = 18 — 레버는 사무실 슬롯 총합");
        }
    }
}
