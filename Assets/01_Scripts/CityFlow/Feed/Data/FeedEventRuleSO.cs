using System;
using UnityEngine;

namespace CityFlow.Feed
{
    [CreateAssetMenu(
        fileName = "FeedEventRule",
        menuName = "GreenLight/Feed/Event Rule")]
    public sealed class FeedEventRuleSO : ScriptableObject
    {
        [SerializeField] private CitizenFeedEventType eventType;
        [SerializeField] private bool ruleEnabled = true;
        [SerializeField, Range(0f, 1f)] private float baseChance = 0.8f;
        [SerializeField, Min(0f)] private float cooldownGameHours = 2f;

        [Header("Score")]
        [SerializeField, Range(0f, 100f)] private float baseScore = 40f;
        [SerializeField, Min(0f)] private float densityScoreMultiplier = 50f;
        [SerializeField, Min(0f)] private float stabilityDeltaScoreMultiplier = 0f;
        [SerializeField, Min(0f)] private float routeDistanceScoreMultiplier = 0f;
        [SerializeField, Min(0f)] private float vehicleCountScoreMultiplier = 0f;

        [Header("Allowed Authors")]
        [SerializeField] private CitizenFeedRole[] allowedRoles = Array.Empty<CitizenFeedRole>();

        public CitizenFeedEventType EventType => eventType;
        public bool RuleEnabled => ruleEnabled;
        public float BaseChance => baseChance;
        public float CooldownGameHours => cooldownGameHours;

        public float CalculateScore(in CitizenFeedContext context)
        {
            float stabilityDelta = Mathf.Abs(context.CurrentStability01 - context.PreviousStability01);
            return baseScore +
                   Mathf.Clamp01(context.Density01) * densityScoreMultiplier +
                   stabilityDelta * stabilityDeltaScoreMultiplier +
                   Mathf.Max(0f, context.RouteDistanceTiles) * routeDistanceScoreMultiplier +
                   Mathf.Max(0, context.ActiveVehicleCount) * vehicleCountScoreMultiplier;
        }

        public bool AllowsRole(CitizenFeedRole role)
        {
            if (allowedRoles == null || allowedRoles.Length == 0)
            {
                return true;
            }

            return Array.IndexOf(allowedRoles, role) >= 0;
        }

#if UNITY_EDITOR
        public void Configure(
            CitizenFeedEventType targetEventType,
            float targetChance,
            float targetCooldownHours,
            float targetBaseScore,
            float targetDensityMultiplier,
            CitizenFeedRole[] targetAllowedRoles,
            float targetStabilityDeltaMultiplier = 0f,
            float targetRouteDistanceMultiplier = 0f,
            float targetVehicleCountMultiplier = 0f)
        {
            eventType = targetEventType;
            ruleEnabled = true;
            baseChance = Mathf.Clamp01(targetChance);
            cooldownGameHours = Mathf.Max(0f, targetCooldownHours);
            baseScore = Mathf.Clamp(targetBaseScore, 0f, 100f);
            densityScoreMultiplier = Mathf.Max(0f, targetDensityMultiplier);
            stabilityDeltaScoreMultiplier = Mathf.Max(0f, targetStabilityDeltaMultiplier);
            routeDistanceScoreMultiplier = Mathf.Max(0f, targetRouteDistanceMultiplier);
            vehicleCountScoreMultiplier = Mathf.Max(0f, targetVehicleCountMultiplier);
            allowedRoles = targetAllowedRoles ?? Array.Empty<CitizenFeedRole>();
        }
#endif

        // Unity setup: Create one rule asset per supported feed event.
    }
}
