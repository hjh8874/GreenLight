using System.Reflection;
using CityFlow.Content;
using CityFlow.Gameplay.Economy;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Tests
{
    public sealed class WeeklyEconomyLoopServiceTests
    {
        [Test]
        public void WeeklyEconomy_ChunkedIncomeAddsPendingBreakdownOnce()
        {
            GameObject economyObject = new GameObject("economy-test");
            GameObject weeklyObject = new GameObject("weekly-test");
            try
            {
                BasicEconomySystem economy =
                    economyObject.AddComponent<BasicEconomySystem>();
                WeeklyEconomyLoopService weekly =
                    weeklyObject.AddComponent<WeeklyEconomyLoopService>();
                FieldInfo economyField = typeof(WeeklyEconomyLoopService)
                    .GetField(
                        "economySystem",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                economyField.SetValue(weekly, economy);

                long amount = (long)int.MaxValue + 123L;
                weekly.AddPendingCoins(amount, "vehicle arrival");

                Assert.AreEqual(amount, weekly.PendingCoins);
                Assert.AreEqual(
                    amount,
                    weekly.PendingBreakdown["vehicle arrival"]);
            }
            finally
            {
                Object.DestroyImmediate(weeklyObject);
                Object.DestroyImmediate(economyObject);
            }
        }
    }
}
