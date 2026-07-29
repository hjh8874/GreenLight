using System.Collections.Generic;
using CityFlow.ViewKit;
using UnityEngine;

namespace CityFlow.View
{
    /// <summary>
    /// MainCityView가 직접 생성하지 않은 기능 차량도 기존 차량 간격 계산에
    /// 참여할 수 있게 하는 좁은 연결 지점입니다.
    /// </summary>
    public sealed partial class MainCityView
    {
        private sealed class ExternalTrafficVehicle
        {
            public Object Owner;
            public Vector3 Position;
            public Vector3 Direction;
            public float Speed;
            public bool Active;
            public Vector2Int CurrentTile;
            public bool HasCurrentTile;
            public float HalfLength;
            public float HalfWidth;
        }

        private readonly List<ExternalTrafficVehicle>
            externalTrafficVehicles = new();

        public void UpdateExternalTrafficVehicle(
            Object owner,
            Vector3 localPosition,
            Vector3 localDirection,
            float speed,
            bool active,
            Vector2Int currentTile,
            bool hasCurrentTile,
            float halfLength,
            float halfWidth)
        {
            if (owner == null)
            {
                return;
            }

            ExternalTrafficVehicle state =
                FindExternalTrafficVehicle(owner);
            if (state == null)
            {
                state = new ExternalTrafficVehicle
                {
                    Owner = owner
                };
                externalTrafficVehicles.Add(state);
            }

            state.Position = localPosition;
            state.Direction =
                localDirection.sqrMagnitude > 0.0001f
                    ? localDirection.normalized
                    : Vector3.right;
            state.Speed = Mathf.Max(0f, speed);
            state.Active = active;
            state.CurrentTile = currentTile;
            state.HasCurrentTile = hasCurrentTile;
            state.HalfLength =
                Mathf.Max(0.05f, halfLength);
            state.HalfWidth =
                Mathf.Max(0.04f, halfWidth);
        }

        public void RemoveExternalTrafficVehicle(Object owner)
        {
            for (int i =
                     externalTrafficVehicles.Count - 1;
                 i >= 0;
                 i--)
            {
                ExternalTrafficVehicle state =
                    externalTrafficVehicles[i];
                if (state.Owner == null ||
                    state.Owner == owner)
                {
                    externalTrafficVehicles.RemoveAt(i);
                }
            }
        }

        public bool CanExternalTrafficAdvance(
            Object owner,
            Vector3 currentLocalPosition,
            Vector3 nextLocalPosition,
            Vector3 localDirection,
            float minimumHeadway,
            Vector2Int nextTile)
        {
            ResolveExternalTrafficFootprint(
                owner,
                out float halfLength,
                out float halfWidth);
            float proposedAdvance =
                Vector3.Distance(
                    currentLocalPosition,
                    nextLocalPosition);
            float allowedAdvance =
                LimitExternalTrafficVisualAdvance(
                    owner,
                    currentLocalPosition,
                    nextLocalPosition,
                    localDirection,
                    minimumHeadway,
                    halfLength,
                    halfWidth);

            return allowedAdvance >=
                   proposedAdvance - 0.0001f;
        }

        public bool CanExternalTrafficMoveVisual(
            Object owner,
            Vector3 currentLocalPosition,
            Vector3 nextLocalPosition,
            Vector3 localDirection,
            float minimumHeadway,
            float halfLength,
            float halfWidth)
        {
            float proposedAdvance =
                Vector3.Distance(
                    currentLocalPosition,
                    nextLocalPosition);
            float allowedAdvance =
                LimitExternalTrafficVisualAdvance(
                    owner,
                    currentLocalPosition,
                    nextLocalPosition,
                    localDirection,
                    minimumHeadway,
                    halfLength,
                    halfWidth);

            return allowedAdvance >=
                   proposedAdvance - 0.0001f;
        }

