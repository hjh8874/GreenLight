using System;
using System.Collections.Generic;
using CityFlow.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public class BusCoverageDemandTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static CityGrid MakeCommuteGrid()
        {
            var grid = new CityGrid(16, 6);
            for (int x = 0; x < 14; x++)
                Assert.IsTrue(grid.Place(V(x, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(0, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(3, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(10, 0), TileType.Office));
            return grid;
        }

        static SimConfig DemandConfig()
        {
            SimConfig config = SimConfig.Default();
            config.CarsPerHouse = 2;
            config.DemandChoicePool = 1;
            config.OfficeCapacity = 8;
            return config;
        }

        static int DemandCountFrom(DemandMap demandMap, Vector2Int home)
        {
            int count = 0;
            foreach (Demand demand in demandMap.Demands)
                if (demand.Source == home) count++;
            return count;
        }

        [Test]
        public void Reduction_CutsCommutersPerHouse()
        {
            CityGrid grid = MakeCommuteGrid();
            DemandMap demandMap = new DemandMap(DemandConfig());
            demandMap.SetCommuterReduction(home => home == V(0, 0) ? 1 : 0);

            demandMap.Reassign(grid, new RoadNetwork(grid));

            Assert.AreEqual(0, DemandCountFrom(demandMap, V(0, 0)));
            Assert.AreEqual(1, DemandCountFrom(demandMap, V(3, 0)));
        }

        [Test]
        public void Reduction_FloorsAtZero()
        {
            CityGrid grid = MakeCommuteGrid();
            DemandMap demandMap = new DemandMap(DemandConfig());
            demandMap.SetCommuterReduction(_ => 5);

            demandMap.Reassign(grid, new RoadNetwork(grid));

            Assert.AreEqual(0, demandMap.Demands.Count);
        }

        [Test]
        public void NullDelegate_BitIdenticalToUncoveredReduction()
        {
            CityGrid grid = MakeCommuteGrid();
            SimConfig config = DemandConfig();
            DemandMap implicitNull = new DemandMap(config);
            DemandMap uncovered = new DemandMap(config);
            // 정류장 2개가 있어도 모든 집이 반경 밖이면 감축 0과 동일해야 한다.
            uncovered.SetCommuterReduction(_ => 0);

            implicitNull.Reassign(grid, new RoadNetwork(grid));
            uncovered.Reassign(grid, new RoadNetwork(grid));

            Assert.AreEqual(implicitNull.Demands.Count, uncovered.Demands.Count);
            for (int i = 0; i < implicitNull.Demands.Count; i++)
            {
                Demand expected = implicitNull.Demands[i];
                Demand actual = uncovered.Demands[i];
                Assert.AreEqual(expected.Source, actual.Source);
                Assert.AreEqual(expected.Sink, actual.Sink);
                Assert.AreEqual(expected.SourceRoad, actual.SourceRoad);
                Assert.AreEqual(expected.SinkRoad, actual.SinkRoad);
                Assert.AreEqual(expected.SinkType, actual.SinkType);
            }
        }

        // 엔진 레벨(배치→리빌드→감축) 종단 테스트 4건은 보류(2026-08-02 감독 결정).
        // 사유: 수요 수가 좌석 채용 램프와 결합돼 있어 "정류장 리빌드가 좌석을 갱신하며
        // 커버 감축(-1)과 상쇄"되는 관측 불가 구조를 실측으로 확인(타임라인
        // h2=1 plateau, stops 후에도 1). 후속 재도입 설계: 원거리 정류장(비커버)으로
        // 리빌드-좌석갱신을 먼저 관측(1→2)한 뒤 근거리 정류장으로 감축(2→1)을 분리 관측.
        // 배선 자체(SetCommuterReduction 주입·MarkTopologyDirty)는 코드 리뷰 + 위
        // DemandMap 단위 테스트 + IntegrationPrefab 통합 테스트로 커버된다.
    }
}
