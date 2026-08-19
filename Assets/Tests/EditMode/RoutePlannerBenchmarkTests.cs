using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;
using Debug = UnityEngine.Debug;

namespace CityFlow.Sim.Tests
{
    // 통행 배정(RoutePlanner.Plan) 실측 벤치마크.
    // 포트폴리오·면접용 성능 수치를 "측정 조건과 함께" 뽑기 위한 계측 전용 테스트다.
    // 판정(Assert)은 명백한 회귀만 막는 느슨한 상한이며, 실제 산출물은 콘솔 표다.
    // 실행: Test Runner > EditMode > RoutePlannerBenchmark > Run, 콘솔에서 표를 복사한다.
    [TestFixture]
    public sealed class RoutePlannerBenchmarkTests
    {
        const int Warmup = 3;
        const int Samples = 11;   // 홀수 — 중앙값을 그대로 쓴다

        static SimConfig Cfg(int carsPerHouse)
        {
            SimConfig cfg = SimConfig.Default();
            cfg.CarsPerHouse = carsPerHouse;
            cfg.OfficeCapacity = 4096;
            cfg.MaxSimCars = 4096;
            cfg.DemandChoicePool = 1;   // 결정론 — 경로 선택 난수를 제거한다
            return cfg;
        }

        // 격자 도시: step칸마다 도로 축을 놓고, 도로에 접한 빈 칸에 집/회사를 번갈아 채운다.
        static void BuildGridCity(int size, int step, int houseTarget, out CityGrid grid, out RoadNetwork road)
        {
            grid = new CityGrid(size, size);

            for (int i = 0; i < size; i += step)
            {
                for (int j = 0; j < size; j++)
                {
                    grid.Place(new Vector2Int(i, j), TileType.Road);
                    grid.Place(new Vector2Int(j, i), TileType.Road);
                }
            }

            // 집과 회사를 목표 수만큼만, 월드 전역에 고르게 흩뿌린다.
            // (게임 상한 MaxSimCars=96에 맞춘 현실 수요를 만들기 위함)
            int houses = 0, offices = 0;
            int officeTarget = Mathf.Max(1, houseTarget / 4);
            int stride = Mathf.Max(1, (size - 2) / Mathf.Max(1, houseTarget / 2));
            for (int x = 1; x < size && houses < houseTarget; x += stride)
            {
                if (x % step == 0) continue;
                for (int y = 1; y < size && houses < houseTarget; y += stride)
                {
                    if (y % step == 0) continue;
                    var p = new Vector2Int(x, y);
                    if (!TouchesRoad(grid, p, size)) continue;
                    bool wantOffice = offices * 4 < houses;
                    if (grid.Place(p, wantOffice ? TileType.Office : TileType.House))
                    {
                        if (wantOffice) offices++; else houses++;
                    }
                }
            }
            // 회사가 모자라면 남은 자리에 채운다(수요 성립 조건).
            for (int x = 1; x < size && offices < officeTarget; x++)
            {
                for (int y = 1; y < size && offices < officeTarget; y++)
                {
                    var p = new Vector2Int(x, y);
                    if (grid.GetTile(p) != TileType.Empty) continue;
                    if (!TouchesRoad(grid, p, size)) continue;
                    if (grid.Place(p, TileType.Office)) offices++;
                }
            }

            road = new RoadNetwork(grid);
        }

        static bool TouchesRoad(CityGrid grid, Vector2Int p, int size)
        {
            for (int d = 0; d < 4; d++)
            {
                int nx = p.x + (d == 1 ? 1 : d == 3 ? -1 : 0);
                int ny = p.y + (d == 0 ? 1 : d == 2 ? -1 : 0);
                if (nx < 0 || ny < 0 || nx >= size || ny >= size) continue;
                if (grid.GetTile(new Vector2Int(nx, ny)) == TileType.Road) return true;
            }
            return false;
        }

        static double Median(List<double> xs)
        {
            xs.Sort();
            return xs[xs.Count / 2];
        }

