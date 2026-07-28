using System;
using UnityEngine;

namespace CityFlow.Contracts
{
    public readonly struct HappinessEffectDescriptor
    {
        public HappinessEffectDescriptor(
            string sourceId,
            string effectKey,
            string buildingId,
            Vector2Int anchor)
        {
            SourceId = sourceId ?? string.Empty;
            EffectKey = effectKey ?? string.Empty;
            BuildingId = buildingId ?? string.Empty;
            Anchor = anchor;
        }

        public string SourceId { get; }
        public string EffectKey { get; }
        public string BuildingId { get; }
        public Vector2Int Anchor { get; }
    }

    public readonly struct HappinessEffectChangedEvent
    {
        public HappinessEffectChangedEvent(
            HappinessEffectDescriptor effect,
            bool isActive)
        {
            Effect = effect;
            IsActive = isActive;
        }

        public HappinessEffectDescriptor Effect { get; }
        public bool IsActive { get; }
    }

    public interface IHappinessEffectSource
    {
        event Action<HappinessEffectChangedEvent> HappinessEffectChanged;

        HappinessEffectDescriptor[] CreateActiveHappinessEffectSnapshot();
    }
}
