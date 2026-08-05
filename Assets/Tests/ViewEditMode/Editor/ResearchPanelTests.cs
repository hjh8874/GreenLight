using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Gameplay.Research;
using CityFlow.Contracts;
using CityFlow.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// 이름 필터로 돌린다: run_tests(group_names=[".*ResearchPanelTests.*"])
// 상태·개수·잠금 여부만 단정한다 — TMP 텍스트 내용은 카피 변경에 취약해 보지 않는다.
public class ResearchPanelTests
{
    [Test]
    public void Initialize_CanvasWithoutRaycaster_AddsGraphicRaycaster()
    {
        var owner = new GameObject("panel", typeof(Canvas));
        try
        {
            ResearchPanelController controller = CreateController(owner, null);

            Assert.IsNotNull(owner.GetComponent<GraphicRaycaster>(),
                "독립 Canvas인 연구 패널은 클릭 입력용 GraphicRaycaster가 필요하다");
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void Initialize_BuildsOneRowPerEntry_WithLockState()
    {
        var owner = new GameObject("panel");
        var serviceOwner = new GameObject("research");
        try
        {
            var services = new CityFlowServices(new SimEventHub(), null, null);
            var research = serviceOwner.AddComponent<ResearchUnlockService>();
            var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
            Configure(catalog, ("research_a", 1), ("research_b", 99));
            SetPrivate(research, "catalog", catalog);
            research.inputsOverrideForTest = () => new ResearchConditionInputs(1, 0, null);
            research.Initialize(services);
            Assert.IsTrue(research.TryUnlock("research_a"), "테스트 준비: 하나는 열림");

            ResearchPanelController controller = CreateController(owner, services,
                ("research_a", 1), ("research_b", 99));

            Assert.AreEqual(2, controller.RowsForTest.Count, "칸 수만큼 행 생성");
            Assert.IsTrue(controller.RowsForTest[0].IsUnlocked, "열린 행은 열림 상태");
            Assert.IsFalse(controller.RowsForTest[1].IsUnlocked, "미달 행은 잠긴 채");
            Assert.IsTrue(controller.RowsForTest[1].Instance.activeSelf,
                "잠긴 행도 노출한다 — 숨기면 목표가 사라진다");
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(serviceOwner);
        }
    }

    [Test]
    public void ResearchUnlockedEvent_RefreshesRowState()
    {
        var owner = new GameObject("panel");
        var serviceOwner = new GameObject("research");
        try
        {
            var services = new CityFlowServices(new SimEventHub(), null, null);
            var research = serviceOwner.AddComponent<ResearchUnlockService>();
            var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
            Configure(catalog, ("research_a", 1), ("research_b", 99));
            SetPrivate(research, "catalog", catalog);
            research.inputsOverrideForTest = () => new ResearchConditionInputs(1, 0, null);
            research.Initialize(services);

            ResearchPanelController controller = CreateController(owner, services,
                ("research_a", 1), ("research_b", 99));
            Assert.IsFalse(controller.RowsForTest[0].IsUnlocked, "시작은 전부 잠김");

            research.TryUnlock("research_a");

            Assert.IsTrue(controller.RowsForTest[0].IsUnlocked, "이벤트 발화 → 행 상태 갱신");
            Assert.IsFalse(controller.RowsForTest[1].IsUnlocked);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(serviceOwner);
        }
    }

    [Test]
    public void Initialize_RebindsDockResearchPanelToThisPanel()
    {
        var panelOwner = new GameObject("new panel");
        var oldPanelOwner = new GameObject("old panel");
        var dockOwner = new GameObject("dock");
        try
        {
            var dock = dockOwner.AddComponent<UIDockController>();
            SetPrivate(dock, "panelResearch", oldPanelOwner);
            var controller = CreateController(panelOwner, null);

            controller.Initialize(null);

            Assert.AreSame(panelOwner, GetPrivate<GameObject>(dock, "panelResearch"),
                "새 연구 패널이 도크의 연구 패널로 자동 연결되어야 한다");
        }
        finally
        {
            Object.DestroyImmediate(panelOwner);
            Object.DestroyImmediate(oldPanelOwner);
            Object.DestroyImmediate(dockOwner);
        }
    }

    [Test]
    public void Initialize_DisablesOtherResearchPanelControllers()
    {
        var panelOwner = new GameObject("new panel");
        var oldPanelOwner = new GameObject("old panel");
        try
        {
            oldPanelOwner.AddComponent<ResearchPanelController>();
            var controller = CreateController(panelOwner, null);

            controller.Initialize(null);

            Assert.IsFalse(oldPanelOwner.activeSelf, "구 연구 패널은 자동으로 비활성화되어야 한다");
        }
        finally
        {
            Object.DestroyImmediate(panelOwner);
            Object.DestroyImmediate(oldPanelOwner);
        }
    }

    [Test]
    public void Initialize_WithoutDock_DoesNotThrow()
    {
        var panelOwner = new GameObject("panel");
        try
        {
            var controller = CreateController(panelOwner, null);

            Assert.DoesNotThrow(() => controller.Initialize(null));
        }
        finally
        {
            Object.DestroyImmediate(panelOwner);
        }
    }

    static ResearchPanelController CreateController(GameObject owner,
        CityFlowServices services, params (string id, int threshold)[] rows)
    {
        var controller = owner.AddComponent<ResearchPanelController>();
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        Configure(catalog, rows);
        SetPrivate(controller, "catalog", catalog);

        var template = new GameObject("RowTemplate");
        template.AddComponent<RectTransform>();
        template.transform.SetParent(owner.transform);
        template.SetActive(false);
        SetPrivate(controller, "rowTemplate", template);

        controller.Initialize(services);
        return controller;
    }

    static void Configure(ResearchCatalogSO catalog,
        params (string id, int threshold)[] rows)
    {
        var so = new UnityEditor.SerializedObject(catalog);
        var list = so.FindProperty("entries");
        for (int i = 0; i < rows.Length; i++)
        {
            list.InsertArrayElementAtIndex(i);
            var p = list.GetArrayElementAtIndex(i);
            p.FindPropertyRelative("researchId").stringValue = rows[i].id;
            p.FindPropertyRelative("displayName").stringValue = rows[i].id;
            p.FindPropertyRelative("conditionKind").enumValueIndex =
                (int)ResearchConditionKind.DailyArrivals;
            p.FindPropertyRelative("threshold").intValue = rows[i].threshold;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetPrivate(object target, string field, object value) =>
        target.GetType().GetField(field,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)
            .SetValue(target, value);

    static T GetPrivate<T>(object target, string field) =>
        (T)target.GetType().GetField(field,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)
            .GetValue(target);
}
