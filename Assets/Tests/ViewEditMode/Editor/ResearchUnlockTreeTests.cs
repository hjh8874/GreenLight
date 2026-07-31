using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Gameplay.Research;
using NUnit.Framework;
using UnityEngine;

public class ResearchUnlockTreeTests
{
    [Test]
    public void PrerequisiteLocked_MakesSatisfiedResearchNotReadyOrUnlockable()
    {
        var owner = new GameObject("research");
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        try
        {
            ConfigureCatalog(catalog,
                ("root", "", ResearchConditionKind.Population, 1),
                ("child", "root", ResearchConditionKind.Population, 1));
            var service = owner.AddComponent<ResearchUnlockService>();
            SetPrivate(service, "catalog", catalog);
            service.Initialize(new CityFlowServices(new SimEventHub(), null, null));
            service.inputsOverrideForTest = () => new ResearchConditionInputs(0, 10, null);
            service.EvaluatePendingResearch();

            Assert.IsFalse(service.IsReady("child"));
            Assert.IsFalse(service.TryUnlock("child"));
            Assert.IsFalse(service.IsUnlocked("child"));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void PrerequisiteUnlockedAndSatisfied_CanUnlockOnce()
    {
        var owner = new GameObject("research");
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        try
        {
            ConfigureCatalog(catalog,
                ("root", "", ResearchConditionKind.Population, 1),
                ("child", "root", ResearchConditionKind.Population, 1));
            var service = owner.AddComponent<ResearchUnlockService>();
            SetPrivate(service, "catalog", catalog);
            service.Initialize(new CityFlowServices(new SimEventHub(), null, null));
            service.inputsOverrideForTest = () => new ResearchConditionInputs(0, 10, null);
            service.EvaluatePendingResearch();
            Assert.IsTrue(service.TryUnlock("root"));
            service.EvaluatePendingResearch();

            Assert.IsTrue(service.IsReady("child"));
            Assert.IsTrue(service.TryUnlock("child"));
            Assert.IsFalse(service.TryUnlock("child"));
            Assert.IsTrue(service.IsUnlocked("child"));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void RootResearch_IsReadyFromItsConditionAlone()
    {
        var owner = new GameObject("research");
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        try
        {
            ConfigureCatalog(catalog,
                ("root", "", ResearchConditionKind.Population, 10));
            var service = owner.AddComponent<ResearchUnlockService>();
            SetPrivate(service, "catalog", catalog);
            service.Initialize(new CityFlowServices(new SimEventHub(), null, null));
            service.inputsOverrideForTest = () => new ResearchConditionInputs(0, 10, null);
            service.EvaluatePendingResearch();

            Assert.IsTrue(service.IsReady("root"));
            Assert.IsFalse(service.IsUnlocked("root"));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void EvaluatePendingResearch_DoesNotAutomaticallyUnlockReadyResearch()
    {
        var owner = new GameObject("research");
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        try
        {
            ConfigureCatalog(catalog,
                ("root", "", ResearchConditionKind.Population, 1));
            var service = owner.AddComponent<ResearchUnlockService>();
            SetPrivate(service, "catalog", catalog);
            service.Initialize(new CityFlowServices(new SimEventHub(), null, null));
            service.inputsOverrideForTest = () => new ResearchConditionInputs(0, 10, null);
            service.EvaluatePendingResearch();

            Assert.IsTrue(service.IsReady("root"));
            Assert.IsFalse(service.IsUnlocked("root"));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(catalog);
        }
    }

    static void ConfigureCatalog(ResearchCatalogSO catalog,
        params (string id, string prerequisite, ResearchConditionKind kind, int threshold)[] rows)
    {
        var so = new UnityEditor.SerializedObject(catalog);
        var list = so.FindProperty("entries");
        list.arraySize = rows.Length;
        for (int i = 0; i < rows.Length; i++)
        {
            var p = list.GetArrayElementAtIndex(i);
            p.FindPropertyRelative("researchId").stringValue = rows[i].id;
            p.FindPropertyRelative("prerequisiteId").stringValue = rows[i].prerequisite;
            p.FindPropertyRelative("displayName").stringValue = rows[i].id;
            p.FindPropertyRelative("conditionKind").enumValueIndex = (int)rows[i].kind;
            p.FindPropertyRelative("threshold").intValue = rows[i].threshold;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetPrivate(object target, string field, object value) =>
        target.GetType().GetField(field,
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance).SetValue(target, value);
}
