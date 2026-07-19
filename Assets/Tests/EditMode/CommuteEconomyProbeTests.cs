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

        // 주: 캡 통일(work 슬롯 상한을 일자리 용량으로) 시도는 2026-07-18 되돌렸다 —
        // 슬롯 20이 되면 한 건물 타일에 20대가 주차해 뷰가 겹치고 도착 시 플래쉬가 생겼다(환 라이브).
        // 위 측정 테스트들은 그대로 유효(명시적 officeSlots 인자로 측정) — 경제 병목 사실은 여전하나,
        // 해법은 '뷰 주차 슬롯과 경제 일자리 수의 분리'가 선행돼야 한다.

        // ── 리빌드 연속성: 건설이 이동 중인 차를 주차장으로 텔레포트시키면 안 된다 ──────
        // 증상(환 라이브 2026-07-18): 무언가 배치할 때마다 도로 위 차가 사라졌다가 주차장에 생김.
        // 원인: CarSim.Rebuild가 무조건 _needsSnap=true → 다음 Step이 RemoveAllCars + 전 차량
        // SnapToHour. CommuteScheduler의 sticky 생존 매칭(State·Distance 보존)이 즉시 덮어써졌다.
        [Test]
        public void Rebuild_KeepsInTransitCars_NoTeleportToParking()
        {
            SimConfig cfg = SimConfig.Default();
            cfg.DemandChoicePool = 1;
            cfg.MorningStartHour = 6f;
            cfg.MorningEndHour = 6.01f;    // 출발 시각을 6.0으로 고정
            cfg.EveningStartHour = 17f;
            cfg.EveningEndHour = 18f;

            var grid = new CityGrid(12, 3);
            for (int x = 0; x <= 10; x++) Assert.IsTrue(grid.Place(V(x, 1), TileType.Road));
            Assert.IsTrue(grid.Place(V(0, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(11, 1), TileType.Office));

            var road = new RoadNetwork(grid);
            var demands = new DemandMap(cfg);
            demands.Reassign(grid, road);
            var planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demands, road, grid, cfg);

            var net = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            net.RebuildTopology(grid);
            var sim = new CarSim(cfg);
            sim.Rebuild(demands, planner, net);
            var events = new SimEventBuffer(new SimEventHub());

            for (int t = 0; t < 5; t++) sim.Step(6.5f, net, events);

            CarSnapshot before = sim.GetCar(0);
            Assert.AreEqual(CarState.Outbound, before.State, "사전조건: 차가 이동 중");
            Assert.Greater(before.TileIndex, 0, "사전조건: 도로 중간까지 진행");

            // 무관한 건설 = 토폴로지 리빌드 (경로는 그대로)
            sim.Rebuild(demands, planner, net);
            sim.Step(6.5f, net, events);

            CarSnapshot after = sim.GetCar(0);
            Assert.AreEqual(CarState.Outbound, after.State,
                "리빌드가 이동 중인 차를 주차 상태로 되돌리면 안 된다");
            Assert.GreaterOrEqual(after.TileIndex, before.TileIndex,
                "진행 위치가 보존(또는 전진)돼야 한다 — 출발점으로 되감기 금지");
        }
    }
}
