namespace CityFlow.Contracts
{
    public readonly struct WeeklySettlementCompletedEvent
    {
        public long Amount { get; }
        public long BalanceAfterSettlement { get; }
        public int SettlementDays { get; }

        public WeeklySettlementCompletedEvent(
            long amount,
            long balanceAfterSettlement,
            int settlementDays)
        {
            Amount = amount;
            BalanceAfterSettlement = balanceAfterSettlement;
            SettlementDays = settlementDays;
        }
    }
}
