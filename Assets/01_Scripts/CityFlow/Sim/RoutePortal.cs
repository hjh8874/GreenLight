using UnityEngine;

namespace CityFlow.Sim
{
    internal readonly struct RoutePortal
    {
        public RoutePortal(
            Vector2Int from,
            Vector2Int to,
            int sourceRegion,
            int targetRegion,
            bool isHighway)
        {
            From = from;
            To = to;
            SourceRegion = sourceRegion;
            TargetRegion = targetRegion;
            IsHighway = isHighway;
        }

        public Vector2Int From { get; }
        public Vector2Int To { get; }
        public int SourceRegion { get; }
        public int TargetRegion { get; }
        public bool IsHighway { get; }
    }
}
