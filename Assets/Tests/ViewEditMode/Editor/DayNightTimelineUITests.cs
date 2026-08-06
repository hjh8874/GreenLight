using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace CityFlow.Tests.ViewEditMode
{
    public sealed class DayNightTimelineUITests
    {
        [Test]
        public void Ensure_CreatesOneTimelineInsideTopBarBehindHudContent()
        {
            GameObject canvas = new GameObject("Canvas", typeof(RectTransform));
            GameObject topBar = new GameObject(
                "HUD_TopBar",
                typeof(RectTransform),
                typeof(Image),
                typeof(CanvasGroup));

            try
            {
                topBar.transform.SetParent(canvas.transform, false);
                RectTransform topBarRect = topBar.GetComponent<RectTransform>();
                topBarRect.anchorMin = new Vector2(0f, 1f);
                topBarRect.anchorMax = new Vector2(1f, 1f);
                topBarRect.pivot = new Vector2(0.5f, 1f);
                topBarRect.sizeDelta = new Vector2(0f, 60f);

                GameObject timeText = CreateHeaderText(
                    topBar.transform,
                    "TimeText");
                RectTransform timeRect =
                    timeText.GetComponent<RectTransform>();
                timeRect.anchorMin = new Vector2(0f, 1f);
                timeRect.anchorMax = new Vector2(0f, 1f);
                timeRect.pivot = new Vector2(0f, 1f);
                timeRect.anchoredPosition = new Vector2(16f, -14f);
                timeRect.sizeDelta = new Vector2(210f, 30f);
                GameObject vehicleCountText = CreateHeaderText(
                    topBar.transform,
                    "VehicleCountText");
                GameObject coinText = CreateHeaderText(
                    topBar.transform,
                    "CoinText");
                GameObject congestionDot = new GameObject(
                    "CongestionDot",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                congestionDot.transform.SetParent(topBar.transform, false);
                GameObject harvest = new GameObject(
                    "CoinHarvestButton",
                    typeof(RectTransform));
                harvest.transform.SetParent(topBar.transform, false);
                RectTransform harvestRect =
                    harvest.GetComponent<RectTransform>();
                harvestRect.anchorMin = harvestRect.anchorMax =
                    new Vector2(0.5f, 1f);
                harvestRect.pivot = new Vector2(0.5f, 1f);
                harvestRect.anchoredPosition = new Vector2(0f, -18f);

                CityFlowServices services = new CityFlowServices(
                    new SimEventHub(),
                    null,
                    null);

                DayNightTimelineUI.Ensure(topBarRect, services);
                DayNightTimelineUI.Ensure(topBarRect, services);

                Transform timeline = topBar.transform.Find("DayNightTimeline");
                Assert.That(timeline, Is.Not.Null);
                Assert.That(canvas.transform.Find("DayNightTimeline"), Is.Null);
                Assert.That(CountNamedChildren(topBar.transform, "DayNightTimeline"),
                    Is.EqualTo(1));

                RectTransform timelineRect =
                    timeline.GetComponent<RectTransform>();
                Assert.That(timelineRect.anchorMin, Is.EqualTo(Vector2.zero));
                Assert.That(timelineRect.anchorMax, Is.EqualTo(Vector2.one));
                Assert.That(timelineRect.sizeDelta, Is.EqualTo(Vector2.zero));
                Assert.That(timeline.GetComponent<Graphic>(), Is.Null);
                Assert.That(timeline.GetSiblingIndex(), Is.EqualTo(0));
                Assert.That(timeText.transform.GetSiblingIndex(),
                    Is.GreaterThan(timeline.GetSiblingIndex()));
                Assert.That(harvest.transform.GetSiblingIndex(),
                    Is.GreaterThan(timeline.GetSiblingIndex()));
                Assert.That(harvestRect.anchorMin,
                    Is.EqualTo(new Vector2(0.5f, 0.5f)));
                Assert.That(harvestRect.anchorMax,
                    Is.EqualTo(new Vector2(0.5f, 0.5f)));
                Assert.That(harvestRect.pivot,
                    Is.EqualTo(new Vector2(0.5f, 0.5f)));
                Assert.That(harvestRect.anchoredPosition,
                    Is.EqualTo(Vector2.zero));

                RectTransform congestionDotRect =
                    congestionDot.GetComponent<RectTransform>();
                Assert.That(congestionDotRect.anchorMin,
                    Is.EqualTo(new Vector2(0f, 0.5f)));
                Assert.That(congestionDotRect.anchorMax,
                    Is.EqualTo(new Vector2(0f, 0.5f)));
                Assert.That(congestionDotRect.pivot,
                    Is.EqualTo(new Vector2(0.5f, 0.5f)));
                Assert.That(congestionDotRect.anchoredPosition,
                    Is.EqualTo(new Vector2(16f, 0f)));
                Assert.That(congestionDotRect.sizeDelta,
                    Is.EqualTo(new Vector2(12f, 12f)));
                Image congestionDotImage =
                    congestionDot.GetComponent<Image>();
                Assert.That(congestionDotImage.sprite, Is.Not.Null);
                Assert.That(congestionDotImage.preserveAspect, Is.True);
                Assert.That(timeRect.anchoredPosition.x,
                    Is.GreaterThanOrEqualTo(34f));

                timeRect.anchoredPosition = new Vector2(16f, -14f);
                timeline.SendMessage("LateUpdate");
                Assert.That(timeRect.anchoredPosition.x,
                    Is.GreaterThanOrEqualTo(34f));

                AssertHeaderOutline(timeText);
                AssertHeaderOutline(vehicleCountText);
                AssertHeaderOutline(coinText);

                RectTransform divider = timeline.Find("NoonDivider")
                    .GetComponent<RectTransform>();
                Assert.That(divider.anchorMin.x, Is.EqualTo(0.5f));
                Assert.That(divider.anchorMax.x, Is.EqualTo(0.5f));

                RectTransform marker = timeline.Find("CelestialMarker")
                    .GetComponent<RectTransform>();
                Assert.That(marker.sizeDelta, Is.EqualTo(new Vector2(60f, 60f)));
                Assert.That(marker.anchoredPosition.x, Is.EqualTo(0f));
                RawImage moon = marker.Find("Moon")
                    .GetComponent<RawImage>();
                Assert.That(moon, Is.Not.Null);
                Assert.That(
                    moon.material.shader.name,
                    Is.EqualTo("CityFlow/Celestial Overlay"));
                Assert.That(
                    moon.material.renderQueue,
                    Is.EqualTo((int)RenderQueue.Transparent));
                Assert.That(
                    divider.GetSiblingIndex(),
                    Is.GreaterThan(marker.GetSiblingIndex()));
            }
            finally
            {
                Object.DestroyImmediate(canvas);
            }
        }

        [Test]
        public void RefreshInterval_IsAtLeastPointTwoSeconds()
        {
            System.Reflection.FieldInfo field =
                typeof(DayNightTimelineUI).GetField(
                    "RefreshIntervalSeconds",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static);

            Assert.That(field, Is.Not.Null);
            Assert.That((float)field.GetRawConstantValue(),
                Is.GreaterThanOrEqualTo(0.2f));
        }

        [TestCase(0f, false)]
        [TestCase(0.249f, false)]
        [TestCase(0.25f, true)]
        [TestCase(0.5f, true)]
        [TestCase(0.749f, true)]
        [TestCase(0.75f, false)]
        public void IsSunTime_UsesSixToEighteenHourWindow(
            float timeOfDay01,
            bool expected)
        {
            Assert.That(
                DayNightTimelineUI.IsSunTime(timeOfDay01),
                Is.EqualTo(expected));
        }

        [TestCase(0.25f, 0f)]
        [TestCase(0.5f, 0.5f)]
        [TestCase(0.75f, 0f)]
        [TestCase(0f, 0.5f)]
        public void CalculateCycleProgress_UsesSeparateDayAndNightTracks(
            float timeOfDay01,
            float expected)
        {
            Assert.That(
                DayNightTimelineUI.CalculateCycleProgress(timeOfDay01),
                Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void CalculateCycleProgress_ReachesRightEdgeBeforeIconSwitch()
        {
            float justBeforeSunrise = (6f - 0.001f) / 24f;
            float justBeforeSunset = (18f - 0.001f) / 24f;

            Assert.That(
                DayNightTimelineUI.CalculateCycleProgress(justBeforeSunrise),
                Is.GreaterThan(0.999f));
            Assert.That(
                DayNightTimelineUI.CalculateCycleProgress(justBeforeSunset),
                Is.GreaterThan(0.999f));
        }

        [TestCase(0.25f, 30f)]
        [TestCase(0.5f, 0f)]
        [TestCase(0.75f, 30f)]
        [TestCase(0f, 0f)]
        public void CalculateTrackOffset_KeepsCircleInsidePanel(
            float timeOfDay01,
            float expected)
        {
            Assert.That(
                DayNightTimelineUI.CalculateTrackOffset(
                    timeOfDay01,
                    60f),
                Is.EqualTo(expected).Within(0.0001f));
        }

        private static int CountNamedChildren(Transform parent, string name)
        {
            int count = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i).name == name)
                {
                    count++;
                }
            }

            return count;
        }

        private static GameObject CreateHeaderText(
            Transform parent,
            string name)
        {
            GameObject text = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            text.transform.SetParent(parent, false);
            return text;
        }

        private static void AssertHeaderOutline(GameObject text)
        {
            TMP_Text tmpText = text.GetComponent<TMP_Text>();
            Assert.That(tmpText, Is.Not.Null);
            Assert.That(text.GetComponent<Outline>(), Is.Null);
            Assert.That(tmpText.fontMaterial, Is.Not.Null);
            Assert.That(
                tmpText.fontMaterial.IsKeywordEnabled("OUTLINE_ON"),
                Is.True);
            Assert.That(tmpText.fontMaterial.GetColor("_OutlineColor"),
                Is.EqualTo(new Color(0f, 0f, 0f, 1f)));
            Assert.That(tmpText.fontMaterial.GetFloat("_OutlineWidth"),
                Is.EqualTo(0.3f).Within(0.001f));
        }
    }
}
