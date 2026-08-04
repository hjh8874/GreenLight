using System;
using System.IO;
using System.Reflection;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.Gameplay.Economy;
using CityFlow.Save;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Tests
{
    public sealed class WeeklyEconomyLoopServiceTests
    {
        [Test]
        public void WeeklyEconomy_SaveLoadHarvest_PreservesBreakdownTotal()
        {
            string id = Guid.NewGuid().ToString("N");
            string savePath = Path.Combine(
                Path.GetTempPath(),
                $"greenlight_weekly_{id}.json");
            string backupPath = Path.Combine(
                Path.GetTempPath(),
                $"greenlight_weekly_{id}_backup.json");
            GameObject economyObject = new GameObject("economy-test");
            GameObject basicEconomyObject = new GameObject("basic-economy-test");
            GameObject weeklyObject = new GameObject("weekly-test");

            try
            {
                EconomyService economy =
                    economyObject.AddComponent<EconomyService>();
                BasicEconomySystem basicEconomy =
                    basicEconomyObject.AddComponent<BasicEconomySystem>();
                WeeklyEconomyLoopService weekly =
                    weeklyObject.AddComponent<WeeklyEconomyLoopService>();
                SetPrivateField(basicEconomy, "economyService", economy);
                SetPrivateField(weekly, "economySystem", basicEconomy);
                CityFlowServices services = new CityFlowServices(
                    new SimEventHub(),
                    null,
                    null,
                    economy: economy);
                SetPrivateField(weekly, "services", services);

                weekly.AddPendingCoins(500L, "vehicle arrival");
                JsonSaveRepository repository =
                    new JsonSaveRepository(savePath, backupPath);
                Assert.IsTrue(repository.TrySave(new GameSaveData
                {
                    SaveVersion = SaveConstants.CurrentSaveVersion,
                    WeeklySettlement = weekly.CreateSnapshot()
                }));
                Assert.IsTrue(repository.TryLoad(out GameSaveData loaded));

                weekly.RestoreSnapshot(loaded.WeeklySettlement);
                long breakdownTotal = 0L;
                foreach (long value in weekly.PendingBreakdown.Values)
                {
                    breakdownTotal += value;
                }

                Assert.AreEqual(weekly.PendingCoins, breakdownTotal);
                Assert.IsTrue(weekly.TryHarvestPendingCoins());
                Assert.AreEqual(0L, weekly.PendingCoins);
                Assert.AreEqual(0, weekly.PendingBreakdown.Count);
            }
            finally
            {
                DeleteTestPath(savePath);
                DeleteTestPath(backupPath);
                UnityEngine.Object.DestroyImmediate(weeklyObject);
                UnityEngine.Object.DestroyImmediate(basicEconomyObject);
                UnityEngine.Object.DestroyImmediate(economyObject);
            }
        }

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
                UnityEngine.Object.DestroyImmediate(weeklyObject);
                UnityEngine.Object.DestroyImmediate(economyObject);
            }
        }

        private static void SetPrivateField(
            object target,
            string fieldName,
            object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Missing field: {fieldName}");
            field.SetValue(target, value);
        }

        private static void DeleteTestPath(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }
}
