namespace CityFlow.Contracts
{
    public readonly struct CompanyStaffing
    {
        public readonly int Filled;
        public readonly int Capacity;

        public CompanyStaffing(
            int filled,
            int capacity
        )
        {
            Filled = filled;
            Capacity = capacity;
        }
    }

    public interface IReadOnlyCityStats
    {
        int ActiveVehicleCount { get; }


        bool TryGetCompanyStaffing(
            UnityEngine.Vector2Int tile,
            out CompanyStaffing staffing
        );
    }
}
