using System;
using UnityEngine;

namespace CityFlow.Feed
{
    /// <summary>
    /// 피드 데이터 한 벌을 통째로 담는 목록. 씬의 직렬화 배열 대신 이 에셋 하나를
    /// 참조하게 해서, 규칙·템플릿이 늘어나도 씬을 다시 굽지 않게 하는 것이 목적이다.
    ///
    /// 왜 필요한가: 씬은 각 에셋을 GUID로 하나씩 참조한다. 새 규칙을 추가하거나
    /// 에셋을 재생성하면 씬의 배열에는 들어가지 않거나 참조가 끊긴다.
    /// 실제로 이번 작업 중 템플릿 에셋을 재생성하자 씬의 신규 5종 참조가 전부
    /// null이 되어 해당 이벤트가 조용히 무동작이 됐다.
    /// </summary>
    [CreateAssetMenu(
        fileName = "FeedCatalog",
        menuName = "GreenLight/Feed/Catalog")]
    public sealed class FeedCatalogSO : ScriptableObject
    {
        /// <summary>
        /// 내용이 바뀔 때마다 올린다. 설치기가 로그에 남겨 "어느 판이 물렸는지"를
        /// 추적할 수 있게 하는 용도다. 동작을 가르지는 않는다.
        /// </summary>
        [SerializeField, Min(1)] private int catalogVersion = 1;
        [SerializeField] private FeedSystemSettingsSO settings;
        [SerializeField] private FeedEventRuleSO[] rules = Array.Empty<FeedEventRuleSO>();
        [SerializeField] private FeedAuthorProfileSO[] authors = Array.Empty<FeedAuthorProfileSO>();
        [SerializeField]
        private FeedTemplateCollectionSO[] templateCollections =
            Array.Empty<FeedTemplateCollectionSO>();

        public int CatalogVersion => catalogVersion;
        public FeedSystemSettingsSO Settings => settings;
        public FeedEventRuleSO[] Rules => rules;
        public FeedAuthorProfileSO[] Authors => authors;
        public FeedTemplateCollectionSO[] TemplateCollections => templateCollections;

#if UNITY_EDITOR
        public void Configure(
            int targetVersion,
            FeedSystemSettingsSO targetSettings,
            FeedEventRuleSO[] targetRules,
            FeedAuthorProfileSO[] targetAuthors,
            FeedTemplateCollectionSO[] targetTemplateCollections)
        {
            catalogVersion = Mathf.Max(1, targetVersion);
            settings = targetSettings;
            rules = targetRules ?? Array.Empty<FeedEventRuleSO>();
            authors = targetAuthors ?? Array.Empty<FeedAuthorProfileSO>();
            templateCollections =
                targetTemplateCollections ?? Array.Empty<FeedTemplateCollectionSO>();
        }
#endif

        // Unity setup: Tools > GreenLight > Feed > Create or Upgrade Feed Data 가 갱신한다.
    }
}
