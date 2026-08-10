using System.Linq;
using CityFlow.Contracts;
using CityFlow.UI.Data;
using NUnit.Framework;
using UnityEngine;

// 건물 정보 카드의 "예상 수익"은 시뮬레이션 값이 아니라 타일 좌표 해시로 만든
// 합성값이었다(coinsPerPerson = 3 + seed % 6). 실제 경제와 연결이 없어
// 플레이어에게 거짓 기대를 주므로 제거했다.
//
// 이 테스트는 그 값이 되살아나는 것을 막는다 — 지표를 다시 넣으려면
// 실제 경제(EconomyService·DistanceRewardService)에서 끌어와야 한다.
public class BuildingStoryDataNoFakeIncomeTests
{
    [Test]
    public void BuildingStoryData_HasNoIncomeField()
    {
        var fieldNames = typeof(BuildingStoryData)
            .GetFields()
            .Select(f => f.Name)
            .ToArray();

        CollectionAssert.DoesNotContain(
            fieldNames,
            "IncomePerMin",
            "예상 수익은 합성값이었다. 되살리려면 실제 경제에서 끌어와야 한다.");

        // 남아 있어야 할 것들 — 이 테스트가 과잉 삭제를 잡는 용도로도 쓰인다.
        CollectionAssert.Contains(fieldNames, "TotalStaff");
        CollectionAssert.Contains(fieldNames, "TardyStaff");
    }

    // 팩토리가 여전히 동작하는지(과잉 삭제로 깨지지 않았는지) 확인한다.
    [Test]
    public void Synthesize_StillProducesStaffData()
    {
        BuildingStoryData data = BuildingStoryDataFactory.Synthesize(
            new Vector2Int(5, 7),
            TileType.Office,
            density01: 0.5f,
            congestion: CongestionLevel.Free,
            accumulatedDelay: 0f,
            staffingFilled: 4,
            staffingCapacity: 10);

        Assert.Greater(data.TotalStaff, 0, "총 인원은 계속 나와야 한다");
        Assert.AreEqual(
            6,
            data.TardyStaff,
            "실제 채용 데이터가 있으면 미출근 = 정원 - 출근");
    }
}
