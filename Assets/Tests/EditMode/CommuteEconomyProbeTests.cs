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

        // 주: 리빌드 연속성 시도(건설 시 전 차량 스냅 → 신규만 스냅 + 재개 위치 복원)도
        // 2026-07-18 되돌렸다. 재인큐가 차 인덱스 순서(tail 삽입)라 같은 타일 두 차가 슬롯을
        // 맞바꿔 서로를 통과했고, 재인큐 실패 시 QueueSlot=-1 → 뷰가 거리 0(집)으로 그려
        // 경로 전체를 왕복하는 글리치가 건설할 때마다 터졌다. 재시도 시 물리적 순서 정렬과
        // 실패 시 이전 포즈 유지가 선행돼야 한다.
    }
}
