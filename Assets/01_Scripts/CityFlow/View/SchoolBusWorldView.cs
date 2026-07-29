using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Content.Transit;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.View
{
    /// <summary>
    /// 스쿨버스 운행 상태를 Debug 씬의 3D 차량으로 표시합니다.
    /// 시뮬레이션은 BusRoute가 소유하고 이 컴포넌트는 표시만 담당합니다.
    /// </summary>
    [RequireComponent(typeof(BusRoute))]
    public sealed class SchoolBusWorldView :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        [SerializeField] private BusDefinitionSO definition;
        [SerializeField] private BusRoute busRoute;
        [SerializeField] private MainCityView cityView;
        [SerializeField] private Material busMaterial;

        [Header("Presentation")]
        [SerializeField, Min(0.01f)]
        private float visualScale = 0.085f;
        [SerializeField]
        private float visualDepth = -0.38f;
        [SerializeField, Min(0.01f)]
        private float movementDuration = 0.22f;
        [SerializeField, Min(0)]
        [Tooltip("학교 지상 주차장의 중앙 슬롯을 사용합니다.")]
        private int schoolParkingSlot = 1;
        [SerializeField, Min(0.1f)]
        [Tooltip("주차 슬롯 앞에서 차체를 곧게 정렬하는 진입 거리입니다.")]
        private float parkingApproachDistance = 0.7f;

        private IReadOnlyTileData tileData;
        private Transform visual;
        private Vector3 movementStartPosition;
        private Vector3 targetLocalPosition;
        private Quaternion movementStartRotation;
        private Quaternion targetLocalRotation;
        private Vector3 movementControlPoint;
        private float movementElapsed;
        private float currentMovementDuration;
        private bool hasTarget;
        private bool targetIsRoad;
        private bool targetIsSchoolParking;
        private bool useCurvedMovement;
        private Vector2Int lastRoadTile;
        private Vector2 lastTravelDirection;
        private Vector2 schoolParkingForward;
        private Vector2 currentVisualDirection;
        private bool hasLastRoadTile;
        private bool subscribed;
        private Vector3 previousVisualPosition;
        private float currentVisualSpeed;
        private bool visualBlockedByTraffic;

        public bool HasVisibleBus =>
            visual != null &&
            visual.gameObject.activeInHierarchy;

        public void Initialize(CityFlowServices services)
        {
            tileData = services?.TileData;
            ResolveReferences();
            EnsureVisual();
            Subscribe();
        }

        private void Awake()
        {
            ResolveReferences();
            EnsureVisual();
        }

        private void OnEnable()
        {
            ResolveReferences();
            EnsureVisual();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (!hasTarget)
            {
                if (busRoute != null)
                {
                    HandleTileChanged(busRoute.CurrentTile);
                }
                return;
            }

            if (visual == null)
            {
                return;
            }

            if (movementElapsed >= currentMovementDuration)
            {
                visualBlockedByTraffic = false;
                currentVisualSpeed = 0f;
                previousVisualPosition = visual.localPosition;
                PublishExternalTraffic();
                return;
            }

            float nextMovementElapsed =
                Mathf.Min(
                    movementElapsed + Time.deltaTime,
                    currentMovementDuration);
            float progress = EvaluateMovementProgress(
                nextMovementElapsed,
                currentMovementDuration);
            float rotationProgress = Mathf.SmoothStep(
                0f,
                1f,
                progress);
            Vector3 candidatePosition;
            Quaternion candidateRotation;
            Vector2 candidateDirection;

            if (useCurvedMovement)
            {
                candidatePosition =
                    EvaluateQuadraticPoint(
                        movementStartPosition,
                        movementControlPoint,
                        targetLocalPosition,
                        progress);
                Vector3 tangent =
                    EvaluateQuadraticTangent(
                        movementStartPosition,
                        movementControlPoint,
                        targetLocalPosition,
                        progress);
                Vector2 tangent2D =
                    new(tangent.x, tangent.y);
                if (tangent2D.sqrMagnitude > 0.0001f)
                {
                    candidateDirection =
                        tangent2D.normalized;
                    candidateRotation =
                        CreateRotation(candidateDirection);
                }
                else
                {
                    candidateDirection =
                        currentVisualDirection;
                    candidateRotation =
                        visual.localRotation;
                }
            }
            else
            {
                candidatePosition = Vector3.Lerp(
                    movementStartPosition,
                    targetLocalPosition,
                    progress);
                candidateRotation = Quaternion.Slerp(
                    movementStartRotation,
                    targetLocalRotation,
                    rotationProgress);
                candidateDirection =
                    lastTravelDirection;
            }

            if (cityView != null &&
                TryGetTrafficFootprint(
                    out float collisionHalfLength,
                    out float collisionHalfWidth) &&
                !cityView.CanExternalTrafficMoveVisual(
                    this,
                    visual.localPosition,
                    candidatePosition,
                    new Vector3(
                        candidateDirection.x,
                        candidateDirection.y,
                        0f),
                    GetMinimumHeadway(),
                    collisionHalfLength,
                    collisionHalfWidth))
            {
                visualBlockedByTraffic = true;
                currentVisualSpeed = 0f;
                previousVisualPosition =
                    visual.localPosition;
                PublishExternalTraffic();
                return;
            }

            visualBlockedByTraffic = false;
            movementElapsed = nextMovementElapsed;
            visual.localPosition = candidatePosition;
            visual.localRotation = candidateRotation;
            currentVisualDirection = candidateDirection;
            float deltaTime = Mathf.Max(
                Time.deltaTime,
                0.0001f);
            currentVisualSpeed =
                Vector3.Distance(
                    previousVisualPosition,
                    visual.localPosition) /
                deltaTime;
            previousVisualPosition = visual.localPosition;
            PublishExternalTraffic();
        }

        private void ResolveReferences()
        {
            busRoute ??= GetComponent<BusRoute>();
            cityView ??= FindAnyObjectByType<MainCityView>();
        }

        private void Subscribe()
        {
            if (subscribed || busRoute == null)
            {
                return;
            }

            busRoute.TileChanged += HandleTileChanged;
            busRoute.RouteUnavailable += HandleRouteUnavailable;
            busRoute.CanEnterTile = CanEnterTile;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || busRoute == null)
            {
                return;
            }

            busRoute.TileChanged -= HandleTileChanged;
            busRoute.RouteUnavailable -= HandleRouteUnavailable;
            busRoute.CanEnterTile = null;
            cityView?.RemoveExternalTrafficVehicle(this);
            subscribed = false;
        }

        private void EnsureVisual()
        {
            GameObject visualPrefab =
                definition?.VehicleVisualPrefab;

            if (visual != null ||
                visualPrefab == null ||
                cityView == null)
            {
                return;
            }

            GameObject instance = Instantiate(
                visualPrefab,
                cityView.transform);
            instance.name = "SchoolBusVisual";
            visual = instance.transform;
            visual.localScale = Vector3.one * visualScale;
            ApplyFeatureMaterial(instance);
            instance.SetActive(false);
            previousVisualPosition = visual.localPosition;
        }

        private void ApplyFeatureMaterial(GameObject instance)
        {
            if (busMaterial == null)
            {
                return;
            }

            Renderer[] renderers =
                instance.GetComponentsInChildren<Renderer>(true);

            foreach (Renderer targetRenderer in renderers)
            {
                Material[] materials =
                    targetRenderer.sharedMaterials;

                for (int i = 0; i < materials.Length; i++)
                {
                    materials[i] = busMaterial;
                }

                targetRenderer.sharedMaterials = materials;
            }
        }

        private void HandleTileChanged(Vector2Int tile)
        {
            EnsureVisual();

            if (visual == null || cityView == null)
            {
                return;
            }

            bool isRoad = IsRoad(tile);
            Vector2 travelDirection;
            Vector3 nextPosition;

            if (isRoad)
            {
                travelDirection = ResolveTravelDirection(tile);
                nextPosition = CreateLanePosition(
                    tile,
                    travelDirection);
            }
            else if (!TryGetSchoolParkingPose(
                         tile,
                         out nextPosition,
                         out travelDirection))
            {
                return;
            }

            Quaternion nextRotation =
                CreateRotation(travelDirection);
            bool isSchoolParking = !isRoad;

            if (hasTarget &&
                isSchoolParking &&
                targetIsSchoolParking &&
                Vector3.SqrMagnitude(
                    visual.localPosition -
                    nextPosition) <= 0.0001f)
            {
                visual.localPosition = nextPosition;
                visual.localRotation = nextRotation;
                movementStartPosition = nextPosition;
                targetLocalPosition = nextPosition;
                movementStartRotation = nextRotation;
                targetLocalRotation = nextRotation;
                currentMovementDuration =
                    movementDuration;
                movementElapsed =
                    currentMovementDuration;
                useCurvedMovement = false;
                visualBlockedByTraffic = false;
                schoolParkingForward =
                    travelDirection;
                currentVisualDirection =
                    travelDirection;
                PublishExternalTraffic();
                return;
            }

            if (!hasTarget)
            {
                visual.localPosition = nextPosition;
                visual.localRotation = nextRotation;
                visual.gameObject.SetActive(true);
                movementStartPosition = nextPosition;
                targetLocalPosition = nextPosition;
                movementStartRotation = nextRotation;
                targetLocalRotation = nextRotation;
                currentMovementDuration = movementDuration;
                movementElapsed = currentMovementDuration;
                hasTarget = true;
                targetIsRoad = isRoad;
                targetIsSchoolParking = isSchoolParking;
                useCurvedMovement = false;
                visualBlockedByTraffic = false;
                currentVisualDirection = travelDirection;
                if (isSchoolParking)
                {
                    schoolParkingForward =
                        travelDirection;
                }
                if (isRoad)
                {
                    RememberRoadTile(tile, travelDirection);
                }
                previousVisualPosition = visual.localPosition;
                currentVisualSpeed = 0f;
                PublishExternalTraffic();
                return;
            }

            movementStartPosition = visual.localPosition;
            movementStartRotation = visual.localRotation;
            targetLocalPosition = nextPosition;
            targetLocalRotation = nextRotation;
            movementElapsed = 0f;
            visualBlockedByTraffic = false;
            bool isLeavingSchoolParking =
                isRoad && targetIsSchoolParking;
            useCurvedMovement =
                isSchoolParking ||
                isLeavingSchoolParking;

            if (isSchoolParking)
            {
                schoolParkingForward = travelDirection;
                movementControlPoint =
                    targetLocalPosition -
                    ToVector3(schoolParkingForward) *
                    GetParkingApproachDistance();
            }
            else if (isLeavingSchoolParking)
            {
                movementControlPoint =
                    movementStartPosition -
                    ToVector3(schoolParkingForward) *
                    GetParkingApproachDistance();
            }

            currentMovementDuration =
                useCurvedMovement
                    ? CalculateCurvedMovementDuration(
                        movementStartPosition,
                        movementControlPoint,
                        targetLocalPosition)
                    : CalculateMovementDuration(
                        movementStartPosition,
                        targetLocalPosition);
            targetIsRoad = isRoad;
            targetIsSchoolParking = isSchoolParking;
            if (isRoad)
            {
                RememberRoadTile(tile, travelDirection);
            }
            visual.gameObject.SetActive(true);
            PublishExternalTraffic();
        }

        private bool CanEnterTile(
            Vector2Int currentTile,
            Vector2Int nextTile)
        {
            if (visualBlockedByTraffic)
            {
                return false;
            }

            if (cityView == null || !IsRoad(nextTile))
            {
                return true;
            }

            Vector2 direction =
                TryGetCardinalDirection(
                    nextTile - currentTile,
                    out Vector2 cardinal)
                    ? cardinal
                    : ResolveTravelDirection(nextTile);
            if (direction.sqrMagnitude < 0.5f)
            {
                direction = Vector2.right;
            }

            Vector3 currentPosition =
                hasLastRoadTile && visual != null
                    ? visual.localPosition
                    : CreateLanePosition(
                        currentTile,
                        direction);
            Vector3 nextPosition =
                CreateLanePosition(nextTile, direction);
            Vector3 forward =
                new(direction.x, direction.y, 0f);

            return cityView.CanExternalTrafficAdvance(
                this,
                currentPosition,
                nextPosition,
                forward,
                GetMinimumHeadway(),
                nextTile);
        }

        private Vector3 CreateLanePosition(
            Vector2Int tile,
            Vector2 travelDirection)
        {
            Vector3 position =
                cityView.GridToLocal(
                    tile,
                    visualDepth);
            return position +
                   GetRightLaneOffset(
                       travelDirection,
                       cityView.LaneOffset *
                       cityView.TileSize);
        }

        private void PublishExternalTraffic()
        {
            if (cityView == null || visual == null)
            {
                return;
            }

            if (!TryGetTrafficFootprint(
                    out float collisionHalfLength,
                    out float collisionHalfWidth))
            {
                return;
            }

            cityView.UpdateExternalTrafficVehicle(
                this,
                visual.localPosition,
                new Vector3(
                    currentVisualDirection.x,
                    currentVisualDirection.y,
                    0f),
                currentVisualSpeed,
                ShouldPublishAsTraffic(
                    hasTarget,
                    visual.gameObject.activeInHierarchy),
                lastRoadTile,
                hasLastRoadTile,
                collisionHalfLength,
                collisionHalfWidth);
        }

        private static bool ShouldPublishAsTraffic(
            bool hasVisualTarget,
            bool isVisible)
        {
            return hasVisualTarget && isVisible;
        }

        private bool TryGetSchoolParkingPose(
            Vector2Int tile,
            out Vector3 localPosition,
            out Vector2 forward)
        {
            localPosition = default;
            forward = default;

            if (tileData == null ||
                tileData.GetTileType(tile) != TileType.School)
            {
                return false;
            }

            Vector2Int schoolAnchor = tile;
            tileData.TryGetFootprintAnchor(
                tile,
                out schoolAnchor);

            if (!cityView.TryGetBuildingParkingPose(
                    schoolAnchor,
                    schoolParkingSlot,
                    out localPosition,
                    out Vector3 localForward))
            {
                return false;
            }

            localPosition.z = visualDepth;
            forward = new Vector2(
                localForward.x,
                localForward.y).normalized;
            return forward.sqrMagnitude > 0.5f;
        }

        private float CalculateMovementDuration(
            Vector3 from,
            Vector3 to)
        {
            float distance = Vector2.Distance(
                new Vector2(from.x, from.y),
                new Vector2(to.x, to.y));
            return Mathf.Max(
                0.01f,
                distance /
                Mathf.Max(0.01f, cityView.TileSize) *
                GetSecondsPerTile());
        }

        private float CalculateCurvedMovementDuration(
            Vector3 from,
            Vector3 control,
            Vector3 to)
        {
            float controlPolygonLength =
                Vector2.Distance(
                    new Vector2(from.x, from.y),
                    new Vector2(control.x, control.y)) +
                Vector2.Distance(
                    new Vector2(control.x, control.y),
                    new Vector2(to.x, to.y));
            return Mathf.Max(
                0.01f,
                controlPolygonLength /
                Mathf.Max(0.01f, cityView.TileSize) *
                GetSecondsPerTile());
        }

        private float GetMinimumHeadway()
        {
            return cityView.VehicleMinHeadway *
                   cityView.TileSize;
        }

        private float GetParkingApproachDistance()
        {
            return parkingApproachDistance *
                   cityView.TileSize;
        }

        private bool TryGetTrafficFootprint(
            out float halfLength,
            out float halfWidth)
        {
            halfLength = 0f;
            halfWidth = 0f;
            if (cityView == null || definition == null)
            {
                return false;
            }

            cityView.GetTrafficFootprint(
                definition.VehicleLengthTiles,
                definition.VehicleWidthTiles,
                out halfLength,
                out halfWidth);
            return true;
        }

        private float GetSecondsPerTile()
        {
            return Mathf.Max(
                0.01f,
                busRoute != null
                    ? busRoute.SecondsPerTile
                    : movementDuration);
        }

        private bool IsRoad(Vector2Int tile)
        {
            return tileData == null ||
                   tileData.GetTileType(tile) == TileType.Road;
        }

        private Vector2 ResolveTravelDirection(Vector2Int tile)
        {
            if (hasLastRoadTile &&
                TryGetCardinalDirection(
                    tile - lastRoadTile,
                    out Vector2 incoming))
            {
                return incoming;
            }

            if (busRoute != null)
            {
                IReadOnlyList<Vector2Int> path =
                    busRoute.CurrentRoadPath;
                int startIndex =
                    busRoute.CurrentRoadPathIndex + 1;

                for (int i = startIndex; i < path.Count; i++)
                {
                    if (IsRoad(path[i]) &&
                        TryGetCardinalDirection(
                            path[i] - tile,
                            out Vector2 outgoing))
                    {
                        return outgoing;
                    }
                }
            }

            return lastTravelDirection.sqrMagnitude > 0.5f
                ? lastTravelDirection
                : Vector2.right;
        }

        private static bool TryGetCardinalDirection(
            Vector2 delta,
            out Vector2 direction)
        {
            if (Mathf.Abs(delta.x) > 0.001f &&
                Mathf.Abs(delta.y) <= 0.001f)
            {
                direction =
                    new Vector2(Mathf.Sign(delta.x), 0f);
                return true;
            }

            if (Mathf.Abs(delta.y) > 0.001f &&
                Mathf.Abs(delta.x) <= 0.001f)
            {
                direction =
                    new Vector2(0f, Mathf.Sign(delta.y));
                return true;
            }

            direction = default;
            return false;
        }

        private static Quaternion CreateRotation(Vector2 direction)
        {
            float angle =
                Mathf.Atan2(direction.y, direction.x) *
                Mathf.Rad2Deg;
            return Quaternion.Euler(0f, 0f, angle + 90f) *
                   Quaternion.Euler(90f, 0f, 0f);
        }

        private static Vector3 GetRightLaneOffset(
            Vector2 travelDirection,
            float offset)
        {
            return new Vector3(
                travelDirection.y,
                -travelDirection.x,
                0f) * Mathf.Max(0f, offset);
        }

        private static float EvaluateMovementProgress(
            float elapsed,
            float duration)
        {
            return Mathf.Clamp01(
                Mathf.Max(0f, elapsed) /
                Mathf.Max(0.01f, duration));
        }

        private static Vector3 EvaluateQuadraticPoint(
            Vector3 start,
            Vector3 control,
            Vector3 end,
            float progress)
        {
            float t = Mathf.Clamp01(progress);
            float inverse = 1f - t;
            return inverse * inverse * start +
                   2f * inverse * t * control +
                   t * t * end;
        }

        private static Vector3 EvaluateQuadraticTangent(
            Vector3 start,
            Vector3 control,
            Vector3 end,
            float progress)
        {
            float t = Mathf.Clamp01(progress);
            return 2f * (1f - t) *
                   (control - start) +
                   2f * t * (end - control);
        }

        private static Vector3 ToVector3(
            Vector2 value)
        {
            return new Vector3(
                value.x,
                value.y,
                0f);
        }

        private void RememberRoadTile(
            Vector2Int tile,
            Vector2 direction)
        {
            lastRoadTile = tile;
            lastTravelDirection = direction;
            hasLastRoadTile = true;
        }

        private void HandleRouteUnavailable()
        {
            hasTarget = false;
            targetIsRoad = false;
            targetIsSchoolParking = false;
            useCurvedMovement = false;
            hasLastRoadTile = false;
            lastTravelDirection = default;
            currentVisualDirection = default;
            visualBlockedByTraffic = false;

            if (visual != null)
            {
                visual.gameObject.SetActive(false);
            }

            currentVisualSpeed = 0f;
            cityView?.RemoveExternalTrafficVehicle(this);
        }
    }
}
