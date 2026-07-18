using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

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

        // ── 단핵도시(monocentric) 측정 하니스 ────────────────────────────────
        // 인천/김포 → 서울 패턴: 도심 오피스 클러스터 + 외곽 주거 링.
        // 배정 수·실현 통근 수·통근거리·일일 코인을 수치화해 캡 통일 전후를 비교한다.

        const int CityN = 20;
        const int CoreLo = 9, CoreHi = 11;              // 도심 3x3 오피스
        const int OfficeCount = (CoreHi - CoreLo + 1) * (CoreHi - CoreLo + 1);   // 9

        struct CityMetrics
        {
            public int AssignedDemands;     // DemandMap이 만든 (집,회사) 짝
            public int RealizedCommuters;   // CommuteScheduler가 실제 통근시킨 차 (= 코인 지급 횟수)
            public float AvgDistance;       // 실현 통근의 평균 맨해튼 거리
            public int MaxDistance;
            public long DailyCoin;          // 실현 × CoinPerTrip
        }

        // 도심 오피스 + 외곽 주거 링 + 나머지 전부 도로(완전 연결 — 배정이 순수 거리·용량으로 결정되게).
        static CityMetrics MeasureMonocentric(in SimConfig cfg, int officeSlots)
        {
            var g = new CityGrid(CityN, CityN);
            for (int y = CoreLo; y <= CoreHi; y++)
                for (int x = CoreLo; x <= CoreHi; x++)
                    g.Place(V(x, y), TileType.Office);

            for (int i = 0; i < CityN; i++)
            {
                PlaceIfEmpty(g, V(i, 0), TileType.House);
                PlaceIfEmpty(g, V(i, CityN - 1), TileType.House);
                PlaceIfEmpty(g, V(0, i), TileType.House);
                PlaceIfEmpty(g, V(CityN - 1, i), TileType.House);
            }

            for (int y = 0; y < CityN; y++)
                for (int x = 0; x < CityN; x++)
                    PlaceIfEmpty(g, V(x, y), TileType.Road);

            var dm = new DemandMap(cfg);
            dm.Reassign(g, new RoadNetwork(g));

            var homes = new List<Vector2Int>();
            var works = new List<Vector2Int>();
            foreach (var d in dm.Demands) { homes.Add(d.Source); works.Add(d.Sink); }

            var sched = new CommuteScheduler();
            sched.Rebuild(homes, works, officeSlots, cfg.CarsPerHouse, cfg.MaxSimCars,
                cfg.MorningStartHour, cfg.MorningEndHour, cfg.EveningStartHour, cfg.EveningEndHour);

            long sum = 0;
            int max = 0;
            foreach (var car in sched.Cars)
            {
                int d = Mathf.Abs(car.Home.x - car.Work.x) + Mathf.Abs(car.Home.y - car.Work.y);
                sum += d;
                if (d > max) max = d;
            }

            int n = sched.Cars.Count;
            return new CityMetrics
            {
                AssignedDemands = dm.Demands.Count,
                RealizedCommuters = n,
                AvgDistance = n == 0 ? 0f : (float)sum / n,
                MaxDistance = max,
                DailyCoin = (long)n * cfg.CoinPerTrip,
            };
        }

        static void PlaceIfEmpty(CityGrid g, Vector2Int p, TileType t)
        {
            if (g.GetTile(p) == TileType.Empty) g.Place(p, t);
        }

        static void Report(string label, in CityMetrics m) =>
            TestContext.WriteLine(
                $"[{label}] assigned={m.AssignedDemands} realized={m.RealizedCommuters} " +
                $"avgDist={m.AvgDistance:F2} maxDist={m.MaxDistance} dailyCoin={m.DailyCoin}");

        // 베이스라인: 현재 캡(슬롯 6)이 단핵도시에서 통근·코인을 얼마나 조르는가.
        [Test]
        public void Monocentric_Baseline_SlotsThrottleRealizedCommuters()
        {
            var cfg = SimConfig.Default();
            CityMetrics m = MeasureMonocentric(cfg, cfg.OfficeParkingSlots);
            Report("baseline slots=6", m);

            Assert.Greater(m.AssignedDemands, m.RealizedCommuters,
                "배정됐지만 슬롯 부족으로 통근 못 하는 집이 존재해야 한다(현 캡 불일치)");
            Assert.LessOrEqual(m.RealizedCommuters, OfficeCount * cfg.OfficeParkingSlots,
                "실현 통근 상한 = 오피스 9 × 슬롯 6 = 54");
        }

        // 캡을 배정 용량(OfficeCapacity)으로 통일하면 통근·코인이 얼마나 열리는가.
        [Test]
        public void Monocentric_UnifiedCap_UnlocksCommutersAndCoin()
        {
            var cfg = SimConfig.Default();
            CityMetrics base6 = MeasureMonocentric(cfg, cfg.OfficeParkingSlots);
            CityMetrics unified = MeasureMonocentric(cfg, cfg.OfficeCapacity);
            Report("baseline slots=6", base6);
            Report("unified slots=20", unified);

            Assert.Greater(unified.RealizedCommuters, base6.RealizedCommuters,
                "캡 통일 시 실현 통근이 늘어야 한다 — 이게 A안(단핵)의 레버");
            Assert.Greater(unified.DailyCoin, base6.DailyCoin, "일일 코인도 함께 열려야 한다");
        }

        // ── 스텝 1: 캡 통일 (일자리 수 = 실제 통근 수) ────────────────────────
        // CarSim이 CommuteScheduler에 넘기는 work 슬롯 상한이 '주차 슬롯'이 아니라
        // '일자리 용량(OfficeCapacity/SchoolCapacity)'을 따라야 한다.
        // 그래야 도심 오피스에 배정된 집이 슬롯 부족으로 사장되지 않는다.
        [Test]
        public void CarSim_WorkSlots_FollowJobCapacity_NotParkingSlots()
        {
            SimConfig cfg = SimConfig.Default();
            cfg.DemandChoicePool = 1;
            const int Houses = 8;   // ParkingSlots(6) 초과, OfficeCapacity(20) 이내

            var grid = new CityGrid(12, 3);
            for (int x = 0; x <= 10; x++) Assert.IsTrue(grid.Place(V(x, 1), TileType.Road));
            for (int x = 0; x < Houses; x++) Assert.IsTrue(grid.Place(V(x, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(11, 1), TileType.Office));

            var road = new RoadNetwork(grid);
            var demands = new DemandMap(cfg);
            demands.Reassign(grid, road);
            Assert.AreEqual(Houses, demands.Demands.Count, "8집 전부 한 오피스에 배정(용량 20)");

            var planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demands, road, grid, cfg);

            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);
            var sim = new CarSim(cfg);
            sim.Rebuild(demands, planner, net);

            Assert.AreEqual(Houses, sim.CarCount,
                "일자리 8개면 통근 차도 8대여야 한다 — 주차슬롯(6)이 경제를 조르면 안 된다");
        }
    }
}
