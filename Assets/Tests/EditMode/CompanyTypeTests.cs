using System.Text.RegularExpressions;
using CityFlow.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public class CompanyTypeTests
    {
        static CompanyTypeInfo NewType(string id, float start, float end, int capacity = 6) =>
            new CompanyTypeInfo(new CommuteWindow(id, start, 4f, end, 4f), capacity);

        [Test]
        public void CompanyTypes_LookUpById_AndRejectUnknown()
        {
            var engine = new SimEngine(SimConfig.Default(), new SimEventHub());
            engine.SetCompanyTypes(new[] { NewType("office", 6f, 17f), NewType("factory", 20f, 5f) });

            Assert.IsTrue(engine.TryGetCompanyType("office", out CompanyTypeInfo office));
            Assert.AreEqual(6f, office.Window.StartHour);
            Assert.IsTrue(engine.TryGetCompanyType("factory", out CompanyTypeInfo factory));
            Assert.AreEqual(20f, factory.Window.StartHour, "공장은 야간 출근");
            Assert.AreEqual(5f, factory.Window.EndHour, "퇴근이 출근보다 이르다 = 자정을 넘는다");

            Assert.IsFalse(engine.TryGetCompanyType("warehouse", out _), "없는 id는 false");
            Assert.IsFalse(engine.TryGetCompanyType(null, out _), "null도 false");
            Assert.IsFalse(engine.TryGetCompanyType("", out _), "빈 문자열도 false");
        }

        [Test]
        public void FallbackWindow_ComesFromSimConfig()
        {
            SimConfig cfg = SimConfig.Default();
            var engine = new SimEngine(cfg, new SimEventHub());

            CommuteWindow w = engine.FallbackCommuteWindow();
            Assert.AreEqual(string.Empty, w.CompanyTypeId, "폴백은 무명 유형");
            Assert.AreEqual(cfg.MorningStartHour, w.StartHour);
            Assert.AreEqual(cfg.MorningEndHour - cfg.MorningStartHour, w.StartWindow);
            Assert.AreEqual(cfg.EveningStartHour, w.EndHour);
            Assert.AreEqual(cfg.EveningEndHour - cfg.EveningStartHour, w.EndWindow);
        }

        [Test]
        public void SetCompanyTypes_ReplacesTable_AndSkipsNamelessEntries()
        {
            var engine = new SimEngine(SimConfig.Default(), new SimEventHub());
            engine.SetCompanyTypes(new[] { NewType("office", 6f, 17f), NewType("  ", 6f, 17f) });
            Assert.AreEqual(1, engine.CompanyTypeCountForTest, "무명 유형은 표에 들어가지 않는다");

            engine.SetCompanyTypes(new[] { NewType("factory", 20f, 5f) });
            Assert.IsFalse(engine.TryGetCompanyType("office", out _), "재주입은 표를 교체한다");
            Assert.IsTrue(engine.TryGetCompanyType("factory", out _));

            engine.SetCompanyTypes(null);
            Assert.AreEqual(0, engine.CompanyTypeCountForTest, "null 은 표를 비운다");
        }

        static SimEngine NewEngineWithTypes()
        {
            SimConfig cfg = SimConfig.Default();
            cfg.GridWidth = 8;
            cfg.GridHeight = 4;
            var engine = new SimEngine(cfg, new SimEventHub());
            engine.SetCompanyTypes(new[] {
                NewType("factory",   20f, 5f,  capacity: 10),
                NewType("warehouse",  4f, 13f, capacity: 4),
            });
            return engine;
        }

        // 유형 미지정은 거부하지 않는다(환 결정 2026-07-30 — UI 상점이 3종으로 갈리므로
        // 미지정 경로 자체가 없어진다). 대신 등록되지 않은 id 는 경고를 남기고 폴백한다.
        [Test]
        public void PlaceOffice_StoresCompanyTypeId_AndTolerantOfMissingType()
        {
            SimEngine engine = NewEngineWithTypes();

            Assert.IsTrue(engine.Place(new Vector2Int(0, 0), TileType.Office,
                PlacementDirection.North, "factory"));
            Assert.IsTrue(engine.TryGetCompanyTypeIdForTest(new Vector2Int(0, 0), out string id));
            Assert.AreEqual("factory", id);

            Assert.IsTrue(engine.Place(new Vector2Int(4, 0), TileType.Office),
                "유형 미지정도 배치된다 — 종전 호출자가 깨지지 않는다");
            Assert.IsFalse(engine.TryGetCompanyTypeIdForTest(new Vector2Int(4, 0), out _),
                "유형 없는 회사는 폴백 창을 쓴다");
        }

        [Test]
        public void PlaceOffice_UnknownTypeId_WarnsAndFallsBack()
        {
            SimEngine engine = NewEngineWithTypes();

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new Regex("nope"));
            Assert.IsTrue(engine.Place(new Vector2Int(0, 0), TileType.Office,
                PlacementDirection.North, "nope"));
            Assert.IsFalse(engine.TryGetCompanyTypeIdForTest(new Vector2Int(0, 0), out _),
                "등록되지 않은 id 는 싣지 않는다");
        }

        [Test]
        public void CompanyCapacity_FollowsTypeDefinition()
        {
            SimEngine engine = NewEngineWithTypes();

            Assert.IsTrue(engine.Place(new Vector2Int(0, 0), TileType.Office,
                PlacementDirection.North, "factory"));
            Assert.IsTrue(engine.Place(new Vector2Int(4, 0), TileType.Office,
                PlacementDirection.North, "warehouse"));
            Assert.IsTrue(engine.Place(new Vector2Int(0, 2), TileType.Office));

            Assert.IsTrue(engine.TryGetCompanyStaffing(new Vector2Int(0, 0), out CompanyStaffing f));
            Assert.IsTrue(engine.TryGetCompanyStaffing(new Vector2Int(4, 0), out CompanyStaffing w));
            Assert.IsTrue(engine.TryGetCompanyStaffing(new Vector2Int(0, 2), out CompanyStaffing plain));
            Assert.AreEqual(10, f.Capacity, "공장은 유형 정원 10 — SimConfig.OfficeCapacity(6)에 깎이지 않는다");
            Assert.AreEqual(4, w.Capacity, "물류창고는 유형 정원 4");
            Assert.AreEqual(SimConfig.Default().OfficeCapacity, plain.Capacity, "유형 없으면 SimConfig 폴백");
        }

        // 배치 → DemandMap → 창 조회 사슬. CarSim 은 이 조회를 스케줄러에 콜백으로 넘긴다.
        [Test]
        public void CommuteWindowAt_UsesTypeWindow_AndFallsBackWithoutType()
        {
            SimEngine engine = NewEngineWithTypes();
            Assert.IsTrue(engine.Place(new Vector2Int(0, 0), TileType.Office,
                PlacementDirection.North, "factory"));
            Assert.IsTrue(engine.Place(new Vector2Int(4, 0), TileType.Office));

            CommuteWindow f = engine.CommuteWindowAtForTest(new Vector2Int(0, 0));
            Assert.AreEqual("factory", f.CompanyTypeId);
            Assert.AreEqual(20f, f.StartHour, "공장 출근 20시");
            Assert.AreEqual(5f, f.EndHour, "공장 퇴근 5시 — 자정 넘김");

            SimConfig cfg = SimConfig.Default();
            CommuteWindow plain = engine.CommuteWindowAtForTest(new Vector2Int(4, 0));
            Assert.AreEqual(string.Empty, plain.CompanyTypeId, "유형 없으면 폴백 창");
            Assert.AreEqual(cfg.MorningStartHour, plain.StartHour);
            Assert.AreEqual(cfg.EveningStartHour, plain.EndHour);
        }

        // 완성된 회사의 유형이 세이브에 실려야 한다. 없으면 로드 후 전부 폴백 창으로 되돌아간다
        // (RegisterRestoredCompany 가 타일 목록에서 회사를 다시 만드는 구조라 조용히 사라진다).
        [Test]
        public void CompanyType_SurvivesSaveRoundTrip_WhenAlreadyBuilt()
        {
            SimEngine engine = NewEngineWithTypes();
            Assert.IsTrue(engine.Place(new Vector2Int(0, 0), TileType.Office,
                PlacementDirection.North, "factory"));

            SimConfig cfg = SimConfig.Default();
            cfg.GridWidth = 8;
            cfg.GridHeight = 4;
            var restored = new SimEngine(cfg, new SimEventHub());
            restored.SetCompanyTypes(new[] { NewType("factory", 20f, 5f, capacity: 10) });
            restored.RestoreSnapshot(engine.CreateSnapshot());

            Assert.IsTrue(restored.TryGetCompanyTypeIdForTest(new Vector2Int(0, 0), out string id));
            Assert.AreEqual("factory", id, "로드 후에도 공장이다");
            Assert.IsTrue(restored.TryGetCompanyStaffing(new Vector2Int(0, 0), out CompanyStaffing f));
            Assert.AreEqual(10, f.Capacity, "유형 정원도 함께 복원된다");
            Assert.AreEqual(20f, restored.CommuteWindowAtForTest(new Vector2Int(0, 0)).StartHour);
        }

        // 공사 중 저장 → 로드 → 완성. 유형을 안 실으면 완성 시 전부 사무실이 된다(설계 결정 ④).
        [Test]
        public void CompanyType_SurvivesConstruction_AndSaveRoundTrip()
        {
            SimConfig cfg = SimConfig.Default();
            cfg.GridWidth = 8;
            cfg.GridHeight = 4;
            cfg.DayLengthSeconds = 24f;      // 1 게임시간 = 1 시뮬초
            cfg.TickInterval = 0.25f;
            cfg.ConstructionHoursOffice = 2f;   // 2 게임시간 = 2 시뮬초 = 8틱
            var engine = new SimEngine(cfg, new SimEventHub());
            engine.SetCompanyTypes(new[] { NewType("factory", 20f, 5f, capacity: 10) });

            Assert.IsTrue(engine.Place(new Vector2Int(4, 0), TileType.Office,
                PlacementDirection.North, "factory"));
            Assert.AreEqual(TileType.UnderConstruction, engine.GetTileType(new Vector2Int(4, 0)));

            var restored = new SimEngine(cfg, new SimEventHub());
            restored.SetCompanyTypes(new[] { NewType("factory", 20f, 5f, capacity: 10) });
            restored.RestoreSnapshot(engine.CreateSnapshot());
            Assert.AreEqual(TileType.UnderConstruction, restored.GetTileType(new Vector2Int(4, 0)),
                "공사 중 상태로 복원된다");

            for (int i = 0; i < 12; i++) restored.Tick(0.25f);

            Assert.AreEqual(TileType.Office, restored.GetTileType(new Vector2Int(4, 0)), "완성됨");
            Assert.IsTrue(restored.TryGetCompanyTypeIdForTest(new Vector2Int(4, 0), out string id));
            Assert.AreEqual("factory", id, "공사·세이브를 거쳐도 사무실로 되돌아가지 않는다");
        }

        // ApplyConfig 재적용이 유형 정원을 SimConfig 상한으로 깎지 않는다(조용한 축소 방지).
        [Test]
        public void ApplyConfig_KeepsTypeCapacity()
        {
            SimEngine engine = NewEngineWithTypes();
            Assert.IsTrue(engine.Place(new Vector2Int(0, 0), TileType.Office,
                PlacementDirection.North, "factory"));

            SimConfig next = SimConfig.Default();
            next.GridWidth = 8;
            next.GridHeight = 4;
            Assert.IsTrue(engine.ApplyConfig(next));

            Assert.IsTrue(engine.TryGetCompanyStaffing(new Vector2Int(0, 0), out CompanyStaffing f));
            Assert.AreEqual(10, f.Capacity, "재적용 후에도 공장 정원 10");
        }
    }
}
