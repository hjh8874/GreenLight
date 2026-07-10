using System;
using UnityEngine;

namespace CityFlow.Content
{
    [Serializable]
    public class EconomyWallet
    {
        [SerializeField] private int coin;
        [SerializeField] private int prosperity;

        public int Coin => coin;
        public int Prosperity => prosperity;

        public event Action<int> OnCoinChanged;
        public event Action<int> OnProsperityChanged;

        public void AddCoin(int amount)
        {
            if (amount <= 0)
                return;

            coin += amount;
            OnCoinChanged?.Invoke(coin);
        }

        public bool SpendCoin(int amount)
        {
            if (amount <= 0)
                return false;

            if (coin < amount)
                return false;

            coin -= amount;
            OnCoinChanged?.Invoke(coin);
            return true;
        }

        public void AddProsperity(int amount)
        {
            if (amount <= 0)
                return;

            prosperity += amount;
            OnProsperityChanged?.Invoke(prosperity);
        }
    }
}
