using CityFlow.UI.Quests;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Tests.ViewEditMode
{
    public sealed class QuestBubbleUITests
    {
        [Test]
        public void Create_PlacesExpandedAndMinimizedControlsBelowTimeline()
        {
            GameObject canvas = new GameObject(
                "Canvas",
                typeof(RectTransform));

            try
            {
                QuestBubbleUI controller =
                    QuestBubbleUI.Create(canvas.transform);
                RectTransform bubble = controller.transform
                    .Find("QuestBubble")
                    .GetComponent<RectTransform>();
                RectTransform close = bubble
                    .Find("CloseButton")
                    .GetComponent<RectTransform>();
                RectTransform minimized = controller.transform
                    .Find("QuestMinimizedButton")
                    .GetComponent<RectTransform>();

                Assert.That(
                    bubble.anchoredPosition,
                    Is.EqualTo(new Vector2(0f, -120f)));
                Assert.That(
                    bubble.anchorMin,
                    Is.EqualTo(new Vector2(1f, 1f)));
                Assert.That(
                    bubble.pivot,
                    Is.EqualTo(new Vector2(1f, 1f)));
                Assert.That(
                    minimized.anchoredPosition,
                    Is.EqualTo(new Vector2(-29f, -149f)));
                Assert.That(
                    minimized.anchorMin,
                    Is.EqualTo(new Vector2(1f, 1f)));
                Assert.That(
                    minimized.pivot,
                    Is.EqualTo(new Vector2(0.5f, 0.5f)));
                Assert.That(
                    close.pivot,
                    Is.EqualTo(new Vector2(0.5f, 0.5f)));
                Assert.That(
                    close.sizeDelta,
                    Is.EqualTo(minimized.sizeDelta));

                RectTransform forwardLine = close
                    .Find("XLineForward")
                    .GetComponent<RectTransform>();
                RectTransform backwardLine = close
                    .Find("XLineBackward")
                    .GetComponent<RectTransform>();
                float expectedLineLength =
                    minimized.sizeDelta.x * 0.6f /
                    Mathf.Sqrt(2f);
                Assert.That(
                    forwardLine.sizeDelta.x,
                    Is.EqualTo(expectedLineLength).Within(0.0001f));
                Assert.That(
                    backwardLine.sizeDelta.x,
                    Is.EqualTo(expectedLineLength).Within(0.0001f));

                Vector2 closeCenter = new Vector2(
                    bubble.anchoredPosition.x +
                    close.anchoredPosition.x,
                    bubble.anchoredPosition.y +
                    close.anchoredPosition.y);
                Vector2 minimizedCenter =
                    minimized.anchoredPosition;

                Assert.That(minimizedCenter, Is.EqualTo(closeCenter));
            }
            finally
            {
                Object.DestroyImmediate(canvas);
            }
        }
    }
}
