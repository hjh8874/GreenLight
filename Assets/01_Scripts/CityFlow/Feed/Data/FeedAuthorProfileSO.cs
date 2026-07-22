using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Feed
{
    [CreateAssetMenu(
        fileName = "FeedAuthorProfile",
        menuName = "GreenLight/Feed/Author Profile")]
    public sealed class FeedAuthorProfileSO : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string displayName = "Citizen";
        [SerializeField] private string avatarInitial = "C";
        [SerializeField] private string roleLabel = "Citizen";
        [SerializeField] private Color accentColor = new Color(0.2f, 0.78f, 0.55f, 1f);
        [SerializeField] private CitizenFeedRole role;
        [SerializeField] private CitizenFeedPersonality personality;

        [Header("Posting")]
        [SerializeField, Min(0f)] private float postingWeight = 1f;
        [SerializeField] private CitizenFeedEventType[] preferredEvents = Array.Empty<CitizenFeedEventType>();

        [Header("Daily Routine")]
        [SerializeField, Range(0, 23)] private int activeFromHour;
        [SerializeField, Range(0, 24)] private int activeUntilHour = 24;

        [Header("Tone Affinity")]
        [SerializeField, Range(0f, 2f)] private float complaintTendency = 1f;
        [SerializeField, Range(0f, 2f)] private float humorTendency = 1f;
        [SerializeField, Range(0f, 2f)] private float praiseTendency = 1f;

        [Header("Writing Habits")]
        [SerializeField] private string[] emojiSuffixes = Array.Empty<string>();
        [SerializeField] private string[] commonHashtags = Array.Empty<string>();

        public string DisplayName => displayName;
        public string AvatarInitial => avatarInitial;
        public string RoleLabel => roleLabel;
        public Color AccentColor => accentColor;
        public CitizenFeedRole Role => role;
        public CitizenFeedPersonality Personality => personality;
        public float PostingWeight => postingWeight;
        public IReadOnlyList<string> EmojiSuffixes => emojiSuffixes;
        public IReadOnlyList<string> CommonHashtags => commonHashtags;

        public bool Supports(CitizenFeedEventType eventType)
        {
            if (preferredEvents == null || preferredEvents.Length == 0)
            {
                return true;
            }

            return Array.IndexOf(preferredEvents, eventType) >= 0;
        }

        public bool IsActiveAtHour(int gameHour)
        {
            int hour = Mathf.Clamp(gameHour, 0, 23);
            if (activeFromHour == activeUntilHour || activeUntilHour == 24 && activeFromHour == 0)
            {
                return true;
            }

            if (activeFromHour < activeUntilHour)
            {
                return hour >= activeFromHour && hour < activeUntilHour;
            }

            return hour >= activeFromHour || hour < activeUntilHour;
        }

        public float GetToneWeight(CitizenFeedTone tone)
        {
            switch (tone)
            {
                case CitizenFeedTone.Complaint:
                case CitizenFeedTone.Cynical:
                    return complaintTendency;
                case CitizenFeedTone.Humor:
                    return humorTendency;
                case CitizenFeedTone.Praise:
                    return praiseTendency;
                default:
                    return 1f;
            }
        }

#if UNITY_EDITOR
        public void Configure(
            string targetDisplayName,
            string targetAvatarInitial,
            string targetRoleLabel,
            Color targetAccentColor,
            CitizenFeedRole targetRole,
            CitizenFeedPersonality targetPersonality,
            float targetPostingWeight,
            CitizenFeedEventType[] targetPreferredEvents,
            int targetActiveFromHour = 0,
            int targetActiveUntilHour = 24,
            float targetComplaintTendency = 1f,
            float targetHumorTendency = 1f,
            float targetPraiseTendency = 1f,
            string[] targetEmojiSuffixes = null,
            string[] targetCommonHashtags = null)
        {
            displayName = targetDisplayName;
            avatarInitial = targetAvatarInitial;
            roleLabel = targetRoleLabel;
            accentColor = targetAccentColor;
            role = targetRole;
            personality = targetPersonality;
            postingWeight = Mathf.Max(0f, targetPostingWeight);
            preferredEvents = targetPreferredEvents ?? Array.Empty<CitizenFeedEventType>();
            activeFromHour = Mathf.Clamp(targetActiveFromHour, 0, 23);
            activeUntilHour = Mathf.Clamp(targetActiveUntilHour, 0, 24);
            complaintTendency = Mathf.Max(0f, targetComplaintTendency);
            humorTendency = Mathf.Max(0f, targetHumorTendency);
            praiseTendency = Mathf.Max(0f, targetPraiseTendency);
            emojiSuffixes = targetEmojiSuffixes ?? Array.Empty<string>();
            commonHashtags = targetCommonHashtags ?? Array.Empty<string>();
        }
#endif

        // Unity setup: Create one profile per recurring Green SNS citizen.
    }
}
