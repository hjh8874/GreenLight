using System.Collections.Generic;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Gameplay.Research;
using NUnit.Framework;
using UnityEngine;

// 기본 에디터 어셈블리. 실행: run_tests(group_names=[".*ResearchConditionTests.*"])
public class ResearchConditionTests
{
    static ResearchEntry Entry(string id, ResearchConditionKind kind, int threshold,
        TileType target = TileType.Empty) =>
        new ResearchEntry { researchId = id, displayName = id,
            conditionKind = kind, threshold = threshold, targetTileType = target };

    static ResearchConditionInputs Inputs(int arrivals = 0, int population = 0,
        int schools = 0, int hospitals = 0) =>
        new ResearchConditionInputs(arrivals, population,
            t => t == TileType.School ? schools : t == TileType.Hospital ? hospitals : 0);

    [Test]
    public void DailyArrivals_IsSatisfiedAtThreshold_HalfOpenBelow()
    {
        var e = Entry("a", ResearchConditionKind.DailyArrivals, 60);
        Assert.IsFalse(ResearchConditionEvaluator.IsSatisfied(e, Inputs(arrivals: 59)));
        Assert.IsTrue(ResearchConditionEvaluator.IsSatisfied(e, Inputs(arrivals: 60)), "경계 = 충족");
        Assert.IsTrue(ResearchConditionEvaluator.IsSatisfied(e, Inputs(arrivals: 200)));
    }

    [Test]
    public void Population_And_BuildingCount_ReadTheirOwnInputs()
    {
        var pop = Entry("p", ResearchConditionKind.Population, 20);
        Assert.IsFalse(ResearchConditionEvaluator.IsSatisfied(pop, Inputs(population: 19)));
        Assert.IsTrue(ResearchConditionEvaluator.IsSatisfied(pop, Inputs(population: 20)));

        var school = Entry("s", ResearchConditionKind.BuildingCount, 1, TileType.School);
        Assert.IsFalse(ResearchConditionEvaluator.IsSatisfied(school, Inputs(schools: 0)));
        Assert.IsTrue(ResearchConditionEvaluator.IsSatisfied(school, Inputs(schools: 1)));
        Assert.IsFalse(ResearchConditionEvaluator.IsSatisfied(school, Inputs(hospitals: 3)),
            "다른 타일 개수는 안 센다");
    }

    [Test]
    public void CurrentValue_ReturnsTheConditionSourceValue()
    {
        Assert.AreEqual(131, ResearchConditionEvaluator.CurrentValue(
            Entry("a", ResearchConditionKind.DailyArrivals, 150), Inputs(arrivals: 131)));
        Assert.AreEqual(84, ResearchConditionEvaluator.CurrentValue(
            Entry("p", ResearchConditionKind.Population, 80), Inputs(population: 84)));
    }

    [Test]
    public void Catalog_ValidEntries_WarnsAndSkips_EmptyAndDuplicateIds()
    {
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        var so = new UnityEditor.SerializedObject(catalog);
        var list = so.FindProperty("entries");
        list.arraySize = 3;
        // [0] 정상, [1] 빈 id, [2] 중복 id — managed reference 가 아니라 plain serializable 이므로
        // 자식 프로퍼티를 직접 채운다
        SetEntry(list.GetArrayElementAtIndex(0), "research_a");
        SetEntry(list.GetArrayElementAtIndex(1), "  ");
        SetEntry(list.GetArrayElementAtIndex(2), "research_a");
        so.ApplyModifiedPropertiesWithoutUndo();

        UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
            new System.Text.RegularExpressions.Regex("id"));
        UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
            new System.Text.RegularExpressions.Regex("중복"));
        List<ResearchEntry> valid = catalog.ValidEntries();
        Assert.AreEqual(1, valid.Count);
        Assert.AreEqual("research_a", valid[0].researchId);
    }

    static void SetEntry(UnityEditor.SerializedProperty p, string id)
    {
        p.FindPropertyRelative("researchId").stringValue = id;
        p.FindPropertyRelative("displayName").stringValue = id;
        p.FindPropertyRelative("threshold").intValue = 1;
    }
}