        public float LimitExternalTrafficVisualAdvance(
            Object owner,
            Vector3 currentLocalPosition,
            Vector3 nextLocalPosition,
            Vector3 localDirection,
            float minimumHeadway,
            float halfLength,
            float halfWidth)
        {
            float proposedAdvance =
                Vector3.Distance(
                    currentLocalPosition,
                    nextLocalPosition);
            if (proposedAdvance <= 0.0001f)
            {
                return 0f;
            }

            Vector3 forward =
                localDirection.sqrMagnitude > 0.0001f
                    ? localDirection.normalized
                    : (nextLocalPosition -
                       currentLocalPosition).normalized;
            if (forward.sqrMagnitude <= 0.0001f)
            {
                forward = Vector3.right;
            }

            float safeHalfLength =
                Mathf.Max(0.05f, halfLength);
            float safeHalfWidth =
                Mathf.Max(0.04f, halfWidth);
            float safeHeadway =
                Mathf.Max(0.05f, minimumHeadway);
            float safetyMargin =
                Mathf.Max(0.005f, tileSize * 0.015f);
            float maximumAhead =
                proposedAdvance +
                Mathf.Max(
                    safeHeadway,
                    safeHalfLength +
                    tileSize * VehicleBodyLengthTiles);
            float allowedAdvance = proposedAdvance;

            if (TryGetExternalTrafficLeaderHeadway(
                    owner,
                    currentLocalPosition,
                    forward,
                    maximumAhead,
                    safeHalfWidth,
                    out float headway,
                    out float leaderHalfLength))
            {
                float requiredHeadway =
                    Mathf.Max(
                        safeHeadway,
                        safeHalfLength +
                        leaderHalfLength +
                        safetyMargin);
                allowedAdvance =
                    VehicleSpacingMath.LimitAdvance(
                        proposedAdvance,
                        headway,
                        requiredHeadway);
            }

            if (allowedAdvance <= 0.0001f)
            {
                return 0f;
            }

            Vector3 travelDirection =
                (nextLocalPosition -
                 currentLocalPosition).normalized;
            float stepDistance =
                Mathf.Max(
                    0.01f,
                    tileSize * 0.025f);
            int steps =
                Mathf.Max(
                    1,
                    Mathf.CeilToInt(
                        allowedAdvance /
                        stepDistance));
            float safeAdvance = 0f;

            for (int step = 1; step <= steps; step++)
            {
                float candidateAdvance =
                    allowedAdvance *
                    step /
                    steps;
                Vector3 candidate =
                    currentLocalPosition +
                    travelDirection *
                    candidateAdvance;
                if (!IsTrafficFootprintClear(
                        null,
                        owner,
                        candidate,
                        forward,
                        safeHalfLength,
                        safeHalfWidth))
                {
                    break;
                }

                safeAdvance = candidateAdvance;
            }

            return safeAdvance;
        }

        private void ResolveExternalTrafficFootprint(
            Object owner,
            out float halfLength,
            out float halfWidth)
        {
            ExternalTrafficVehicle state =
                FindExternalTrafficVehicle(owner);
            if (state != null)
            {
                halfLength = state.HalfLength;
                halfWidth = state.HalfWidth;
                return;
            }

            GetTrafficFootprint(
                VehicleBodyLengthTiles,
                VehicleBodyWidthTiles,
                out halfLength,
                out halfWidth);
        }

