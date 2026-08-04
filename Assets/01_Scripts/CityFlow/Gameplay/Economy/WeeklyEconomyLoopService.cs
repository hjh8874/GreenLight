using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Gameplay.Economy
{
    public sealed class WeeklyEconomyLoopService :
        MonoBehaviour,
        ICityFlowServiceConsumer,
        IWeeklyEconomyService,
        IWeeklySettlementSaveSource
    {
        [Header("Dependencies")]
        [SerializeField] private BasicEconomySystem economySystem;
        [SerializeField] private EconomyConfigSO economyConfig;

        private CityFlowServices services;
        private IGameCalendarService calendar;
        private int daysIntoCurrentWeek;
        private long lastProcessedTotalDays = -1L;
        private bool awaitingLegacyCalendarBaseline;
        private readonly Dictionary<string, long> pendingBreakdown =
            new Dictionary<string, long>();

        public long PendingCoins => economySystem?.WeeklyAccumulatedCoin ?? 0L;
        public IReadOnlyDictionary<string, long> PendingBreakdown => pendingBreakdown;
        public int DaysIntoCurrentWeek => daysIntoCurrentWeek;
        public int SettlementDays => Mathf.Max(1, economyConfig?.SettlementDays ?? 7);

        public event Action<long> PendingCoinsChanged;
        public event Action<WeeklySettlementCompletedEvent> SettlementCompleted;

        public void Initialize(CityFlowServices services)
        {
            this.services = services;

            if (!ValidateDependencies())
            {
                return;
            }

            services.Events.Arrival += OnArrival;
            services.GameCalendarRegistered += OnGameCalendarRegistered;

            if (services.Save != null)
            {
                services.Save.RestoreCompleted += OnRestoreCompleted;
            }

            if (services.GameCalendar != null)
            {
                BindCalendar(services.GameCalendar);
            }

            services.RegisterWeeklyEconomy(this);
            PublishPendingCoins();

            Debug.Log("[WeeklyEconomyLoopService] Manual coin harvest initialized.");
        }

        private void OnDestroy()
        {
            if (services != null)
            {
                services.Events.Arrival -= OnArrival;
                services.GameCalendarRegistered -= OnGameCalendarRegistered;

                if (services.Save != null)
                {
                    services.Save.RestoreCompleted -= OnRestoreCompleted;
                }
            }

            if (calendar != null)
            {
                calendar.DayChanged -= OnCalendarDayChanged;
            }
        }

        public WeeklySettlementSaveData CreateSnapshot()
        {
            return new WeeklySettlementSaveData
            {
                PendingCoins = PendingCoins,
                DaysIntoCurrentWeek = daysIntoCurrentWeek,
                LastProcessedTotalDays = lastProcessedTotalDays,
                HasCycleProgress = true
            };
        }

        public void RestoreSnapshot(WeeklySettlementSaveData snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            economySystem.RestoreWeeklyAccumulatedCoin(snapshot.PendingCoins);
            pendingBreakdown.Clear();
            long restoredPendingCoins = PendingCoins;
            if (restoredPendingCoins > 0L)
            {
                pendingBreakdown["previous accrual"] = restoredPendingCoins;
            }


            if (snapshot.HasCycleProgress)
            {
                daysIntoCurrentWeek = Mathf.Clamp(
                    snapshot.DaysIntoCurrentWeek,
                    0,
                    SettlementDays - 1);
                lastProcessedTotalDays = Math.Max(
                    -1L,
                    snapshot.LastProcessedTotalDays);
                awaitingLegacyCalendarBaseline = false;
            }
            else
            {
                daysIntoCurrentWeek = 0;
                lastProcessedTotalDays = -1L;
                awaitingLegacyCalendarBaseline = true;
            }

            PublishPendingCoins();
        }

        public void AddPendingCoins(
            long amount,
            string reason = "weekly income")
        {
            AddPendingCoinsInternal(amount, reason);
        }

        private void AddPendingCoinsInternal(
            long amount,
            string reason)
        {
            if (amount <= 0L)
            {
                return;
            }

            long availableCapacity = long.MaxValue - PendingCoins;
            long remaining = Math.Min(amount, availableCapacity);
            long acceptedAmount = remaining;

            if (remaining < amount)
            {
                Debug.LogWarning(
                    "[WeeklyEconomyLoopService] Pending income reached the long integer limit.",
                    this);
            }

            while (remaining > 0L)
            {
                int chunk = (int)Math.Min(remaining, int.MaxValue);
                economySystem.AddWeeklyIncome(chunk, reason);
                remaining -= chunk;
            }

            if (acceptedAmount > 0L)
            {
                string safeReason = string.IsNullOrEmpty(reason)
                    ? "weekly income"
                    : reason;
                long current = pendingBreakdown.TryGetValue(
                    safeReason,
                    out long existing)
                    ? existing
                    : 0L;
                pendingBreakdown[safeReason] = current >
                    long.MaxValue - acceptedAmount
                    ? long.MaxValue
                    : current + acceptedAmount;
            }


            PublishPendingCoins();
        }

        public bool TryHarvestPendingCoins()
        {
            if (!TryClaimPendingCoins(true, out long amount))
            {
                return false;
            }

            bool saved = services?.Save?.Save() == true;

            if (!saved)
            {
                Debug.LogWarning(
                    "[WeeklyEconomyLoopService] Coin harvest succeeded, but the single save file could not be updated.",
                    this);
            }

            Debug.Log(
                $"[WeeklyEconomyLoopService] Pending coins harvested. " +
                $"Amount: {amount}, Balance: {services?.Economy?.Coins ?? 0L}.");

            return true;
        }

        private void OnArrival(ArrivalEvent arrival)
        {
            if (arrival.Coins <= 0 || services?.Save?.IsRestoring == true)
            {
                return;
            }

            AddPendingCoins(arrival.Coins, "vehicle arrival");
        }

        private void OnGameCalendarRegistered(IGameCalendarService gameCalendar)
        {
            BindCalendar(gameCalendar);
        }

        private void BindCalendar(IGameCalendarService gameCalendar)
        {
            if (calendar == gameCalendar)
            {
                return;
            }

            if (calendar != null)
            {
                calendar.DayChanged -= OnCalendarDayChanged;
            }

            calendar = gameCalendar;

            if (calendar == null)
            {
                return;
            }

            calendar.DayChanged += OnCalendarDayChanged;

            if (lastProcessedTotalDays < 0L && services?.Save?.IsRestoring != true)
            {
                lastProcessedTotalDays = calendar.TotalDays;
            }
        }

        private void OnCalendarDayChanged(int _)
        {
            if (services?.Save?.IsRestoring == true)
            {
                CaptureLegacyCalendarBaseline();
                return;
            }

            ProcessCalendarProgress();
        }

        private void OnRestoreCompleted(RestoreCompletedEvent _)
        {
            CaptureLegacyCalendarBaseline();

            PublishPendingCoins();
        }

        private bool TryClaimPendingCoins(
            bool publishHarvestResult,
            out long claimedAmount)
        {
            claimedAmount = PendingCoins;

            if (claimedAmount <= 0L)
            {
                return false;
            }

            if (!economySystem.ClaimWeeklySettlement())
            {
                claimedAmount = 0L;
                PublishPendingCoins();
                return false;
            }

            pendingBreakdown.Clear();

            PublishPendingCoins();

            if (publishHarvestResult)
            {
                SettlementCompleted?.Invoke(
                    new WeeklySettlementCompletedEvent(
                        claimedAmount,
                        services?.Economy?.Coins ?? 0L,
                        SettlementDays));
            }

            return true;
        }

        private void CaptureLegacyCalendarBaseline()
        {
            if (!awaitingLegacyCalendarBaseline || calendar == null)
            {
                return;
            }

            lastProcessedTotalDays = Math.Max(0L, calendar.TotalDays);
            daysIntoCurrentWeek = (int)(
                lastProcessedTotalDays % SettlementDays);
            awaitingLegacyCalendarBaseline = false;

            Debug.Log(
                "[WeeklyEconomyLoopService] Legacy weekly save aligned to the restored calendar.");
        }

        private bool ProcessCalendarProgress()
        {
            if (calendar == null)
            {
                return false;
            }

            long currentTotalDays = Math.Max(0L, calendar.TotalDays);

            if (lastProcessedTotalDays < 0L)
            {
                lastProcessedTotalDays = currentTotalDays;
                return true;
            }

            long elapsedDays = currentTotalDays - lastProcessedTotalDays;

            if (elapsedDays <= 0L)
            {
                bool changed = lastProcessedTotalDays != currentTotalDays;
                lastProcessedTotalDays = currentTotalDays;
                return changed;
            }

            long progressedDays = daysIntoCurrentWeek + elapsedDays;
            daysIntoCurrentWeek = (int)(progressedDays % SettlementDays);
            lastProcessedTotalDays = currentTotalDays;

            return true;
        }

        private bool ValidateDependencies()
        {
            if (economySystem == null)
            {
                Debug.LogError(
                    "[WeeklyEconomyLoopService] BasicEconomySystem is not assigned.",
                    this);
                return false;
            }

            if (economyConfig == null)
            {
                Debug.LogError(
                    "[WeeklyEconomyLoopService] EconomyConfigSO is not assigned.",
                    this);
                return false;
            }

            return true;
        }

        private void PublishPendingCoins()
        {
            PendingCoinsChanged?.Invoke(PendingCoins);
        }

        // Unity setup: Add this component beside BasicEconomySystem and assign the same EconomyConfigSO asset.
    }
}
