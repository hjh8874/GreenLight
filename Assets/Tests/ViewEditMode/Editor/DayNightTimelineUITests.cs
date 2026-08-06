using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.Tests.ViewEditMode
{
    public sealed class DayNightTimelineUITests
    {
        [Test]
        public void Ensure_CreatesOneTimelineDirectlyBelowTopBar()
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

                CityFlowServices services = new CityFlowServices(
                    new SimEventHub(),
                    null,
                    null);

                DayNightTimelineUI.Ensure(topBarRect, services);
                DayNightTimelineUI.Ensure(topBarRect, services);

                Transform timeline = canvas.transform.Find("DayNightTimeline");
                Assert.That(timeline, Is.Not.Null);
                Assert.That(CountNamedChildren(canvas.transform, "DayNightTimeline"),
                    Is.EqualTo(1));

                RectTransform timelineRect =
                    timeline.GetComponent<RectTransform>();
                Assert.That(timelineRect.sizeDelta.y, Is.EqualTo(60f));
                Assert.That(timelineRect.anchoredPosition.y, Is.EqualTo(-60f));
                Assert.That(
                    timeline.GetComponent<Image>().raycastTarget,
                    Is.EqualTo(topBar.GetComponent<Image>().raycastTarget));
                Assert.That(
                    timeline.GetComponent<CanvasGroup>().blocksRaycasts,
                    Is.EqualTo(topBar.GetComponent<CanvasGroup>().blocksRaycasts));

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
                    divider.GetSiblingIndex(),
                    Is.GreaterThan(marker.GetSiblingIndex()));
            }
            finally
            {
                Object.DestroyImmediate(canvas);
            }
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
    }
}