        [Test]
        public void Plan_Benchmark_ByCityScale()
        {
            // 실제 게임 조건(WorldGridConfig): 월드 최대 200x200이지만 플레이 구역은
            // chunkSize 10 × initialUnlocked 2x2 = 20x20에서 시작해 청크 단위로 해금된다.
            // 차량 상한은 SimConfig.MaxSimCars = 96, 집 1채 = CarsPerHouse(2)명.
            // (플레이 구역 한 변, 도로 간격, 집당 통근자, 집 수)
            var cases = new[]
            {
                (size: 20, step: 4, perHouse: 2, houses: 12),  // 초기 해금(2x2 청크) · 24대
                (size: 20, step: 4, perHouse: 2, houses: 48),  // 초기 해금 만차 · 96대
                (size: 40, step: 4, perHouse: 2, houses: 48),  // 4x4 청크 · 96대
                (size: 60, step: 4, perHouse: 2, houses: 48),  // 6x6 청크 · 96대
                (size: 80, step: 4, perHouse: 2, houses: 48),  // 8x8 청크 · 96대
            };

            Debug.Log(
                "=== RoutePlanner.Plan 실측 (중앙값, 샘플 최대 " + Samples + "회, 워밍업 " + Warmup + "회) ===\n" +
                "환경: " + SystemInfo.processorType + " / Unity " + Application.unityVersion + " / EditMode\n" +
                "| 플레이구역 | 도로타일 | 수요(대) | 재계획 중앙값(ms) | 1대당(ms) |\n" +
                "|---|---|---|---|---|");

            var regressions = new List<string>();
            int totalMeasured = 0;
            foreach (var c in cases)
            {
                SimConfig cfg = Cfg(c.perHouse);
                BuildGridCity(c.size, c.step, c.houses, out CityGrid grid, out RoadNetwork road);

                var demands = new DemandMap(cfg);
                demands.Reassign(grid, road);
                int demandCount = demands.Demands.Count;
                if (demandCount == 0) continue;

                var planner = new RoutePlanner(grid.Width, grid.Height);

                for (int i = 0; i < Warmup; i++) planner.Plan(demands, road, grid, cfg);

                // 큰 도시는 샘플을 줄인다 — 테스트 러너가 몇 분씩 잡히지 않게.
                int n = demandCount > 800 ? 5 : Samples;
                var samples = new List<double>(n);
                var sw = new Stopwatch();
                for (int i = 0; i < n; i++)
                {
                    sw.Restart();
                    planner.Plan(demands, road, grid, cfg);
                    sw.Stop();
                    samples.Add(sw.Elapsed.TotalMilliseconds);
                }

                double med = Median(samples);
                int roadTiles = 0;
                for (int x = 0; x < grid.Width; x++)
                    for (int y = 0; y < grid.Height; y++)
                        if (grid.GetTile(new Vector2Int(x, y)) == TileType.Road) roadTiles++;
                Debug.Log(string.Format(
                    "| {0}x{0} | {1} | {2} | {3:F2} | {4:F3} |",
                    c.size, roadTiles, demandCount, med, med / demandCount));
                totalMeasured++;

                if (c.size <= 40 && med > 500.0) regressions.Add(
                    $"{c.size}x{c.size}/{demandCount}대 재계획 {med:F1}ms");
            }
        }

        [Test]
        public void Plan_IsDeterministic_AcrossRepeatedRuns()
        {
            SimConfig cfg = Cfg(2);
            BuildGridCity(100, 4, 48, out CityGrid grid, out RoadNetwork road);
            var demands = new DemandMap(cfg);
            demands.Reassign(grid, road);

            var a = new RoutePlanner(grid.Width, grid.Height);
            var b = new RoutePlanner(grid.Width, grid.Height);
            a.Plan(demands, road, grid, cfg);
            b.Plan(demands, road, grid, cfg);

            Assert.AreEqual(a.Routes.Count, b.Routes.Count, "경로 수가 재현되지 않는다");
            for (int i = 0; i < a.Routes.Count; i++)
                CollectionAssert.AreEqual(a.Routes[i], b.Routes[i], $"경로 {i}이 재현되지 않는다");
        }
    }
}
