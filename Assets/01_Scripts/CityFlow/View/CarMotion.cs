using System.Collections.Generic;
using CityFlow.Sim;
using CityFlow.ViewKit;
using UnityEngine;

namespace CityFlow.View
{
    // MainCityView의 주행 파트. 4인 분담을 위한 파일 분리이며 partial이라
    // 필드·상태는 MainCityView.cs와 그대로 공유한다 — 결합도는 줄지 않았다.
    // 튜닝 노브([SerializeField])는 인스펙터 헤더 그룹 유지를 위해 MainCityView.cs에 남아 있다.
    public sealed partial class MainCityView
    {
        private int ComputeDisplayRouteHash(List<Vector2Int> route)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < route.Count; i++)
                {
                    hash = hash * 31 + route[i].GetHashCode();
                    if (i < route.Count - 1 && TryGetDiagonalTurnBridge(route, i, out Vector2Int bridge))
                    {
                        hash = hash * 31 + bridge.GetHashCode();
                    }
                }
                return hash;
            }
        }

        private bool TryGetDiagonalTurnBridge(List<Vector2Int> route, int segmentIndex, out Vector2Int bridge)
        {
            bridge = default;
            Vector2Int from = route[segmentIndex];
            Vector2Int to = route[segmentIndex + 1];
            int dx = to.x - from.x;
            int dy = to.y - from.y;
            if (Mathf.Abs(dx) != 1 || Mathf.Abs(dy) != 1)
            {
                return false;
            }

            Vector2Int horizontalBridge = new Vector2Int(to.x, from.y);
            Vector2Int verticalBridge = new Vector2Int(from.x, to.y);
            bool canTurnHorizontalFirst = IsRoadTile(horizontalBridge);
            bool canTurnVerticalFirst = IsRoadTile(verticalBridge);

            if (!canTurnHorizontalFirst && !canTurnVerticalFirst)
            {
                return false;
            }

            bool horizontalFirst = canTurnHorizontalFirst;
            if (canTurnHorizontalFirst && canTurnVerticalFirst)
            {
                if (segmentIndex > 0)
                {
                    Vector2Int incoming = from - route[segmentIndex - 1];
                    horizontalFirst = incoming.y == 0;
                }
                else if (segmentIndex + 2 < route.Count)
                {
                    Vector2Int outgoing = route[segmentIndex + 2] - to;
                    horizontalFirst = outgoing.x == 0;
                }
            }

            bridge = horizontalFirst ? horizontalBridge : verticalBridge;
            // 로터리 중심도 표시 경로에 넣어 꺾는 차가 대각선으로 섬을 가로지르지 않게 한다.
            return true;
        }

        private float GetCornerTurnRadiusFraction()
        {
            return cornerTurnRadius > 0f
                ? Mathf.Clamp(cornerTurnRadius, 0.6f, 0.85f)
                : 0.75f;
        }

        private bool IsRoundaboutTile(Vector2Int tile)
        {
            if (intersectionFacility == null)
            {
                return false;
            }

            return ContainsSignal(intersectionFacility.RoundaboutTiles, tile);   // 선형 목록 검색 헬퍼 공용
        }

        private void RefreshCommuteVehicles()
        {
            if (simEngine == null)
            {
                return;
            }

            EnsureVehicleCount(simEngine.CarSimMaxCars);
            SyncCommutePopulation();

            // 틱 경계 검출: 위상은 틱 안에서 단조증가하다 Step 프레임에만 되감긴다.
            // "천장이 움직이는가"는 틱 단위 상태라 프레임마다 갱신하면 틱 사이 39프레임이
            // 플래그를 지워 영원히 false가 된다.
            float tickProgress = simEngine.TickProgress01;
            tickEdge = tickProgress < lastTickProgress - 0.0001f;
            lastTickProgress = tickProgress;

            ResolveLaneLeaders();

            for (int i = 0; i < carSimMirrors.Count; i++)
            {
                CommuteCar car = carSimMirrors[i];
                CarSnapshot snapshot = simEngine.GetCarSnapshot(i);
                car.State = snapshot.State;
                if (carVehicles.TryGetValue(car, out RouteVehicle vehicle)
                    && vehicle != null
                    && vehicle.Object.activeSelf)
                {
                    MoveCarSimVehicle(i, car, snapshot, vehicle);
                }
            }
        }

        // 차선 = (타일, 진입방향). Sim의 큐 키와 같은 축이며, 뷰가 자기 폴리라인에서
        // 동일하게 유도한다(Sim: route[p]-route[p-1], 뷰: TileAt(p)-TileAt(p-1)) —
        // 그래서 CarSnapshot에 방향을 추가할 필요가 없다.
        private Vector2Int[] laneTile = System.Array.Empty<Vector2Int>();
        private Vector2Int[] laneDelta = System.Array.Empty<Vector2Int>();
        private Vector2Int[] laneNextTile = System.Array.Empty<Vector2Int>();
        private Vector2Int[] laneNextDelta = System.Array.Empty<Vector2Int>();
        private int[] laneSlot = System.Array.Empty<int>();
        private bool[] laneValid = System.Array.Empty<bool>();
        private bool[] laneHasNext = System.Array.Empty<bool>();
        private int[] laneLeader = System.Array.Empty<int>();

        // 앞차 = "같은 차선에서 나보다 하나 앞" 또는 "내 다음 차선의 꼬리". 둘 다 Sim의
        // 전진 순서를 그대로 따르는 선형 관계라, 앞차 체인은 사이클을 만들지 않는다.
        // 교차 차선 차량은 후보에 아예 들어오지 않는다 — 기하 근접만으로 차를 세우면
        // 교차 관계가 생겨 데드락이 재발한다(dev-log-17).
        private void ResolveLaneLeaders()
        {
            int count = carSimMirrors.Count;
            if (laneValid.Length < count)
            {
                laneTile = new Vector2Int[count];
                laneDelta = new Vector2Int[count];
                laneNextTile = new Vector2Int[count];
                laneNextDelta = new Vector2Int[count];
                laneSlot = new int[count];
                laneValid = new bool[count];
                laneHasNext = new bool[count];
                laneLeader = new int[count];
            }

            for (int i = 0; i < count; i++)
            {
                laneValid[i] = false;
                laneHasNext[i] = false;
                laneLeader[i] = -1;

                CarSnapshot snapshot = simEngine.GetCarSnapshot(i);
                bool inbound = snapshot.State == CarState.Inbound;
                if (snapshot.State != CarState.Outbound && !inbound) continue;
                if (!bakedRoutes.TryGetValue(carSimMirrors[i].RouteIndex, out BakedRoutePair pair)) continue;

                RoutePolyline poly = inbound ? pair.Inbound : pair.Outbound;
                if (poly == null || poly.TileCount < 2) continue;

                int tileIndex = Mathf.Clamp(snapshot.TileIndex, 0, poly.TileCount - 1);
                Vector2Int tile = poly.TileAt(tileIndex);
                laneTile[i] = tile;
                // 진입방향: 출발 타일(0)만 다음 타일에서 유도 — Sim의 TryEnqueueDepartures와 동일.
                laneDelta[i] = tileIndex > 0
                    ? tile - poly.TileAt(tileIndex - 1)
                    : poly.TileAt(1) - tile;
                laneSlot[i] = Mathf.Max(0, snapshot.QueueSlot);
                laneValid[i] = true;

                if (tileIndex + 1 < poly.TileCount)
                {
                    Vector2Int next = poly.TileAt(tileIndex + 1);
                    laneNextTile[i] = next;
                    laneNextDelta[i] = next - tile;
                    laneHasNext[i] = true;
                }
            }

            // 앞차는 **Sim 슬롯이 아니라 화면 위치**로 고른다. 슬롯으로 고르면 순서가 뒤집힌다 —
            // 차마다 지연이 달라(최대 2.86타일) Sim이 "앞"이라는 차가 화면에선 뒤에 있을 수 있고,
            // 그러면 추종 모델이 엉뚱한 차를 붙잡아 진짜 앞차를 그대로 관통한다
            // (계측 2026-07-20: 앞차와의 간격 최소 −0.378타일 = 앞차가 뒤에 있음).
            // 위치로 고르면 순서 역전이 정의상 불가능하다. 후보는 여전히 **같은 차선뿐**이라
            // (내 차선 + 경로상 다음 타일의 차선) 정지 관계는 선형이고 데드락 사이클이 없다.
            for (int i = 0; i < count; i++)
            {
                if (!laneValid[i]) continue;
                if (!carVehicles.TryGetValue(carSimMirrors[i], out RouteVehicle self) || self == null) continue;
                if (self.Dir.sqrMagnitude < 0.0001f) continue;
                Vector3 forward = self.Dir.normalized;

                float bestAhead = float.MaxValue;
                int simOrderFallback = -1;
                int fallbackSlot = -1;
                for (int j = 0; j < count; j++)
                {
                    if (j == i || !laneValid[j]) continue;
                    bool sameLane = laneTile[j] == laneTile[i] && laneDelta[j] == laneDelta[i];
                    bool nextLane = laneHasNext[i]
                        && laneTile[j] == laneNextTile[i] && laneDelta[j] == laneNextDelta[i];
                    if (!sameLane && !nextLane) continue;
                    if (!carVehicles.TryGetValue(carSimMirrors[j], out RouteVehicle other) || other == null) continue;

                    float ahead = Vector3.Dot(other.Pos - self.Pos, forward);
                    if (ahead > 0f)
                    {
                        if (ahead < bestAhead) { bestAhead = ahead; laneLeader[i] = j; }
                        continue;
                    }

                    // 화면상 앞에 아무도 없더라도, Sim 순서상 앞인 차가 나와 겹쳐 있으면
                    // 그 차를 앞차로 삼는다 — 안 그러면 완전히 포개진 쌍이 서로 무제약이 된다.
                    bool simAhead = sameLane
                        ? laneSlot[j] < laneSlot[i]
                        : true;
                    if (simAhead && laneSlot[j] > fallbackSlot) { fallbackSlot = laneSlot[j]; simOrderFallback = j; }
                }
                if (laneLeader[i] < 0) laneLeader[i] = simOrderFallback;
            }
        }

        // 앞차까지의 간격(월드 유닛). 직선거리가 아니라 **내 진행 방향 성분**을 쓴다 —
        // 코너에서 직선거리를 쓰면 안쪽에 있는 앞차를 실제보다 가깝게 봐서 괜히 선다.
        private bool TryGetLaneHeadway(int carIndex, RouteVehicle vehicle, out float headway, out float leaderSpeed)
        {
            headway = 0f;
            leaderSpeed = 0f;
            if (carIndex < 0 || carIndex >= laneLeader.Length) return false;
            int leader = laneLeader[carIndex];
            if (leader < 0 || leader >= carSimMirrors.Count) return false;
            if (!carVehicles.TryGetValue(carSimMirrors[leader], out RouteVehicle ahead) || ahead == null) return false;
            if (vehicle.Dir.sqrMagnitude < 0.0001f) return false;

            leaderSpeed = ahead.TravelSpeed;
            headway = Vector3.Dot(ahead.Pos - vehicle.Pos, vehicle.Dir.normalized);
            // 예전엔 `headway > 0`일 때만 제약을 걸었다 — 앞차를 파고든 순간 제약이 통째로
            // 사라져서 그대로 관통했다(계측 2026-07-20: 추종을 넣고도 SAME-DIR 겹침이
            // 385→359로 거의 안 줄었다). 겹친 순간이야말로 가장 강하게 눌러야 한다.
            // 여유가 0이면 desired = 앞차 속도 → 더 파고들지 않고 속도만 맞춘다.
            // 앞차가 서면 나도 선다(같은 차선 = 정당한 정지), 앞차가 가면 나도 간다 → 데드락 불가.
            return true;
        }

        // ActiveRoutes(브리지 포함) 또는 출/도착지가 하나라도 바뀌면 리빌드+재베이크.
        private void SyncCommutePopulation()
        {
            IReadOnlyList<List<Vector2Int>> routes = simEngine.ActiveRoutes;
            SyncCarSimMirrors();
            int hash = ComputeCarSimRoutesHash(routes);
            int tuningHash = ComputeRoundaboutTuningHash();
            if (commuteRoutesBuilt && hash == commuteRoutesHash)
            {
                // 경로는 그대로 — 로터리 노브만 바뀌었으면 스케줄러/차량 진행 보존한 채 폴리라인만 재베이크(QA G).
                if (tuningHash != commuteTuningHash)
                {
                    commuteTuningHash = tuningHash;
                    RebakeCommuteGeometry(routes);
                }

                return;
            }

            commuteRoutesHash = hash;
            commuteTuningHash = tuningHash;
            commuteRoutesBuilt = true;
            RebuildCommute(routes);
        }

        private void SyncCarSimMirrors()
        {
            int count = simEngine.ActiveVehicleCount;
            while (carSimMirrors.Count < count) carSimMirrors.Add(new CommuteCar());
            while (carSimMirrors.Count > count) carSimMirrors.RemoveAt(carSimMirrors.Count - 1);
            for (int i = 0; i < count; i++)
            {
                CarSnapshot snapshot = simEngine.GetCarSnapshot(i);
                CommuteCar car = carSimMirrors[i];
                car.Home = snapshot.Home;
                car.Work = snapshot.Work;
                car.RouteIndex = snapshot.RouteIndex;
                car.HomeSlot = snapshot.HomeSlot;
                car.WorkSlot = snapshot.WorkSlot;
                car.State = snapshot.State;
            }
        }

        private int ComputeCarSimRoutesHash(IReadOnlyList<List<Vector2Int>> routes)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < carSimMirrors.Count; i++)
                {
                    CommuteCar car = carSimMirrors[i];
                    hash = hash * 31 + car.RouteIndex;
                    hash = hash * 31 + car.Home.GetHashCode();
                    hash = hash * 31 + car.Work.GetHashCode();
                    List<Vector2Int> route = car.RouteIndex >= 0 && car.RouteIndex < routes.Count
                        ? routes[car.RouteIndex]
                        : null;
                    hash = hash * 31 + (route == null ? -1 : ComputeDisplayRouteHash(route));
                    List<Vector2Int> returnRoute = car.RouteIndex >= 0
                        && car.RouteIndex < simEngine.ActiveReturnRoutes.Count
                        ? simEngine.ActiveReturnRoutes[car.RouteIndex]
                        : null;
                    hash = hash * 31 + (returnRoute == null ? -1 : ComputeDisplayRouteHash(returnRoute));
                }
                return hash;
            }
        }

        // 로터리 라이브 노브 3종의 해시 — 재생 중 슬라이더 조정 감지용(QA G).
        private int ComputeRoundaboutTuningHash()
        {
            int hash = 17;
            hash = hash * 31 + roundaboutOrbitRadius.GetHashCode();
            hash = hash * 31 + roundaboutEntryExitDeg.GetHashCode();
            hash = hash * 31 + roundaboutTransitionTiles.GetHashCode();
            return hash;
        }

        private void RebuildCommute(IReadOnlyList<List<Vector2Int>> routes)
        {
            FlushAllPendingCoinPops();   // 위상 변경 전 대기 코인 전액 타일 팝 — 유실 금지(금액 보존 불변식)

            // sticky(QA A): 리빌드 전 각 차의 구 폴리라인을 붙잡아 "경로 타일이 실제로 바뀐 차"만 골라낸다.
            var previousBakes = new Dictionary<CommuteCar, BakedRoutePair>(carVehicles.Count);
            foreach (KeyValuePair<CommuteCar, RouteVehicle> kv in carVehicles)
            {
                if (bakedRoutes.TryGetValue(kv.Key.RouteIndex, out BakedRoutePair old))
                {
                    previousBakes[kv.Key] = old;
                }
            }

            BakeAllRoutes(routes);
            SyncCommuteVehicleBindings(previousBakes);
            RebuildParkingVisuals();
        }

        // 로터리 노브만 바뀐 경우: 스케줄러·차량 진행을 건드리지 않고 폴리라인만 재베이크(QA G).
        // 경로 타일은 동일하므로 sticky previousBakes로 각 차가 새 지오메트리에 이어붙는다(위상 보존).
        private void RebakeCommuteGeometry(IReadOnlyList<List<Vector2Int>> routes)
        {
            var previousBakes = new Dictionary<CommuteCar, BakedRoutePair>(carVehicles.Count);
            foreach (KeyValuePair<CommuteCar, RouteVehicle> kv in carVehicles)
            {
                if (bakedRoutes.TryGetValue(kv.Key.RouteIndex, out BakedRoutePair old))
                {
                    previousBakes[kv.Key] = old;
                }
            }

            BakeAllRoutes(routes);
            SyncCommuteVehicleBindings(previousBakes);
        }

        // bakedRoutes를 현재 스케줄러 차량·라이브 로터리 노브로 다시 채운다(RebuildCommute·RebakeCommuteGeometry 공용).
        private void BakeAllRoutes(IReadOnlyList<List<Vector2Int>> routes)
        {
            bakedRoutes.Clear();
            IReadOnlyList<CommuteCar> cars = CurrentCommuteCars();
            for (int i = 0; i < cars.Count; i++)
            {
                CommuteCar car = cars[i];
                if (car.RouteIndex < 0 || car.RouteIndex >= routes.Count)
                {
                    continue;
                }

                List<Vector2Int> source = routes[car.RouteIndex];
                if (source == null || source.Count <= 1)
                {
                    continue;
                }

                // 각 폴리라인은 자기 타일 리스트를 참조 보관(RoutePolyline._tiles) — 공유·재사용 금지.
                List<Vector2Int> outboundTiles = new List<Vector2Int>(source.Count);
                BuildBridgedRoute(source, outboundTiles);
                if (outboundTiles.Count <= 1)
                {
                    continue;
                }

                Vector3 homeAnchor = GetParkingAnchor(car.Home, outboundTiles[0], car.HomeSlot, simEngine.CarSimHomeParkingSlots);
                Vector3 workAnchor = GetParkingAnchor(car.Work, outboundTiles[outboundTiles.Count - 1], car.WorkSlot, simEngine.CarSimOfficeParkingSlots);

                List<Vector2Int> inboundTiles;
                if (car.RouteIndex < simEngine.ActiveReturnRoutes.Count
                    && simEngine.ActiveReturnRoutes[car.RouteIndex] != null)
                {
                    inboundTiles = new List<Vector2Int>(simEngine.ActiveReturnRoutes[car.RouteIndex].Count);
                    BuildBridgedRoute(simEngine.ActiveReturnRoutes[car.RouteIndex], inboundTiles);
                }
                else
                {
                    inboundTiles = new List<Vector2Int>(outboundTiles);
                    inboundTiles.Reverse();
                }
                if (inboundTiles.Count <= 1) continue;

                bakedRoutes[car.RouteIndex] = new BakedRoutePair
                {
                    Outbound = BakeCommuteRoute(outboundTiles, homeAnchor, workAnchor),
                    Inbound = BakeCommuteRoute(inboundTiles, workAnchor, homeAnchor),
                };
            }
        }

        private IReadOnlyList<CommuteCar> CurrentCommuteCars() => carSimMirrors;

        // 대각 브리지 삽입 로직을 차량 상태 없이 재현(베이크 입력용).
        private void BuildBridgedRoute(List<Vector2Int> source, List<Vector2Int> dest)
        {
            dest.Clear();
            if (source.Count == 0)
            {
                return;
            }

            dest.Add(source[0]);
            for (int i = 0; i < source.Count - 1; i++)
            {
                if (TryGetDiagonalTurnBridge(source, i, out Vector2Int bridge)
                    && dest[dest.Count - 1] != bridge)
                {
                    dest.Add(bridge);
                }

                Vector2Int next = source[i + 1];
                if (dest[dest.Count - 1] != next)
                {
                    dest.Add(next);
                }
            }
        }

        private RoutePolyline BakeCommuteRoute(IReadOnlyList<Vector2Int> tiles, Vector3 startAnchor, Vector3 endAnchor)
        {
            return RoutePolyline.Bake(new BakeInput
            {
                Tiles = tiles,
                TileSize = tileSize,
                LaneOffset = laneOffset,
                CornerRadiusFraction = GetCornerTurnRadiusFraction(),   // 베이커는 클램프 안 함 — 여기서 해석해 전달(리뷰 #2)
                OrbitRadius = roundaboutOrbitRadius,
                EntryExitOffsetRad = roundaboutEntryExitDeg * Mathf.Deg2Rad,   // α (QA G)
                TransitionLength = roundaboutTransitionTiles,
                Z = vehicleZ,
                IsRoundabout = IsRoundaboutTile,   // 항상 non-null(리뷰 #6)
                StartAnchor = startAnchor,
                EndAnchor = endAnchor,
                SamplesPerSegment = 8,
            });
        }

        // 건물 프리팹에 "ParkingSlot_N" 자식이 있으면 그 위치(뷰 로컬), 없으면 절차 폴백.
        // slotCount = 건물 타입별 칸 수(회사=officeSlots, 집=homeSlots) — 집 1칸은 중앙 정렬.
        private Vector3 GetParkingAnchor(Vector2Int building, Vector2Int frontageRoad, int slotIndex, int slotCount)
        {
            if (tileVisuals.TryGetValue(building, out TileVisual visual))
            {
                Transform named = visual.Object.transform.Find($"ParkingSlot_{slotIndex}");
                if (named != null)
                {
                    // 폴리라인/차량 로컬 좌표계(transform 기준)와 정합 — world→view-local 변환.
                    return transform.InverseTransformPoint(named.position);
                }
            }

            Vector3 center = GridToLocal(building, vehicleZ);
            Vector3 toRoad = (GridToLocal(frontageRoad, vehicleZ) - center).normalized;
            Vector3 side = new Vector3(toRoad.y, -toRoad.x, 0f);
            Vector2 offset = PolylineMath.ParkingSlotOffset(slotIndex, slotCount, parkingSlotInset);
            return center + toRoad * (tileSize * offset.x)
                          + side * (tileSize * offset.y);
        }

        // sticky 바인딩(QA A): 생존 차는 vehicle·위치 무접촉 유지, 사라진 짝만 풀 반납, 신규만 할당.
        // 경로 타일이 실제로 바뀐 이동 중 차만 페이드(렌더러 off) + 주차 상태로 개별 수렴(순간이동 금지).
        // 총 인구 상한 = SimConfig.MaxSimCars = 풀 크기.
        private void SyncCommuteVehicleBindings(Dictionary<CommuteCar, BakedRoutePair> previousBakes)
        {
            IReadOnlyList<CommuteCar> cars = CurrentCommuteCars();
            var alive = new HashSet<CommuteCar>();
            for (int i = 0; i < cars.Count; i++)
            {
                alive.Add(cars[i]);
            }

            List<CommuteCar> stale = null;
            foreach (KeyValuePair<CommuteCar, RouteVehicle> kv in carVehicles)
            {
                if (!alive.Contains(kv.Key))
                {
                    (stale ??= new List<CommuteCar>()).Add(kv.Key);
                }
            }

            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                {
                    DeactivateCommuteVehicle(carVehicles[stale[i]]);
                    carVehicles.Remove(stale[i]);
                }
            }

            for (int i = 0; i < cars.Count; i++)
            {
                CommuteCar car = cars[i];
                bool hasBake = bakedRoutes.TryGetValue(car.RouteIndex, out BakedRoutePair pair);
                if (carVehicles.TryGetValue(car, out RouteVehicle vehicle))
                {
                    vehicle.RouteIndex = car.RouteIndex;   // bakedRoutes 짝 키 동기화(색은 개성 팔레트로 분리됨)
                    if (!hasBake)
                    {
                        // 새 위상에서 경로 소실(미연결 등): 페이드 후 다음 리빌드에서 재배치.
                        SetVehicleRenderersEnabled(vehicle, false);
                        continue;
                    }

                    bool moving = car.State == CarState.Outbound || car.State == CarState.Inbound;
                    if (moving
                        && previousBakes.TryGetValue(car, out BakedRoutePair old)
                        && !SamePolylineTiles(old.Outbound, pair.Outbound))
                    {
                        // 이동 중 경로 변경: Distance가 새 폴리라인과 무의미 — 페이드 + 주차로 개별 수렴.
                        SetVehicleRenderersEnabled(vehicle, false);
                    }

                    // 주차 차/경로 불변 이동 차: 무접촉 — 위치·상태 그대로(주차 앵커는 다음 프레임 재계산).
                }
                else
                {
                    if (!hasBake)
                    {
                        continue;   // 경로 너무 짧아 베이크 실패 → 이 car는 그리지 않음
                    }

                    RouteVehicle fresh = TakeFreeVehicle();
                    if (fresh == null)
                    {
                        continue;   // 풀 고갈 — 정원 초과분 미표시
                    }

                    ResetVehicleForCommute(fresh, car.RouteIndex);
                    // 리바인드 불변식: 언바운드 차는 Distance≈0(신규/스냅 직후)에서만 발생 — 바인딩 로직 변경 시 재검토.
                    ApplyCarStyle(fresh, car);
                    carVehicles[car] = fresh;
                }
            }

            // 어떤 car에도 안 묶인 활성 차 정리(통근 모드 런타임 전환 등 안전망).
            var bound = new HashSet<RouteVehicle>(carVehicles.Values);
            for (int i = 0; i < vehicles.Count; i++)
            {
                if (vehicles[i].Object.activeSelf && !bound.Contains(vehicles[i]))
                {
                    DeactivateCommuteVehicle(vehicles[i]);
                }
            }
        }

        private RouteVehicle TakeFreeVehicle()
        {
            for (int i = 0; i < vehicles.Count; i++)
            {
                if (!vehicles[i].Object.activeSelf)
                {
                    return vehicles[i];
                }
            }

            return null;
        }

        private static bool SamePolylineTiles(RoutePolyline a, RoutePolyline b)
        {
            if (a.TileCount != b.TileCount)
            {
                return false;
            }

            for (int i = 0; i < a.TileCount; i++)
            {
                if (a.TileAt(i) != b.TileAt(i))
                {
                    return false;
                }
            }

            return true;
        }

        private void ResetVehicleForCommute(RouteVehicle vehicle, int routeIndex)
        {
            vehicle.RouteIndex = routeIndex;
            vehicle.CurrentSpeed = 0f;
            vehicle.TargetDistance = 0f;
            vehicle.TargetTileIndex = -1;
            vehicle.TargetRouteIndex = -1;
            vehicle.HasTickTarget = false;
            vehicle.Dir = Vector3.zero;
            vehicle.HasCurrentTile = false;
            vehicle.Settling = false;
            vehicle.SettleRate = 0f;
            vehicle.TravelSpeed = 0f;
            vehicle.HasLastState = false;
            vehicle.BrakeOn = false;
            if (vehicle.BrakeLight != null)
            {
                vehicle.BrakeLight.SetActive(false);   // 하드 리셋(캐시 가드 우회) — 풀 재사용 desync 방지
            }
            HideJamMarks(vehicle);
            if (vehicle.Renderer != null)
            {
                vehicle.Renderer.enabled = true;
            }

            if (vehicle.DetailRenderer != null)
            {
                vehicle.DetailRenderer.enabled = true;
            }

            vehicle.Object.SetActive(true);
        }

        private void DeactivateCommuteVehicle(RouteVehicle vehicle)
        {
            // 선택 추적 중인 차가 stale 처리되면 드라이브 뷰를 먼저 종료한다(#103 방어 이식).
            // 비활성화 직후 같은 풀 오브젝트가 TakeFreeVehicle()로 신규 통근차에 재사용되면
            // DriveViewCamera가 볼 때 대상이 다시 active라 자동 종료 조건도 통과하지 못해,
            // 카메라가 엉뚱한 차를 계속 추적하게 된다.
            if (selectedVehicle == vehicle)
            {
                ExitDriveView();
            }

            vehicle.Object.SetActive(false);
            vehicle.RouteIndex = -1;
            vehicle.HasCurrentTile = false;
            vehicle.CurrentSpeed = 0f;
            vehicle.HasTickTarget = false;
            vehicle.Dir = Vector3.zero;
            vehicle.Settling = false;
            vehicle.SettleRate = 0f;
            vehicle.TravelSpeed = 0f;
            vehicle.HasLastState = false;
            vehicle.BrakeOn = false;
            if (vehicle.BrakeLight != null)
            {
                vehicle.BrakeLight.SetActive(false);
            }
            HideJamMarks(vehicle);
        }

        private void MoveCarSimVehicle(int carIndex, CommuteCar car, CarSnapshot snapshot, RouteVehicle vehicle)
        {
            if (!bakedRoutes.TryGetValue(car.RouteIndex, out BakedRoutePair pair))
            {
                SetVehicleRenderersEnabled(vehicle, false);
                return;
            }

            bool inbound = snapshot.State == CarState.Inbound;
            bool moving = snapshot.State == CarState.Outbound || inbound;
            CarState previous = vehicle.LastState;
            bool hadPrevious = vehicle.HasLastState;
            if (!moving)
            {
                bool arrivedAtWork = hadPrevious
                    && previous == CarState.Outbound
                    && snapshot.State == CarState.ParkedWork;
                bool arrivedAtHome = hadPrevious
                    && previous == CarState.Inbound
                    && snapshot.State == CarState.ParkedHome;
                if (arrivedAtWork || arrivedAtHome)
                {
                    vehicle.Settling = true;
                    vehicle.SettleRate = 0f;   // 이번 정착의 남은거리로 다시 산출
                }

                // Sim은 뷰보다 먼저 도착할 수 있다. 이때 현재 월드 위치→주차 앵커 MoveTowards는
                // 꺾인 도로를 chord로 가로지르므로, 도착 방향 폴리라인의 누적거리 끝까지 따라간다.
                RoutePolyline parkingPolyline = snapshot.State == CarState.ParkedWork
                    ? pair.Outbound
                    : pair.Inbound;
                Sample parked;
                bool followingParkingPath = vehicle.Settling;
                if (followingParkingPath)
                {
                    // 정착 속도는 '남은 스퍼 거리 / 정착시간'의 등속이어야 한다.
                    // 예전엔 parkingPolyline.Length(경로 전체 길이)를 썼다 — 20타일 경로면
                    // 20/0.09 ≈ 222유닛/초 = 60fps에서 프레임당 3.7타일이라, 남은 0.5~2타일을
                    // 한 프레임에 주파했다. 즉 parkingSettleSeconds(0.3s) 연출은 한 번도
                    // 실행된 적이 없고 항상 순간이동이었다(환 라이브 "주차장에 갑자기 생김").
                    // TickInterval 클램프도 제거 — 도착한 차는 더 이상 Sim 갱신과 경합하지 않는다.
                    if (vehicle.SettleRate <= 0f)
                    {
                        float settleSeconds = Mathf.Max(0.001f, parkingSettleSeconds);
                        float remaining = Mathf.Max(0.0001f, parkingPolyline.Length - car.Distance);
                        // 고정 시간(0.3s)으로만 나누면 남은 거리가 길수록 빨라진다. 뷰가 Sim을
                        // 약 0.8타일 뒤에서 따라가므로 도착 시점의 남은 거리가 그만큼 늘었고,
                        // 회사 진입이 순항보다 몇 배 빨라 보였다(환 라이브 2026-07-20).
                        // 순항 속도를 상한으로 둬서 '주차장으로 들어가는 속도'를 유지한다.
                        float cruiseCap = tileSize / Mathf.Max(0.0001f, simEngine.TickInterval)
                            * parkingApproachSpeedRatio;
                        vehicle.SettleRate = Mathf.Min(remaining / settleSeconds, cruiseCap);
                    }
                    parked = parkingPolyline.AdvanceTowardEnd(
                        ref car.Distance,
                        vehicle.SettleRate * Time.deltaTime);
                    vehicle.Settling = car.Distance < parkingPolyline.Length - 0.001f;
                    if (!vehicle.Settling) vehicle.SettleRate = 0f;
                }
                else
                {
                    car.Distance = parkingPolyline.Length;
                    parked = parkingPolyline.SampleAt(car.Distance);
                }

                Vector3 parkedPos = parked.Pos;
                vehicle.Object.transform.localPosition = parkedPos;
                vehicle.Pos = parkedPos;
                vehicle.Dir = followingParkingPath ? parked.Dir : Vector3.zero;
                vehicle.CurrentSpeed = followingParkingPath
                    ? vehicleSpeed * vehicle.Style.SpeedMul
                    : 0f;
                if (followingParkingPath && parked.Dir.sqrMagnitude > 0.001f)
                {
                    float angle = Mathf.Atan2(parked.Dir.y, parked.Dir.x) * Mathf.Rad2Deg;
                    vehicle.Object.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                }
                vehicle.CurrentTile = snapshot.State == CarState.ParkedWork ? car.Work : car.Home;
                vehicle.HasCurrentTile = true;
                SetVehicleRenderersEnabled(vehicle, true);
                SetBrakeLight(vehicle, false);
                HideJamMarks(vehicle);
                if (snapshot.State == CarState.ParkedWork && !vehicle.Settling)
                    FlushPendingCoinPop(car.Work, vehicle.Object.transform.position);
                vehicle.LastState = snapshot.State;
                vehicle.HasLastState = true;
                return;
            }

            vehicle.Settling = false;
            vehicle.SettleRate = 0f;
            RoutePolyline poly = inbound ? pair.Inbound : pair.Outbound;
            int tileIndex = Mathf.Clamp(snapshot.TileIndex, 0, poly.TileCount - 1);
            Vector2Int simTile = poly.TileAt(tileIndex);
            float headInset = simEngine.IsSharedCarIntersection(simTile)
                ? intersectionQueueInset * tileSize
                : 0f;
            // 큐 간격은 **뷰가 정한다**(MM 전환 레버 A, 2026-07-20). 예전엔 "대기줄이 타일 안에
            // 들어가야 한다"는 제약 때문에 간격을 (타일−inset)/정원 = 0.250타일로 조였는데,
            // 차 길이가 0.38~0.44라 **차 길이의 43%가 항상 앞차와 겹쳤다.** 이건 줌 불변이라
            // (0.44 > 0.25는 타일 단위 비율) 카메라를 아무리 밀어도 안 사라진다 —
            // 실측 2026-07-20: 최대확대 1080p에서 차 41.5px에 겹침 17.9px.
            //
            // **슬롯은 위치를 정하지 않는다.** 목표는 언제나 '내 타일의 머리'이고, 차 사이
            // 간격은 앞차 추종이 낳는다. 이게 레버 A의 핵심이다 —
            //   · 슬롯 간격을 0.25로 조이면: 차 길이 0.44의 43%가 항상 겹친다(줌 불변).
            //   · 슬롯 간격만 0.55로 넓히면: 큐가 2.2타일로 늘어나 **상류 타일 slot0 차와 충돌**
            //     한다(계측 2026-07-20: SAME-DIR 겹침 385→769). 타일별 슬롯 산술은 다른 타일의
            //     차를 모르기 때문에, 간격을 넓히는 순간 타일 경계에서 깨진다.
            //   · 추종이 간격을 낳으면: 앞차 관계가 이미 타일 경계를 넘어 정의돼 있어
            //     (선두면 다음 타일 큐의 꼬리) 줄이 상류로 자연스럽게 이어진다.
            // Sim 정원(=처리량·경제)은 하나도 건드리지 않는다.
            // QueueSlot<0(큐 진입 실패 등)이어도 안전하다 — 어차피 타일 머리를 쓴다.
            float targetDistance = poly.DistanceAtQueueSlot(
                tileIndex,
                0,
                0f,
                headInset);
            float previousDistance = car.Distance;
            bool stateChanged = hadPrevious && previous != snapshot.State;
            // ⚠️ QueueSlot은 조건에 넣지 않는다. 슬롯이 위치에서 빠졌으므로(위) 슬롯 변화는
            // 목표 불변인데, 조건에 남겨두면 슬롯이 바뀔 때마다 "목표 전진"으로 오판해
            // 홀드 중에도 TargetAdvancing이 스퓨리어스로 켜졌다(계측 2026-07-21) —
            // Sim이 잡고 있는 차에 v² 부스트가 들어가 남몰래 앞으로 기어나가는 버그.
            bool targetChanged = !vehicle.HasTickTarget
                || stateChanged
                || vehicle.TargetTileIndex != tileIndex
                || vehicle.TargetRouteIndex != car.RouteIndex
                || Mathf.Abs(vehicle.TargetDistance - targetDistance) > 0.0001f;
            if (targetChanged)
            {
                if (stateChanged) car.Distance = 0f;   // 방향 전환 = 새 폴리라인의 시작
                vehicle.TargetDistance = targetDistance;
                vehicle.TargetTileIndex = tileIndex;
                vehicle.TargetRouteIndex = car.RouteIndex;
                vehicle.HasTickTarget = true;
                vehicle.TargetAdvancing = true;    // 흐르는 중 — 천장이 나와 같이 전진한다
            }
            else if (tickEdge)
            {
                // 틱이 지났는데 목표가 그대로 = Sim이 나를 잡고 있다(신호·정원). 천장이 멈췄다.
                vehicle.TargetAdvancing = false;
            }

            // 속도 기반 주행(MM 전환 Phase 2, 2026-07-20). 차가 프레임 위치의 주인이고,
            // Sim 위치는 목표가 아니라 **코리도 상한**이다. 예전엔 Sim 목표에 닿으면 다음
            // 틱까지 서 있었다(계측: 주행 차량의 프레임 정지 비율 0.898, 로터리 중간 정지).
            // 이제 앞차만 비면 틱 사이에도 계속 굴러간다.
            float dt = Mathf.Max(0f, Time.deltaTime);
            float brakeAccel = Mathf.Max(0.01f, vehicleBrakeAccel);
            // 평균 속도는 Sim이 정한다(틱당 1타일) — 개성으로 최고속도를 바꾸면 느린 차는
            // 영원히 목표를 못 따라잡는다. 개성은 가속도(AccelMul)에만 건다.
            float nominal = tileSize / Mathf.Max(0.0001f, simEngine.TickInterval);
            // 천장 = Sim 목표 + 1타일 (틱마다 이산 전진). 연속 천장(틱위상 보간)을 계측으로
            // 시험했으나(2026-07-21) 기각했다: 감속 이벤트가 줄지 않았고(106 vs 101/1k)
            // 뷰가 Sim 스냅샷보다 최대 2타일 앞서 헌법의 "Sim+1 상한"을 어겼다.
            // 남는 중간 브레이크의 뿌리는 뷰가 아니라 **Sim 틱 양자화**다(0.4s 홀드) — §4.10.
            // 도로 끝 클램프는 poly.Length — 목적지 도달 차를 회사 앞에서 얼리지 않는다(§4.6.2).
            float corridor = Mathf.Min(
                vehicle.TargetDistance + vehicleCorridorTiles * tileSize,
                poly.Length);
            bool signalEntryLimited = TryGetSignalEntryStopDistance(
                poly,
                tileIndex,
                vehicle,
                out float signalStopDistance);
            if (signalEntryLimited)
            {
                corridor = Mathf.Min(corridor, signalStopDistance);
            }

            if (corridor < car.Distance)
            {
                // 상한이 뒤로 갔다(밸브 초과 슬롯·재베이크). 스냅하면 순간이동이므로 굴러서 물러난다.
                car.Distance = Mathf.MoveTowards(car.Distance, corridor, nominal * dt);
                vehicle.TravelSpeed = 0f;
            }
            else
            {
                float toTarget = vehicle.TargetDistance - car.Distance;
                // 순항 상한이 Sim 속도와 정확히 같으면 제동으로 잃은 시간을 영원히 못 만회해
                // 지연이 발산한다(계측: 1.03 -> 1.98타일). 뒤처진 만큼 연속적으로 여유를 준다.
                // 계단식 임계값을 쓰면 그 위에서 진동하며 브레이크등이 고속 주행 중에 깜빡인다.
                float behind = Mathf.Clamp01(
                    (toTarget - tileSize * vehicleCatchUpStart) / Mathf.Max(0.01f, tileSize * vehicleCatchUpRamp));
                float cruise = nominal * (1f + behind * vehicleCatchUpRange);

                // 제동식은 **상대가 움직이는지**를 반영해야 한다. √(2a·여유)는 '정지한 벽'
                // 공식이라 이산 천장에선 여유가 줄 때마다 감속하는 톱니를 탄다(§4.6.1 계측:
                // 정지의 81.4%가 앞차 없음 + 여유 0.000). 천장이 전진 중이면 v² 부스트로
                // 계속 굴러가고, Sim이 잡으면(TargetAdvancing=false) 진짜 벽으로 보고 선다.
                float ceilingSpeed = signalEntryLimited
                    ? 0f
                    : (vehicle.TargetAdvancing ? nominal : 0f);
                float desired = Mathf.Min(cruise, Mathf.Sqrt(
                    ceilingSpeed * ceilingSpeed + 2f * brakeAccel * Mathf.Max(0f, corridor - car.Distance)));

                // 정지 사유 둘 중 나머지: 같은 차선 앞차와의 간격. 교차 차선 차량은 후보에
                // 들어오지 않으므로 정지 관계는 선형이고 사이클(=데드락)이 생기지 않는다.
                float headway;
                float leaderSpeed;
                if (TryGetLaneHeadway(carIndex, vehicle, out headway, out leaderSpeed))
                {
                    // 상한 없이 노브 그대로 쓴다. 슬롯은 더 이상 위치의 주인이 아니므로
                    // Sim 슬롯 간격(0.250)으로 누를 이유가 없다.
                    float minHeadway = vehicleMinHeadway * tileSize;
                    float slack = Mathf.Max(0f, headway - minHeadway);
                    float follow = Mathf.Sqrt(leaderSpeed * leaderSpeed + 2f * brakeAccel * slack);
                    // 간격이 최소치보다 좁으면 앞차보다 **느리게** 가야 간격이 다시 벌어진다.
                    // 속도를 맞추기만 하면 한번 겹친 쌍이 영원히 겹친 채로 굳는다.
                    // 부족분이 최소치만큼이면(=완전히 포개짐) 0 → 서서 앞차를 보낸다.
                    // 앞차는 나에게 막히지 않으므로(선형 관계) 간격은 반드시 회복된다.
                    if (headway < minHeadway)
                    {
                        float recover = Mathf.Clamp01(headway / Mathf.Max(0.0001f, minHeadway));
                        follow = Mathf.Min(follow, leaderSpeed * recover);
                    }
                    desired = Mathf.Min(desired, follow);
                }

                bool decelerating = desired < vehicle.TravelSpeed - 0.01f;
                float rate = decelerating
                    ? brakeAccel
                    : Mathf.Max(0.01f, vehicleDriveAccel) * vehicle.Style.AccelMul;
                vehicle.TravelSpeed = Mathf.MoveTowards(vehicle.TravelSpeed, desired, rate * dt);
                car.Distance = Mathf.Min(corridor, car.Distance + vehicle.TravelSpeed * dt);
            }
            Sample sample = poly.SampleAt(car.Distance);
            vehicle.Object.transform.localPosition = sample.Pos;
            vehicle.Pos = sample.Pos;
            vehicle.Dir = sample.Dir;
            vehicle.CurrentSpeed = Time.deltaTime > 0f
                ? Mathf.Abs(car.Distance - previousDistance) / (tileSize * Time.deltaTime)
                : 0f;
            vehicle.CurrentTile = poly.TileAt(Mathf.Clamp(sample.TileIndex, 0, poly.TileCount - 1));
            vehicle.HasCurrentTile = true;
            if (sample.Dir.sqrMagnitude > 0.001f)
            {
                float angle = Mathf.Atan2(sample.Dir.y, sample.Dir.x) * Mathf.Rad2Deg;
                vehicle.Object.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
            SetVehicleRenderersEnabled(vehicle, true);
            // 브레이크등은 '순항보다 확연히 느린가'로 판정한다. desired 순간값으로 켜면
            // 따라잡기 여유가 매 프레임 흔들려 고속 주행 중에도 깜빡인다(계측: 토글 256회).
            // 점등/소등 문턱을 벌려(히스테리시스) 경계에서 떨지 않게 한다.
            float brakeOnSpeed = nominal * 0.55f;
            float brakeOffSpeed = nominal * 0.70f;
            SetBrakeLight(vehicle, vehicle.BrakeOn
                ? vehicle.TravelSpeed < brakeOffSpeed
                : vehicle.TravelSpeed < brakeOnSpeed);
            HideJamMarks(vehicle);
            vehicle.LastState = snapshot.State;
            vehicle.HasLastState = true;
        }

        private void ResetCommuteState()
        {
            bakedRoutes.Clear();
            carVehicles.Clear();
            DestroyParkingVisuals();
            carSimMirrors.Clear();
            commuteRoutesBuilt = false;
            commuteRoutesHash = 0;
            commuteTuningHash = 0;
        }
    }
}
