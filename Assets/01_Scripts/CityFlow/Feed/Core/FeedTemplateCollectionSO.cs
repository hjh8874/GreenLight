using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Feed
{
    [Serializable]
    public sealed class CitizenFeedTemplateEntry
    {
        [SerializeField] private string templateId = "Template";
        [SerializeField, TextArea(2, 5)] private string text = "{Location}에 새로운 소식이 있습니다.";
        [SerializeField] private CitizenFeedTone tone = CitizenFeedTone.Neutral;
        [SerializeField] private CitizenFeedCategory category = CitizenFeedCategory.TrafficReport;
        [SerializeField, Min(0f)] private float weight = 1f;
        [SerializeField] private CitizenFeedRole[] allowedRoles = Array.Empty<CitizenFeedRole>();
        [SerializeField] private CitizenFeedPersonality[] allowedPersonalities = Array.Empty<CitizenFeedPersonality>();
        [SerializeField] private CitizenFeedTimePeriod[] allowedTimePeriods = Array.Empty<CitizenFeedTimePeriod>();
        // "제가 저번에 말한 그곳" 류의 이어받는 문구. 앞선 불만을 전제하므로
        // 장부에서 작성자가 나온 경우에만 쓸 수 있다. 기존 에셋은 false로 들어와
        // 동작이 바뀌지 않는다.
        [SerializeField] private bool followUpOnly;

        public string TemplateId => templateId;
        public string Text => text;
        public CitizenFeedTone Tone => tone;
        public CitizenFeedCategory Category => category;
        public float Weight => weight;
        public bool FollowUpOnly => followUpOnly;

        public bool Allows(FeedAuthorProfileSO author, CitizenFeedTimePeriod timePeriod)
        {
            if (author == null)
            {
                return false;
            }

            bool roleAllowed = allowedRoles == null || allowedRoles.Length == 0 ||
                               Array.IndexOf(allowedRoles, author.Role) >= 0;
            bool personalityAllowed = allowedPersonalities == null || allowedPersonalities.Length == 0 ||
                                      Array.IndexOf(allowedPersonalities, author.Personality) >= 0;
            bool timeAllowed = allowedTimePeriods == null || allowedTimePeriods.Length == 0 ||
                               Array.IndexOf(allowedTimePeriods, timePeriod) >= 0;
            return roleAllowed && personalityAllowed && timeAllowed;
        }

#if UNITY_EDITOR
        public void Configure(
            string targetTemplateId,
            string targetText,
            CitizenFeedTone targetTone,
            float targetWeight,
            CitizenFeedRole[] targetAllowedRoles,
            CitizenFeedPersonality[] targetAllowedPersonalities = null,
            CitizenFeedCategory targetCategory = CitizenFeedCategory.TrafficReport,
            CitizenFeedTimePeriod[] targetAllowedTimePeriods = null,
            bool targetFollowUpOnly = false)
        {
            templateId = targetTemplateId;
            text = targetText;
            tone = targetTone;
            category = targetCategory;
            weight = Mathf.Max(0f, targetWeight);
            allowedRoles = targetAllowedRoles ?? Array.Empty<CitizenFeedRole>();
            allowedPersonalities = targetAllowedPersonalities ?? Array.Empty<CitizenFeedPersonality>();
            allowedTimePeriods = targetAllowedTimePeriods ?? Array.Empty<CitizenFeedTimePeriod>();
            followUpOnly = targetFollowUpOnly;
        }
#endif
    }

    [CreateAssetMenu(
        fileName = "FeedTemplateCollection",
        menuName = "GreenLight/Feed/Template Collection")]
    public sealed class FeedTemplateCollectionSO : ScriptableObject
    {
        [SerializeField] private CitizenFeedEventType eventType;
        [SerializeField] private List<CitizenFeedTemplateEntry> templates = new List<CitizenFeedTemplateEntry>();

        public CitizenFeedEventType EventType => eventType;
        public IReadOnlyList<CitizenFeedTemplateEntry> Templates => templates;

#if UNITY_EDITOR
        public void Configure(
            CitizenFeedEventType targetEventType,
            IEnumerable<CitizenFeedTemplateEntry> targetTemplates)
        {
            eventType = targetEventType;
            templates = targetTemplates != null
                ? new List<CitizenFeedTemplateEntry>(targetTemplates)
                : new List<CitizenFeedTemplateEntry>();
        }

        public void AddMissingTemplates(IEnumerable<CitizenFeedTemplateEntry> candidates)
        {
            if (candidates == null)
            {
                return;
            }

            templates ??= new List<CitizenFeedTemplateEntry>();
            foreach (CitizenFeedTemplateEntry candidate in candidates)
            {
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.TemplateId))
                {
                    continue;
                }

                bool exists = templates.Exists(entry =>
                    entry != null && entry.TemplateId == candidate.TemplateId);
                if (!exists)
                {
                    templates.Add(candidate);
                }
            }
        }
#endif

        // Unity setup: Keep related sentences together in one collection per event type.
    }
}
