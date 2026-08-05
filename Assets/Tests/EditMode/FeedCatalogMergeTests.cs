using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using CityFlow.Feed;

namespace CityFlow.Sim.Tests
{
    /// <summary>
    /// 목록(FeedCatalogSO) 자체의 무결성을 검사한다.
    ///
    /// 병합 API(CitizenFeedService.MergeFrom)와 설치기는 Assembly-CSharp에 있어
    /// 이 어셈블리에서 참조할 수 없다(CityFlow.Sim.Tests는 Assembly-CSharp을
    /// 참조하지 못한다). 대신 **실제 회귀가 일어나는 지점**을 여기서 막는다 —
    /// 규칙·템플릿이 늘었는데 목록에 안 들어가거나 참조가 끊기면, 병합 API가
    /// 아무리 정확해도 그 이벤트는 조용히 무동작이 된다.
    /// </summary>
    public class FeedCatalogMergeTests
    {
        private const string GeneratedFolder = "Assets/05_ScriptableObjects/Feed";
        private const string CatalogPath = GeneratedFolder + "/FeedCatalog.asset";
        private const string PrefabPath =
            "Assets/02_Prefabs/Feed/CitizenFeedIntegration.prefab";

        private static FeedCatalogSO LoadCatalog() =>
            AssetDatabase.LoadAssetAtPath<FeedCatalogSO>(CatalogPath);

        /// <summary>
        /// 폐기 에셋(#164에서 제거된 Stability 계열)은 목록 대상이 아니다.
        /// 파일이 남아 있을 뿐이라 세면 목록과 개수가 어긋난다.
        /// </summary>
        private static int CountGenerated(string filter)
        {
            string[] guids = AssetDatabase.FindAssets(filter, new[] { GeneratedFolder });
            int count = 0;
            foreach (string guid in guids)
            {
                if (!AssetDatabase.GUIDToAssetPath(guid).Contains("_DEPRECATED")) count++;
            }

            return count;
        }

        [Test]
        public void Catalog_Exists()
        {
            Assert.IsNotNull(
                LoadCatalog(),
                "목록이 없다. Tools > GreenLight > Feed > Create or Upgrade Feed Data 실행 필요");
        }

        [Test]
        public void Catalog_CoversEveryGeneratedRule()
        {
            FeedCatalogSO catalog = LoadCatalog();
            if (catalog == null) Assert.Ignore("목록 없음 — Catalog_Exists가 따로 잡는다");

            Assert.AreEqual(
                CountGenerated("t:FeedEventRuleSO"),
                catalog.Rules.Length,
                "생성된 규칙 에셋이 전부 목록에 있어야 한다. " +
                "빠지면 그 이벤트는 통합 후에도 조용히 무동작이다.");
        }

        [Test]
        public void Catalog_CoversEveryGeneratedTemplateCollection()
        {
            FeedCatalogSO catalog = LoadCatalog();
            if (catalog == null) Assert.Ignore("목록 없음");

            Assert.AreEqual(
                CountGenerated("t:FeedTemplateCollectionSO"),
                catalog.TemplateCollections.Length);
        }

        [Test]
        public void Catalog_HasNoBrokenReferences()
        {
            FeedCatalogSO catalog = LoadCatalog();
            if (catalog == null) Assert.Ignore("목록 없음");

            Assert.IsNotNull(catalog.Settings, "설정 참조가 끊겼다");

            for (int i = 0; i < catalog.Rules.Length; i++)
            {
                Assert.IsNotNull(catalog.Rules[i], $"규칙[{i}] 참조가 끊겼다");
            }

            for (int i = 0; i < catalog.TemplateCollections.Length; i++)
            {
                Assert.IsNotNull(
                    catalog.TemplateCollections[i], $"템플릿[{i}] 참조가 끊겼다");
            }

            for (int i = 0; i < catalog.Authors.Length; i++)
            {
                Assert.IsNotNull(catalog.Authors[i], $"작성자[{i}] 참조가 끊겼다");
            }
        }

        [Test]
        public void Catalog_RuleAndTemplateEventTypesMatch()
        {
            FeedCatalogSO catalog = LoadCatalog();
            if (catalog == null) Assert.Ignore("목록 없음");

            var ruleTypes = new System.Collections.Generic.HashSet<CitizenFeedEventType>();
            foreach (FeedEventRuleSO rule in catalog.Rules)
            {
                if (rule != null) ruleTypes.Add(rule.EventType);
            }

            foreach (FeedTemplateCollectionSO collection in catalog.TemplateCollections)
            {
                if (collection == null) continue;
                Assert.IsTrue(
                    ruleTypes.Contains(collection.EventType),
                    $"{collection.EventType} 템플릿은 있는데 규칙이 없다 — 글이 안 나간다");
            }
        }

        [Test]
        public void IntegrationPrefab_ExistsWithCatalogWired()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(
                prefab,
                "통합 프리팹이 없다. 통합 담당자가 배치할 물건이므로 반드시 있어야 한다.");

            // 설치기 타입은 Assembly-CSharp이라 이 어셈블리에서 참조할 수 없다.
            // 프리팹 파일이 목록 에셋의 GUID를 실제로 담고 있는지로 확인한다.
            string catalogGuid = AssetDatabase.AssetPathToGUID(CatalogPath);
            Assert.IsFalse(
                string.IsNullOrEmpty(catalogGuid), "목록 에셋 GUID를 얻지 못했다");

            string prefabText = System.IO.File.ReadAllText(PrefabPath);
            Assert.IsTrue(
                prefabText.Contains(catalogGuid),
                "프리팹에 목록 참조가 없으면 배치해도 아무 일도 일어나지 않는다");
        }
    }
}
