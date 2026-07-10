using System;

namespace CityFlow.Contracts
{
    public interface IEconomyService
    {
        long Coins { get; }

        event Action<long> CoinsChanged;

        bool TrySpend(long amount);
        void AddCoins(long amount, string reason);
    }
}
