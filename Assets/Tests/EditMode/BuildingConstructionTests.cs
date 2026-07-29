using CityFlow.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public class BuildingConstructionTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // CarSimEngineTests.Cfg()와 같은 형태. DayLengthSeconds=24 → 1 게임시간 = 1 시뮬초.
        static SimConfig Cfg()
        {
            SimConfig cfg = SimConfig.Default();
            cfg.GridWidth = 8;
            cfg.GridHeight = 4;
            cfg.TickInterval = 0.25f;
            cfg.MaxStepsPerFrame = 20;
            cfg.DayLengthSeconds = 24f;
            cfg.CompanyHiringSlotsPerGameHour = 100f;
            return cfg;
        }

        [Test]
        public void Promote_ReplacesFootprintTypeAndKeepsAnchorAndDirection()
        {
            var grid = new CityGrid(8, 4);
            Assert.IsTrue(grid.Place(V(0, 0), TileType.UnderConstruction, PlacementDirection.East));

            Assert.IsTrue(grid.Promote(V(0, 0), TileType.House));

            // 2x2 풋프린트 전체가 교체된다
            Assert.AreEqual(TileType.House, grid.GetTile(V(0, 0)));
            Assert.AreEqual(TileType.House, grid.GetTile(V(1, 0)));
            Assert.AreEqual(TileType.House, grid.GetTile(V(0, 1)));
            Assert.AreEqual(TileType.House, grid.GetTile(V(1, 1)));
            // 방향과 앵커는 보존된다
            Assert.AreEqual(PlacementDirection.East, grid.GetDirection(V(1, 1)));
            Assert.IsTrue(grid.TryGetFootprintAnchor(V(1, 1), out Vector2Int anchor));
            Assert.AreEqual(V(0, 0), anchor);
        }

        [Test]
        public void Promote_ReturnsFalseForNonAnchorOrEmptyTile()
        {
            var grid = new CityGrid(8, 4);
            Assert.IsTrue(grid.Place(V(0, 0), TileType.UnderConstruction));

            Assert.IsFalse(grid.Promote(V(1, 1), TileType.House), "앵커가 아닌 타일은 거부");
            Assert.IsFalse(grid.Promote(V(5, 3), TileType.House), "빈 타일은 거부");
            Assert.IsFalse(grid.Promote(V(-1, 0), TileType.House), "격자 밖은 거부");
        }

        // Place/TryRemove/Clear 만 _roadTileIndices 를 유지하는데 Promote 는 _tiles 를 직접 쓴다.
        // 도로가 승격에 끼면 그 인덱스가 조용히 어긋나 RoadTileCount 가 틀어진다.
        [Test]
        public void Promote_RejectsRoad_AndKeepsRoadTileCountIntact()
        {
            var grid = new CityGrid(8, 4);
            Assert.IsTrue(grid.Place(V(3, 3), TileType.Road));
            Assert.IsTrue(grid.Place(V(0, 0), TileType.UnderConstruction));
            int roadsBefore = grid.RoadTileCount;

            Assert.IsFalse(grid.Promote(V(3, 3), TileType.House),
                "도로를 건물로 승격할 수 없다");
            Assert.IsFalse(grid.Promote(V(0, 0), TileType.Road),
                "건물을 도로로 승격할 수 없다");

            Assert.AreEqual(TileType.Road, grid.GetTile(V(3, 3)), "거부되면 원본 불변");
            Assert.AreEqual(TileType.UnderConstruction, grid.GetTile(V(0, 0)), "거부되면 원본 불변");
            Assert.AreEqual(roadsBefore, grid.RoadTileCount,
                "도로 인덱스가 어긋나지 않는다");
        }

        [Test]
        public void Promote_WhenTargetFootprintExceedsSource_ReturnsFalseWithoutPartialWrite()
        {
            var grid = new CityGrid(8, 4);
            Vector2Int edge = V(7, 3);
            Assert.IsTrue(grid.Place(edge, TileType.Road));

            Assert.IsFalse(grid.Promote(edge, TileType.House));
            Assert.AreEqual(TileType.Road, grid.GetTile(edge), "실패한 승격은 원래 타일을 변경하지 않아야 한다");
        }

        [Test]
        public void Building_StaysUnderConstruction_UntilDurationElapses()
        {
            SimConfig cfg = Cfg();
            cfg.ConstructionHoursHouse = 2f;   // 1게임시간=1시뮬초 → 2초 = 8틱
            var engine = new SimEngine(cfg, new SimEventHub());

            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));
            Assert.AreEqual(TileType.UnderConstruction, engine.GetTileType(V(0, 0)),
                "배치 직후는 공사 중");

            for (int i = 0; i < 7; i++) engine.Tick(0.25f);
            Assert.AreEqual(TileType.UnderConstruction, engine.GetTileType(V(0, 0)),
                "7틱(1.75초)까지는 미완");

            engine.Tick(0.25f);
            Assert.AreEqual(TileType.House, engine.GetTileType(V(0, 0)),
                "8틱(2.0초)에 완성");
        }

        [Test]
        public void ZeroConstructionHours_CompletesImmediately()
        {
            SimConfig cfg = Cfg();
            cfg.ConstructionHoursHouse = 0f;
            var engine = new SimEngine(cfg, new SimEventHub());

            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));
            Assert.AreEqual(TileType.House, engine.GetTileType(V(0, 0)),
                "0 이하 = 즉시 완성 (구 config·미기입 자산 방어)");
        }

        [Test]
        public void UnderConstructionBuilding_ProducesNoCommute()
        {
            SimConfig cfg = Cfg();
            cfg.ConstructionHoursHouse = 100f;
            cfg.ConstructionHoursOffice = 100f;
            var engine = new SimEngine(cfg, new SimEventHub());
            for (int x = 0; x <= 7; x++) Assert.IsTrue(engine.Place(V(x, 2), TileType.Road));
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));
            Assert.IsTrue(engine.Place(V(4, 0), TileType.Office));
            engine.SetGameHour(7f);

            for (int i = 0; i < 12; i++) engine.Tick(0.25f);

            Assert.AreEqual(0, engine.ActiveVehicleCount,
                "공사 중 건물은 통근을 만들지 않는다 — 소비자 수정 없이 데이터로 강제됨");
        }

        [Test]
        public void UnderConstructionTile_RejectsOverlappingPlacement()
        {
            SimConfig cfg = Cfg();
            cfg.ConstructionHoursHouse = 100f;
            var engine = new SimEngine(cfg, new SimEventHub());
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));

            Assert.IsFalse(engine.Place(V(0, 0), TileType.Office), "같은 앵커 중복 배치 불가");
            Assert.IsFalse(engine.Place(V(1, 1), TileType.Office), "2x2 풋프린트 겹침도 불가");
        }

        [Test]
        public void PublicPlacement_RejectsDirectUnderConstructionWithoutOrphanSaveData()
        {
            SimConfig cfg = Cfg();
            var engine = new SimEngine(cfg, new SimEventHub());
            Vector2Int tile = V(0, 0);

            Assert.IsFalse(engine.CanPlace(tile, TileType.UnderConstruction));
            Assert.IsFalse(engine.Place(tile, TileType.UnderConstruction));
            Assert.AreEqual(TileType.Empty, engine.GetTileType(tile));
            Assert.AreEqual(0, engine.ConstructionSiteCountForTest);

            CityFlow.Contracts.Save.SimSaveData snapshot =
                engine.CreateSnapshot();
            Assert.That(snapshot.PlacedTiles, Is.Empty);
            Assert.That(snapshot.Constructions, Is.Empty);
        }

        [Test]
        public void OfficeHiringRamp_StartsAtCompletion_NotPlacement()
        {
            SimConfig cfg = Cfg();
            cfg.ConstructionHoursOffice = 2f;   // 8틱
            var engine = new SimEngine(cfg, new SimEventHub());

            Assert.IsTrue(engine.Place(V(4, 0), TileType.Office));
            Assert.IsFalse(engine.TryGetCompanyStaffing(V(4, 0), out _),
                "공사 중엔 회사로 등록되지 않는다");

            for (int i = 0; i < 8; i++) engine.Tick(0.25f);

            Assert.IsTrue(engine.TryGetCompanyStaffing(V(4, 0), out CompanyStaffing staffing),
                "완성 시각부터 회사로 등록 — 공사와 채용 램프가 직렬로 이어진다");
            Assert.AreEqual(cfg.OfficeCapacity, staffing.Capacity);
        }

        [Test]
        public void Construction_DoesNotAdvanceWithoutTicks()
        {
            SimConfig cfg = Cfg();
            cfg.ConstructionHoursHouse = 2f;   // 8틱
            var engine = new SimEngine(cfg, new SimEventHub());
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));

            for (int i = 0; i < 4; i++) engine.Tick(0.25f);   // 절반만 진행

            // Tick을 부르지 않는 동안(= 게임이 꺼진 동안) 아무리 시간이 흘러도 진행이 없다.
            // _simTime은 Step()에서만 증가하므로 오프라인 정지가 자동 성립한다.
            engine.SetGameHour(23f);   // 게임 시각만 크게 움직여도
            Assert.AreEqual(TileType.UnderConstruction, engine.GetTileType(V(0, 0)),
                "Tick 없이는 공사가 진행되지 않는다 — 오프라인 정지");

            for (int i = 0; i < 4; i++) engine.Tick(0.25f);
            Assert.AreEqual(TileType.House, engine.GetTileType(V(0, 0)),
                "틱이 재개되면 남은 만큼만 진행해 완성");
        }

        [Test]
        public void RemovingUnderConstruction_ClearsSiteAndDoesNotResurrect()
        {
            SimConfig cfg = Cfg();
            cfg.ConstructionHoursHouse = 2f;   // 8틱
            var engine = new SimEngine(cfg, new SimEventHub());
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));
            Assert.AreEqual(1, engine.ConstructionSiteCountForTest, "배치 직후 공사 사이트 1건");

            Assert.IsTrue(engine.Remove(V(1, 1)), "풋프린트 비앵커 타일 철거도 앵커 사이트를 지워야 한다");
            Assert.AreEqual(0, engine.ConstructionSiteCountForTest, "철거 시 사이트가 즉시 제거된다");
            Assert.AreEqual(TileType.Empty, engine.GetTileType(V(0, 0)));

            for (int i = 0; i < 20; i++) engine.Tick(0.25f);

            Assert.AreEqual(TileType.Empty, engine.GetTileType(V(0, 0)),
                "철거된 사이트는 완성 시각이 지나도 되살아나지 않는다");
            Assert.AreEqual(0, engine.ConstructionSiteCountForTest, "좀비 사이트가 남지 않는다");
        }

        [Test]
        public void Construction_SurvivesSaveRoundTrip_WithRemainingTime()
        {
            SimConfig cfg = Cfg();
            cfg.ConstructionHoursHouse = 4f;   // 4초 = 16틱
            var engine = new SimEngine(cfg, new SimEventHub());
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));
            for (int i = 0; i < 8; i++) engine.Tick(0.25f);   // 절반(2초) 진행

            CityFlow.Contracts.Save.SimSaveData snap = engine.CreateSnapshot();
            Assert.AreEqual(1, snap.Constructions.Length);
            Assert.AreEqual(2f, snap.Constructions[0].RemainingSimSeconds, 0.01f,
                "절대 완료시각이 아니라 잔여시간으로 저장한다");

            var restored = new SimEngine(cfg, new SimEventHub());
            restored.RestoreSnapshot(snap);
            Assert.AreEqual(TileType.UnderConstruction, restored.GetTileType(V(0, 0)));

            for (int i = 0; i < 7; i++) restored.Tick(0.25f);
            Assert.AreEqual(TileType.UnderConstruction, restored.GetTileType(V(0, 0)),
                "잔여 2초 중 1.75초 경과 — 아직 미완");

            restored.Tick(0.25f);
            Assert.AreEqual(TileType.House, restored.GetTileType(V(0, 0)),
                "잔여시간을 이어받아 완성");
        }

        [Test]
        public void LegacySave_WithoutConstructions_RestoresWithoutError()
        {
            SimConfig cfg = Cfg();
            var engine = new SimEngine(cfg, new SimEventHub());
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));   // 공사시간 0 = 즉시 완성

            CityFlow.Contracts.Save.SimSaveData snap = engine.CreateSnapshot();
            snap.Constructions = null;   // 구세이브 모사

            var restored = new SimEngine(cfg, new SimEventHub());
            Assert.DoesNotThrow(() => restored.RestoreSnapshot(snap));
            Assert.AreEqual(TileType.House, restored.GetTileType(V(0, 0)));
        }

        [Test]
        public void ConstructionProgress_ReportsFraction_AndFalseWhenNotUnderConstruction()
        {
            SimConfig cfg = Cfg();
            cfg.ConstructionHoursHouse = 4f;   // 4초 = 16틱
            var engine = new SimEngine(cfg, new SimEventHub());
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));

            Assert.IsTrue(engine.TryGetConstructionProgress01(V(0, 0), out float start));
            Assert.AreEqual(0f, start, 0.01f);

            for (int i = 0; i < 8; i++) engine.Tick(0.25f);
            Assert.IsTrue(engine.TryGetConstructionProgress01(V(0, 0), out float half));
            Assert.AreEqual(0.5f, half, 0.01f);

            // 풋프린트 비앵커 타일로 물어도 같은 값
            Assert.IsTrue(engine.TryGetConstructionProgress01(V(1, 1), out float halfAtNonAnchor));
            Assert.AreEqual(0.5f, halfAtNonAnchor, 0.01f);

            for (int i = 0; i < 8; i++) engine.Tick(0.25f);
            Assert.IsFalse(engine.TryGetConstructionProgress01(V(0, 0), out _),
                "완성 후엔 false");
            Assert.IsFalse(engine.TryGetConstructionProgress01(V(6, 3), out _),
                "빈 타일도 false");
        }
    }
}
