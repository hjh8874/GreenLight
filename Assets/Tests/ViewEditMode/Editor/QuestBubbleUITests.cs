using System.Reflection;
using CityFlow.Audio;
using CityFlow.Managers;
using CityFlow.UI.Quests;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CityFlow.Tests.ViewEditMode
{
    public sealed class QuestBubbleUITests
    {
        [Test]
        public void QuestCompletionFeedback_HasConfettiAndConfiguredSound()
        {
            GameObject confetti = Resources.Load<GameObject>(
                "CityFlow/FX_QuestClearConfetti");
            Assert.IsNotNull(
                confetti,
                "퀘스트 완료 컨페티 Resources 프리팹이 필요하다");

            FieldInfo clearSfxId = typeof(QuestClearBurst).GetField(
                "ClearSfxId",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(clearSfxId);
            Assert.AreEqual(
                SoundIds.PositiveNotification,
                clearSfxId.GetRawConstantValue());

            SoundCatalog catalog = AssetDatabase.LoadAssetAtPath<SoundCatalog>(
                "Assets/04_Audio/Configs/SoundCatalog.asset");
            Assert.IsNotNull(catalog);
            Assert.IsTrue(
                catalog.TryGetSound(
                    SoundIds.PositiveNotification,
                    out SoundCatalog.SoundEntry sound));
            Assert.IsNotNull(
                sound.Clip,
                "퀘스트 완료음으로 사용할 긍정 알림 클립이 연결돼야 한다");
        }

        [Test]
        public void Create_AttachesExpandedAndMinimizedControlsToTopBar()
        {
            GameObject canvas = new GameObject(
                "Canvas",
                typeof(RectTransform));
            GameObject topBar = new GameObject(
                "HUD_TopBar",
                typeof(RectTransform));
            topBar.transform.SetParent(canvas.transform, false);
            topBar.GetComponent<RectTransform>().sizeDelta =
                new Vector2(0f, 60f);

            try
            {
                QuestBubbleUI controller =
                    QuestBubbleUI.Create(canvas.transform, topBar.GetComponent<RectTransform>());
                RectTransform bubble = controller.transform
                    .Find("QuestBubble")
                    .GetComponent<RectTransform>();
                RectTransform close = bubble
                    .Find("CloseButton")
                    .GetComponent<RectTransform>();
                RectTransform action = bubble
                    .Find("ActionButton")
                    .GetComponent<RectTransform>();
                RectTransform minimized = controller.transform
                    .Find("QuestMinimizedButton")
                    .GetComponent<RectTransform>();

                Assert.That(controller.transform.parent, Is.EqualTo(canvas.transform));

                Assert.That(
                    bubble.anchoredPosition,
                    Is.EqualTo(new Vector2(0f, -60f)));
                Assert.That(
                    bubble.anchorMin,
                    Is.EqualTo(new Vector2(1f, 1f)));
                Assert.That(
                    bubble.pivot,
                    Is.EqualTo(new Vector2(1f, 1f)));
                Assert.That(
                    minimized.anchoredPosition,
                    Is.EqualTo(new Vector2(-29f, -89f)));
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
                Assert.That(action.sizeDelta, Is.EqualTo(new Vector2(92f, 34f)));
                Assert.IsFalse(
                    action.gameObject.activeSelf,
                    "일반 퀘스트에서는 안내 넘기기 버튼이 기본으로 숨겨져야 한다");

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
