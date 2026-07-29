using CityFlow.ViewKit;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public class VehicleSpacingMathTests
    {
        [Test]
        public void CalculateLookaheadTiles_LowSpeedUsesMinimumRange()
        {
            int result = VehicleSpacingMath.CalculateLookaheadTiles(
                speed: 0.5f,
                brakeAcceleration: 5f,
                reactionSeconds: 0.4f,
                minimumHeadway: 0.55f,
                tileSize: 1f);

            Assert.AreEqual(2, result);
        }

        [Test]
        public void CalculateLookaheadTiles_NominalSpeedUsesThreeTiles()
        {
            int result = VehicleSpacingMath.CalculateLookaheadTiles(
                speed: 2.5f,
                brakeAcceleration: 5f,
                reactionSeconds: 0.4f,
                minimumHeadway: 0.55f,
                tileSize: 1f);

            Assert.AreEqual(3, result);
        }

        [Test]
        public void CalculateLookaheadTiles_HighSpeedIsCappedAtMaximumRange()
        {
            int result = VehicleSpacingMath.CalculateLookaheadTiles(
                speed: 20f,
                brakeAcceleration: 1f,
                reactionSeconds: 1f,
                minimumHeadway: 1f,
                tileSize: 1f);

            Assert.AreEqual(3, result);
        }

        [Test]
        public void LimitAdvance_PreservesMinimumHeadway()
        {
            float result = VehicleSpacingMath.LimitAdvance(
                proposedAdvance: 0.5f,
                headway: 0.8f,
                minimumHeadway: 0.55f);

            Assert.AreEqual(0.25f, result, 1e-4f);
        }

        [Test]
        public void LimitAdvance_AlreadyInsideMinimumHeadwayStopsMovement()
        {
            float result = VehicleSpacingMath.LimitAdvance(
                proposedAdvance: 0.5f,
                headway: 0.4f,
                minimumHeadway: 0.55f);

            Assert.AreEqual(0f, result, 1e-4f);
        }

        [Test]
        public void IsSameFlowDirection_OppositeLaneIsNotLeader()
        {
            Assert.IsFalse(
                VehicleSpacingMath.IsSameFlowDirection(
                    Vector3.right,
                    Vector3.left));
        }

        [Test]
        public void IsSameFlowDirection_FollowsTurnDirection()
        {
            Assert.IsTrue(
                VehicleSpacingMath.IsSameFlowDirection(
                    Vector3.up,
                    new Vector3(0.1f, 1f, 0f)));
        }

        [Test]
        public void ClampCorridorToForwardProgress_DoesNotMoveVehicleBackward()
        {
            float result =
                VehicleSpacingMath
                    .ClampCorridorToForwardProgress(
                        currentDistance: 4f,
                        authorizedDistance: 2f);

            Assert.AreEqual(4f, result, 1e-4f);
        }

        [Test]
        public void ClampCorridorToForwardProgress_AllowsForwardAuthority()
        {
            float result =
                VehicleSpacingMath
                    .ClampCorridorToForwardProgress(
                        currentDistance: 2f,
                        authorizedDistance: 4f);

            Assert.AreEqual(4f, result, 1e-4f);
        }
    }
}
