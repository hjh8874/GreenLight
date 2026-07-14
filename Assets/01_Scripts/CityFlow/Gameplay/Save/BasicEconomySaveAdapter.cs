using CityFlow.Content;
using CityFlow.Contracts.Save;

namespace CityFlow.Gameplay.Save
{
    public sealed class BasicEconomySaveAdapter : IWeeklySettlementSaveSource
    {
        private readonly BasicEconomySystem economySystem;

        public BasicEconomySaveAdapter(BasicEconomySystem economySystem)
        {
            this.economySystem = economySystem;
        }

        public WeeklySettlementSaveData CreateSnapshot()
        {
            return new WeeklySettlementSaveData
            {
                PendingCoins = economySystem.WeeklyAccumulatedCoin
            };
        }

        public void RestoreSnapshot(WeeklySettlementSaveData snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            economySystem.RestoreWeeklyAccumulatedCoin(snapshot.PendingCoins);
        }
    }
}
