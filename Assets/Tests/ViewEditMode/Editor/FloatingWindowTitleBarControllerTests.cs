using CityFlow.UI;
using CityFlow.UI.Controllers;
using CityFlow.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.Tests.ViewEditMode
{
    public sealed class FloatingWindowTitleBarControllerTests
    {
        [Test]
        public void ApplyTopInset_ConvertsPhysicalPixelsToCanvasUnits()
        {
            GameObject canvasObject = new GameObject(
                "Canvas",
                typeof(RectTransform),
                typeof(Canvas));
            GameObject contentObject = new GameObject(
                "Content",
                typeof(RectTransform));
            contentObject.transform.SetParent(canvasObject.transform, false);

            try
            {
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.scaleFactor = 2f;
                RectTransform content =
                    contentObject.GetComponent<RectTransform>();

                FloatingWindowTitleBarController.ApplyTopInset(
                    content,
                    canvas,
                    FloatingWindowTitleBarController.TitleBarHeight);

                float expectedInset =
                    FloatingWindowTitleBarController.TitleBarHeight;

                Assert.That(content.anchorMin, Is.EqualTo(Vector2.zero));
                Assert.That(content.anchorMax, Is.EqualTo(Vector2.one));
                Assert.That(
                    content.sizeDelta.y,
                    Is.EqualTo(-expectedInset).Within(0.00001f));
                Assert.That(
                    content.anchoredPosition.y,
                    Is.EqualTo(-expectedInset * 0.5f).Within(0.00001f));
            }
            finally
            {
                Object.DestroyImmediate(canvasObject);
            }
        }

        [TestCase(0.75f, 27f)]
        [TestCase(1f, 36f)]
        [TestCase(2f, 72f)]
        public void TitleBarHoverZone_MatchesScaledVisibleHeight(
            float scaleFactor,
            float expectedPhysicalHeight)
        {
            float physicalHeight =
                FloatingWindowTitleBarController
                    .CalculatePhysicalTitleBarHeight(scaleFactor);

            Assert.That(
                physicalHeight,
                Is.EqualTo(expectedPhysicalHeight).Within(0.00001f));
            Assert.That(
                FloatingWindowTitleBarController.IsCursorInsideTopZone(
                    1000f - expectedPhysicalHeight,
                    1000f,
                    physicalHeight),
                Is.True);
            Assert.That(
                FloatingWindowTitleBarController.IsCursorInsideTopZone(
                    1000f - expectedPhysicalHeight - 0.01f,
                    1000f,
                    physicalHeight),
                Is.False);
        }

        [Test]
        public void ActionDock_ReparentsUnderHudTopBar()
        {
            GameObject root = new GameObject(
                "FloatingWindowContentRoot",
                typeof(RectTransform));
            GameObject topBar = new GameObject(
                "HUD_TopBar",
                typeof(RectTransform));
            GameObject dock = new GameObject(
                "TopLeftActionDock",
                typeof(RectTransform));
            topBar.transform.SetParent(root.transform, false);
            topBar.GetComponent<RectTransform>().sizeDelta =
                new Vector2(0f, 60f);
            dock.transform.SetParent(root.transform, false);

            try
            {
                TopBarActionDockController controller =
                    dock.AddComponent<TopBarActionDockController>();
                controller.ApplyLayout();

                RectTransform dockRect = dock.GetComponent<RectTransform>();
                Assert.That(dock.transform.parent, Is.SameAs(topBar.transform));
                Assert.That(
                    dockRect.anchorMin,
                    Is.EqualTo(new Vector2(1f, 0.5f)));
                Assert.That(
                    dockRect.anchorMax,
                    Is.EqualTo(new Vector2(1f, 0.5f)));
                Assert.That(dockRect.anchoredPosition, Is.EqualTo(new Vector2(-8f, 0f)));
                Assert.That(
                    dockRect.sizeDelta,
                    Is.EqualTo(new Vector2(196f, 52f)));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CongestionToggle_FitsBesideHarvestInsideHudTopBar()
        {
            GameObject root = new GameObject(
                "FloatingWindowContentRoot",
                typeof(RectTransform));
            GameObject topBar = new GameObject(
                "HUD_TopBar",
                typeof(RectTransform));
            GameObject toggle = new GameObject(
                "CongestionToggle",
                typeof(RectTransform),
                typeof(Image),
                typeof(Toggle));
            topBar.transform.SetParent(root.transform, false);
            topBar.GetComponent<RectTransform>().sizeDelta =
                new Vector2(0f, 60f);
            toggle.transform.SetParent(root.transform, false);

            try
            {
                CongestionTogglePanelController controller =
                    toggle.AddComponent<CongestionTogglePanelController>();
                System.Reflection.MethodInfo configure =
                    typeof(CongestionTogglePanelController).GetMethod(
                        "ConfigureTopBarPresentation",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic);
                Assert.That(configure, Is.Not.Null);
                configure.Invoke(controller, null);

                RectTransform rect =
                    toggle.GetComponent<RectTransform>();
                Assert.That(toggle.transform.parent,
                    Is.SameAs(topBar.transform));
                Assert.That(rect.anchorMin,
                    Is.EqualTo(new Vector2(0.5f, 0.5f)));
                Assert.That(rect.anchorMax,
                    Is.EqualTo(new Vector2(0.5f, 0.5f)));
                Assert.That(rect.pivot,
                    Is.EqualTo(new Vector2(1f, 0.5f)));
                Assert.That(rect.anchoredPosition,
                    Is.EqualTo(new Vector2(-88f, 0f)));
                Assert.That(rect.sizeDelta,
                    Is.EqualTo(new Vector2(156f, 52f)));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
