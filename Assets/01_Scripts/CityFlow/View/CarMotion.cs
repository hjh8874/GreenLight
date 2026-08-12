using System.Collections.Generic;
using CityFlow.Contracts;
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
        // 로터리 중심에서 두 타일 떨어진 접근로의 차선 오프셋까지 포함한다.
        private const float RoundaboutPresentationSpacingRadiusTiles = 2.5f;
        private const float ParkingPresentationScaleRate = 0.28f;

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

        private bool IsWithinRoundaboutPresentationArea(
            Vector3 localPosition)
        {
            if (intersectionFacility == null) return false;

            IReadOnlyList<Vector2Int> roundabouts =
                intersectionFacility.RoundaboutTiles;
            float radius = Mathf.Max(
                0.0001f,
                tileSize * RoundaboutPresentationSpacingRadiusTiles);
            float radiusSquared = radius * radius;
            for (int index = 0; index < roundabouts.Count; index++)
            {
                Vector3 center = GridToLocal(roundabouts[index], 0f);
                float deltaX = localPosition.x - center.x;
                float deltaY = localPosition.y - center.y;
                if (deltaX * deltaX + deltaY * deltaY <= radiusSquared)
                {
                    return true;
                }
            }

            return false;
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
                bool showVehicle = ShouldShowCommuteVehicle(snapshot);
                car.ApplyViewVisibility(showVehicle);
                if (carVehicles.TryGetValue(car, out RouteVehicle vehicle)
                    && vehicle != null
                    && vehicle.Object.activeSelf)
                {
                    if (!showVehicle)
                    {
                        RemoveVehiclePresentation(car);
                        ReleaseCommuteParkingReservation(vehicle);
                        SetVehiclePresentationEnabled(vehicle, false);
                        vehicle.ViewRecovery.Reset();
                        continue;
                    }
                    MoveCarSimVehicle(i, car, snapshot, vehicle);
                }
            }

            UpdateCommuteVehicleOverlapDiagnostics();
        }

        // 차선 = (타일, 진입방향). Sim의 큐 키와 같은 축이며, 뷰가 자기 폴리라인에서
        // 동일하게 유도한다(Sim: route[p]-route[p-1], 뷰: TileAt(p)-TileAt(p-1)) —
        // 그래서 CarSnapshot에 방향을 추가할 필요가 없다.
        private Vector2Int[] laneTile = System.Array.Empty<Vector2Int>();
        private Vector2Int[] laneDelta = System.Array.Empty<Vector2Int>();
        private RoutePolyline[] lanePolyline = System.Array.Empty<RoutePolyline>();
        private int[] laneTileIndex = System.Array.Empty<int>();
        private int[] laneSlot = System.Array.Empty<int>();
        private bool[] laneValid = System.Array.Empty<bool>();
        private int[] laneLeader = System.Array.Empty<int>();
        private int[] laneLeaderRouteOffset = System.Array.Empty<int>();

        private sealed class IntersectionMotionState
        {
            public int RouteIndex { get; set; } = -1;
            public float AuthorizedDistance { get; set; }
            public float AuthorizedSpeed { get; set; }
        }

        private readonly Dictionary<RouteVehicle, IntersectionMotionState> intersectionMotionStates = new();

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
                lanePolyline = new RoutePolyline[count];
                laneTileIndex = new int[count];
                laneSlot = new int[count];
                laneValid = new bool[count];
                laneLeader = new int[count];
                laneLeaderRouteOffset = new int[count];
            }

            for (int i = 0; i < count; i++)
            {
                laneValid[i] = false;
                lanePolyline[i] = null;
                laneTileIndex[i] = -1;
                laneLeader[i] = -1;
                laneLeaderRouteOffset[i] = -1;

                CarSnapshot snapshot = simEngine.GetCarSnapshot(i);
                bool inbound = snapshot.State == CarState.Inbound;
                if (snapshot.State != CarState.Outbound && !inbound) continue;
                if (!ShouldShowCommuteVehicle(snapshot)) continue;
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
                lanePolyline[i] = poly;
                laneTileIndex[i] = tileIndex;
                laneSlot[i] = Mathf.Max(0, snapshot.QueueSlot);
                laneValid[i] = true;
            }

            // 앞차는 **Sim 슬롯이 아니라 화면 위치**로 고른다. 슬롯으로 고르면 순서가 뒤집힌다 —
            // 차마다 지연이 달라(최대 2.86타일) Sim이 "앞"이라는 차가 화면에선 뒤에 있을 수 있고,
            // 그러면 추종 모델이 엉뚱한 차를 붙잡아 진짜 앞차를 그대로 관통한다
            // (계측 2026-07-20: 앞차와의 간격 최소 −0.378타일 = 앞차가 뒤에 있음).
            // Candidate lanes follow the vehicle's directed route for the braking-based 2-3 tile range.
            VehicleFootprint standardFootprint =
                simEngine.StandardVehicleFootprint;
            float standardPresentationHeadway =
                GetRequiredVehiclePresentationHeadway(
                    RoadTrafficAgentKind.Car,
                    standardFootprint,
                    RoadTrafficAgentKind.Car,
                    standardFootprint);
            for (int i = 0; i < count; i++)
            {
                if (!laneValid[i]) continue;
                if (!carVehicles.TryGetValue(carSimMirrors[i], out RouteVehicle self) || self == null) continue;
                Vector3 forward = self.Dir.sqrMagnitude >= 0.0001f
                    ? self.Dir.normalized
                    : new Vector3(laneDelta[i].x, laneDelta[i].y, 0f).normalized;
                int lookaheadTiles = VehicleSpacingMath.CalculateLookaheadTiles(
                    self.TravelSpeed,
                    vehicleBrakeAccel,
                    simEngine.TickInterval,
                    standardPresentationHeadway,
                    tileSize,
                    2,
                    3);

                int bestRouteOffset = int.MaxValue;
                float bestDistance = float.MaxValue;
                int bestSlot = -1;
                for (int j = 0; j < count; j++)
                {
                    if (j == i || !laneValid[j]) continue;
                    if (!TryGetForwardLaneOffset(i, j, lookaheadTiles, out int routeOffset)) continue;
                    if (!carVehicles.TryGetValue(carSimMirrors[j], out RouteVehicle other) || other == null) continue;

                    Vector3 separation = other.Pos - self.Pos;
                    float ahead = Vector3.Dot(separation, forward);
                    bool isAhead = routeOffset > 0 || ahead > 0f || laneSlot[j] < laneSlot[i];
                    if (!isAhead) continue;

                    float distance = routeOffset == 0 && ahead > 0f
                        ? ahead
                        : separation.magnitude;
                    bool isCloser = routeOffset < bestRouteOffset
                        || (routeOffset == bestRouteOffset && distance < bestDistance - 0.0001f)
                        || (routeOffset == bestRouteOffset
                            && Mathf.Abs(distance - bestDistance) <= 0.0001f
                            && laneSlot[j] > bestSlot);
                    if (!isCloser) continue;

                    bestRouteOffset = routeOffset;
                    bestDistance = distance;
                    bestSlot = laneSlot[j];
                    laneLeader[i] = j;
                    laneLeaderRouteOffset[i] = routeOffset;
                }
            }
        }

        private bool TryGetForwardLaneOffset(
            int followerIndex,
            int candidateIndex,
            int lookaheadTiles,
            out int routeOffset)
        {
            routeOffset = -1;
            RoutePolyline poly = lanePolyline[followerIndex];
            int startIndex = laneTileIndex[followerIndex];
            if (poly == null || startIndex < 0) return false;

            int endIndex = Mathf.Min(poly.TileCount - 1, startIndex + Mathf.Max(0, lookaheadTiles));
            for (int tileIndex = startIndex; tileIndex <= endIndex; tileIndex++)
            {
                Vector2Int tile = poly.TileAt(tileIndex);
                Vector2Int direction = tileIndex > 0
                    ? tile - poly.TileAt(tileIndex - 1)
                    : poly.TileAt(1) - tile;
                if (laneTile[candidateIndex] != tile || laneDelta[candidateIndex] != direction) continue;

                routeOffset = tileIndex - startIndex;
                return true;
            }

            return false;
        }

        // Same-tile spacing uses the lane direction; future tiles use world distance across corners.
        private bool TryGetLaneHeadway(
            int carIndex,
            RouteVehicle vehicle,
            bool includeCarPresentationCandidates,
            out float headway,
            out float leaderSpeed,
            out float minimumHeadway)
        {
            headway = float.PositiveInfinity;
            leaderSpeed = 0f;
            minimumHeadway = 0f;
            bool found = false;
            VehicleFootprint standardFootprint =
                simEngine.StandardVehicleFootprint;

            if (carIndex >= 0 && carIndex < laneLeader.Length)
            {
                int leader = laneLeader[carIndex];
                if (leader >= 0 &&
                    leader < carSimMirrors.Count &&
                    carVehicles.TryGetValue(
                        carSimMirrors[leader],
                        out RouteVehicle ahead) &&
                    ahead != null)
                {
                    leaderSpeed = ahead.TravelSpeed;
                    Vector3 separation =
                        ahead.Pos - vehicle.Pos;
                    if (laneLeaderRouteOffset[carIndex] > 0)
                    {
                        headway = separation.magnitude;
                    }
                    else
                    {
                        Vector3 forward =
                            vehicle.Dir.sqrMagnitude >= 0.0001f
                                ? vehicle.Dir.normalized
                                : new Vector3(
                                    laneDelta[carIndex].x,
                                    laneDelta[carIndex].y,
                                    0f).normalized;
                        headway =
                            Vector3.Dot(separation, forward);
                    }

                    minimumHeadway =
                        GetRequiredVehiclePresentationHeadway(
                            RoadTrafficAgentKind.Car,
                            standardFootprint,
                            RoadTrafficAgentKind.Car,
                            standardFootprint);
                    found = true;
                }
            }

            // 일반 도로는 위의 경로 기반 선두차 관계를 사용한다. 로터리 진입·이탈 곡선은
            // 서로 다른 논리 큐가 같은 진행 방향으로 합류하므로 그 구간에서만 다른 승용차도 본다.
            if (TryGetVehiclePresentationLeader(
                    carSimMirrors[carIndex],
                    RoadTrafficAgentKind.Car,
                    standardFootprint,
                    vehicle.Pos,
                    vehicle.Dir,
                    includeCarCandidates:
                        includeCarPresentationCandidates,
                    useConvergingCarEnvelope:
                        includeCarPresentationCandidates,
                    out VehiclePresentationLeader externalLeader) &&
                (!found || externalLeader.Headway < headway))
            {
                headway = externalLeader.Headway;
                leaderSpeed = externalLeader.Speed;
                minimumHeadway =
                    externalLeader.RequiredHeadway;
                found = true;
            }

            // 예전엔 `headway > 0`일 때만 제약을 걸었다 — 앞차를 파고든 순간 제약이 통째로
            // 사라져서 그대로 관통했다(계측 2026-07-20: 추종을 넣고도 SAME-DIR 겹침이
            // 385→359로 거의 안 줄었다). 겹친 순간이야말로 가장 강하게 눌러야 한다.
            // 여유가 0이면 desired = 앞차 속도 → 더 파고들지 않고 속도만 맞춘다.
            // 앞차가 서면 나도 선다(같은 차선 = 정당한 정지), 앞차가 가면 나도 간다 → 데드락 불가.
            return found;
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
            int count = simEngine.CarSimVehicleStorageCount;
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
                car.ApplyViewVisibility(snapshot.IsVisible);
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
                    hash = hash * 31 + (car.IsVisible ? 1 : 0);
                    hash = hash * 31 + car.RouteIndex;
                    hash = hash * 31 + car.Home.GetHashCode();
                    hash = hash * 31 + car.Work.GetHashCode();
                    hash = hash * 31 + car.HomeSlot;
                    hash = hash * 31 + car.WorkSlot;
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

        // 로터리 라이브 노브와 배치 위상의 해시. 로터리는 경로 타일 자체를 바꾸지 않으므로
        // 배치/철거를 포함하지 않으면 기존 차량이 일반 교차로 폴리라인을 계속 사용한다.
        private int ComputeRoundaboutTuningHash()
        {
            int hash = 17;
            hash = hash * 31 +
                   RoundaboutVehicleOrbitRadiusTiles.GetHashCode();
            hash = hash * 31 + roundaboutEntryExitDeg.GetHashCode();
            hash = hash * 31 + roundaboutTransitionTiles.GetHashCode();
            IReadOnlyList<Vector2Int> roundabouts =
                intersectionFacility?.RoundaboutTiles
                ?? simEngine.RoundaboutTiles;
            hash = hash * 31 + roundabouts.Count;
            for (int i = 0; i < roundabouts.Count; i++)
            {
                hash = hash * 31 + roundabouts[i].x;
                hash = hash * 31 + roundabouts[i].y;
            }
            return hash;
        }

        private void RebuildCommute(IReadOnlyList<List<Vector2Int>> routes)
        {
            FlushAllPendingCoinPops();   // 위상 변경 전 대기 코인 전액 타일 팝 — 유실 금지(금액 보존 불변식)

            // sticky(QA A): 리빌드 전 각 차의 구 폴리라인을 붙잡아 "경로 타일이 실제로 바뀐 차"만 골라낸다.
            var previousBakes = new Dictionary<CommuteCar, BakedRoutePair>(carVehicles.Count);
            foreach (KeyValuePair<CommuteCar, RouteVehicle> kv in carVehicles)
            {
                // SyncCarSimMirrors가 이미 car.RouteIndex를 새 경로 키로 바꿨다. 기존 베이크는
                // vehicle에 남아 있는 직전 키로 찾아야 재라우팅 생존 차를 재투영할 수 있다.
                if (bakedRoutes.TryGetValue(kv.Value.RouteIndex, out BakedRoutePair old))
                {
                    previousBakes[kv.Key] = old;
                }
            }

            BakeAllRoutes(routes);
            SyncCommuteVehicleBindings(previousBakes, false);
            RebuildParkingVisuals();
        }

        // 로터리 노브만 바뀐 경우: 스케줄러·차량 진행을 건드리지 않고 폴리라인만 재베이크(QA G).
        // 경로 타일은 동일하므로 sticky previousBakes로 각 차가 새 지오메트리에 이어붙는다(위상 보존).
        private void RebakeCommuteGeometry(IReadOnlyList<List<Vector2Int>> routes)
        {
            var previousBakes = new Dictionary<CommuteCar, BakedRoutePair>(carVehicles.Count);
            foreach (KeyValuePair<CommuteCar, RouteVehicle> kv in carVehicles)
            {
                if (bakedRoutes.TryGetValue(kv.Value.RouteIndex, out BakedRoutePair old))
                {
                    previousBakes[kv.Key] = old;
                }
            }

            BakeAllRoutes(routes);
            SyncCommuteVehicleBindings(previousBakes, true);
        }

        // bakedRoutes를 현재 스케줄러 차량·라이브 로터리 노브로 다시 채운다(RebuildCommute·RebakeCommuteGeometry 공용).
        private void BakeAllRoutes(IReadOnlyList<List<Vector2Int>> routes)
        {
            bakedRoutes.Clear();
            IReadOnlyList<CommuteCar> cars = CurrentCommuteCars();
            for (int i = 0; i < cars.Count; i++)
            {
                CommuteCar car = cars[i];
                if (!car.IsVisible)
                {
                    continue;
                }

                if (car.RouteIndex < 0 || car.RouteIndex >= routes.Count)
                {
                    continue;
                }

                List<Vector2Int> source = routes[car.RouteIndex];
                if (source == null || source.Count == 0)
                {
                    continue;
                }

                // 각 폴리라인은 자기 타일 리스트를 참조 보관(RoutePolyline._tiles) — 공유·재사용 금지.
                List<Vector2Int> outboundTiles = new List<Vector2Int>(source.Count);
                BuildBridgedRoute(source, outboundTiles);
                if (outboundTiles.Count == 0)
                {
                    continue;
                }

                Vector3 homeAnchor = GetParkingAnchor(car.Home, outboundTiles[0], car.HomeSlot, simEngine.CarSimHomeParkingSlots);
                Vector3 workParking = GetParkingAnchor(
                    car.Work,
                    outboundTiles[outboundTiles.Count - 1],
                    car.WorkSlot,
                    simEngine.CarSimOfficeParkingSlots);

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
                if (inboundTiles.Count == 0) continue;

                Vector3 workExit = GetParkingAnchor(
                    car.Work,
                    inboundTiles[0],
                    car.WorkSlot,
                    simEngine.CarSimOfficeParkingSlots);

                bakedRoutes[car.RouteIndex] = new BakedRoutePair
                {
                    Outbound = BakeCommuteRoute(outboundTiles, homeAnchor, workParking),
                    Inbound = BakeCommuteRoute(inboundTiles, workExit, homeAnchor),
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

        /// <summary>
        /// Builds the same right-hand traffic geometry used by commute cars.
        /// Feature vehicles use this seam instead of duplicating lane and
        /// corner calculations.
        /// </summary>
        public RoutePolyline BakeTrafficRoute(
            IReadOnlyList<Vector2Int> tiles,
            float z,
            Vector3? startAnchor = null,
            Vector3? endAnchor = null,
            bool clampAnchorSpurOvershoot = false)
        {
            if (tiles == null || tiles.Count == 0)
            {
                return null;
            }

            return RoutePolyline.Bake(new BakeInput
            {
                Tiles = tiles,
                GridOrigin = gridOrigin,
                TileSize = tileSize,
                LaneOffset = laneOffset,
                CornerRadiusFraction = GetCornerTurnRadiusFraction(),   // 베이커는 클램프 안 함 — 여기서 해석해 전달(리뷰 #2)
                OrbitRadius = RoundaboutVehicleOrbitRadiusTiles,
                EntryExitOffsetRad = roundaboutEntryExitDeg * Mathf.Deg2Rad,   // α (QA G)
                TransitionLength = roundaboutTransitionTiles,
                Z = z,
                IsRoundabout = IsRoundaboutTile,   // 항상 non-null(리뷰 #6)
                StartAnchor = startAnchor,
                EndAnchor = endAnchor,
                ClampAnchorSpurOvershoot =
                    clampAnchorSpurOvershoot,
                SamplesPerSegment = 8,
            });
        }

        private RoutePolyline BakeCommuteRoute(
            IReadOnlyList<Vector2Int> tiles,
            Vector3 startAnchor,
            Vector3 endAnchor)
        {
            return BakeTrafficRoute(
                tiles,
                VehicleGroundZ,
                startAnchor,
                endAnchor);
        }

        // 건물 프리팹에 "ParkingSlot_N" 자식이 있으면 그 위치(뷰 로컬), 없으면 절차 폴백.
        // slotCount = 건물 타입별 칸 수(회사=officeSlots, 집=homeSlots).
        private Vector3 GetParkingAnchor(Vector2Int building, Vector2Int frontageRoad, int slotIndex, int slotCount)
        {
            if (TryGetBuildingParkingPose(
                    building,
                    Mathf.Max(0, slotIndex),
                    out Vector3 authoredPosition,
                    out _))
            {
                return authoredPosition;
            }

            Vector3 center = GridToLocal(building, VehicleGroundZ);
            Vector3 toRoad = (GridToLocal(frontageRoad, VehicleGroundZ) - center).normalized;
            Vector3 side = new Vector3(toRoad.y, -toRoad.x, 0f);
            Vector2 offset = PolylineMath.ParkingSlotOffset(slotIndex, slotCount, parkingSlotInset);
            return center + toRoad * (tileSize * offset.x)
                          + side * (tileSize * offset.y);
        }

        // sticky 바인딩(QA A): 생존 차는 vehicle 바인딩을 유지하고, 사라진 짝만 풀 반납, 신규만 할당.
        // 경로 타일이 바뀐 이동 차는 Sim 논리 위치를 새 아크렝스에 재투영한다. 새 왕복 경로
        // 자체가 베이크되지 않는 진짜 소실만 숨기고, 같은 타일의 노브 재베이크는 무접촉이다.
        // 총 인구 상한 = SimConfig.MaxSimCars = 풀 크기.
        private void SyncCommuteVehicleBindings(
            Dictionary<CommuteCar, BakedRoutePair> previousBakes,
            bool forceGeometryReprojection)
        {
            IReadOnlyList<CommuteCar> cars = CurrentCommuteCars();
            var alive = new HashSet<CommuteCar>();
            for (int i = 0; i < cars.Count; i++)
            {
                if (cars[i].IsVisible)
                {
                    alive.Add(cars[i]);
                }
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
                    RemoveVehiclePresentation(stale[i]);
                    DeactivateCommuteVehicle(carVehicles[stale[i]]);
                    carVehicles.Remove(stale[i]);
                }
            }

            for (int i = 0; i < cars.Count; i++)
            {
                CommuteCar car = cars[i];
                if (!car.IsVisible)
                {
                    continue;
                }
                bool hasBake = bakedRoutes.TryGetValue(car.RouteIndex, out BakedRoutePair pair);
                if (carVehicles.TryGetValue(car, out RouteVehicle vehicle))
                {
                    vehicle.RouteIndex = car.RouteIndex;   // bakedRoutes 짝 키 동기화(색은 개성 팔레트로 분리됨)
                    if (!hasBake)
                    {
                        // 새 위상에서 경로 소실(미연결 등): 페이드 후 다음 리빌드에서 재배치.
                        ReleaseCommuteParkingReservation(vehicle);
                        SetVehiclePresentationEnabled(vehicle, false);
                        continue;
                    }

                    bool moving = car.State == CarState.Outbound || car.State == CarState.Inbound;
                    if (moving
                        && previousBakes.TryGetValue(car, out BakedRoutePair old)
                        && (forceGeometryReprojection
                            || !SamePolylineTiles(
                                car.State == CarState.Inbound ? old.Inbound : old.Outbound,
                                car.State == CarState.Inbound ? pair.Inbound : pair.Outbound)))
                    {
                        CarSnapshot snapshot = simEngine.GetCarSnapshot(i);
                        RoutePolyline newPolyline = snapshot.State == CarState.Inbound
                            ? pair.Inbound
                            : pair.Outbound;
                        float reprojectedDistance = ReprojectDistance(
                            snapshot,
                            newPolyline,
                            snapshot.QueueOffsetTiles);
                        car.Distance = reprojectedDistance;
                        vehicle.HasTickTarget = false;
                        vehicle.TargetTileIndex = -1;
                        vehicle.TargetRouteIndex = -1;
                        intersectionMotionStates.Remove(vehicle);
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
                    CarSnapshot snapshot =
                        simEngine.GetCarSnapshot(i);
                    bool moving =
                        snapshot.State == CarState.Outbound ||
                        snapshot.State == CarState.Inbound;
                    if (moving)
                    {
                        RoutePolyline activePolyline =
                            snapshot.State == CarState.Inbound
                                ? pair.Inbound
                                : pair.Outbound;
                        car.Distance = ReprojectDistance(
                            snapshot,
                            activePolyline,
                            snapshot.QueueOffsetTiles);
                    }
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

        private float ReprojectDistance(
            CarSnapshot snapshot,
            RoutePolyline polyline,
            float queueOffsetTiles)
        {
            int tileIndex = Mathf.Clamp(snapshot.TileIndex, 0, polyline.TileCount - 1);
            Vector2Int simTile = polyline.TileAt(tileIndex);
            float headInset = simEngine.IsSharedCarIntersection(simTile)
                ? intersectionQueueInset * tileSize
                : 0f;
            return polyline.ReprojectDistance(
                tileIndex,
                queueOffsetTiles * tileSize,
                headInset,
                snapshot.IntersectionProgress01,
                snapshot.LinkProgress01,
                snapshot.RoundaboutProgress01,
                RoundaboutTransitionSpan());
        }

        private void ResetVehicleForCommute(RouteVehicle vehicle, int routeIndex)
        {
            ReleaseCommuteParkingReservation(vehicle);
            intersectionMotionStates.Remove(vehicle);
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
            vehicle.ViewRecovery.Reset();
            vehicle.BrakeOn = false;
            vehicle.NightLighting?.SetMoving(false);
            if (vehicle.BrakeLight != null)
            {
                vehicle.BrakeLight.SetActive(false);   // 하드 리셋(캐시 가드 우회) — 풀 재사용 desync 방지
            }
            HideJamMarks(vehicle);
            SetVehiclePresentationEnabled(vehicle, true);

            vehicle.Object.SetActive(true);
        }

        private void DeactivateCommuteVehicle(RouteVehicle vehicle)
        {
            ReleaseCommuteParkingReservation(vehicle);
            intersectionMotionStates.Remove(vehicle);
            // 선택 추적 중인 차가 stale 처리되면 드라이브 뷰를 먼저 종료한다(#103 방어 이식).
            // 비활성화 직후 같은 풀 오브젝트가 TakeFreeVehicle()로 신규 통근차에 재사용되면
            // DriveViewCamera가 볼 때 대상이 다시 active라 자동 종료 조건도 통과하지 못해,
            // 카메라가 엉뚱한 차를 계속 추적하게 된다.
            if (selectedVehicleTarget ==
                vehicle.Object.transform)
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
            vehicle.ViewRecovery.Reset();
            vehicle.BrakeOn = false;
            vehicle.NightLighting?.SetMoving(false);
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
                RemoveVehiclePresentation(car);
                ReleaseCommuteParkingReservation(vehicle);
                SetVehiclePresentationEnabled(vehicle, false);
                return;
            }

            bool inbound = snapshot.State == CarState.Inbound;
            bool moving = snapshot.State == CarState.Outbound || inbound;
            CarState previous = vehicle.LastState;
            bool hadPrevious = vehicle.HasLastState;
            if (moving)
            {
                UpdateParkingPresentationScale(vehicle, 1f);
                Vector2Int destinationBuilding = inbound
                    ? car.Home
                    : car.Work;
                int destinationSlot = inbound
                    ? snapshot.HomeSlot
                    : snapshot.WorkSlot;
                bool hasAuthoredDestination =
                    TryGetBuildingParkingPose(
                        destinationBuilding,
                        Mathf.Max(0, destinationSlot),
                        out _,
                        out _);
                if (!hasAuthoredDestination ||
                    !TryReserveCommuteParkingPose(
                        vehicle,
                        destinationBuilding,
                        destinationSlot))
                {
                    ReleaseCommuteParkingReservation(vehicle);
                }
            }
            if (!moving)
            {
                intersectionMotionStates.Remove(vehicle);
                Vector2Int parkingBuilding =
                    snapshot.State == CarState.ParkedWork
                        ? car.Work
                        : car.Home;
                int parkingSlot = snapshot.State == CarState.ParkedWork
                    ? snapshot.WorkSlot
                    : snapshot.HomeSlot;
                float parkingPresentationScale = 1f;
                bool hasAuthoredParkingPose =
                    TryGetBuildingParkingPose(
                        parkingBuilding,
                        Mathf.Max(0, parkingSlot),
                        out _,
                        out _,
                        out parkingPresentationScale);
                UpdateParkingPresentationScale(
                    vehicle,
                    hasAuthoredParkingPose
                        ? parkingPresentationScale
                        : 1f);
                if (hasAuthoredParkingPose &&
                    !TryReserveCommuteParkingPose(
                        vehicle,
                        parkingBuilding,
                        parkingSlot))
                {
                    vehicle.Settling = false;
                    vehicle.SettleRate = 0f;
                    vehicle.TravelSpeed = 0f;
                    vehicle.HasCurrentTile = false;
                    vehicle.NightLighting?.SetMoving(false);
                    RemoveVehiclePresentation(car);
                    SetVehiclePresentationEnabled(vehicle, false);
                    vehicle.LastState = snapshot.State;
                    vehicle.HasLastState = true;
                    return;
                }

                if (!hasAuthoredParkingPose)
                {
                    ReleaseCommuteParkingReservation(vehicle);
                }

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

                if (!vehicle.Settling)
                {
                    RemoveVehiclePresentation(car);
                    vehicle.ViewRecovery.Reset();
                }

                // Sim은 뷰보다 먼저 도착할 수 있다. 이때 현재 월드 위치→주차 앵커 MoveTowards는
                // 꺾인 도로를 chord로 가로지르므로, 도착 방향 폴리라인의 누적거리 끝까지 따라간다.
                RoutePolyline parkingPolyline = snapshot.State == CarState.ParkedWork
                    ? pair.Outbound
                    : pair.Inbound;
                Sample parked;
                bool followingParkingPath = vehicle.Settling;
                bool parkingBlocked = false;
                float parkingPreviousDistance = car.Distance;
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

                    float proposedParkingDistance =
                        Mathf.MoveTowards(
                            car.Distance,
                            parkingPolyline.Length,
                            vehicle.SettleRate *
                            Time.deltaTime);
                    float limitedParkingDistance =
                        LimitVehiclePresentationAdvance(
                            car,
                            RoadTrafficAgentKind.Car,
                            simEngine.StandardVehicleFootprint,
                            parkingPolyline,
                            car.Distance,
                            proposedParkingDistance,
                            yieldToCrossFlowCars: false,
                            out _);
                    parkingBlocked =
                        limitedParkingDistance <
                        proposedParkingDistance - 0.0001f;
                    car.Distance = limitedParkingDistance;
                    parked =
                        parkingPolyline.SampleAt(
                            car.Distance);
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
                float parkingDeltaTime =
                    Mathf.Max(Time.deltaTime, 0.0001f);
                float parkingTravelSpeed =
                    followingParkingPath
                        ? Mathf.Abs(
                              car.Distance -
                              parkingPreviousDistance) /
                          parkingDeltaTime
                        : 0f;
                vehicle.TravelSpeed =
                    parkingTravelSpeed;
                vehicle.CurrentSpeed =
                    parkingTravelSpeed /
                    Mathf.Max(0.0001f, tileSize);
                vehicle.NightLighting?.SetMoving(
                    vehicle.Settling);
                if (followingParkingPath && parked.Dir.sqrMagnitude > 0.001f)
                {
                    float angle = Mathf.Atan2(parked.Dir.y, parked.Dir.x) * Mathf.Rad2Deg;
                    vehicle.Object.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                }
                if (!vehicle.Settling
                    && (snapshot.State == CarState.ParkedHome || snapshot.State == CarState.ParkedWork))
                {
                    Vector2Int building = snapshot.State == CarState.ParkedWork ? car.Work : car.Home;
                    int finalParkingSlot = snapshot.State == CarState.ParkedWork
                        ? snapshot.WorkSlot
                        : snapshot.HomeSlot;
                    SetForwardBuildingParkingRotation(
                        vehicle,
                        building,
                        finalParkingSlot);
                }
                vehicle.CurrentTile = vehicle.Settling
                    ? parkingPolyline.TileAt(
                        Mathf.Clamp(
                            parked.TileIndex,
                            0,
                            parkingPolyline.TileCount - 1))
                    : snapshot.State == CarState.ParkedWork
                        ? car.Work
                        : car.Home;
                vehicle.HasCurrentTile = true;
                SetVehiclePresentationEnabled(vehicle, true);
                SetBrakeLight(vehicle, parkingBlocked);
                HideJamMarks(vehicle);
                if (vehicle.Settling)
                {
                    PublishVehiclePresentation(
                        car,
                        RoadTrafficAgentKind.Car,
                        simEngine.StandardVehicleFootprint,
                        vehicle.Pos,
                        vehicle.Dir,
                        vehicle.TravelSpeed);
                }
                else
                {
                    RemoveVehiclePresentation(car);
                }
                if (snapshot.State == CarState.ParkedWork && !vehicle.Settling)
                    FlushPendingCoinPop(car.Work, vehicle.Object.transform.position);
                vehicle.LastState = snapshot.State;
                vehicle.HasLastState = true;
                return;
            }

            vehicle.Settling = false;
            vehicle.SettleRate = 0f;
            vehicle.NightLighting?.SetMoving(true);
            RoutePolyline poly = inbound ? pair.Inbound : pair.Outbound;
            int tileIndex = Mathf.Clamp(snapshot.TileIndex, 0, poly.TileCount - 1);
            Vector2Int simTile = poly.TileAt(tileIndex);
            float headInset = simEngine.IsSharedCarIntersection(simTile)
                ? intersectionQueueInset * tileSize
                : 0f;
            // Sim QueueSlot을 코리도 상한에 반영해 같은 타일 차량의 목표를 분리한다.
            // 간격은 기존 앞차 추종 노브와 동일하게 두되, 슬롯 증가로 상한이 현재 위치보다
            // 뒤로 가더라도 아래에서 차를 후진시키지 않는다. 타일 경계를 넘는 연속 대기열은
            // Phase B 범위이며, 이번 단계에서도 앞차 추종의 headway 방어는 그대로 유지한다.
            // 07-20 계측: 슬롯 갭 0.55 단독(추종 방어 없이)은 SAME-DIR 겹침 385→769 —
            // 슬롯 목표는 반드시 headway 추종과 병행한다.
            bool stateChanged = hadPrevious && previous != snapshot.State;
            if (stateChanged) car.Distance = 0f;
            bool isRoundaboutTile = IsRoundaboutTile(simTile);
            bool hasRoundaboutAuthorization = snapshot.RoundaboutProgress01 >= 0f
                && isRoundaboutTile;
            float roundaboutStopDistance = 0f;
            bool roundaboutEntryLimited = !hasRoundaboutAuthorization
                && TryGetRoundaboutEntryStopDistance(
                    poly,
                    tileIndex,
                    vehicle,
                    out roundaboutStopDistance);
            bool usesRoundaboutPresentationSpacing =
                hasRoundaboutAuthorization ||
                roundaboutEntryLimited ||
                IsWithinRoundaboutPresentationArea(
                    poly.SampleAt(car.Distance).Pos);
            // 접근 외곽·arm은 roundaboutEntryLimited가 진입 간격을, 링은 로터리 권한이
            // 위치를 책임진다. 이 구간에 일반 타일 슬롯까지 적용하면 같은 거리를 이중 제한한다.
            float targetQueueOffset = isRoundaboutTile || roundaboutEntryLimited
                ? 0f
                : snapshot.QueueOffsetTiles * tileSize;
            // 매 프레임 주행 권한은 기존 상태 기계가 소유한다. ReprojectDistance는 리빌드
            // 바인딩 시점 전용이며, 여기서 공용화하면 권한 우선순위·단조 상태가 달라진다.
            float targetDistance = poly.DistanceAtQueueOffset(
                tileIndex,
                targetQueueOffset,
                headInset);
            bool hasIntersectionAuthorization = snapshot.IntersectionProgress01 >= 0f;
            float intersectionAuthorizedSpeed = 0f;
            if (hasIntersectionAuthorization)
            {
                float intersectionCenter = poly.DistanceAtTile(tileIndex);
                float stageOffset = (snapshot.IntersectionProgress01 - 0.5f) * tileSize;
                targetDistance = Mathf.Clamp(
                    intersectionCenter + stageOffset,
                    0f,
                    poly.Length);

                if (!intersectionMotionStates.TryGetValue(vehicle, out IntersectionMotionState motion)
                    || motion.RouteIndex != car.RouteIndex)
                {
                    motion = new IntersectionMotionState
                    {
                        RouteIndex = car.RouteIndex,
                        AuthorizedDistance = Mathf.Min(car.Distance, targetDistance),
                    };
                    intersectionMotionStates[vehicle] = motion;
                }

                // The simulation value is an authorization boundary, not a render snap point.
                // Pace each newly granted segment across one simulation tick so the next grant
                // normally arrives before the vehicle has to stop at the boundary.
                if (targetDistance > motion.AuthorizedDistance + 0.0001f)
                {
                    float tickInterval = Mathf.Max(0.0001f, simEngine.TickInterval);
                    float bufferedTravelSeconds = tickInterval
                        * (1f + Mathf.Clamp(intersectionVisualLagRatio, 0f, 0.35f));
                    float minimumStageSpeed = tileSize * 0.25f / bufferedTravelSeconds;
                    motion.AuthorizedSpeed = Mathf.Max(
                        minimumStageSpeed,
                        (targetDistance - motion.AuthorizedDistance) / bufferedTravelSeconds);
                    motion.AuthorizedDistance = targetDistance;
                }

                targetDistance = Mathf.Max(car.Distance, targetDistance);
                intersectionAuthorizedSpeed = Mathf.Max(0.01f, motion.AuthorizedSpeed);
            }
            else
            {
                intersectionMotionStates.Remove(vehicle);
            }
            if (hasRoundaboutAuthorization)
            {
                targetDistance = GetRoundaboutAuthorizedDistance(
                    poly,
                    tileIndex,
                    snapshot.RoundaboutProgress01);
            }
            if (snapshot.LinkProgress01 > 0f && tileIndex + 1 < poly.TileCount)
            {
                hasIntersectionAuthorization = false;
                intersectionMotionStates.Remove(vehicle);
                targetDistance = Mathf.Lerp(
                    poly.DistanceAtTile(tileIndex),
                    poly.DistanceAtTile(tileIndex + 1),
                    snapshot.LinkProgress01);
            }
            if (roundaboutEntryLimited)
            {
                // 일반 큐 목표는 현재 타일 중심이다. 로터리 진입 대기 중에는 전용 정지선까지
                // 접근할 수 있어야 하므로 더 앞쪽의 안전 경계를 이번 틱 목표로 사용한다.
                targetDistance = Mathf.Max(targetDistance, roundaboutStopDistance);
            }
            float previousDistance = car.Distance;
            // QueueSlot 자체가 아니라 그 결과인 목표 거리의 방향을 본다. 슬롯 감소로 목표가
            // 앞으로 갈 때만 TargetAdvancing=true이며, 슬롯 증가로 목표가 후퇴하면 false라
            // ceilingSpeed 사슬에 "전진 중"이라는 오신호를 주지 않는다.
            bool targetChanged = !vehicle.HasTickTarget
                || stateChanged
                || vehicle.TargetTileIndex != tileIndex
                || vehicle.TargetRouteIndex != car.RouteIndex
                || Mathf.Abs(vehicle.TargetDistance - targetDistance) > 0.0001f;
            if (targetChanged)
            {
                bool targetAdvancing = !vehicle.HasTickTarget
                    || stateChanged
                    || vehicle.TargetRouteIndex != car.RouteIndex
                    || targetDistance > vehicle.TargetDistance + 0.0001f;
                vehicle.TargetDistance = targetDistance;
                vehicle.TargetTileIndex = tileIndex;
                vehicle.TargetRouteIndex = car.RouteIndex;
                vehicle.HasTickTarget = true;
                vehicle.TargetAdvancing = targetAdvancing;
            }
            else if (tickEdge && !snapshot.WaitingForSpeedCredit)
            {
                // 틱이 지났는데 목표가 그대로 = Sim이 나를 잡고 있다(신호·정원). 천장이 멈췄다.
                vehicle.TargetAdvancing = false;
            }
            // credit 대기(M1-3)는 "심이 잡은 것"이 아니라 "느린 차의 자발적 페이스" —
            // 천장을 끄지 않고 아래의 감속된 순항으로 자연스럽게 흐른다.

            // 속도 기반 주행(MM 전환 Phase 2, 2026-07-20). 차가 프레임 위치의 주인이고,
            // Sim 위치는 목표가 아니라 **코리도 상한**이다. 예전엔 Sim 목표에 닿으면 다음
            // 틱까지 서 있었다(계측: 주행 차량의 프레임 정지 비율 0.898, 로터리 중간 정지).
            // 이제 앞차만 비면 틱 사이에도 계속 굴러간다.
            float dt = Mathf.Max(0f, Time.deltaTime);
            float brakeAccel = Mathf.Max(0.01f, vehicleBrakeAccel);
            // 평균 속도는 Sim이 정한다(틱당 1타일) — 개성으로 최고속도를 바꾸면 느린 차는
            // 영원히 목표를 못 따라잡는다. 개성은 가속도(AccelMul)에만 건다.
            // 차급 SpeedFactor(M1-3)는 예외: 심의 크레딧 게이트가 같은 값으로 전진을
            // 늦추므로, 뷰가 같은 배율로 순항해야 목표를 따라잡되 앞지르지 않는다.
            float speedFactor = snapshot.SpeedFactorNumerator / 60f;
            float nominal = tileSize * speedFactor
                / Mathf.Max(0.0001f, simEngine.TickInterval);
            // 일반 도로는 QueueSlot 목표(슬롯0 포함), 교차로·로터리·링크는 위에서 덮어쓴
            // 각 권한 거리가 그대로 상한이다. +1타일 선행은 Sim 타일 전환 때 새 슬롯 목표보다
            // 앞서 있던 차를 도로 한가운데 세웠으므로 폐지한다(2026-07-24 라이브 계측).
            float corridor = vehicle.TargetDistance;
            if (roundaboutEntryLimited)
            {
                corridor = Mathf.Min(corridor, roundaboutStopDistance);
            }
            bool signalEntryLimited = TryGetSignalEntryStopDistance(
                poly,
                tileIndex,
                vehicle,
                out float signalStopDistance);
            if (signalEntryLimited)
            {
                corridor = Mathf.Min(corridor, signalStopDistance);
            }

            float intersectionStopDistance = 0f;
            bool intersectionEntryLimited = !hasIntersectionAuthorization
                && TryGetIntersectionEntryLimitDistance(
                    poly,
                    tileIndex,
                    vehicle,
                    out intersectionStopDistance);
            float intersectionApproachSpeed = nominal;
            if (intersectionEntryLimited)
            {
                corridor = Mathf.Min(corridor, intersectionStopDistance);
                float remainingTickSeconds = Mathf.Max(
                    0.02f,
                    (1f - simEngine.TickProgress01) * simEngine.TickInterval);
                float distanceToBoundary = Mathf.Max(0f, corridor - car.Distance);
                intersectionApproachSpeed = Mathf.Min(
                    nominal,
                    distanceToBoundary / remainingTickSeconds);
            }

            bool spacingBlocked = false;
            bool corridorRegressed =
                corridor < car.Distance;
            corridor =
                VehicleSpacingMath
                    .ClampCorridorToForwardProgress(
                        car.Distance,
                        corridor);
            if (corridorRegressed)
            {
                // Sim 상한이 뒤로 이동해도 화면 차량은 후진하거나 다른 곡선 구간으로
                // 순간 이동하지 않는다. 현재 위치에서 새 권한을 기다린다.
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
                float authorizedCruise = hasIntersectionAuthorization
                    ? intersectionAuthorizedSpeed
                    : (intersectionEntryLimited ? intersectionApproachSpeed : nominal);
                bool paceQueueArrival = vehicle.TargetAdvancing
                    && !hasIntersectionAuthorization
                    && !hasRoundaboutAuthorization
                    && snapshot.LinkProgress01 <= 0f
                    && !roundaboutEntryLimited
                    && !signalEntryLimited
                    && !intersectionEntryLimited;
                if (paceQueueArrival)
                {
                    float remainingTickSeconds = Mathf.Max(
                        0.02f,
                        (1f - simEngine.TickProgress01) * simEngine.TickInterval);
                    float pacedArrivalSpeed = Mathf.Min(
                        nominal,
                        Mathf.Max(0f, corridor - car.Distance) / remainingTickSeconds);
                    // 2026-07-21에 기각한 "연속 천장 보간"과 다르다: corridor를 앞으로
                    // 발명하지 않고 고정된 Sim 상한 안에서 속도만 낮춰 틱 끝 도착을 맞춘다.
                    // 기존 가감속과 뒤처짐 catch-up은 이 속도 목표의 바깥에서 그대로 작동한다.
                    authorizedCruise = Mathf.Min(authorizedCruise, pacedArrivalSpeed);
                }
                // 개성 SpeedMul(±7%, M1-3)은 순항에만 합성 — 교차로 권한 속도는 심의
                // 마이크로그리드 페이스라 개성을 태우지 않는다(설계 Q4). 느린 개성(0.93)의
                // 누적 뒤처짐은 아래 behind catch-up이 흡수한다.
                float personalityMul = hasIntersectionAuthorization
                    ? 1f
                    : vehicle.Style.SpeedMul;
                float cruise = authorizedCruise * personalityMul
                    * (1f + behind * vehicleCatchUpRange);

                // 제동식은 **상대가 움직이는지**를 반영해야 한다. √(2a·여유)는 '정지한 벽'
                // 공식이라 이산 천장에선 여유가 줄 때마다 감속하는 톱니를 탄다(§4.6.1 계측:
                // 정지의 81.4%가 앞차 없음 + 여유 0.000). 천장이 전진 중이면 v² 부스트로
                // 계속 굴러가고, Sim이 잡으면(TargetAdvancing=false) 진짜 벽으로 보고 선다.
                float ceilingSpeed = vehicle.TargetAdvancing ? authorizedCruise : 0f;
                float desired = Mathf.Min(cruise, Mathf.Sqrt(
                    ceilingSpeed * ceilingSpeed + 2f * brakeAccel * Mathf.Max(0f, corridor - car.Distance)));
                float hardHeadway = float.PositiveInfinity;
                float hardMinimumHeadway = 0f;
                float hardLeaderSpeed = 0f;

                // 정지 사유 둘 중 나머지: 같은 차선 앞차와의 간격. 로터리 합류 차량 쌍은
                // 대칭인 공유 진행축으로 순서를 정하므로 서로를 동시에 앞차로 보지 않는다.
                float headway;
                float leaderSpeed;
                float minimumHeadway;
                if (TryGetLaneHeadway(
                        carIndex,
                        vehicle,
                        usesRoundaboutPresentationSpacing,
                        out headway,
                        out leaderSpeed,
                        out minimumHeadway))
                {
                    float desiredBeforeFollowing = desired;
                    // 상한 없이 노브 그대로 쓴다. 슬롯은 더 이상 위치의 주인이 아니므로
                    // Sim 슬롯 간격(0.250)으로 누를 이유가 없다.
                    hardHeadway = headway;
                    hardMinimumHeadway = minimumHeadway;
                    hardLeaderSpeed = leaderSpeed;
                    float slack = Mathf.Max(
                        0f,
                        headway - minimumHeadway);
                    float follow = Mathf.Sqrt(leaderSpeed * leaderSpeed + 2f * brakeAccel * slack);
                    // 간격이 최소치보다 좁으면 앞차보다 **느리게** 가야 간격이 다시 벌어진다.
                    // 속도를 맞추기만 하면 한번 겹친 쌍이 영원히 겹친 채로 굳는다.
                    // 부족분이 최소치만큼이면(=완전히 포개짐) 0 → 서서 앞차를 보낸다.
                    // 앞차는 나에게 막히지 않으므로(선형 관계) 간격은 반드시 회복된다.
                    if (headway < minimumHeadway)
                    {
                        float recover = Mathf.Clamp01(
                            headway /
                            Mathf.Max(
                                0.0001f,
                                minimumHeadway));
                        follow = Mathf.Min(follow, leaderSpeed * recover);
                    }
                    desired = Mathf.Min(desired, follow);
                    // 권위 위치보다 뒤처진 차량을 복구할 때 실제로 속도를 제한 중인 앞차를
                    // 건너뛰면 정지 직후 순간이동하며 관통한다. 제안 이동량이 이미 0이어도
                    // 최소 간격 안이거나 추종 속도가 더 낮으면 복구 스냅을 계속 금지한다.
                    if (IsVehicleViewRecoveryBlockedByLeader(
                            headway,
                            minimumHeadway,
                            follow,
                            desiredBeforeFollowing,
                            Mathf.Max(0f, corridor - car.Distance)))
                    {
                        spacingBlocked = true;
                    }
                }

                bool decelerating = desired < vehicle.TravelSpeed - 0.01f;
                float rate = decelerating
                    ? brakeAccel
                    : Mathf.Max(0.01f, vehicleDriveAccel) * vehicle.Style.AccelMul;
                vehicle.TravelSpeed = Mathf.MoveTowards(vehicle.TravelSpeed, desired, rate * dt);
                float proposedAdvance = vehicle.TravelSpeed * dt;
                float allowedAdvance = float.IsPositiveInfinity(hardHeadway)
                    ? proposedAdvance
                    : VehicleSpacingMath.LimitAdvance(
                        proposedAdvance,
                        hardHeadway,
                        hardMinimumHeadway);
                if (allowedAdvance < proposedAdvance - 0.0001f)
                {
                    spacingBlocked = true;
                    vehicle.TravelSpeed = Mathf.Min(vehicle.TravelSpeed, hardLeaderSpeed);
                }
                car.Distance = Mathf.Min(corridor, car.Distance + allowedAdvance);
            }

            TryRecoverCommuteVehicleView(
                car,
                snapshot,
                vehicle,
                poly,
                corridor,
                spacingBlocked);

            Sample sample = poly.SampleAt(car.Distance);
            if (snapshot.LinkProgress01 > 0f)
                sample.Pos.z -= 0.35f * Mathf.Sin(snapshot.LinkProgress01 * Mathf.PI);
            vehicle.Object.transform.localPosition = sample.Pos;
            vehicle.Pos = sample.Pos;
            vehicle.Dir = sample.Dir;
            vehicle.CurrentSpeed = Time.deltaTime > 0f
                ? Mathf.Abs(car.Distance - previousDistance) / (tileSize * Time.deltaTime)
                : 0f;
            vehicle.CurrentTile = poly.TileAt(Mathf.Clamp(sample.TileIndex, 0, poly.TileCount - 1));
            vehicle.HasCurrentTile = true;
            PublishVehiclePresentation(
                car,
                RoadTrafficAgentKind.Car,
                simEngine.StandardVehicleFootprint,
                vehicle.Pos,
                vehicle.Dir,
                vehicle.TravelSpeed);
            if (sample.Dir.sqrMagnitude > 0.001f)
            {
                float angle = Mathf.Atan2(sample.Dir.y, sample.Dir.x) * Mathf.Rad2Deg;
                vehicle.Object.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
            SetVehiclePresentationEnabled(vehicle, true);
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

        internal static bool IsVehicleViewRecoveryBlockedByLeader(
            float headway,
            float minimumHeadway,
            float followingSpeed,
            float unconstrainedSpeed,
            float authoritativeAdvance) =>
            headway <= minimumHeadway + 0.0001f ||
            followingSpeed < unconstrainedSpeed - 0.0001f ||
            authoritativeAdvance >
            Mathf.Max(0f, headway - minimumHeadway) + 0.0001f;

        internal static bool ShouldShowCommuteVehicle(CarSnapshot snapshot) =>
            ShouldShowCommuteVehicle(
                snapshot.IsVisible,
                snapshot.State,
                snapshot.QueueSlot,
                snapshot.IntersectionProgress01,
                snapshot.LinkProgress01,
                snapshot.RoundaboutProgress01);

        internal static bool ShouldShowCommuteVehicle(
            bool isVisible,
            CarState state,
            int queueSlot,
            float intersectionProgress,
            float linkProgress,
            float roundaboutProgress)
        {
            if (!isVisible) return false;
            bool moving = state == CarState.Outbound ||
                state == CarState.Inbound;
            if (!moving) return true;

            // 스케줄은 출발 시각에 먼저 Moving 상태가 되지만 실제 도로 큐 입장은
            // 수용량이 빌 때까지 지연될 수 있다. 이 구간을 route[0]에 그리면 아직
            // 입장하지 않은 차량이 도로 위에서 멈춘 것처럼 보이므로 위치가 확인된
            // 차량만 주행 비주얼과 간격 후보에 포함한다.
            return queueSlot >= 0 ||
                intersectionProgress >= 0f ||
                linkProgress > 0f ||
                roundaboutProgress >= 0f;
        }

        internal static float ResolveVehicleViewRecoveryDistance(
            float previousDistance,
            float recoveredDistance,
            float authoritativeDistance,
            float polylineLength) =>
            Mathf.Clamp(
                Mathf.Max(
                    previousDistance,
                    Mathf.Min(recoveredDistance, authoritativeDistance)),
                0f,
                polylineLength);

        private bool TryRecoverCommuteVehicleView(
            CommuteCar car,
            CarSnapshot snapshot,
            RouteVehicle vehicle,
            RoutePolyline polyline,
            float authoritativeDistance,
            bool spacingBlocked)
        {
            int routeVersion = unchecked(
                car.RouteIndex * 31 + (int)snapshot.State);
            VehicleViewRecoveryReason reason =
                vehicle.ViewRecovery.Observe(
                    car.Distance,
                    authoritativeDistance,
                    tileSize,
                    Time.unscaledDeltaTime,
                    routeVersion,
                    eligible: !spacingBlocked &&
                              (snapshot.State == CarState.Outbound ||
                               snapshot.State == CarState.Inbound),
                    stopPresentationPending: false,
                    profile: VehicleViewRecoveryProfile);
            if (reason == VehicleViewRecoveryReason.None)
            {
                return false;
            }

            float previousDistance = car.Distance;
            float recoveredDistance = ReprojectDistance(
                snapshot,
                polyline,
                snapshot.QueueOffsetTiles);
            car.Distance = spacingBlocked
                ? previousDistance
                : ResolveVehicleViewRecoveryDistance(
                    previousDistance,
                    recoveredDistance,
                    authoritativeDistance,
                    polyline.Length);
            vehicle.CurrentSpeed = 0f;
            vehicle.TravelSpeed = 0f;
            vehicle.HasTickTarget = false;
            vehicle.TargetTileIndex = -1;
            vehicle.TargetRouteIndex = -1;
            intersectionMotionStates.Remove(vehicle);
            RemoveVehiclePresentation(car);
            commuteRoutesBuilt = false;
            vehicle.ViewRecovery.Synchronize(
                car.Distance,
                routeVersion);

            Debug.LogWarning(
                $"[VehicleViewRecovery] Car view recovered ({reason}). " +
                $"mode={(spacingBlocked ? "rebake" : "resync")}, " +
                $"route={car.RouteIndex}, tile={snapshot.TileIndex}, " +
                $"distance={previousDistance:F3}->{car.Distance:F3}.",
                vehicle.Object);
            return true;
        }

        private static void UpdateParkingPresentationScale(
            RouteVehicle vehicle,
            float multiplier)
        {
            if (vehicle?.Object == null)
            {
                return;
            }

            if (vehicle.NominalLocalScale.sqrMagnitude <= 0.000001f)
            {
                vehicle.NominalLocalScale =
                    vehicle.Object.transform.localScale;
            }

            Vector3 nominalScale = vehicle.NominalLocalScale;
            float nominalMagnitude = nominalScale.magnitude;
            if (nominalMagnitude <= 0.0001f)
            {
                return;
            }

            float currentMultiplier =
                vehicle.Object.transform.localScale.magnitude /
                nominalMagnitude;
            float nextMultiplier =
                MoveParkingPresentationScaleMultiplier(
                    currentMultiplier,
                    multiplier,
                    Time.deltaTime);
            vehicle.Object.transform.localScale =
                nominalScale * nextMultiplier;
        }

        internal static float MoveParkingPresentationScaleMultiplier(
            float current,
            float target,
            float deltaTime)
        {
            return Mathf.MoveTowards(
                current,
                Mathf.Clamp(target, 0.5f, 1f),
                ParkingPresentationScaleRate * Mathf.Max(0f, deltaTime));
        }

        private bool TryReserveCommuteParkingPose(
            RouteVehicle vehicle,
            Vector2Int building,
            int slotIndex)
        {
            if (vehicle?.Object == null || slotIndex < 0)
            {
                return false;
            }

            if (vehicle.HasParkingReservation &&
                vehicle.ReservedParkingBuilding == building &&
                vehicle.ReservedParkingSlot == slotIndex)
            {
                return true;
            }

            ReleaseCommuteParkingReservation(vehicle);
            if (!TryReserveBuildingParkingPose(
                    building,
                    slotIndex,
                    vehicle.Object.transform,
                    out _,
                    out _))
            {
                return false;
            }

            vehicle.ReservedParkingBuilding = building;
            vehicle.ReservedParkingSlot = slotIndex;
            vehicle.HasParkingReservation = true;
            return true;
        }

        private void ReleaseCommuteParkingReservation(
            RouteVehicle vehicle)
        {
            if (vehicle == null || !vehicle.HasParkingReservation)
            {
                return;
            }

            if (vehicle.Object != null)
            {
                ReleaseBuildingParkingReservation(
                    vehicle.ReservedParkingBuilding,
                    vehicle.ReservedParkingSlot,
                    vehicle.Object.transform);
            }

            vehicle.ReservedParkingBuilding = default;
            vehicle.ReservedParkingSlot = -1;
            vehicle.HasParkingReservation = false;
        }

        private void SetForwardBuildingParkingRotation(
            RouteVehicle vehicle,
            Vector2Int building,
            int slotIndex)
        {
            if (TryGetBuildingParkingPose(
                    building,
                    Mathf.Max(0, slotIndex),
                    out _,
                    out Vector3 authoredForward) &&
                authoredForward.sqrMagnitude >= 0.001f)
            {
                float authoredAngle = Mathf.Atan2(
                    authoredForward.y,
                    authoredForward.x) * Mathf.Rad2Deg;
                vehicle.Object.transform.localRotation =
                    Quaternion.Euler(0f, 0f, authoredAngle);
                return;
            }

            if (!tileVisuals.TryGetValue(building, out TileVisual buildingVisual))
            {
                return;
            }

            Vector3 worldForward = buildingVisual.Object.transform.TransformDirection(Vector3.up);
            Vector3 localForward = transform.InverseTransformDirection(worldForward);
            if (localForward.sqrMagnitude < 0.001f)
            {
                return;
            }

            float angle = Mathf.Atan2(localForward.y, localForward.x) * Mathf.Rad2Deg;
            vehicle.Object.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private void ResetCommuteState()
        {
            bakedRoutes.Clear();
            foreach (KeyValuePair<CommuteCar, RouteVehicle> entry in
                     carVehicles)
            {
                RemoveVehiclePresentation(entry.Key);
                ReleaseCommuteParkingReservation(entry.Value);
            }

            carVehicles.Clear();
            DestroyParkingVisuals();
            carSimMirrors.Clear();
            commuteRoutesBuilt = false;
            commuteRoutesHash = 0;
            commuteTuningHash = 0;
        }
    }
}
