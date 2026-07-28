using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Gameplay.Research
{
    [DisallowMultipleComponent]
    public sealed class ResearchUnlockService :
        MonoBehaviour,
        ICityFlowServiceConsumer,
        IResearchUnlockService,
        IResearchSaveSource
    {
        [SerializeField]
        private string playModeTestResearchId = "research_building_mall";

        private readonly HashSet<string> unlockedResearchIds =
            new(StringComparer.Ordinal);
        private readonly HashSet<string> purchasedUpgradeIds =
            new(StringComparer.Ordinal);
        private bool initialized;

        public int UnlockedCount => unlockedResearchIds.Count;

        public event Action<string> ResearchUnlocked;
        public event Action ResearchStateRestored;

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled || initialized)
            {
                return;
            }

            if (services == null)
            {
                Debug.LogWarning(
                    "[ResearchUnlockService] Research registration failed.",
                    this);
                return;
            }

            initialized = true;
            if (!services.RegisterResearch(this))
            {
                initialized = false;
                Debug.LogWarning(
                    "[ResearchUnlockService] Research registration failed.",
                    this);
                return;
            }

            Debug.Log("[ResearchUnlockService] Registered.", this);
        }

        public bool IsUnlocked(string researchId)
        {
            string normalizedId = NormalizeId(researchId);
            return normalizedId.Length > 0 &&
                   unlockedResearchIds.Contains(normalizedId);
        }

        public bool TryUnlock(string researchId)
        {
            string normalizedId = NormalizeId(researchId);
            if (!initialized || normalizedId.Length == 0 ||
                !unlockedResearchIds.Add(normalizedId))
            {
                return false;
            }

            Debug.Log(
                $"[ResearchUnlockService] Unlocked {normalizedId}.",
                this);
            ResearchUnlocked?.Invoke(normalizedId);
            return true;
        }

        public ResearchSaveData CreateSnapshot()
        {
            return new ResearchSaveData
            {
                UnlockedResearchIds = CreateSortedSnapshot(
                    unlockedResearchIds),
                PurchasedUpgradeIds = CreateSortedSnapshot(
                    purchasedUpgradeIds)
            };
        }

        public void RestoreSnapshot(ResearchSaveData snapshot)
        {
            RestoreSet(
                unlockedResearchIds,
                snapshot?.UnlockedResearchIds);
            RestoreSet(
                purchasedUpgradeIds,
                snapshot?.PurchasedUpgradeIds);
            ResearchStateRestored?.Invoke();
        }

#if UNITY_EDITOR
        [ContextMenu("Play Mode Test/Unlock Selected Research")]
        private void UnlockSelectedResearchForPlayModeTest()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning(
                    "[ResearchUnlockService] Enter Play Mode first.",
                    this);
                return;
            }

            if (!TryUnlock(playModeTestResearchId))
            {
                Debug.LogWarning(
                    "[ResearchUnlockService] The selected research is " +
                    "invalid or already unlocked.",
                    this);
            }
        }

        private void OnValidate()
        {
            playModeTestResearchId = NormalizeId(
                playModeTestResearchId);
        }
#endif

        private static string NormalizeId(string value) =>
            value?.Trim() ?? string.Empty;

        private static string[] CreateSortedSnapshot(
            HashSet<string> source)
        {
            var result = new string[source.Count];
            source.CopyTo(result);
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }

        private static void RestoreSet(
            HashSet<string> destination,
            string[] source)
        {
            destination.Clear();
            if (source == null)
            {
                return;
            }

            for (int index = 0; index < source.Length; index++)
            {
                string normalizedId = NormalizeId(source[index]);
                if (normalizedId.Length > 0)
                {
                    destination.Add(normalizedId);
                }
            }
        }
    }
}

// Unity setup: This component is prewired in SpecialBuildingSystem.prefab.
