using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Content
{
    [Serializable]
    public sealed class DistanceRewardTier
    {
        [SerializeField, Min(0f)] private float minimumDistanceTiles;
        [SerializeField, Min(1f)] private float rewardMultiplier = 1f;

        public float MinimumDistanceTiles => minimumDistanceTiles;
        public float RewardMultiplier => rewardMultiplier;

        public DistanceRewardTier(float minimumDistanceTiles, float rewardMultiplier)
        {
            this.minimumDistanceTiles = minimumDistanceTiles;
            this.rewardMultiplier = rewardMultiplier;
        }

        public void Validate()
        {
            minimumDistanceTiles = Mathf.Max(0f, minimumDistanceTiles);
            rewardMultiplier = Mathf.Max(1f, rewardMultiplier);
        }
    }

    [CreateAssetMenu(
        fileName = "DistanceRewardConfig",
        menuName = "CityFlow/Content/Distance Reward Config")]
    public sealed class DistanceRewardConfigSO : ScriptableObject
    {
        [Header("Distance Reward Tiers")]
        [SerializeField] private List<DistanceRewardTier> rewardTiers =
            new List<DistanceRewardTier>
            {
                new DistanceRewardTier(0f, 1f),
                new DistanceRewardTier(5f, 1.1f),
                new DistanceRewardTier(6f, 1.2f),
                new DistanceRewardTier(7f, 1.3f),
                new DistanceRewardTier(8f, 1.4f),
                new DistanceRewardTier(9f, 1.5f),
                new DistanceRewardTier(10f, 1.6f),
                new DistanceRewardTier(11f, 1.7f),
                new DistanceRewardTier(12f, 1.8f),
                new DistanceRewardTier(13f, 1.9f),
                new DistanceRewardTier(14f, 2f)
            };

        public IReadOnlyList<DistanceRewardTier> RewardTiers => rewardTiers;

        public float CalculateMultiplier(float distanceTiles)
        {
            float sanitizedDistance = Mathf.Max(0f, distanceTiles);
            float selectedThreshold = float.MinValue;
            float selectedMultiplier = 1f;

            if (rewardTiers == null)
            {
                return selectedMultiplier;
            }

            for (int i = 0; i < rewardTiers.Count; i++)
            {
                DistanceRewardTier tier = rewardTiers[i];
                if (tier == null ||
                    tier.MinimumDistanceTiles > sanitizedDistance ||
                    tier.MinimumDistanceTiles < selectedThreshold)
                {
                    continue;
                }

                selectedThreshold = tier.MinimumDistanceTiles;
                selectedMultiplier = Mathf.Max(1f, tier.RewardMultiplier);
            }

            return selectedMultiplier;
        }

        public double CalculateBonusAmount(long baseReward, float distanceTiles)
        {
            if (baseReward <= 0L)
            {
                return 0.0;
            }

            double bonusMultiplier = CalculateMultiplier(distanceTiles) - 1f;
            return baseReward * bonusMultiplier;
        }

        private void OnValidate()
        {
            if (rewardTiers == null)
            {
                rewardTiers = new List<DistanceRewardTier>();
                return;
            }

            for (int i = 0; i < rewardTiers.Count; i++)
            {
                rewardTiers[i]?.Validate();
            }
        }

        // Unity setup: Create this asset and assign it to DistanceRewardService.
    }
}
