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
        public float RouteDistanceTiles { get; }
        public int ActiveVehicleCount { get; }
        public CitizenFeedInfrastructureType InfrastructureType { get; }
        public bool IsRemoval { get; }
        public int GameHour { get; }
        public Vector2Int Home { get; }
        public Vector2Int OldWork { get; }
        public Vector2Int NewWork { get; }

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
            RouteDistanceTiles = Mathf.Max(0f, routeDistanceTiles);
            ActiveVehicleCount = Mathf.Max(0, activeVehicleCount);
            InfrastructureType = infrastructureType;
            IsRemoval = isRemoval;
            GameHour = Mathf.Clamp(gameHour, 0, 23);
            Home = Vector2Int.zero;
            OldWork = Vector2Int.zero;
            NewWork = Vector2Int.zero;
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
                0,
                CitizenFeedInfrastructureType.Signal,
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
                0,
                infrastructureType,
                isRemoval,
                gameHour);
        }

        /// <summary>
        /// 타일 하나만 있으면 되는 사건용(흐름 폭발·건물 설치·구급 출동/결말).
        /// ForInfrastructure가 이미 범용 타일 컨텍스트라 그대로 위임한다.
        /// </summary>
        public static CitizenFeedContext ForTile(
            CitizenFeedEventType eventType,
            Vector2Int tile,
            int gameHour)
        {
            return ForInfrastructure(
                eventType,
                tile,
                CitizenFeedInfrastructureType.None,
                false,
                gameHour);
        }

        /// <summary>
        /// 시간대 진입은 장소가 없다. VehicleSurge와 같은 이유로 타일은 zero다.
        /// </summary>
        public static CitizenFeedContext ForTimePeriod(int gameHour)
        {
            return ForInfrastructure(
                CitizenFeedEventType.TimePeriodChanged,
                Vector2Int.zero,
                CitizenFeedInfrastructureType.None,
                false,
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
                activeVehicleCount,
                CitizenFeedInfrastructureType.None,
                false,
                gameHour);
        }

        public static CitizenFeedContext ForJobChanged(
            Vector2Int home, Vector2Int oldWork, Vector2Int newWork, int gameHour)
        {
            CitizenFeedContext context = new CitizenFeedContext(
                CitizenFeedEventType.JobChanged,
                newWork,
                0f,
                CongestionLevel.Free,
                CongestionLevel.Free,
                0, 0, 0, 0, 0f, 0,
                CitizenFeedInfrastructureType.None,
                false,
                gameHour);
            return new CitizenFeedContext(
                context, home, oldWork, newWork);
        }

        private CitizenFeedContext(
            CitizenFeedContext source, Vector2Int home, Vector2Int oldWork, Vector2Int newWork)
        {
            EventType = source.EventType; Tile = source.Tile; Density01 = source.Density01;
            PreviousCongestion = source.PreviousCongestion; CurrentCongestion = source.CurrentCongestion;
            PreviousGreenSlots = source.PreviousGreenSlots; CurrentGreenSlots = source.CurrentGreenSlots;
            PreviousOffsetSlots = source.PreviousOffsetSlots; CurrentOffsetSlots = source.CurrentOffsetSlots;
            RouteDistanceTiles = source.RouteDistanceTiles; ActiveVehicleCount = source.ActiveVehicleCount;
            InfrastructureType = source.InfrastructureType; IsRemoval = source.IsRemoval; GameHour = source.GameHour;
            Home = home; OldWork = oldWork; NewWork = newWork;
        }
    }

    public readonly struct CitizenFeedPost
    {
        public CitizenFeedEventType EventType { get; }
        public Vector2Int Tile { get; }

        /// <summary>
        /// 글이 실제로 어딘가를 가리키는가. 시간대 훅과 차량 급증은 도시 전체 이야기라
        /// Tile이 Vector2Int.zero인데, 이걸 좌표로 믿으면 클릭 시 격자 모서리가 열린다.
        /// (0,0)은 유효한 타일이라 값만 보고는 구분할 수 없어 이벤트 종류로 판단한다.
        /// </summary>
        public bool HasLocation =>
            EventType != CitizenFeedEventType.TimePeriodChanged &&
            EventType != CitizenFeedEventType.VehicleSurge;
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
