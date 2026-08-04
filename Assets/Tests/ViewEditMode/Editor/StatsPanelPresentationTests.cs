using CityFlow.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StatsPanelPresentationTests
{
    [Test]
    public void Awake_BuildsCompactDashboardWithoutSceneWiring()
    {
        var owner = new GameObject(
            "Statistics_Panel",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        try
        {
            StatsPanelController controller =
                owner.AddComponent<StatsPanelController>();

            RectTransform dashboard = controller.DashboardRootForTest;
            Assert.NotNull(dashboard);
            Assert.That(dashboard.rect.width, Is.LessThanOrEqualTo(540f));
            Assert.That(dashboard.rect.height, Is.LessThanOrEqualTo(360f));
            Assert.AreEqual(new Vector2(1f, 0f), dashboard.anchorMin);
            Assert.AreEqual(new Vector2(1f, 0f), dashboard.anchorMax);
            Assert.AreEqual(new Vector2(1f, 0f), dashboard.pivot);

            Assert.NotNull(owner.transform.Find("StatsDashboard/Title"));
            Assert.NotNull(owner.transform.Find("StatsDashboard/ActiveVehicles/Value"));
            Assert.NotNull(owner.transform.Find("StatsDashboard/CongestedRoads/Value"));
            Assert.NotNull(owner.transform.Find("StatsDashboard/IncomePerMinute/Value"));
            Assert.NotNull(owner.transform.Find("StatsDashboard/TrafficHealth"));
            Assert.NotNull(owner.transform.Find("StatsDashboard/IncomeTrend"));
            Assert.NotNull(owner.transform.Find("StatsDashboard/CitySummary"));
            Assert.IsNull(owner.transform.Find("StatsDashboard/Population"));
            Assert.IsNull(owner.transform.Find("StatsDashboard/Wallet"));

            Assert.AreEqual(10, controller.IncomeBarsForTest.Count);
            for (int index = 0; index < controller.IncomeBarsForTest.Count; index++)
            {
                Assert.NotNull(controller.IncomeBarsForTest[index]);
            }

            TMP_Text title = owner.transform
                .Find("StatsDashboard/Title")
                .GetComponent<TMP_Text>();
            Assert.AreEqual("도시 통계", title.text);
            Assert.That(title.color.a, Is.EqualTo(1f).Within(0.01f));
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void Reenable_ReusesExistingDashboardWithoutDuplicates()
    {
        var owner = new GameObject(
            "Statistics_Panel",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        try
        {
            StatsPanelController controller =
                owner.AddComponent<StatsPanelController>();
            RectTransform initialDashboard =
                controller.DashboardRootForTest;

            owner.SetActive(false);
            owner.SetActive(true);

            Assert.AreSame(
                initialDashboard,
                controller.DashboardRootForTest);
            Assert.AreEqual(10, controller.IncomeBarsForTest.Count);

            int dashboardCount = 0;
            for (int index = 0; index < owner.transform.childCount; index++)
            {
                if (owner.transform.GetChild(index).name == "StatsDashboard")
                {
                    dashboardCount++;
                }
            }

            Assert.AreEqual(1, dashboardCount);
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [TestCase(0, 0f, "분석 대기")]
    [TestCase(20, 0.04f, "원활")]
    [TestCase(20, 0.05f, "주의")]
    [TestCase(20, 0.19f, "주의")]
    [TestCase(20, 0.20f, "혼잡")]
    public void EvaluateTrafficState_UsesReadableThresholds(
        int roadCount,
        float congestionRatio,
        string expected)
    {
        StatsPanelController.TrafficState state =
            StatsPanelController.EvaluateTrafficState(
                roadCount,
                congestionRatio);

        Assert.AreEqual(expected, state.Label);
        Assert.That(state.Color.a, Is.EqualTo(1f).Within(0.01f));
    }
}
