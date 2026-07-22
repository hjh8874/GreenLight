using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Feed
{
    public readonly struct CitizenFeedContext
    {
        public CitizenFeedEventType EventType { get; }
        public Vector2Int Tile { get; }
        public float Density01 { get; }
        public CongestionLevel PreviousCongestion { get; }
        public CongestionLevel CurrentCongestion { get; }
        public int PreviousGreenSlots { get; }
        public int CurrentGreenSlots { get; }
        public int PreviousOffsetSlots { get; }
        public int CurrentOffsetSlots { get; }
        public float PreviousStability01 { get; }
        public float CurrentStability01 { get; }
        public float RouteDistanceTiles { get; }
        public int ActiveVehicleCount { get; }
        public CitizenFeedInfrastructureType InfrastructureType { get; }
        public bool IsRemoval { get; }
        public int GameHour { get; }

        private CitizenFeedContext(
            CitizenFeedEventType eventType,
            Vector2Int tile,
            float density01,
            CongestionLevel previousCongestion,
            CongestionLevel currentCongestion,
            int previousGreenSlots,
            int currentGreenSlots,
            int previousOffsetSlots,
            int currentOffsetSlots,
            float previousStability01,
            float currentStability01,
            float routeDistanceTiles,
            int activeVehicleCount,
            CitizenFeedInfrastructureType infrastructureType,
            bool isRemoval,
            int gameHour)
        {
            EventType = eventType;
            Tile = tile;
            Density01 = Mathf.Clamp01(density01);
            PreviousCongestion = previousCongestion;
            CurrentCongestion = currentCongestion;
            PreviousGreenSlots = previousGreenSlots;
            CurrentGreenSlots = currentGreenSlots;
            PreviousOffsetSlots = previousOffsetSlots;
            CurrentOffsetSlots = currentOffsetSlots;
            PreviousStability01 = Mathf.Clamp01(previousStability01);
            CurrentStability01 = Mathf.Clamp01(currentStability01);
            RouteDistanceTiles = Mathf.Max(0f, routeDistanceTiles);
            ActiveVehicleCount = Mathf.Max(0, activeVehicleCount);
            InfrastructureType = infrastructureType;
            IsRemoval = isRemoval;
            GameHour = Mathf.Clamp(gameHour, 0, 23);
        }

        public static CitizenFeedContext ForCongestion(
            CitizenFeedEventType eventType,
            Vector2Int tile,
            float density01,
            CongestionLevel previousCongestion,
            CongestionLevel currentCongestion,
            int gameHour)
        {
            return new CitizenFeedContext(
                eventType,
                tile,
                density01,
                previousCongestion,
                currentCongestion,
                0,
                0,
                0,
                0,
                0f,
                0f,
                0f,
                0,
                CitizenFeedInfrastructureType.None,
                false,
                gameHour);
        }

        public static CitizenFeedContext ForSignal(
            Vector2Int tile,
            int previousGreenSlots,
            int currentGreenSlots,
            int previousOffsetSlots,
            int currentOffsetSlots,
            int gameHour)
        {
            return new CitizenFeedContext(
                CitizenFeedEventType.SignalChanged,
                tile,
                0f,
                CongestionLevel.Free,
                CongestionLevel.Free,
                previousGreenSlots,
                currentGreenSlots,
                previousOffsetSlots,
                currentOffsetSlots,
                0f,
                0f,
                0f,
                0,
                CitizenFeedInfrastructureType.Signal,
                false,
                gameHour);
        }

        public static CitizenFeedContext ForStability(
            CitizenFeedEventType eventType,
            float previousStability01,
            float currentStability01,
            int gameHour)
        {
            return new CitizenFeedContext(
                eventType,
                Vector2Int.zero,
                0f,
                CongestionLevel.Free,
                CongestionLevel.Free,
                0,
                0,
                0,
                0,
                previousStability01,
                currentStability01,
                0f,
                0,
                CitizenFeedInfrastructureType.None,
                false,
                gameHour);
        }

        public static CitizenFeedContext ForInfrastructure(
            CitizenFeedEventType eventType,
            Vector2Int tile,
            CitizenFeedInfrastructureType infrastructureType,
            bool isRemoval,
            int gameHour)
        {
            return new CitizenFeedContext(
                eventType,
                tile,
                0f,
                CongestionLevel.Free,
                CongestionLevel.Free,
                0,
                0,
                0,
                0,
                0f,
                0f,
                0f,
                0,
                infrastructureType,
                isRemoval,
                gameHour);
        }

        public static CitizenFeedContext ForArrival(
            Vector2Int destination,
            float routeDistanceTiles,
            int gameHour)
        {
            return new CitizenFeedContext(
                CitizenFeedEventType.NotableArrival,
                destination,
                0f,
                CongestionLevel.Free,
                CongestionLevel.Free,
                0,
                0,
                0,
                0,
                0f,
                0f,
                routeDistanceTiles,
                0,
                CitizenFeedInfrastructureType.None,
                false,
                gameHour);
        }

        public static CitizenFeedContext ForVehicleSurge(int activeVehicleCount, int gameHour)
        {
            return new CitizenFeedContext(
                CitizenFeedEventType.VehicleSurge,
                Vector2Int.zero,
                0f,
                CongestionLevel.Free,
                CongestionLevel.Free,
                0,
                0,
                0,
                0,
                0f,
                0f,
                0f,
                activeVehicleCount,
                CitizenFeedInfrastructureType.None,
                false,
                gameHour);
        }
    }

    public readonly struct CitizenFeedPost
    {
        public CitizenFeedEventType EventType { get; }
        public Vector2Int Tile { get; }
        public string AuthorName { get; }
        public string RoleLabel { get; }
        public string Message { get; }
        public string Timestamp { get; }
        public string AvatarInitial { get; }
        public Color AccentColor { get; }

        public CitizenFeedPost(
            CitizenFeedEventType eventType,
            Vector2Int tile,
            string authorName,
            string roleLabel,
            string message,
            string timestamp,
            string avatarInitial,
            Color accentColor)
        {
            EventType = eventType;
            Tile = tile;
            AuthorName = authorName;
            RoleLabel = roleLabel;
            Message = message;
            Timestamp = timestamp;
            AvatarInitial = avatarInitial;
            AccentColor = accentColor;
        }
    }
}