        private bool TryGetExternalTrafficLeaderHeadway(
            Object owner,
            Vector3 currentLocalPosition,
            Vector3 localDirection,
            float maximumAhead,
            float subjectHalfWidth,
            out float headway,
            out float leaderHalfLength)
        {
            headway = float.PositiveInfinity;
            leaderHalfLength = 0f;
            bool found = false;
            Vector3 forward =
                localDirection.sqrMagnitude > 0.0001f
                    ? localDirection.normalized
                    : Vector3.right;
            float safetyMargin =
                Mathf.Max(0.005f, tileSize * 0.015f);

            for (int i = 0; i < vehicles.Count; i++)
            {
                RouteVehicle vehicle = vehicles[i];
                if (!IsTrafficVehicleActive(vehicle))
                {
                    continue;
                }

                Vector3 vehicleDirection =
                    ResolveVehicleDirection(vehicle);
                if (!VehicleSpacingMath.IsSameFlowDirection(
                        forward,
                        vehicleDirection))
                {
                    continue;
                }

                GetVehicleFootprint(
                    vehicle,
                    out float candidateHalfLength,
                    out float candidateHalfWidth);
                float lateralLimit =
                    subjectHalfWidth +
                    candidateHalfWidth +
                    safetyMargin;
                if (!IsInsideForwardCorridor(
                        vehicle.Pos,
                        currentLocalPosition,
                        forward,
                        maximumAhead,
                        lateralLimit,
                        out float candidateHeadway) ||
                    candidateHeadway >= headway)
                {
                    continue;
                }

                headway = candidateHeadway;
                leaderHalfLength =
                    candidateHalfLength;
                found = true;
            }

            for (int i = 0;
                 i < externalTrafficVehicles.Count;
                 i++)
            {
                ExternalTrafficVehicle state =
                    externalTrafficVehicles[i];
                if (!state.Active ||
                    state.Owner == null ||
                    state.Owner == owner ||
                    !VehicleSpacingMath.IsSameFlowDirection(
                        forward,
                        state.Direction))
                {
                    continue;
                }

                float lateralLimit =
                    subjectHalfWidth +
                    state.HalfWidth +
                    safetyMargin;
                if (!IsInsideForwardCorridor(
                        state.Position,
                        currentLocalPosition,
                        forward,
                        maximumAhead,
                        lateralLimit,
                        out float candidateHeadway) ||
                    candidateHeadway >= headway)
                {
                    continue;
                }

                headway = candidateHeadway;
                leaderHalfLength =
                    state.HalfLength;
                found = true;
            }

            return found;
        }

        private float LimitVehicleTravelDistance(
            RouteVehicle subject,
            RoutePolyline path,
            float currentDistance,
            float desiredDistance)
        {
            if (subject == null || path == null)
            {
                return desiredDistance;
            }

            // Managed cars keep the lane-order headway model. An all-pairs sweep
            // between them can create cyclic waits at crossings; this gate exists
            // only while a school bus or another feature vehicle occupies traffic.
            if (!HasActiveExternalTrafficVehicle())
            {
                return desiredDistance;
            }

            float from =
                Mathf.Clamp(
                    currentDistance,
                    0f,
                    path.Length);
            float to =
                Mathf.Clamp(
                    desiredDistance,
                    0f,
                    path.Length);
            float travel = Mathf.Abs(to - from);
            if (travel <= 0.0001f)
            {
                return to;
            }

            float stepDistance =
                Mathf.Max(
                    0.01f,
                    tileSize * 0.025f);
            int steps =
                Mathf.Max(
                    1,
                    Mathf.CeilToInt(
                        travel / stepDistance));
            float safeDistance = from;
            GetVehicleFootprint(
                subject,
                out float halfLength,
                out float halfWidth);

            for (int step = 1; step <= steps; step++)
            {
                float candidateDistance =
                    Mathf.Lerp(
                        from,
                        to,
                        (float)step / steps);
                Sample candidate =
                    path.SampleAt(candidateDistance);
                if (!IsTrafficFootprintClear(
                        subject,
                        null,
                        candidate.Pos,
                        candidate.Dir,
                        halfLength,
                        halfWidth))
                {
                    break;
                }

                safeDistance = candidateDistance;
            }

            return safeDistance;
        }

