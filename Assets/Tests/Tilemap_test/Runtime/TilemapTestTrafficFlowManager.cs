using System;
using System.Collections.Generic;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.TilemapTest
{
    public sealed class TilemapTestTrafficFlowManager : MonoBehaviour
    {
        private static readonly Vector2Int[] Directions =
        {
            Vector2Int.up,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.left
        };

        [Header("References")]
        [SerializeField] private TilemapTestField field;
        [SerializeField] private Transform vehiclePrefab;

        [Header("Spawning")]
        [SerializeField] private int maxWaitingTripsPerHouse = 12;
        [SerializeField] private int maxSpawnAttemptsPerFrame = 8;
        [SerializeField] private int maxActiveVehicles = 120;
        [SerializeField] private float workerDepartureSpreadHours = 2f;
        [SerializeField] private float studentDepartureSpreadHours = 1.5f;

        [Header("Failure Feedback")]
        [SerializeField] private float tripSpawnTimeoutHours = 2f;
        [SerializeField] private Color blockedFacilityWarningColor = Color.red;
        [SerializeField] private float blockedFacilityFlashDuration = 1.2f;
        [SerializeField] private float blockedFacilityFlashInterval = 0.12f;

        [Header("Time")]
        [SerializeField] private float startHour = 6f;
        [SerializeField] private float minutesPerRealSecond = 30f;
        [SerializeField] private bool loopDay = true;

        [Header("Movement")]
        [SerializeField] private float targetSpeed = 3f;
        [SerializeField] private float acceleration = 8f;
        [SerializeField] private float minVehicleGap = 0.55f;
        [SerializeField] private float slowGap = 1.4f;
        [SerializeField] private bool useTrafficCellOccupancy = true;
        [SerializeField] private float cellLookAheadDistance = 0.35f;
        [SerializeField] private float cellOccupiedSpeedMultiplier = 0.15f;
        [SerializeField] private float vehicleHeight = 0.18f;

        private readonly Dictionary<Vector2Int, TileType> tileTypes = new Dictionary<Vector2Int, TileType>();
        private readonly Dictionary<Vector2Int, TilemapTestTile> tilesByPosition = new Dictionary<Vector2Int, TilemapTestTile>();
        [SerializeField] private List<HouseDemandState> houseDemandStates = new List<HouseDemandState>();
        private readonly Dictionary<Vector2Int, HouseDemandState> housesByPosition = new Dictionary<Vector2Int, HouseDemandState>();

        private readonly List<Vector2Int> offices = new List<Vector2Int>();
        private readonly List<Vector2Int> schools = new List<Vector2Int>();
        private readonly List<Vector2Int> shops = new List<Vector2Int>();
        private readonly Dictionary<Vector2Int, int> shopCapacities = new Dictionary<Vector2Int, int>();
        private readonly List<ShopVisitState> activeShopVisits = new List<ShopVisitState>();
        private readonly List<RouteData> routes = new List<RouteData>();
        private readonly List<VehicleData> vehicles = new List<VehicleData>();
        private readonly Dictionary<TrafficCellKey, VehicleData> occupiedCells = new Dictionary<TrafficCellKey, VehicleData>();
        private readonly Queue<Vector2Int> bfsQueue = new Queue<Vector2Int>();
        private readonly Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();

        private int nextHouseIndex;
        private int nextLaneIndex;
        private float currentHour;
        private int currentDayIndex;
        private int missedWorkerCommutes;
        private int missedStudentCommutes;
        private int missedWorkerReturns;
        private int missedStudentReturns;

        public TilemapTestField Field { get; private set; }
        public Transform VehiclePrefab { get; private set; }
        public int ActiveVehicleCount => vehicles.Count;
        public float CurrentHour => currentHour;
        public int CurrentDayIndex => currentDayIndex;
        public float MinutesPerRealSecond => minutesPerRealSecond;
        public int TotalWaitingTrips => GetTotalWaitingTrips();
        public int MissedWorkerCommutes => missedWorkerCommutes;
        public int MissedStudentCommutes => missedStudentCommutes;
        public int MissedWorkerReturns => missedWorkerReturns;
        public int MissedStudentReturns => missedStudentReturns;
        public int TotalMissedCommutes => missedWorkerCommutes + missedStudentCommutes;
        public int TotalMissedReturns => missedWorkerReturns + missedStudentReturns;

        public void Configure(TilemapTestField testField, Transform prefab)
        {
            field = testField;
            vehiclePrefab = prefab;
            Field = field;
            VehiclePrefab = vehiclePrefab;
            RebuildNetwork();
        }

        public void RebuildNetwork()
        {
            Field = field;
            VehiclePrefab = vehiclePrefab;
            tileTypes.Clear();
            tilesByPosition.Clear();
            houseDemandStates.Clear();
            housesByPosition.Clear();
            offices.Clear();
            schools.Clear();
            shops.Clear();
            shopCapacities.Clear();
            activeShopVisits.Clear();
            routes.Clear();
            currentHour = startHour;
            currentDayIndex = Mathf.FloorToInt(startHour / 24f);
            missedWorkerCommutes = 0;
            missedStudentCommutes = 0;
            missedWorkerReturns = 0;
            missedStudentReturns = 0;

            if (field == null)
            {
                Debug.LogWarning("TrafficFlowManager needs a TilemapTestField reference.");
                return;
            }

            TilemapTestTile[] tiles = field.GetComponentsInChildren<TilemapTestTile>(true);
            foreach (TilemapTestTile tile in tiles)
            {
                tileTypes[tile.GridPosition] = tile.TileType;
                tilesByPosition[tile.GridPosition] = tile;

                switch (tile.FacilityKind)
                {
                    case TilemapTestFacilityKind.House:
                        HouseDemandState house = new HouseDemandState(tile.GridPosition, tile.OfficeWorkers, tile.Students, tile.Homebodies);
                        houseDemandStates.Add(house);
                        housesByPosition[tile.GridPosition] = house;
                        break;
                    case TilemapTestFacilityKind.Office:
                        offices.Add(tile.GridPosition);
                        break;
                    case TilemapTestFacilityKind.School:
                        schools.Add(tile.GridPosition);
                        break;
                    case TilemapTestFacilityKind.Shop:
                        shops.Add(tile.GridPosition);
                        shopCapacities[tile.GridPosition] = tile.MaxCapacity;
                        break;
                }
            }

            Debug.Log($"TrafficFlowManager network rebuilt. Houses: {houseDemandStates.Count}, Offices: {offices.Count}, Schools: {schools.Count}, Shops: {shops.Count}.");
        }

        private void Awake()
        {
            Field = field;
            VehiclePrefab = vehiclePrefab;
            RebuildNetwork();
        }

        private void Update()
        {
            if (field == null || vehiclePrefab == null)
            {
                return;
            }

            UpdateClock(Time.deltaTime);
            ReleaseFinishedShopVisits();
            ExpireTimedOutTrips();
            UpdateHouseDemand(Time.deltaTime);
            TrySpawnWaitingTrips();
            UpdateVehicles(Time.deltaTime);
        }

        private void UpdateHouseDemand(float deltaTime)
        {
            float deltaHours = (minutesPerRealSecond * deltaTime) / 60f;
            DemandSettings settings = DemandSettings.FromField(
                field,
                workerDepartureSpreadHours,
                studentDepartureSpreadHours);

            for (int i = 0; i < houseDemandStates.Count; i++)
            {
                HouseDemandState house = houseDemandStates[i];
                house.AddDailyDemand(deltaHours, currentHour, currentDayIndex, GetAbsoluteHour(), maxWaitingTripsPerHouse, settings);
            }
        }

        private void ExpireTimedOutTrips()
        {
            float absoluteHour = GetAbsoluteHour();
            for (int i = 0; i < houseDemandStates.Count; i++)
            {
                HouseDemandState house = houseDemandStates[i];
                house.ExpireTimedOutTrips(
                    absoluteHour,
                    tripSpawnTimeoutHours,
                    out int workerCommutes,
                    out int studentCommutes,
                    out int workerReturns,
                    out int studentReturns);

                if (workerCommutes > 0 || studentCommutes > 0)
                {
                    missedWorkerCommutes += workerCommutes;
                    missedStudentCommutes += studentCommutes;
                    FlashTile(house.GridPosition);
                }

                if (workerReturns > 0)
                {
                    missedWorkerReturns += workerReturns;
                    if (TryFindNearest(house.GridPosition, offices, out Vector2Int office))
                    {
                        FlashTile(office);
                    }
                }

                if (studentReturns > 0)
                {
                    missedStudentReturns += studentReturns;
                    if (TryFindNearest(house.GridPosition, schools, out Vector2Int school))
                    {
                        FlashTile(school);
                    }
                }
            }
        }

        private void FlashTile(Vector2Int position)
        {
            if (tilesByPosition.TryGetValue(position, out TilemapTestTile tile))
            {
                tile.FlashWarning(blockedFacilityWarningColor, blockedFacilityFlashDuration, blockedFacilityFlashInterval);
            }
        }

        private void UpdateClock(float deltaTime)
        {
            float deltaHours = (minutesPerRealSecond * deltaTime) / 60f;
            currentHour += deltaHours;

            if (loopDay)
            {
                if (currentHour >= 24f)
                {
                    int elapsedDays = Mathf.FloorToInt(currentHour / 24f);
                    currentDayIndex += Mathf.Max(1, elapsedDays);
                    currentHour = Mathf.Repeat(currentHour, 24f);
                }
            }
            else
            {
                currentDayIndex = Mathf.FloorToInt(currentHour / 24f);
                currentHour = Mathf.Min(24f, currentHour);
            }
        }

        private void TrySpawnWaitingTrips()
        {
            if (vehicles.Count >= maxActiveVehicles || houseDemandStates.Count == 0)
            {
                return;
            }

            int attempts = Mathf.Min(maxSpawnAttemptsPerFrame, houseDemandStates.Count);
            for (int i = 0; i < attempts && vehicles.Count < maxActiveVehicles; i++)
            {
                HouseDemandState house = houseDemandStates[nextHouseIndex % houseDemandStates.Count];
                nextHouseIndex++;

                if (house.WaitingTrips <= 0)
                {
                    continue;
                }

                TilemapTestTripCategory category = house.PeekNextTripCategory();
                if (TrySpawnVehicleForHouse(house.GridPosition, category))
                {
                    house.ConsumeWaitingTrip(category);
                }
            }
        }

        private bool TrySpawnVehicleForHouse(Vector2Int house, TilemapTestTripCategory category)
        {
            if (!TrySelectTripEndpoints(house, category, out Vector2Int source, out Vector2Int destination))
            {
                return false;
            }

            int laneIndex = nextLaneIndex;
            nextLaneIndex = 1 - nextLaneIndex;

            if (!TryGetOrCreateRoute(source, destination, laneIndex, out RouteData route))
            {
                return false;
            }

            if (!CanSpawnOnLane(route, laneIndex))
            {
                return false;
            }

            Transform vehicle = Instantiate(vehiclePrefab, transform);
            VehicleData data = new VehicleData(vehicle, route, laneIndex, category, house);
            ApplyVehicleTransform(data);
            vehicles.Add(data);
            return true;
        }

        private bool TrySelectTripEndpoints(
            Vector2Int house,
            TilemapTestTripCategory category,
            out Vector2Int source,
            out Vector2Int destination)
        {
            source = house;
            destination = default;

            switch (category)
            {
                case TilemapTestTripCategory.Worker:
                    return TryFindNearest(house, offices, out destination);
                case TilemapTestTripCategory.Student:
                    return TryFindNearest(house, schools, out destination);
                case TilemapTestTripCategory.WorkerReturnHome:
                    destination = house;
                    return TryFindNearest(house, offices, out source);
                case TilemapTestTripCategory.StudentReturnHome:
                    destination = house;
                    return TryFindNearest(house, schools, out source);
                case TilemapTestTripCategory.WorkerShopping:
                case TilemapTestTripCategory.StudentShopping:
                case TilemapTestTripCategory.HomebodyShopping:
                    return TryFindNearestAvailableShop(house, out destination);
                case TilemapTestTripCategory.WorkerShoppingReturnHome:
                case TilemapTestTripCategory.StudentShoppingReturnHome:
                case TilemapTestTripCategory.HomebodyShoppingReturnHome:
                    destination = house;
                    return TryFindNearest(house, shops, out source);
                default:
                    return false;
            }
        }

        private static bool IsWithinHourRange(float hour, float start, float end)
        {
            if (Mathf.Approximately(start, end))
            {
                return true;
            }

            if (start < end)
            {
                return hour >= start && hour < end;
            }

            return hour >= start || hour < end;
        }

        private bool TryFindNearest(Vector2Int from, IReadOnlyList<Vector2Int> candidates, out Vector2Int nearest)
        {
            nearest = default;

            if (candidates.Count == 0)
            {
                return false;
            }

            int bestDistance = int.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                Vector2Int candidate = candidates[i];
                int distance = Math.Abs(from.x - candidate.x) + Math.Abs(from.y - candidate.y);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    nearest = candidate;
                }
            }

            return true;
        }

        private bool TryFindNearestAvailableShop(Vector2Int from, out Vector2Int nearest)
        {
            nearest = default;

            if (shops.Count == 0)
            {
                return false;
            }

            int bestDistance = int.MaxValue;
            for (int i = 0; i < shops.Count; i++)
            {
                Vector2Int shop = shops[i];
                if (!HasShopCapacity(shop))
                {
                    continue;
                }

                int distance = Math.Abs(from.x - shop.x) + Math.Abs(from.y - shop.y);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    nearest = shop;
                }
            }

            return bestDistance != int.MaxValue;
        }

        private bool HasShopCapacity(Vector2Int shop)
        {
            int capacity = shopCapacities.TryGetValue(shop, out int storedCapacity) ? storedCapacity : 0;
            if (capacity <= 0)
            {
                return false;
            }

            int occupied = 0;
            for (int i = 0; i < activeShopVisits.Count; i++)
            {
                if (activeShopVisits[i].Shop == shop)
                {
                    occupied++;
                }
            }

            return occupied < capacity;
        }

        private bool TryGetOrCreateRoute(Vector2Int source, Vector2Int destination, int laneIndex, out RouteData route)
        {
            for (int i = 0; i < routes.Count; i++)
            {
                if (routes[i].Source == source && routes[i].Destination == destination && routes[i].LaneIndex == laneIndex)
                {
                    route = routes[i];
                    return true;
                }
            }

            route = null;

            if (!TryGetAccessRoad(source, out Vector2Int fromRoad) ||
                !TryGetAccessRoad(destination, out Vector2Int toRoad) ||
                !TryFindRoadPath(fromRoad, toRoad, out List<Vector2Int> gridPath))
            {
                return false;
            }

            route = new RouteData(source, destination, laneIndex, BuildRoutePoints(gridPath, laneIndex));
            routes.Add(route);
            return true;
        }

        private bool TryGetAccessRoad(Vector2Int building, out Vector2Int road)
        {
            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int candidate = building + Directions[i];
                if (IsRoad(candidate))
                {
                    road = candidate;
                    return true;
                }
            }

            road = default;
            return false;
        }

        private bool TryFindRoadPath(Vector2Int from, Vector2Int to, out List<Vector2Int> path)
        {
            path = null;

            if (!IsRoad(from) || !IsRoad(to))
            {
                return false;
            }

            bfsQueue.Clear();
            cameFrom.Clear();
            bfsQueue.Enqueue(from);
            cameFrom[from] = from;

            while (bfsQueue.Count > 0)
            {
                Vector2Int current = bfsQueue.Dequeue();

                if (current == to)
                {
                    path = BuildGridPath(from, to);
                    return true;
                }

                for (int i = 0; i < Directions.Length; i++)
                {
                    Vector2Int next = current + Directions[i];
                    if (!IsRoad(next) || cameFrom.ContainsKey(next))
                    {
                        continue;
                    }

                    cameFrom[next] = current;
                    bfsQueue.Enqueue(next);
                }
            }

            return false;
        }

        private List<Vector2Int> BuildGridPath(Vector2Int from, Vector2Int to)
        {
            List<Vector2Int> path = new List<Vector2Int>();
            Vector2Int current = to;

            while (current != from)
            {
                path.Add(current);
                current = cameFrom[current];
            }

            path.Add(from);
            path.Reverse();
            return path;
        }

        private List<RoutePoint> BuildRoutePoints(IReadOnlyList<Vector2Int> gridPath, int laneIndex)
        {
            List<RoutePoint> routePoints = new List<RoutePoint>(gridPath.Count);

            for (int i = 0; i < gridPath.Count; i++)
            {
                Vector2Int previous = i > 0 ? gridPath[i - 1] : gridPath[i];
                Vector2Int next = i < gridPath.Count - 1 ? gridPath[i + 1] : gridPath[i];
                Vector2Int direction = next != gridPath[i] ? next - gridPath[i] : gridPath[i] - previous;
                Vector2Int localCell = GetLaneLocalCell(direction, laneIndex);
                Vector3 point = TrafficCellToWorld(gridPath[i], localCell);
                point.y += vehicleHeight;
                routePoints.Add(new RoutePoint(gridPath[i], localCell, direction, laneIndex, point));
            }

            return routePoints;
        }

        private Vector3 TrafficCellToWorld(Vector2Int userTile, Vector2Int localCell)
        {
            float quarter = field.TileSize * 0.25f;
            Vector3 tileCenter = field.GridToWorld(userTile);

            return tileCenter + new Vector3(
                localCell.x == 0 ? -quarter : quarter,
                0f,
                localCell.y == 0 ? -quarter : quarter);
        }

        private static Vector2Int GetLaneLocalCell(Vector2Int direction, int laneIndex)
        {
            if (direction.x > 0)
            {
                return new Vector2Int(laneIndex, 1);
            }

            if (direction.x < 0)
            {
                return new Vector2Int(1 - laneIndex, 0);
            }

            if (direction.y > 0)
            {
                return new Vector2Int(0, laneIndex);
            }

            if (direction.y < 0)
            {
                return new Vector2Int(1, 1 - laneIndex);
            }

            return laneIndex == 0 ? new Vector2Int(0, 0) : new Vector2Int(1, 1);
        }

        private bool CanSpawnOnLane(RouteData route, int laneIndex)
        {
            if (useTrafficCellOccupancy)
            {
                RebuildTrafficCellOccupancy();
                TrafficCellKey spawnKey = route.GetTrafficCellKey(0f);
                TrafficCellKey aheadKey = route.GetTrafficCellKey(cellLookAheadDistance);
                if (IsCellBlockedByAnotherVehicle(spawnKey, null) ||
                    IsCellBlockedByAnotherVehicle(aheadKey, null))
                {
                    return false;
                }
            }

            route.Sample(0f, out Vector3 spawnPosition, out _);

            for (int i = 0; i < vehicles.Count; i++)
            {
                VehicleData vehicle = vehicles[i];
                vehicle.Route.Sample(vehicle.Distance, out Vector3 vehiclePosition, out _);

                if (Vector3.Distance(vehiclePosition, spawnPosition) < minVehicleGap)
                {
                    return false;
                }

                if (vehicle.Route != route || vehicle.LaneIndex != laneIndex)
                {
                    continue;
                }

                if (vehicle.Distance < minVehicleGap)
                {
                    return false;
                }
            }

            return true;
        }

        private void UpdateVehicles(float deltaTime)
        {
            RebuildTrafficCellOccupancy();

            for (int i = vehicles.Count - 1; i >= 0; i--)
            {
                VehicleData vehicle = vehicles[i];
                float targetLaneSpeed = GetSpeedWithGap(vehicle);
                vehicle.CurrentSpeed = Mathf.MoveTowards(
                    vehicle.CurrentSpeed,
                    targetLaneSpeed,
                    acceleration * deltaTime);

                vehicle.Distance += vehicle.CurrentSpeed * deltaTime;

                if (vehicle.Distance >= vehicle.Route.TotalLength)
                {
                    RegisterArrival(vehicle);
                    Destroy(vehicle.Transform.gameObject);
                    vehicles.RemoveAt(i);
                    continue;
                }

                ApplyVehicleTransform(vehicle);
            }
        }

        private void RebuildTrafficCellOccupancy()
        {
            occupiedCells.Clear();

            if (!useTrafficCellOccupancy)
            {
                return;
            }

            for (int i = 0; i < vehicles.Count; i++)
            {
                VehicleData vehicle = vehicles[i];
                TrafficCellKey key = vehicle.Route.GetTrafficCellKey(vehicle.Distance);
                if (!key.IsValid || occupiedCells.ContainsKey(key))
                {
                    continue;
                }

                occupiedCells.Add(key, vehicle);
            }
        }

        private float GetSpeedWithGap(VehicleData vehicle)
        {
            if (useTrafficCellOccupancy)
            {
                float cellSpeed = GetSpeedWithTrafficCellOccupancy(vehicle);
                if (cellSpeed <= 0f)
                {
                    return 0f;
                }

                float routeSpeed = GetSpeedWithRouteGap(vehicle);
                return Mathf.Min(cellSpeed, routeSpeed);
            }

            return GetSpeedWithRouteGap(vehicle);
        }

        private float GetSpeedWithTrafficCellOccupancy(VehicleData vehicle)
        {
            TrafficCellKey currentKey = vehicle.Route.GetTrafficCellKey(vehicle.Distance);
            TrafficCellKey aheadKey = vehicle.Route.GetTrafficCellKey(vehicle.Distance + cellLookAheadDistance);

            if (IsCellBlockedByAnotherVehicle(currentKey, vehicle) ||
                IsCellBlockedByAnotherVehicle(aheadKey, vehicle))
            {
                return 0f;
            }

            TrafficCellKey farAheadKey = vehicle.Route.GetTrafficCellKey(vehicle.Distance + slowGap);
            if (IsCellBlockedByAnotherVehicle(farAheadKey, vehicle))
            {
                return targetSpeed * Mathf.Clamp01(cellOccupiedSpeedMultiplier);
            }

            return targetSpeed;
        }

        private bool IsCellBlockedByAnotherVehicle(TrafficCellKey key, VehicleData vehicle)
        {
            return key.IsValid &&
                   occupiedCells.TryGetValue(key, out VehicleData occupyingVehicle) &&
                   occupyingVehicle != vehicle;
        }

        private float GetSpeedWithRouteGap(VehicleData vehicle)
        {
            float distanceToFront = float.PositiveInfinity;

            for (int i = 0; i < vehicles.Count; i++)
            {
                VehicleData other = vehicles[i];
                if (other == vehicle || other.Route != vehicle.Route || other.LaneIndex != vehicle.LaneIndex)
                {
                    continue;
                }

                float gap = other.Distance - vehicle.Distance;
                if (gap > 0f && gap < distanceToFront)
                {
                    distanceToFront = gap;
                }
            }

            if (distanceToFront < minVehicleGap)
            {
                return 0f;
            }

            if (distanceToFront < slowGap)
            {
                return targetSpeed * Mathf.InverseLerp(minVehicleGap, slowGap, distanceToFront);
            }

            return targetSpeed;
        }

        private void ApplyVehicleTransform(VehicleData vehicle)
        {
            vehicle.Route.Sample(vehicle.Distance, out Vector3 position, out Vector3 forward);
            vehicle.Transform.position = position;
            vehicle.Transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private void RegisterArrival(VehicleData vehicle)
        {
            if (!housesByPosition.TryGetValue(vehicle.Home, out HouseDemandState house))
            {
                return;
            }

            switch (vehicle.Category)
            {
                case TilemapTestTripCategory.Worker:
                    house.ArriveAtWork();
                    break;
                case TilemapTestTripCategory.Student:
                    house.ArriveAtSchool();
                    break;
                case TilemapTestTripCategory.WorkerReturnHome:
                    house.ArriveWorkerHome();
                    break;
                case TilemapTestTripCategory.StudentReturnHome:
                    house.ArriveStudentHome();
                    break;
                case TilemapTestTripCategory.WorkerShopping:
                    RegisterShopArrival(vehicle.Route.Destination, vehicle.Home, TilemapTestTripCategory.WorkerShoppingReturnHome);
                    break;
                case TilemapTestTripCategory.StudentShopping:
                    RegisterShopArrival(vehicle.Route.Destination, vehicle.Home, TilemapTestTripCategory.StudentShoppingReturnHome);
                    break;
                case TilemapTestTripCategory.HomebodyShopping:
                    RegisterShopArrival(vehicle.Route.Destination, vehicle.Home, TilemapTestTripCategory.HomebodyShoppingReturnHome);
                    break;
                case TilemapTestTripCategory.WorkerShoppingReturnHome:
                    house.ArriveWorkerHome();
                    break;
                case TilemapTestTripCategory.StudentShoppingReturnHome:
                    house.ArriveStudentHome();
                    break;
                case TilemapTestTripCategory.HomebodyShoppingReturnHome:
                    house.ArriveHomebodyHome();
                    break;
            }
        }

        private void RegisterShopArrival(Vector2Int shop, Vector2Int home, TilemapTestTripCategory returnCategory)
        {
            float releaseHour = GetAbsoluteHour() + field.ShopDwellHours;
            activeShopVisits.Add(new ShopVisitState(shop, home, returnCategory, releaseHour));
        }

        private void ReleaseFinishedShopVisits()
        {
            float absoluteHour = GetAbsoluteHour();
            for (int i = activeShopVisits.Count - 1; i >= 0; i--)
            {
                ShopVisitState visit = activeShopVisits[i];
                if (visit.ReleaseAbsoluteHour > absoluteHour)
                {
                    continue;
                }

                if (!housesByPosition.TryGetValue(visit.Home, out HouseDemandState house))
                {
                    activeShopVisits.RemoveAt(i);
                    continue;
                }

                if (house.QueueReturnFromShop(visit.ReturnCategory, maxWaitingTripsPerHouse))
                {
                    activeShopVisits.RemoveAt(i);
                }
            }
        }

        private float GetAbsoluteHour()
        {
            return (currentDayIndex * 24f) + currentHour;
        }

        private int GetTotalWaitingTrips()
        {
            int total = 0;
            for (int i = 0; i < houseDemandStates.Count; i++)
            {
                total += houseDemandStates[i].WaitingTrips;
            }

            return total;
        }

        private bool IsRoad(Vector2Int tile)
        {
            return tileTypes.TryGetValue(tile, out TileType type) && type == TileType.Road;
        }

        private sealed class VehicleData
        {
            public VehicleData(Transform transform, RouteData route, int laneIndex, TilemapTestTripCategory category, Vector2Int home)
            {
                Transform = transform;
                Route = route;
                LaneIndex = laneIndex;
                Category = category;
                Home = home;
            }

            public Transform Transform { get; }
            public RouteData Route { get; }
            public int LaneIndex { get; }
            public TilemapTestTripCategory Category { get; }
            public Vector2Int Home { get; }
            public float Distance { get; set; }
            public float CurrentSpeed { get; set; }
        }

        private readonly struct ShopVisitState
        {
            public ShopVisitState(
                Vector2Int shop,
                Vector2Int home,
                TilemapTestTripCategory returnCategory,
                float releaseAbsoluteHour)
            {
                Shop = shop;
                Home = home;
                ReturnCategory = returnCategory;
                ReleaseAbsoluteHour = releaseAbsoluteHour;
            }

            public Vector2Int Shop { get; }
            public Vector2Int Home { get; }
            public TilemapTestTripCategory ReturnCategory { get; }
            public float ReleaseAbsoluteHour { get; }
        }

        [Serializable]
        private sealed class HouseDemandState
        {
            [SerializeField] private Vector2Int gridPosition;
            [SerializeField] private int totalWorkers;
            [SerializeField] private int totalStudents;
            [SerializeField] private int totalHomebodies;
            [SerializeField] private int workersAtHome;
            [SerializeField] private int studentsAtHome;
            [SerializeField] private int homebodiesAtHome;
            [SerializeField] private int workersAtWork;
            [SerializeField] private int studentsAtSchool;
            [SerializeField] private int lastPreparedDay = -1;
            [SerializeField] private int workerCommutesGeneratedToday;
            [SerializeField] private int studentCommutesGeneratedToday;
            [SerializeField] private int workerShopVisitsGeneratedToday;
            [SerializeField] private int studentShopVisitsGeneratedToday;
            [SerializeField] private int homebodyShopVisitsGeneratedToday;
            [SerializeField] private int workerShopVisitsToday;
            [SerializeField] private int studentShopVisitsToday;
            [SerializeField] private int homebodyShopVisitsToday;
            [SerializeField] private float workerCommuteAccumulator;
            [SerializeField] private float studentCommuteAccumulator;
            [SerializeField] private float workerShopAccumulator;
            [SerializeField] private float studentShopAccumulator;
            [SerializeField] private float homebodyShopAccumulator;
            [SerializeField] private int waitingWorkerTrips;
            [SerializeField] private int waitingStudentTrips;
            [SerializeField] private int waitingWorkerReturnTrips;
            [SerializeField] private int waitingStudentReturnTrips;
            [SerializeField] private int waitingWorkerShoppingTrips;
            [SerializeField] private int waitingStudentShoppingTrips;
            [SerializeField] private int waitingHomebodyShoppingTrips;
            [SerializeField] private int waitingWorkerShoppingReturnTrips;
            [SerializeField] private int waitingStudentShoppingReturnTrips;
            [SerializeField] private int waitingHomebodyShoppingReturnTrips;
            [SerializeField] private int waitingShoppingTrips;
            [SerializeField] private int waitingTrips;
            [SerializeField] private int totalGeneratedTrips;
            [SerializeField] private int totalSpawnedTrips;
            [NonSerialized] private readonly Queue<float> workerCommuteQueuedHours = new Queue<float>();
            [NonSerialized] private readonly Queue<float> studentCommuteQueuedHours = new Queue<float>();
            [NonSerialized] private readonly Queue<float> workerReturnQueuedHours = new Queue<float>();
            [NonSerialized] private readonly Queue<float> studentReturnQueuedHours = new Queue<float>();

            public HouseDemandState(Vector2Int position, int workerCount, int studentCount, int homebodyCount)
            {
                gridPosition = position;
                totalWorkers = Mathf.Max(0, workerCount);
                totalStudents = Mathf.Max(0, studentCount);
                totalHomebodies = Mathf.Max(0, homebodyCount);
                workersAtHome = totalWorkers;
                studentsAtHome = totalStudents;
                homebodiesAtHome = totalHomebodies;
            }

            public Vector2Int GridPosition => gridPosition;
            public int WaitingTrips => waitingTrips;

            public void AddDailyDemand(
                float deltaHours,
                float currentHour,
                int dayIndex,
                float absoluteHour,
                int maxWaitingTrips,
                DemandSettings settings)
            {
                PrepareDay(dayIndex, settings);
                AddCommuteDemand(deltaHours, currentHour, absoluteHour, maxWaitingTrips, settings);
                AddShoppingDemand(deltaHours, currentHour, absoluteHour, maxWaitingTrips, settings);
                RecalculateWaitingTrips();
            }

            public TilemapTestTripCategory PeekNextTripCategory()
            {
                if (waitingWorkerReturnTrips > 0)
                {
                    return TilemapTestTripCategory.WorkerReturnHome;
                }

                if (waitingStudentReturnTrips > 0)
                {
                    return TilemapTestTripCategory.StudentReturnHome;
                }

                if (waitingWorkerShoppingReturnTrips > 0)
                {
                    return TilemapTestTripCategory.WorkerShoppingReturnHome;
                }

                if (waitingStudentShoppingReturnTrips > 0)
                {
                    return TilemapTestTripCategory.StudentShoppingReturnHome;
                }

                if (waitingHomebodyShoppingReturnTrips > 0)
                {
                    return TilemapTestTripCategory.HomebodyShoppingReturnHome;
                }

                if (waitingWorkerTrips > 0)
                {
                    return TilemapTestTripCategory.Worker;
                }

                if (waitingStudentTrips > 0)
                {
                    return TilemapTestTripCategory.Student;
                }

                if (waitingWorkerShoppingTrips > 0)
                {
                    return TilemapTestTripCategory.WorkerShopping;
                }

                if (waitingStudentShoppingTrips > 0)
                {
                    return TilemapTestTripCategory.StudentShopping;
                }

                if (waitingHomebodyShoppingTrips > 0)
                {
                    return TilemapTestTripCategory.HomebodyShopping;
                }

                return TilemapTestTripCategory.None;
            }

            public void ConsumeWaitingTrip(TilemapTestTripCategory category)
            {
                if (waitingTrips <= 0)
                {
                    return;
                }

                switch (category)
                {
                    case TilemapTestTripCategory.Worker:
                        waitingWorkerTrips--;
                        DequeueIfAvailable(workerCommuteQueuedHours);
                        break;
                    case TilemapTestTripCategory.Student:
                        waitingStudentTrips--;
                        DequeueIfAvailable(studentCommuteQueuedHours);
                        break;
                    case TilemapTestTripCategory.WorkerReturnHome:
                        waitingWorkerReturnTrips--;
                        DequeueIfAvailable(workerReturnQueuedHours);
                        break;
                    case TilemapTestTripCategory.StudentReturnHome:
                        waitingStudentReturnTrips--;
                        DequeueIfAvailable(studentReturnQueuedHours);
                        break;
                    case TilemapTestTripCategory.WorkerShopping:
                        waitingWorkerShoppingTrips--;
                        break;
                    case TilemapTestTripCategory.StudentShopping:
                        waitingStudentShoppingTrips--;
                        break;
                    case TilemapTestTripCategory.HomebodyShopping:
                        waitingHomebodyShoppingTrips--;
                        break;
                    case TilemapTestTripCategory.WorkerShoppingReturnHome:
                        waitingWorkerShoppingReturnTrips--;
                        break;
                    case TilemapTestTripCategory.StudentShoppingReturnHome:
                        waitingStudentShoppingReturnTrips--;
                        break;
                    case TilemapTestTripCategory.HomebodyShoppingReturnHome:
                        waitingHomebodyShoppingReturnTrips--;
                        break;
                }

                RecalculateWaitingTrips();
                totalSpawnedTrips++;
            }

            public void ArriveAtWork()
            {
                workersAtWork++;
            }

            public void ArriveAtSchool()
            {
                studentsAtSchool++;
            }

            public void ArriveWorkerHome()
            {
                workersAtHome++;
            }

            public void ArriveStudentHome()
            {
                studentsAtHome++;
            }

            public void ArriveHomebodyHome()
            {
                homebodiesAtHome++;
            }

            public bool QueueReturnFromShop(TilemapTestTripCategory returnCategory, int maxWaitingTrips)
            {
                RecalculateWaitingTrips();
                if (waitingTrips >= maxWaitingTrips)
                {
                    return false;
                }

                if (!AddWaitingTrip(returnCategory, 0f))
                {
                    return false;
                }

                totalGeneratedTrips++;
                RecalculateWaitingTrips();
                return true;
            }

            public void ExpireTimedOutTrips(
                float absoluteHour,
                float timeoutHours,
                out int workerCommutes,
                out int studentCommutes,
                out int workerReturns,
                out int studentReturns)
            {
                workerCommutes = ExpireQueue(workerCommuteQueuedHours, absoluteHour, timeoutHours, ref waitingWorkerTrips);
                if (workerCommutes > 0)
                {
                    workersAtHome += workerCommutes;
                }

                studentCommutes = ExpireQueue(studentCommuteQueuedHours, absoluteHour, timeoutHours, ref waitingStudentTrips);
                if (studentCommutes > 0)
                {
                    studentsAtHome += studentCommutes;
                }

                workerReturns = ExpireQueue(workerReturnQueuedHours, absoluteHour, timeoutHours, ref waitingWorkerReturnTrips);
                if (workerReturns > 0)
                {
                    workersAtWork += workerReturns;
                }

                studentReturns = ExpireQueue(studentReturnQueuedHours, absoluteHour, timeoutHours, ref waitingStudentReturnTrips);
                if (studentReturns > 0)
                {
                    studentsAtSchool += studentReturns;
                }

                RecalculateWaitingTrips();
            }

            private void PrepareDay(int dayIndex, DemandSettings settings)
            {
                if (lastPreparedDay == dayIndex)
                {
                    return;
                }

                lastPreparedDay = dayIndex;
                workerCommutesGeneratedToday = 0;
                studentCommutesGeneratedToday = 0;
                workerShopVisitsGeneratedToday = 0;
                studentShopVisitsGeneratedToday = 0;
                homebodyShopVisitsGeneratedToday = 0;
                workerCommuteAccumulator = 0f;
                studentCommuteAccumulator = 0f;
                workerShopAccumulator = 0f;
                studentShopAccumulator = 0f;
                homebodyShopAccumulator = 0f;
                workerShopVisitsToday = SampleDailyVisits(totalWorkers, settings.WorkerDailyShopVisitProbability, dayIndex, 17);
                studentShopVisitsToday = SampleDailyVisits(totalStudents, settings.StudentDailyShopVisitProbability, dayIndex, 31);
                homebodyShopVisitsToday = SampleDailyVisits(totalHomebodies, settings.HomebodyDailyShopVisitProbability, dayIndex, 47);
            }

            private void AddCommuteDemand(float deltaHours, float currentHour, float absoluteHour, int maxWaitingTrips, DemandSettings settings)
            {
                if (IsWithinHourRange(currentHour, settings.WorkerLeaveHomeHour, settings.WorkerCommuteEndHour))
                {
                    workerCommuteAccumulator += totalWorkers * deltaHours / settings.WorkerDepartureSpreadHours;
                    AddQueuedTripsFromAccumulator(
                        ref workerCommuteAccumulator,
                        ref workerCommutesGeneratedToday,
                        totalWorkers,
                        absoluteHour,
                        maxWaitingTrips,
                        TilemapTestTripCategory.Worker);
                }

                if (IsWithinHourRange(currentHour, settings.StudentLeaveHomeHour, settings.StudentCommuteEndHour))
                {
                    studentCommuteAccumulator += totalStudents * deltaHours / settings.StudentDepartureSpreadHours;
                    AddQueuedTripsFromAccumulator(
                        ref studentCommuteAccumulator,
                        ref studentCommutesGeneratedToday,
                        totalStudents,
                        absoluteHour,
                        maxWaitingTrips,
                        TilemapTestTripCategory.Student);
                }

                if (IsWithinHourRange(currentHour, settings.WorkerReturnHomeHour, settings.WorkerReturnEndHour))
                {
                    QueueFacilityReturns(ref workersAtWork, ref waitingWorkerReturnTrips, workerReturnQueuedHours, absoluteHour, maxWaitingTrips);
                }

                if (IsWithinHourRange(currentHour, settings.StudentReturnHomeHour, settings.StudentReturnEndHour))
                {
                    QueueFacilityReturns(ref studentsAtSchool, ref waitingStudentReturnTrips, studentReturnQueuedHours, absoluteHour, maxWaitingTrips);
                }
            }

            private void AddShoppingDemand(float deltaHours, float currentHour, float absoluteHour, int maxWaitingTrips, DemandSettings settings)
            {
                if (!IsWithinHourRange(currentHour, settings.WorkerLeaveHomeHour, settings.WorkerReturnHomeHour))
                {
                    AddCategoryShoppingDemand(
                        deltaHours,
                        settings.WorkerShoppingAvailableHours,
                        workerShopVisitsToday,
                        ref workerShopVisitsGeneratedToday,
                        ref workerShopAccumulator,
                        absoluteHour,
                        maxWaitingTrips,
                        TilemapTestTripCategory.WorkerShopping);
                }

                if (!IsWithinHourRange(currentHour, settings.StudentLeaveHomeHour, settings.StudentReturnHomeHour))
                {
                    AddCategoryShoppingDemand(
                        deltaHours,
                        settings.StudentShoppingAvailableHours,
                        studentShopVisitsToday,
                        ref studentShopVisitsGeneratedToday,
                        ref studentShopAccumulator,
                        absoluteHour,
                        maxWaitingTrips,
                        TilemapTestTripCategory.StudentShopping);
                }

                AddCategoryShoppingDemand(
                    deltaHours,
                    24f,
                    homebodyShopVisitsToday,
                    ref homebodyShopVisitsGeneratedToday,
                    ref homebodyShopAccumulator,
                    absoluteHour,
                    maxWaitingTrips,
                    TilemapTestTripCategory.HomebodyShopping);
            }

            private void AddCategoryShoppingDemand(
                float deltaHours,
                float availableHours,
                int targetVisits,
                ref int generatedVisits,
                ref float accumulator,
                float absoluteHour,
                int maxWaitingTrips,
                TilemapTestTripCategory category)
            {
                if (targetVisits <= generatedVisits || availableHours <= 0f)
                {
                    return;
                }

                accumulator += targetVisits * deltaHours / availableHours;
                AddQueuedTripsFromAccumulator(
                    ref accumulator,
                    ref generatedVisits,
                    targetVisits,
                    absoluteHour,
                    maxWaitingTrips,
                    category);
            }

            private void AddQueuedTripsFromAccumulator(
                ref float accumulator,
                ref int generatedCount,
                int targetCount,
                float absoluteHour,
                int maxWaitingTrips,
                TilemapTestTripCategory category)
            {
                while (accumulator >= 1f && generatedCount < targetCount)
                {
                    accumulator -= 1f;
                    RecalculateWaitingTrips();

                    if (waitingTrips >= maxWaitingTrips)
                    {
                        return;
                    }

                    if (AddWaitingTrip(category, absoluteHour))
                    {
                        generatedCount++;
                        totalGeneratedTrips++;
                    }
                }

                RecalculateWaitingTrips();
            }

            private bool AddWaitingTrip(TilemapTestTripCategory category, float absoluteHour)
            {
                switch (category)
                {
                    case TilemapTestTripCategory.Worker:
                        if (workersAtHome <= 0)
                        {
                            return false;
                        }

                        workersAtHome--;
                        waitingWorkerTrips++;
                        workerCommuteQueuedHours.Enqueue(absoluteHour);
                        return true;
                    case TilemapTestTripCategory.Student:
                        if (studentsAtHome <= 0)
                        {
                            return false;
                        }

                        studentsAtHome--;
                        waitingStudentTrips++;
                        studentCommuteQueuedHours.Enqueue(absoluteHour);
                        return true;
                    case TilemapTestTripCategory.WorkerReturnHome:
                        waitingWorkerReturnTrips++;
                        workerReturnQueuedHours.Enqueue(absoluteHour);
                        return true;
                    case TilemapTestTripCategory.StudentReturnHome:
                        waitingStudentReturnTrips++;
                        studentReturnQueuedHours.Enqueue(absoluteHour);
                        return true;
                    case TilemapTestTripCategory.WorkerShopping:
                        if (workersAtHome <= 0)
                        {
                            return false;
                        }

                        workersAtHome--;
                        waitingWorkerShoppingTrips++;
                        return true;
                    case TilemapTestTripCategory.StudentShopping:
                        if (studentsAtHome <= 0)
                        {
                            return false;
                        }

                        studentsAtHome--;
                        waitingStudentShoppingTrips++;
                        return true;
                    case TilemapTestTripCategory.HomebodyShopping:
                        if (homebodiesAtHome <= 0)
                        {
                            return false;
                        }

                        homebodiesAtHome--;
                        waitingHomebodyShoppingTrips++;
                        return true;
                    case TilemapTestTripCategory.WorkerShoppingReturnHome:
                        waitingWorkerShoppingReturnTrips++;
                        return true;
                    case TilemapTestTripCategory.StudentShoppingReturnHome:
                        waitingStudentShoppingReturnTrips++;
                        return true;
                    case TilemapTestTripCategory.HomebodyShoppingReturnHome:
                        waitingHomebodyShoppingReturnTrips++;
                        return true;
                    default:
                        return false;
                }
            }

            private void QueueFacilityReturns(
                ref int facilityCount,
                ref int waitingReturnTrips,
                Queue<float> returnQueuedHours,
                float absoluteHour,
                int maxWaitingTrips)
            {
                while (facilityCount > 0)
                {
                    RecalculateWaitingTrips();
                    if (waitingTrips >= maxWaitingTrips)
                    {
                        return;
                    }

                    facilityCount--;
                    waitingReturnTrips++;
                    returnQueuedHours.Enqueue(absoluteHour);
                    totalGeneratedTrips++;
                }
            }

            private void RecalculateWaitingTrips()
            {
                waitingShoppingTrips =
                    waitingWorkerShoppingTrips +
                    waitingStudentShoppingTrips +
                    waitingHomebodyShoppingTrips +
                    waitingWorkerShoppingReturnTrips +
                    waitingStudentShoppingReturnTrips +
                    waitingHomebodyShoppingReturnTrips;

                waitingTrips =
                    waitingWorkerTrips +
                    waitingStudentTrips +
                    waitingWorkerReturnTrips +
                    waitingStudentReturnTrips +
                    waitingShoppingTrips;
            }

            private static int ExpireQueue(Queue<float> queuedHours, float absoluteHour, float timeoutHours, ref int waitingCount)
            {
                int expired = 0;
                while (queuedHours.Count > 0 && absoluteHour - queuedHours.Peek() >= timeoutHours)
                {
                    queuedHours.Dequeue();
                    waitingCount = Mathf.Max(0, waitingCount - 1);
                    expired++;
                }

                return expired;
            }

            private static void DequeueIfAvailable(Queue<float> queue)
            {
                if (queue.Count > 0)
                {
                    queue.Dequeue();
                }
            }

            private int SampleDailyVisits(int population, float probability, int dayIndex, int salt)
            {
                int visits = 0;
                int seed = (gridPosition.x * 73856093) ^
                           (gridPosition.y * 19349663) ^
                           (dayIndex * 83492791) ^
                           salt;
                System.Random random = new System.Random(seed & int.MaxValue);

                for (int i = 0; i < population; i++)
                {
                    if (random.NextDouble() <= probability)
                    {
                        visits++;
                    }
                }

                return visits;
            }
        }

        private readonly struct DemandSettings
        {
            private DemandSettings(
                float workerLeaveHomeHour,
                float workerReturnHomeHour,
                float studentLeaveHomeHour,
                float studentReturnHomeHour,
                float workerDepartureSpreadHours,
                float studentDepartureSpreadHours,
                float workerDailyShopVisitProbability,
                float studentDailyShopVisitProbability,
                float homebodyDailyShopVisitProbability)
            {
                WorkerLeaveHomeHour = workerLeaveHomeHour;
                WorkerReturnHomeHour = workerReturnHomeHour;
                StudentLeaveHomeHour = studentLeaveHomeHour;
                StudentReturnHomeHour = studentReturnHomeHour;
                WorkerDepartureSpreadHours = Mathf.Max(0.1f, workerDepartureSpreadHours);
                StudentDepartureSpreadHours = Mathf.Max(0.1f, studentDepartureSpreadHours);
                WorkerCommuteEndHour = Mathf.Repeat(workerLeaveHomeHour + WorkerDepartureSpreadHours, 24f);
                StudentCommuteEndHour = Mathf.Repeat(studentLeaveHomeHour + StudentDepartureSpreadHours, 24f);
                WorkerReturnEndHour = Mathf.Repeat(workerReturnHomeHour + WorkerDepartureSpreadHours, 24f);
                StudentReturnEndHour = Mathf.Repeat(studentReturnHomeHour + StudentDepartureSpreadHours, 24f);
                WorkerDailyShopVisitProbability = workerDailyShopVisitProbability;
                StudentDailyShopVisitProbability = studentDailyShopVisitProbability;
                HomebodyDailyShopVisitProbability = homebodyDailyShopVisitProbability;
                WorkerShoppingAvailableHours = Mathf.Max(0.1f, 24f - GetHourRangeLength(workerLeaveHomeHour, workerReturnHomeHour));
                StudentShoppingAvailableHours = Mathf.Max(0.1f, 24f - GetHourRangeLength(studentLeaveHomeHour, studentReturnHomeHour));
            }

            public float WorkerLeaveHomeHour { get; }
            public float WorkerReturnHomeHour { get; }
            public float StudentLeaveHomeHour { get; }
            public float StudentReturnHomeHour { get; }
            public float WorkerCommuteEndHour { get; }
            public float StudentCommuteEndHour { get; }
            public float WorkerReturnEndHour { get; }
            public float StudentReturnEndHour { get; }
            public float WorkerDepartureSpreadHours { get; }
            public float StudentDepartureSpreadHours { get; }
            public float WorkerDailyShopVisitProbability { get; }
            public float StudentDailyShopVisitProbability { get; }
            public float HomebodyDailyShopVisitProbability { get; }
            public float WorkerShoppingAvailableHours { get; }
            public float StudentShoppingAvailableHours { get; }

            public static DemandSettings FromField(
                TilemapTestField field,
                float workerDepartureSpreadHours,
                float studentDepartureSpreadHours)
            {
                return new DemandSettings(
                    field.WorkerLeaveHomeHour,
                    field.WorkerReturnHomeHour,
                    field.StudentLeaveHomeHour,
                    field.StudentReturnHomeHour,
                    workerDepartureSpreadHours,
                    studentDepartureSpreadHours,
                    field.WorkerDailyShopVisitProbability,
                    field.StudentDailyShopVisitProbability,
                    field.HomebodyDailyShopVisitProbability);
            }
        }

        private static float GetHourRangeLength(float start, float end)
        {
            if (Mathf.Approximately(start, end))
            {
                return 24f;
            }

            return start < end ? end - start : (24f - start) + end;
        }

        private readonly struct RoutePoint
        {
            public RoutePoint(Vector2Int tile, Vector2Int localCell, Vector2Int direction, int laneIndex, Vector3 position)
            {
                Tile = tile;
                LocalCell = localCell;
                Direction = direction;
                LaneIndex = laneIndex;
                Position = position;
            }

            public Vector2Int Tile { get; }
            public Vector2Int LocalCell { get; }
            public Vector2Int Direction { get; }
            public int LaneIndex { get; }
            public Vector3 Position { get; }

            public TrafficCellKey ToCellKey()
            {
                return new TrafficCellKey(Tile, LocalCell, Direction, LaneIndex);
            }
        }

        private readonly struct TrafficCellKey : IEquatable<TrafficCellKey>
        {
            public TrafficCellKey(Vector2Int tile, Vector2Int localCell, Vector2Int direction, int laneIndex)
            {
                Tile = tile;
                LocalCell = localCell;
                Direction = direction;
                LaneIndex = laneIndex;
                IsValid = true;
            }

            public Vector2Int Tile { get; }
            public Vector2Int LocalCell { get; }
            public Vector2Int Direction { get; }
            public int LaneIndex { get; }
            public bool IsValid { get; }

            public bool Equals(TrafficCellKey other)
            {
                return IsValid == other.IsValid &&
                       Tile == other.Tile &&
                       LocalCell == other.LocalCell;
            }

            public override bool Equals(object obj)
            {
                return obj is TrafficCellKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Tile.GetHashCode();
                    hash = (hash * 397) ^ LocalCell.GetHashCode();
                    hash = (hash * 397) ^ IsValid.GetHashCode();
                    return hash;
                }
            }
        }

        private sealed class RouteData
        {
            private readonly List<RoutePoint> points;
            private readonly float[] cumulativeDistances;

            public RouteData(Vector2Int source, Vector2Int destination, int laneIndex, List<RoutePoint> routePoints)
            {
                Source = source;
                Destination = destination;
                LaneIndex = laneIndex;
                points = routePoints;
                cumulativeDistances = new float[points.Count];

                for (int i = 1; i < points.Count; i++)
                {
                    cumulativeDistances[i] = cumulativeDistances[i - 1] + Vector3.Distance(points[i - 1].Position, points[i].Position);
                }

                TotalLength = cumulativeDistances.Length > 0 ? cumulativeDistances[cumulativeDistances.Length - 1] : 0f;
            }

            public Vector2Int Source { get; }
            public Vector2Int Destination { get; }
            public int LaneIndex { get; }
            public float TotalLength { get; }

            public TrafficCellKey GetTrafficCellKey(float distance)
            {
                if (points.Count == 0)
                {
                    return default;
                }

                if (points.Count == 1)
                {
                    return points[0].ToCellKey();
                }

                float clampedDistance = Mathf.Clamp(distance, 0f, TotalLength);

                for (int i = 1; i < cumulativeDistances.Length; i++)
                {
                    if (clampedDistance <= cumulativeDistances[i])
                    {
                        return points[i].ToCellKey();
                    }
                }

                return points[points.Count - 1].ToCellKey();
            }

            public void Sample(float distance, out Vector3 position, out Vector3 forward)
            {
                if (points.Count == 0)
                {
                    position = Vector3.zero;
                    forward = Vector3.forward;
                    return;
                }

                if (points.Count == 1)
                {
                    position = points[0].Position;
                    forward = Vector3.forward;
                    return;
                }

                float clampedDistance = Mathf.Clamp(distance, 0f, TotalLength);

                for (int i = 1; i < cumulativeDistances.Length; i++)
                {
                    if (clampedDistance > cumulativeDistances[i])
                    {
                        continue;
                    }

                    float segmentStart = cumulativeDistances[i - 1];
                    float segmentLength = Mathf.Max(0.0001f, cumulativeDistances[i] - segmentStart);
                    float t = (clampedDistance - segmentStart) / segmentLength;
                    position = Vector3.Lerp(points[i - 1].Position, points[i].Position, t);
                    forward = (points[i].Position - points[i - 1].Position).normalized;
                    return;
                }

                position = points[points.Count - 1].Position;
                forward = (points[points.Count - 1].Position - points[points.Count - 2].Position).normalized;
            }
        }
    }
}

/*
Unity implementation:
1. Add this component to the empty TrafficFlowManager GameObject.
2. Assign TilemapTestField and a simple vehicle prefab.
3. It centrally manages routes, lanes, spacing, and vehicle Transforms without putting traffic decisions on the vehicle prefab.
*/
