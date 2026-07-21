using CityFlow.Sim;
using NUnit.Framework;

namespace CityFlow.Sim.Tests
{
    public class IntersectionMicroGridTests
    {
        [Test]
        public void OpposingStraightPaths_DoNotConflict()
        {
            IntersectionCell eastbound = IntersectionMicroGrid.MovementMask(Dir.E, Dir.E);
            IntersectionCell westbound = IntersectionMicroGrid.MovementMask(Dir.W, Dir.W);

            Assert.IsFalse(IntersectionMicroGrid.Conflicts(eastbound, westbound));
        }

        [Test]
        public void PerpendicularStraightPaths_Conflict()
        {
            IntersectionCell eastbound = IntersectionMicroGrid.MovementMask(Dir.E, Dir.E);
            IntersectionCell southbound = IntersectionMicroGrid.MovementMask(Dir.S, Dir.S);

            Assert.IsTrue(IntersectionMicroGrid.Conflicts(eastbound, southbound));
        }

        [Test]
        public void LeftTurn_ConflictsWithOpposingStraightPath()
        {
            IntersectionCell eastToNorth = IntersectionMicroGrid.MovementMask(Dir.E, Dir.N);
            IntersectionCell westbound = IntersectionMicroGrid.MovementMask(Dir.W, Dir.W);

            Assert.IsTrue(IntersectionMicroGrid.Conflicts(eastToNorth, westbound));
        }

        [Test]
        public void FourRightTurns_UseDifferentQuadrants()
        {
            IntersectionCell northToEast = IntersectionMicroGrid.MovementMask(Dir.N, Dir.E);
            IntersectionCell eastToSouth = IntersectionMicroGrid.MovementMask(Dir.E, Dir.S);
            IntersectionCell southToWest = IntersectionMicroGrid.MovementMask(Dir.S, Dir.W);
            IntersectionCell westToNorth = IntersectionMicroGrid.MovementMask(Dir.W, Dir.N);

            IntersectionCell combined = northToEast | eastToSouth | southToWest | westToNorth;
            Assert.AreEqual(IntersectionCell.All, combined);
            Assert.IsFalse(IntersectionMicroGrid.Conflicts(northToEast, eastToSouth));
            Assert.IsFalse(IntersectionMicroGrid.Conflicts(southToWest, westToNorth));
        }

        [Test]
        public void Stages_AdvanceFromEntryThroughConflictToExit()
        {
            IntersectionCell entry = IntersectionMicroGrid.StageMask(
                Dir.E,
                Dir.N,
                IntersectionStage.Entry);
            IntersectionCell conflict = IntersectionMicroGrid.StageMask(
                Dir.E,
                Dir.N,
                IntersectionStage.Conflict);
            IntersectionCell exit = IntersectionMicroGrid.StageMask(
                Dir.E,
                Dir.N,
                IntersectionStage.Exit);

            Assert.AreEqual(IntersectionCell.SouthWest, entry);
            Assert.AreEqual(
                IntersectionCell.SouthWest | IntersectionCell.NorthEast,
                conflict);
            Assert.AreEqual(IntersectionCell.NorthEast, exit);
            Assert.AreEqual(
                IntersectionStage.Conflict,
                IntersectionMicroGrid.NextStage(IntersectionStage.Entry));
            Assert.AreEqual(
                IntersectionStage.Exit,
                IntersectionMicroGrid.NextStage(IntersectionStage.Conflict));
        }

        [Test]
        public void Stages_ExposeEntryAndAuthorizedExitProgress()
        {
            Assert.AreEqual(0.25f, IntersectionMicroGrid.Progress01(IntersectionStage.Entry));
            Assert.AreEqual(0.75f, IntersectionMicroGrid.Progress01(IntersectionStage.Conflict));
            Assert.AreEqual(0.75f, IntersectionMicroGrid.Progress01(IntersectionStage.Exit));
            Assert.AreEqual(-1f, IntersectionMicroGrid.Progress01(IntersectionStage.None));
        }

        [Test]
        public void ExitOccupancy_CoversMovementPathUntilVehicleLeaves()
        {
            IntersectionCell occupancy = IntersectionMicroGrid.OccupancyMask(
                Dir.E,
                Dir.N,
                IntersectionStage.Exit);

            Assert.AreEqual(
                IntersectionCell.SouthWest | IntersectionCell.NorthEast,
                occupancy);
        }
    }
}
