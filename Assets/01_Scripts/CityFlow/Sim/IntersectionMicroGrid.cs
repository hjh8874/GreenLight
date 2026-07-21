using System;

namespace CityFlow.Sim
{
    [Flags]
    internal enum IntersectionCell : byte
    {
        None = 0,
        NorthWest = 1 << 0,
        NorthEast = 1 << 1,
        SouthWest = 1 << 2,
        SouthEast = 1 << 3,
        All = NorthWest | NorthEast | SouthWest | SouthEast,
    }

    internal enum IntersectionStage : byte
    {
        None = 0,
        Entry = 1,
        Conflict = 2,
        Exit = 3,
    }

    internal static class IntersectionMicroGrid
    {
        public static IntersectionCell StageMask(
            Dir entry,
            Dir exit,
            IntersectionStage stage)
        {
            return stage switch
            {
                IntersectionStage.Entry => EntryCell(entry),
                IntersectionStage.Conflict => MovementMask(entry, exit),
                IntersectionStage.Exit => ExitCell(exit),
                _ => IntersectionCell.None,
            };
        }

        public static IntersectionCell OccupancyMask(
            Dir entry,
            Dir exit,
            IntersectionStage stage) =>
            stage == IntersectionStage.Exit
                ? MovementMask(entry, exit)
                : StageMask(entry, exit, stage);

        public static IntersectionStage NextStage(IntersectionStage stage) =>
            stage switch
            {
                IntersectionStage.Entry => IntersectionStage.Conflict,
                IntersectionStage.Conflict => IntersectionStage.Exit,
                _ => stage,
            };

        public static float Progress01(IntersectionStage stage) =>
            stage switch
            {
                IntersectionStage.Entry => 0.25f,
                // Conflict grants traversal across the occupied path. It is not a
                // physical center cell, so the view may continue toward the exit cell.
                IntersectionStage.Conflict => 0.75f,
                IntersectionStage.Exit => 0.75f,
                _ => -1f,
            };

        public static IntersectionCell MovementMask(Dir entry, Dir exit)
        {
            return entry switch
            {
                Dir.N => exit switch
                {
                    Dir.N => IntersectionCell.SouthEast | IntersectionCell.NorthEast,
                    Dir.E => IntersectionCell.SouthEast,
                    Dir.W => IntersectionCell.SouthEast | IntersectionCell.NorthWest,
                    _ => IntersectionCell.All,
                },
                Dir.E => exit switch
                {
                    Dir.E => IntersectionCell.SouthWest | IntersectionCell.SouthEast,
                    Dir.S => IntersectionCell.SouthWest,
                    Dir.N => IntersectionCell.SouthWest | IntersectionCell.NorthEast,
                    _ => IntersectionCell.All,
                },
                Dir.S => exit switch
                {
                    Dir.S => IntersectionCell.NorthWest | IntersectionCell.SouthWest,
                    Dir.W => IntersectionCell.NorthWest,
                    Dir.E => IntersectionCell.NorthWest | IntersectionCell.SouthEast,
                    _ => IntersectionCell.All,
                },
                Dir.W => exit switch
                {
                    Dir.W => IntersectionCell.NorthEast | IntersectionCell.NorthWest,
                    Dir.N => IntersectionCell.NorthEast,
                    Dir.S => IntersectionCell.NorthEast | IntersectionCell.SouthWest,
                    _ => IntersectionCell.All,
                },
                _ => IntersectionCell.All,
            };
        }

        public static bool Conflicts(IntersectionCell occupied, IntersectionCell requested) =>
            (occupied & requested) != IntersectionCell.None;

        private static IntersectionCell EntryCell(Dir entry) =>
            entry switch
            {
                Dir.N => IntersectionCell.SouthEast,
                Dir.E => IntersectionCell.SouthWest,
                Dir.S => IntersectionCell.NorthWest,
                Dir.W => IntersectionCell.NorthEast,
                _ => IntersectionCell.All,
            };

        private static IntersectionCell ExitCell(Dir exit) =>
            exit switch
            {
                Dir.N => IntersectionCell.NorthEast,
                Dir.E => IntersectionCell.SouthEast,
                Dir.S => IntersectionCell.SouthWest,
                Dir.W => IntersectionCell.NorthWest,
                _ => IntersectionCell.All,
            };

        // Runtime integration: RoadQueueNetwork reserves these masks before entering intersections.
    }
}
