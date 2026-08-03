using System;

namespace CityFlow.View
{
    [Flags]
    public enum SimpleTownRoadConnections
    {
        None = 0,
        North = 1 << 0,
        East = 1 << 1,
        South = 1 << 2,
        West = 1 << 3
    }

    public enum SimpleTownRoadShape
    {
        Isolated,
        End,
        Straight,
        Corner,
        TIntersection,
        CrossIntersection
    }

    public readonly struct SimpleTownRoadSelection
    {
        public SimpleTownRoadSelection(
            SimpleTownRoadShape shape,
            float rotationDegrees)
        {
            Shape = shape;
            RotationDegrees = rotationDegrees;
        }

        public SimpleTownRoadShape Shape { get; }
        public float RotationDegrees { get; }
    }

    public static class SimpleTownRoadTopology
    {
        public const SimpleTownRoadConnections All =
            SimpleTownRoadConnections.North |
            SimpleTownRoadConnections.East |
            SimpleTownRoadConnections.South |
            SimpleTownRoadConnections.West;

        public static SimpleTownRoadConnections GetPerimeterSides(
            SimpleTownRoadConnections connections)
        {
            return All & ~connections;
        }

        public static bool ShouldDrawCenterLines(
            SimpleTownRoadConnections connections)
        {
            connections &= All;
            int count = CountConnections(connections);
            if (count == 1)
            {
                return true;
            }

            if (count != 2)
            {
                return false;
            }

            return connections ==
                       (SimpleTownRoadConnections.North |
                        SimpleTownRoadConnections.South) ||
                   connections ==
                       (SimpleTownRoadConnections.East |
                        SimpleTownRoadConnections.West);
        }

        public static bool IsCenterLineHorizontal(
            SimpleTownRoadConnections connections)
        {
            connections &= All;
            return (connections &
                    (SimpleTownRoadConnections.East |
                     SimpleTownRoadConnections.West)) != 0;
        }

        public static bool ShouldDrawPerimeterCorner(
            SimpleTownRoadConnections connections,
            SimpleTownRoadConnections firstSide,
            SimpleTownRoadConnections secondSide,
            bool hasDiagonalRoad)
        {
            return !hasDiagonalRoad &&
                   (connections & firstSide) != 0 &&
                   (connections & secondSide) != 0;
        }

        public static SimpleTownRoadSelection Resolve(
            SimpleTownRoadConnections connections)
        {
            connections &= All;
            int count = CountConnections(connections);

            if (count == 0)
            {
                return new SimpleTownRoadSelection(
                    SimpleTownRoadShape.Isolated,
                    0f);
            }

            if (count == 1)
            {
                return new SimpleTownRoadSelection(
                    SimpleTownRoadShape.End,
                    GetEndRotation(connections));
            }

            if (count == 2)
            {
                bool isVertical =
                    connections ==
                    (SimpleTownRoadConnections.North |
                     SimpleTownRoadConnections.South);
                bool isHorizontal =
                    connections ==
                    (SimpleTownRoadConnections.East |
                     SimpleTownRoadConnections.West);

                return isVertical || isHorizontal
                    ? new SimpleTownRoadSelection(
                        SimpleTownRoadShape.Straight,
                        isHorizontal ? 90f : 0f)
                    : new SimpleTownRoadSelection(
                        SimpleTownRoadShape.Corner,
                        GetCornerRotation(connections));
            }

            if (count == 3)
            {
                return new SimpleTownRoadSelection(
                    SimpleTownRoadShape.TIntersection,
                    GetTRotation(connections));
            }

            return new SimpleTownRoadSelection(
                SimpleTownRoadShape.CrossIntersection,
                0f);
        }

        private static int CountConnections(
            SimpleTownRoadConnections connections)
        {
            int value = (int)connections;
            int count = 0;

            while (value != 0)
            {
                count += value & 1;
                value >>= 1;
            }

            return count;
        }

        private static float GetEndRotation(
            SimpleTownRoadConnections connections)
        {
            return connections switch
            {
                SimpleTownRoadConnections.East => -90f,
                SimpleTownRoadConnections.South => 180f,
                SimpleTownRoadConnections.West => 90f,
                _ => 0f
            };
        }

        private static float GetCornerRotation(
            SimpleTownRoadConnections connections)
        {
            return connections switch
            {
                SimpleTownRoadConnections.East |
                SimpleTownRoadConnections.South => -90f,
                SimpleTownRoadConnections.South |
                SimpleTownRoadConnections.West => 180f,
                SimpleTownRoadConnections.West |
                SimpleTownRoadConnections.North => 90f,
                _ => 0f
            };
        }

        private static float GetTRotation(
            SimpleTownRoadConnections connections)
        {
            SimpleTownRoadConnections missing = All & ~connections;
            return missing switch
            {
                SimpleTownRoadConnections.West => -90f,
                SimpleTownRoadConnections.North => 180f,
                SimpleTownRoadConnections.East => 90f,
                _ => 0f
            };
        }
    }
}