        private bool IsTrafficFootprintClear(
            RouteVehicle subjectVehicle,
            Object subjectOwner,
            Vector3 candidatePosition,
            Vector3 candidateDirection,
            float candidateHalfLength,
            float candidateHalfWidth)
        {
            float verticalLimit =
                Mathf.Max(0.05f, tileSize * 0.2f);
            float safetyMargin =
                Mathf.Max(0.005f, tileSize * 0.015f);

            // An external feature vehicle must see managed cars. A managed car
            // intentionally skips this loop and only checks the external list
            // below, preserving the acyclic lane-order invariant.
            if (subjectVehicle == null)
            {
                for (int i = 0; i < vehicles.Count; i++)
                {
                    RouteVehicle other = vehicles[i];
                    if (!IsTrafficVehicleActive(other))
                    {
                        continue;
                    }

                    GetVehicleFootprint(
                        other,
                        out float otherHalfLength,
                        out float otherHalfWidth);
                    Vector3 otherDirection =
                        ResolveVehicleDirection(other);
                    if (TrafficFootprintsOverlap(
                            candidatePosition,
                            candidateDirection,
                            candidateHalfLength,
                            candidateHalfWidth,
                            other.Pos,
                            otherDirection,
                            otherHalfLength,
                            otherHalfWidth,
                            verticalLimit,
                            safetyMargin))
                    {
                        return false;
                    }

                    if (ShouldYieldTrafficConflict(
                            subjectVehicle,
                            subjectOwner,
                            candidateDirection,
                            other,
                            null,
                            otherDirection) &&
                        IsEnteringReservedTrafficCrossing(
                            candidatePosition,
                            candidateDirection,
                            candidateHalfLength,
                            candidateHalfWidth,
                            other.Pos,
                            otherDirection,
                            otherHalfLength,
                            otherHalfWidth,
                            verticalLimit,
                            safetyMargin))
                    {
                        return false;
                    }
                }
            }

            for (int i = 0;
                 i < externalTrafficVehicles.Count;
                 i++)
            {
                ExternalTrafficVehicle other =
                    externalTrafficVehicles[i];
                if (!other.Active ||
                    other.Owner == null ||
                    other.Owner == subjectOwner)
                {
                    continue;
                }

                if (TrafficFootprintsOverlap(
                        candidatePosition,
                        candidateDirection,
                        candidateHalfLength,
                        candidateHalfWidth,
                        other.Position,
                        other.Direction,
                        other.HalfLength,
                        other.HalfWidth,
                        verticalLimit,
                        safetyMargin))
                {
                    return false;
                }

                if (ShouldYieldTrafficConflict(
                        subjectVehicle,
                        subjectOwner,
                        candidateDirection,
                        null,
                        other,
                        other.Direction) &&
                    IsEnteringReservedTrafficCrossing(
                        candidatePosition,
                        candidateDirection,
                        candidateHalfLength,
                        candidateHalfWidth,
                        other.Position,
                        other.Direction,
                        other.HalfLength,
                        other.HalfWidth,
                        verticalLimit,
                        safetyMargin))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsEnteringReservedTrafficCrossing(
            Vector3 subjectPosition,
            Vector3 subjectDirection,
            float subjectHalfLength,
            float subjectHalfWidth,
            Vector3 otherPosition,
            Vector3 otherDirection,
            float otherHalfLength,
            float otherHalfWidth,
            float verticalLimit,
            float safetyMargin)
        {
            if (Mathf.Abs(
                    subjectPosition.z -
                    otherPosition.z) > verticalLimit)
            {
                return false;
            }

            Vector2 subjectForward =
                NormalizeDirection(subjectDirection);
            Vector2 otherForward =
                NormalizeDirection(otherDirection);
            float directionCross =
                Cross2D(subjectForward, otherForward);
            if (Mathf.Abs(directionCross) <= 0.25f)
            {
                return false;
            }

            Vector2 separation =
                new(
                    otherPosition.x - subjectPosition.x,
                    otherPosition.y - subjectPosition.y);
            float subjectDistanceToCrossing =
                Cross2D(separation, otherForward) /
                directionCross;
            float otherDistanceToCrossing =
                Cross2D(separation, subjectForward) /
                directionCross;
            float subjectHoldDistance =
                Mathf.Max(0.01f, subjectHalfLength) +
                ProjectFootprintRadius(
                    otherForward,
                    otherHalfLength,
                    otherHalfWidth,
                    subjectForward) +
                Mathf.Max(0f, safetyMargin);
            float otherClearDistance =
                Mathf.Max(0.01f, otherHalfLength) +
                ProjectFootprintRadius(
                    subjectForward,
                    subjectHalfLength,
                    subjectHalfWidth,
                    otherForward) +
                Mathf.Max(0f, safetyMargin);
            float reservationLookahead =
                Mathf.Max(
                    subjectHoldDistance,
                    Mathf.Max(0.1f, tileSize * 1.5f));

            return
                subjectDistanceToCrossing <= subjectHoldDistance &&
                subjectDistanceToCrossing >= -subjectHoldDistance &&
                otherDistanceToCrossing <= reservationLookahead &&
                otherDistanceToCrossing >= -otherClearDistance;
        }

        private static float ProjectFootprintRadius(
            Vector2 forward,
            float halfLength,
            float halfWidth,
            Vector2 axis)
        {
            Vector2 right = new(-forward.y, forward.x);
            return
                Mathf.Max(0.01f, halfLength) *
                Mathf.Abs(Vector2.Dot(forward, axis)) +
                Mathf.Max(0.01f, halfWidth) *
                Mathf.Abs(Vector2.Dot(right, axis));
        }

        private static float Cross2D(
            Vector2 first,
            Vector2 second)
        {
            return first.x * second.y -
                   first.y * second.x;
        }

        private bool ShouldYieldTrafficConflict(
            RouteVehicle subjectVehicle,
            Object subjectOwner,
            Vector3 subjectDirection,
            RouteVehicle otherVehicle,
            ExternalTrafficVehicle otherExternal,
            Vector3 otherDirection)
        {
            if (VehicleSpacingMath.IsSameFlowDirection(
                    subjectDirection,
                    otherDirection))
            {
                return true;
            }

            ResolveTrafficPriorityState(
                subjectVehicle,
                subjectOwner,
                out EntityId subjectStableId,
                out bool subjectOccupiesIntersection);
            ResolveTrafficPriorityState(
                otherVehicle,
                otherExternal?.Owner,
                out EntityId otherStableId,
                out bool otherOccupiesIntersection);

            return !VehicleSpacingMath
                .HasTrafficConflictPriority(
                    subjectOccupiesIntersection,
                    subjectStableId,
                    otherOccupiesIntersection,
                    otherStableId);
        }

        private void ResolveTrafficPriorityState(
            RouteVehicle managedVehicle,
            Object externalOwner,
            out EntityId stableId,
            out bool occupiesIntersection)
        {
            if (managedVehicle != null)
            {
                stableId =
                    managedVehicle.Object != null
                        ? managedVehicle.Object.GetEntityId()
                        : EntityId.None;
                occupiesIntersection =
                    IsInsideSharedIntersection(
                        managedVehicle.Pos,
                        managedVehicle.CurrentTile,
                        managedVehicle.HasCurrentTile);
                return;
            }

            stableId =
                externalOwner != null
                    ? externalOwner.GetEntityId()
                    : EntityId.None;
            ExternalTrafficVehicle external =
                FindExternalTrafficVehicle(externalOwner);
            occupiesIntersection =
                external != null &&
                IsInsideSharedIntersection(
                    external.Position,
                    external.CurrentTile,
                    external.HasCurrentTile);
        }

        private bool IsInsideSharedIntersection(
            Vector3 position,
            Vector2Int tile,
            bool hasCurrentTile)
        {
            if (!hasCurrentTile ||
                simEngine == null ||
                !simEngine.IsSharedCarIntersection(tile))
            {
                return false;
            }

            Vector3 center =
                GridToLocal(tile, position.z);
            float halfExtent =
                Mathf.Max(0.01f, tileSize * 0.5f);
            return
                Mathf.Abs(position.x - center.x) <=
                halfExtent &&
                Mathf.Abs(position.y - center.y) <=
                halfExtent;
        }

        private void GetVehicleFootprint(
            RouteVehicle vehicle,
            out float halfLength,
            out float halfWidth)
        {
            float lengthScale =
                vehicle.Style.LengthScale > 0f
                    ? vehicle.Style.LengthScale
                    : 1f;
            float widthScale =
                vehicle.Style.WidthScale > 0f
                    ? vehicle.Style.WidthScale
                    : 1f;
            GetTrafficFootprint(
                VehicleBodyLengthTiles * lengthScale,
                VehicleBodyWidthTiles * widthScale,
                out halfLength,
                out halfWidth);
        }

        public void GetTrafficFootprint(
            float lengthTiles,
            float widthTiles,
            out float halfLength,
            out float halfWidth)
        {
            halfLength =
                tileSize *
                Mathf.Max(0.1f, lengthTiles) *
                0.5f;
            halfWidth =
                tileSize *
                Mathf.Max(0.08f, widthTiles) *
                0.5f;
        }

        private static Vector3 ResolveVehicleDirection(
            RouteVehicle vehicle)
        {
            Vector3 direction = vehicle.Dir;
            if (direction.sqrMagnitude <= 0.0001f &&
                vehicle.Object != null)
            {
                direction =
                    vehicle.Object.transform.localRotation *
                    Vector3.right;
            }

            direction.z = 0f;
            return direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector3.right;
        }

        private static bool TrafficFootprintsOverlap(
            Vector3 firstPosition,
            Vector3 firstDirection,
            float firstHalfLength,
            float firstHalfWidth,
            Vector3 secondPosition,
            Vector3 secondDirection,
            float secondHalfLength,
            float secondHalfWidth,
            float verticalLimit,
            float safetyMargin)
        {
            if (Mathf.Abs(
                    firstPosition.z -
                    secondPosition.z) > verticalLimit)
            {
                return false;
            }

            Vector2 firstForward =
                NormalizeDirection(firstDirection);
            Vector2 firstRight =
                new(-firstForward.y, firstForward.x);
            Vector2 secondForward =
                NormalizeDirection(secondDirection);
            Vector2 secondRight =
                new(-secondForward.y, secondForward.x);
            Vector2 separation =
                new(
                    secondPosition.x - firstPosition.x,
                    secondPosition.y - firstPosition.y);

            return !IsSeparatedOnAxis(
                       separation,
                       firstForward,
                       firstForward,
                       firstRight,
                       Mathf.Max(0.01f, firstHalfLength),
                       Mathf.Max(0.01f, firstHalfWidth),
                       secondForward,
                       secondRight,
                       Mathf.Max(0.01f, secondHalfLength),
                       Mathf.Max(0.01f, secondHalfWidth),
                       safetyMargin) &&
                   !IsSeparatedOnAxis(
                       separation,
                       firstRight,
                       firstForward,
                       firstRight,
                       Mathf.Max(0.01f, firstHalfLength),
                       Mathf.Max(0.01f, firstHalfWidth),
                       secondForward,
                       secondRight,
                       Mathf.Max(0.01f, secondHalfLength),
                       Mathf.Max(0.01f, secondHalfWidth),
                       safetyMargin) &&
                   !IsSeparatedOnAxis(
                       separation,
                       secondForward,
                       firstForward,
                       firstRight,
                       Mathf.Max(0.01f, firstHalfLength),
                       Mathf.Max(0.01f, firstHalfWidth),
                       secondForward,
                       secondRight,
                       Mathf.Max(0.01f, secondHalfLength),
                       Mathf.Max(0.01f, secondHalfWidth),
                       safetyMargin) &&
                   !IsSeparatedOnAxis(
                       separation,
                       secondRight,
                       firstForward,
                       firstRight,
                       Mathf.Max(0.01f, firstHalfLength),
                       Mathf.Max(0.01f, firstHalfWidth),
                       secondForward,
                       secondRight,
                       Mathf.Max(0.01f, secondHalfLength),
                       Mathf.Max(0.01f, secondHalfWidth),
                       safetyMargin);
        }

        private static bool IsSeparatedOnAxis(
            Vector2 separation,
            Vector2 axis,
            Vector2 firstForward,
            Vector2 firstRight,
            float firstHalfLength,
            float firstHalfWidth,
            Vector2 secondForward,
            Vector2 secondRight,
            float secondHalfLength,
            float secondHalfWidth,
            float safetyMargin)
        {
            float centerDistance =
                Mathf.Abs(Vector2.Dot(separation, axis));
            float firstRadius =
                firstHalfLength *
                Mathf.Abs(
                    Vector2.Dot(firstForward, axis)) +
                firstHalfWidth *
                Mathf.Abs(
                    Vector2.Dot(firstRight, axis));
            float secondRadius =
                secondHalfLength *
                Mathf.Abs(
                    Vector2.Dot(secondForward, axis)) +
                secondHalfWidth *
                Mathf.Abs(
                    Vector2.Dot(secondRight, axis));
            return centerDistance >=
                   firstRadius +
                   secondRadius +
                   Mathf.Max(0f, safetyMargin);
        }

        private static Vector2 NormalizeDirection(
            Vector3 direction)
        {
            Vector2 direction2D =
                new(direction.x, direction.y);
            return direction2D.sqrMagnitude > 0.0001f
                ? direction2D.normalized
                : Vector2.right;
        }

        /// <summary>
        /// 건물 프리팹이 제공하는 실제 주차 슬롯의 CityView 로컬 포즈를 반환합니다.
        /// 기능 차량도 통근 차량과 같은 주차 배치를 재사용할 수 있습니다.
        /// </summary>
        public bool TryGetBuildingParkingPose(
            Vector2Int buildingTile,
            int slotIndex,
            out Vector3 localPosition,
            out Vector3 localForward)
        {
            localPosition = default;
            localForward = default;

            if (tileData != null &&
                tileData.TryGetFootprintAnchor(
                    buildingTile,
                    out Vector2Int anchor))
            {
                buildingTile = anchor;
            }

            if (!tileVisuals.TryGetValue(
                    buildingTile,
                    out TileVisual visual) ||
                visual?.Object == null)
            {
                return false;
            }

            Transform slot = visual.Object.transform.Find(
                $"ParkingSlot_{Mathf.Max(0, slotIndex)}");
            if (slot == null)
            {
                return false;
            }

            localPosition =
                transform.InverseTransformPoint(slot.position);
            localForward =
                transform.InverseTransformDirection(
                    visual.Object.transform.TransformDirection(
                        Vector3.up));
            localForward.z = 0f;

            if (localForward.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            localForward.Normalize();
            return true;
        }

        private bool TryGetExternalTrafficHeadway(
            RouteVehicle follower,
            RoutePolyline followerPath,
            float followerDistance,
            out float headway,
            out float leaderSpeed)
        {
            headway = float.PositiveInfinity;
            leaderSpeed = 0f;
            bool found = false;
            Vector3 forward =
                follower.Dir.sqrMagnitude > 0.0001f
                    ? follower.Dir.normalized
                    : Vector3.right;
            float lateralLimit =
                Mathf.Max(0.2f, tileSize * 0.36f);
            float lookahead =
                Mathf.Max(
                    tileSize * 3f,
                    vehicleMinHeadway * tileSize);

            for (int i = 0;
                 i < externalTrafficVehicles.Count;
                 i++)
            {
                ExternalTrafficVehicle state =
                    externalTrafficVehicles[i];
                if (!state.Active || state.Owner == null)
                {
                    continue;
                }

                float candidateHeadway =
                    float.PositiveInfinity;
                bool isAhead =
                    followerPath != null &&
                    TryGetForwardPathHeadway(
                        followerPath,
                        followerDistance,
                        state.Position,
                        lookahead,
                        lateralLimit,
                        tileSize * 0.125f,
                        out candidateHeadway);
                if (!isAhead)
                {
                    isAhead = IsInsideForwardCorridor(
                        state.Position,
                        follower.Pos,
                        forward,
                        lookahead,
                        lateralLimit,
                        out candidateHeadway);
                }

                Vector3 candidatePathDirection =
                    followerPath != null &&
                    !float.IsPositiveInfinity(
                        candidateHeadway)
                        ? followerPath.SampleAt(
                            followerDistance +
                            candidateHeadway).Dir
                        : forward;
                if (!VehicleSpacingMath.IsSameFlowDirection(
                        candidatePathDirection,
                        state.Direction))
                {
                    continue;
                }

                if (!isAhead ||
                    candidateHeadway >= headway)
                {
                    continue;
                }

                headway = candidateHeadway;
                leaderSpeed =
                    state.Speed;
                found = true;
            }

            return found;
        }

        private static bool TryGetForwardPathHeadway(
            RoutePolyline path,
            float currentDistance,
            Vector3 candidate,
            float maximumAhead,
            float lateralLimit,
            float sampleStep,
            out float ahead)
        {
            ahead = float.PositiveInfinity;
            if (path == null || path.Length <= 0f)
            {
                return false;
            }

            float startDistance =
                Mathf.Clamp(
                    currentDistance,
                    0f,
                    path.Length);
            float endDistance =
                Mathf.Min(
                    path.Length,
                    startDistance +
                    Mathf.Max(0f, maximumAhead));
            float safeStep =
                Mathf.Max(0.025f, sampleStep);
            Sample previous =
                path.SampleAt(startDistance);
            bool found = false;

            if (Vector3.Distance(
                    previous.Pos,
                    candidate) <= lateralLimit)
            {
                ahead = 0f;
                found = true;
            }

            float segmentStartDistance = startDistance;
            while (segmentStartDistance <
                   endDistance - 0.0001f)
            {
                float segmentEndDistance =
                    Mathf.Min(
                        endDistance,
                        segmentStartDistance + safeStep);
                Sample next =
                    path.SampleAt(segmentEndDistance);
                Vector3 segment =
                    next.Pos - previous.Pos;
                float segmentLength =
                    segment.magnitude;

                if (segmentLength > 0.0001f)
                {
                    float projection =
                        Mathf.Clamp01(
                            Vector3.Dot(
                                candidate - previous.Pos,
                                segment) /
                            segment.sqrMagnitude);
                    Vector3 closest =
                        previous.Pos +
                        segment * projection;
                    float lateralDistance =
                        Vector3.Distance(
                            candidate,
                            closest);
                    float candidateAhead =
                        segmentStartDistance -
                        startDistance +
                        segmentLength * projection;

                    if (lateralDistance <= lateralLimit &&
                        candidateAhead < ahead)
                    {
                        ahead = candidateAhead;
                        found = true;
                    }
                }

                previous = next;
                segmentStartDistance =
                    segmentEndDistance;
            }

            return found;
        }

        private bool HasActiveExternalTrafficVehicle()
        {
            for (int i = 0;
                 i < externalTrafficVehicles.Count;
                 i++)
            {
                ExternalTrafficVehicle vehicle =
                    externalTrafficVehicles[i];
                if (vehicle.Active && vehicle.Owner != null)
                {
                    return true;
                }
            }

            return false;
        }

        private ExternalTrafficVehicle
            FindExternalTrafficVehicle(Object owner)
        {
            for (int i = 0;
                 i < externalTrafficVehicles.Count;
                 i++)
            {
                if (externalTrafficVehicles[i].Owner == owner)
                {
                    return externalTrafficVehicles[i];
                }
            }

            return null;
        }

        private static bool IsTrafficVehicleActive(
            RouteVehicle vehicle)
        {
            return
                vehicle != null &&
                vehicle.Object != null &&
                vehicle.Object.activeInHierarchy &&
                vehicle.HasCurrentTile;
        }

        private static bool IsInsideForwardCorridor(
            Vector3 candidate,
            Vector3 origin,
            Vector3 forward,
            float maximumAhead,
            float lateralLimit,
            out float ahead)
        {
            Vector3 separation = candidate - origin;
            ahead = Vector3.Dot(separation, forward);
            if (ahead <= 0.001f || ahead > maximumAhead)
            {
                return false;
            }

            Vector3 lateral =
                separation - forward * ahead;
            return lateral.magnitude <= lateralLimit;
        }
    }
}
