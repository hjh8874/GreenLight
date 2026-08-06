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
        /// <summary>
        /// 이미 만들어진 프로필에 신규 이벤트 지원을 더한다. 기존 항목은 건드리지 않는다.
        /// 제너레이터의 LoadOrCreate는 에셋이 있으면 Configure를 다시 부르지 않으므로,
        /// 이벤트가 추가돼도 옛 에셋의 preferredEvents는 그대로 남는다. 그러면
        /// Supports()가 false를 돌려 신규 이벤트 글이 한 건도 생성되지 않는다.
        /// </summary>
        /// <returns>실제로 추가된 항목이 있으면 true.</returns>
        public bool AddSupportedEvents(CitizenFeedEventType[] additionalEvents)
        {
            // 비어 있으면 Supports()가 이미 전부 허용이다. 채우면 오히려 좁아진다.
            if (additionalEvents == null ||
                preferredEvents == null ||
                preferredEvents.Length == 0)
            {
                return false;
            }

            var merged = new List<CitizenFeedEventType>(preferredEvents);
            bool changed = false;
            foreach (CitizenFeedEventType candidate in additionalEvents)
            {
                if (merged.Contains(candidate)) continue;
                merged.Add(candidate);
                changed = true;
            }

            if (!changed) return false;
            preferredEvents = merged.ToArray();
            return true;
        }

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
