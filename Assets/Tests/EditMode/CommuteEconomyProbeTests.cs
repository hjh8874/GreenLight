using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    // 배정 ↔ 경제 연결 프로브 (2026-07-22 정원 단일화).
    // DemandMap 배정과 CommuteScheduler 주차가 같은 OfficeCapacity를 사용해
    // 배정됐지만 통근하지 못하는 유령 집이 다시 생기지 않는지 고정한다.
    public class CommuteEconomyProbeTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static void Rebuild(CommuteScheduler sched, List<Vector2Int> homes, List<Vector2Int> works, in SimConfig cfg)
        {
            int capacity = cfg.OfficeCapacity;
            CommuteWindow window = CommuteWindow.FromConfig(cfg);
            sched.Rebuild(homes, works,
                workCapacityFor: _ => capacity,
                windowFor: _ => window,
                homeSlots: cfg.CarsPerHouse,
                maxCars: cfg.MaxSimCars);
        }

        // 한 사무실로 30집이 몰려도 단일 회사 정원(6)으로 캡.
        [Test]
        public void OneOffice_ManyHomes_CommutersCappedByCompanyCapacity()
        {
            var cfg = SimConfig.Default();
            Assert.AreEqual(6, cfg.OfficeCapacity, "회사 배정·주차가 공유하는 단일 정원");

            var office = V(50, 50);
            var homes = new List<Vector2Int>(30);
            var works = new List<Vector2Int>(30);
            for (int i = 0; i < 30; i++) { homes.Add(V(i, 0)); works.Add(office); }

            var sched = new CommuteScheduler();
            Rebuild(sched, homes, works, cfg);

            Assert.AreEqual(cfg.OfficeCapacity, sched.Cars.Count,
                "30집이 한 사무실에 붙어도 통근은 회사 정원을 넘지 않음");

            long maxDailyCoinPerOffice = (long)sched.Cars.Count * cfg.CoinPerTrip;
            Assert.AreEqual(60L, maxDailyCoinPerOffice,
                "사무실 1개 하루 최대 코인 = 정원 6 × 도착 보상 10 = 60");
        }

        // 통근(=코인)의 실질 레버는 회사 정원 총합.
        [Test]
        public void CommuterCount_ScalesWithCompanyCapacitySum_NotHomeCount()
        {
            var cfg = SimConfig.Default();
            var homes = new List<Vector2Int>();
            var works = new List<Vector2Int>();
            for (int o = 0; o < 3; o++)
                for (int s = 0; s < 12; s++)   // 사무실당 12집 후보(슬롯 6의 2배)
                { homes.Add(V(o * 20 + s, 0)); works.Add(V(o * 20, 50)); }

            var sched = new CommuteScheduler();
            Rebuild(sched, homes, works, cfg);

            Assert.AreEqual(3 * cfg.OfficeCapacity, sched.Cars.Count,
                "36집 후보라도 통근은 3사무실 × 정원 6 = 18");
        }

        // ── 단핵도시(monocentric) 측정 하니스 ────────────────────────────────
        // 인천/김포 → 서울 패턴: 도심 오피스 클러스터 + 외곽 주거 링.
        // 배정 수·실현 통근 수·통근거리·일일 코인을 수치화한다.

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
        static CityMetrics MeasureMonocentric(in SimConfig cfg, int companyCapacity)
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
            CommuteWindow window = CommuteWindow.FromConfig(cfg);
            sched.Rebuild(homes, works, _ => companyCapacity, _ => window,
                cfg.CarsPerHouse, cfg.MaxSimCars);

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

        // 단일 정원 적용 후 배정 수와 실제 통근 수가 일치하는지 검증한다.
        [Test]
        public void Monocentric_UnifiedCapacity_HasNoGhostAssignments()
        {
            var cfg = SimConfig.Default();
            CityMetrics m = MeasureMonocentric(cfg, cfg.OfficeCapacity);
            Report("unified capacity=6", m);

            Assert.AreEqual(m.AssignedDemands, m.RealizedCommuters,
                "배정 정원과 주차 정원이 같으므로 유령 집이 없어야 한다");
            Assert.LessOrEqual(m.RealizedCommuters, OfficeCount * cfg.OfficeCapacity,
                "실현 통근 상한 = 오피스 9 × 정원 6 = 54");
        }

        // 명시적으로 더 작은 타일 정원을 전달해도 스케줄러가 그 정원을 그대로 지킨다.
        [Test]
        public void Monocentric_SmallerCapacity_ConstrainsAssignmentAndCoinTogether()
        {
            var cfg = SimConfig.Default();
            CityMetrics full = MeasureMonocentric(cfg, cfg.OfficeCapacity);
            SimConfig smallerConfig = cfg;
            smallerConfig.OfficeCapacity = 3;
            CityMetrics smaller = MeasureMonocentric(
                smallerConfig,
                smallerConfig.OfficeCapacity
            );
            Report("capacity=6", full);
            Report("capacity=3", smaller);

            Assert.Less(smaller.RealizedCommuters, full.RealizedCommuters);
            Assert.Less(smaller.DailyCoin, full.DailyCoin);
        }

        // 주: 리빌드 연속성 시도(건설 시 전 차량 스냅 → 신규만 스냅 + 재개 위치 복원)도
        // 2026-07-18 되돌렸다. 재인큐가 차 인덱스 순서(tail 삽입)라 같은 타일 두 차가 슬롯을
        // 맞바꿔 서로를 통과했고, 재인큐 실패 시 QueueSlot=-1 → 뷰가 거리 0(집)으로 그려
        // 경로 전체를 왕복하는 글리치가 건설할 때마다 터졌다. 재시도 시 물리적 순서 정렬과
        // 실패 시 이전 포즈 유지가 선행돼야 한다.
    }
}
