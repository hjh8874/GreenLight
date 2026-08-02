using CityFlow.Content;
using CityFlow.Sim;
using NUnit.Framework;
using UnityEngine;

// CompanyTypeSO·CompanyTypeCatalogSO 는 Assembly-CSharp 소속이라 CityFlow.Sim.Tests 가 볼 수 없다.
// 그래서 기본 에디터 어셈블리 쪽에 둔다. 이름 필터로 돌린다:
//   run_tests(group_names=[".*CompanyTypeCatalogTests.*"])
public class CompanyTypeCatalogTests
{
    static CompanyTypeSO NewType(string id, float start, float end, int capacity)
    {
        var so = ScriptableObject.CreateInstance<CompanyTypeSO>();
        so.companyTypeId = id;
        so.displayName = id;
        so.capacity = capacity;
        so.workStartHour = start;
        so.workStartWindow = 4f;
        so.workEndHour = end;
        so.workEndWindow = 4f;
        return so;
    }

    static CompanyTypeCatalogSO NewCatalog(params CompanyTypeSO[] entries)
    {
        var catalog = ScriptableObject.CreateInstance<CompanyTypeCatalogSO>();
        // types 는 [SerializeField] private — 에셋 없이 구성하려면 SerializedObject 로 넣는다.
        var serialized = new UnityEditor.SerializedObject(catalog);
        UnityEditor.SerializedProperty list = serialized.FindProperty("types");
        list.arraySize = entries.Length;
        for (int i = 0; i < entries.Length; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = entries[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return catalog;
    }

    [Test]
    public void ToCompanyTypeInfos_ConvertsWindowsAndCapacity()
    {
        CompanyTypeCatalogSO catalog = NewCatalog(
            NewType("office", 6f, 17f, 6),
            NewType("factory", 20f, 5f, 10));

        var infos = catalog.ToCompanyTypeInfos();

        Assert.AreEqual(2, infos.Count);
        Assert.AreEqual("office", infos[0].Window.CompanyTypeId);
        Assert.AreEqual(6, infos[0].Capacity);
        Assert.AreEqual(20f, infos[1].Window.StartHour, "공장은 야간 출근");
        Assert.AreEqual(5f, infos[1].Window.EndHour, "퇴근이 출근보다 이르다 = 자정 넘김");
        Assert.AreEqual(10, infos[1].Capacity);
    }

    // [P2 가드] 뷰의 주차 앵커·디테일은 전역 SimConfig.OfficeCapacity 슬롯 수 하나로 그린다
    // (CarMotion → PolylineMath.ParkingSlotOffset 이 slotCount-1 로 clamp). 카탈로그 정원이
    // 이를 넘으면 초과 슬롯이 마지막 자리에 겹쳐 주차된다. 정원을 6 넘게 올리려면
    // 목적지별 정원을 뷰에 노출하는 작업(뷰 소유자)이 먼저다.
    [Test]
    public void CatalogAsset_CapacitiesFitViewParkingContract()
    {
        var catalog = Resources.Load<CompanyTypeCatalogSO>("CityFlow/CompanyTypeCatalog");
        Assert.IsNotNull(catalog, "Resources/CityFlow/CompanyTypeCatalog.asset 이 있어야 한다");

        int parkingSlots = SimConfig.Default().OfficeCapacity;
        foreach (var info in catalog.ToCompanyTypeInfos())
            Assert.LessOrEqual(info.Capacity, parkingSlots,
                $"'{info.Window.CompanyTypeId}' 정원이 뷰 주차 계약({parkingSlots}칸)을 넘는다");
    }

    [Test]
    public void ToCompanyTypeInfos_WarnsAndSkips_EmptyAndDuplicateIds()
    {
        CompanyTypeCatalogSO catalog = NewCatalog(
            NewType("office", 6f, 17f, 6),
            NewType("  ", 6f, 17f, 6),
            NewType("office", 4f, 13f, 4));

        UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("id 가 없다"));
        UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("중복된"));

        var infos = catalog.ToCompanyTypeInfos();

        Assert.AreEqual(1, infos.Count, "빈 id·중복 id 는 건너뛴다");
        Assert.AreEqual("office", infos[0].Window.CompanyTypeId);
        Assert.AreEqual(6f, infos[0].Window.StartHour, "먼저 온 정의가 남는다");
    }
}
