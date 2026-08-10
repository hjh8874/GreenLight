using System.Collections.Generic;
using System.Reflection;
using CityFlow.Contracts;
using CityFlow.UI.Controllers;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public class CameraRotationButtonControllerTests
{
    private const string PrefabPath =
        "Assets/02_Prefabs/UI/UI_CameraRotationButton.prefab";

    [Test]
    public void Buttons_SendLeftAndRightDirections_OncePerClick()
    {
        var owner = new GameObject("CameraRotationController");
        var leftOwner = CreateButton("Left");
        var rightOwner = CreateButton("Right");
        var receiverOwner = new GameObject("CameraRotationReceiver");

        try
        {
            var receiver =
                receiverOwner.AddComponent<TestCameraRotationController>();
            var controller =
                owner.AddComponent<CameraRotationButtonController>();
            controller.Configure(
                leftOwner.GetComponent<Button>(),
                rightOwner.GetComponent<Button>());
            SetPrivate(controller, "cameraRotation", receiver);

            leftOwner.GetComponent<Button>().onClick.Invoke();
            rightOwner.GetComponent<Button>().onClick.Invoke();
            controller.Configure(
                leftOwner.GetComponent<Button>(),
                rightOwner.GetComponent<Button>());
            leftOwner.GetComponent<Button>().onClick.Invoke();

            CollectionAssert.AreEqual(
                new[] { -1, 1, -1 },
                receiver.Directions);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(leftOwner);
            Object.DestroyImmediate(rightOwner);
            Object.DestroyImmediate(receiverOwner);
        }
    }

    [Test]
    public void Prefab_UsesCameraIconTitleAndTwoDirectionButtons()
    {
        GameObject prefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null);
        Transform cameraIcon = prefab.transform.Find("CameraIcon");
        Transform title = prefab.transform.Find("Title");
        Transform row = prefab.transform.Find("DirectionButtons");
        Transform left = row?.Find("RotateLeftButton");
        Transform right = row?.Find("RotateRightButton");
        Assert.That(cameraIcon, Is.Not.Null);
        Assert.That(
            cameraIcon.GetComponent<Image>()?.sprite,
            Is.Not.Null);
        Assert.That(
            cameraIcon.GetComponent<RectTransform>().sizeDelta,
            Is.EqualTo(new Vector2(34f, 34f)));
        Assert.That(title, Is.Not.Null);
        Assert.That(title.GetComponent<TMP_Text>()?.text, Is.EqualTo("카메라 회전"));
        Assert.That(row, Is.Not.Null);
        Assert.That(left, Is.Not.Null);
        Assert.That(right, Is.Not.Null);
        Assert.That(prefab.GetComponentsInChildren<Button>(true), Has.Length.EqualTo(2));
        Assert.That(prefab.GetComponentsInChildren<TMP_Text>(true), Has.Length.EqualTo(1));
        Assert.That(left.Find("Icon")?.GetComponent<Image>()?.sprite, Is.Not.Null);
        Assert.That(right.Find("Icon")?.GetComponent<Image>()?.sprite, Is.Not.Null);
        Assert.That(
            left.Find("Icon")?.GetComponent<RectTransform>().sizeDelta,
            Is.EqualTo(new Vector2(28f, 28f)));
        Assert.That(
            right.Find("Icon")?.GetComponent<RectTransform>().sizeDelta,
            Is.EqualTo(new Vector2(28f, 28f)));
    }

    [Test]
    public void Configure_MatchesSiblingFloatingButtonBackground()
    {
        var dock = new GameObject("TopLeftActionDock", typeof(RectTransform));
        GameObject floating = CreateButton("Btn_Floating");
        var cameraGroup = new GameObject(
            "CameraRotateButton",
            typeof(RectTransform),
            typeof(LayoutElement));
        cameraGroup.GetComponent<RectTransform>().sizeDelta = Vector2.zero;
        LayoutElement cameraLayout =
            cameraGroup.GetComponent<LayoutElement>();
        cameraLayout.preferredWidth = 104f;
        cameraLayout.preferredHeight = 108f;
        GameObject left = CreateButton("RotateLeftButton");
        GameObject right = CreateButton("RotateRightButton");
        floating.transform.SetParent(dock.transform, false);
        cameraGroup.transform.SetParent(dock.transform, false);
        left.transform.SetParent(cameraGroup.transform, false);
        right.transform.SetParent(cameraGroup.transform, false);

        Sprite cameraSprite = AssetDatabase.LoadAssetAtPath<Sprite>(
            "Assets/99_Download/Layer Lab/GUI-MonoRound/" +
            "ResourcesData/Sprites/Components/Button/" +
            "Btn_Rectangle01_n_Green.png");
        left.GetComponent<Image>().sprite = cameraSprite;

        try
        {
            var controller =
                cameraGroup.AddComponent<CameraRotationButtonController>();
            controller.Configure(
                left.GetComponent<Button>(),
                right.GetComponent<Button>());

            Assert.That(
                floating.GetComponent<Image>().sprite,
                Is.SameAs(cameraSprite));
            Assert.That(
                floating.GetComponent<Image>().color.a,
                Is.EqualTo(0.62f).Within(0.001f));
            Assert.That(
                floating.GetComponent<RectTransform>().sizeDelta,
                Is.EqualTo(new Vector2(104f, 108f)));
            LayoutElement floatingLayout =
                floating.GetComponent<LayoutElement>();
            Assert.That(floatingLayout, Is.Not.Null);
            Assert.That(floatingLayout.preferredWidth, Is.EqualTo(104f));
            Assert.That(floatingLayout.preferredHeight, Is.EqualTo(108f));
        }
        finally
        {
            Object.DestroyImmediate(dock);
        }
    }

    [Test]
    public void DockLayout_MatchesFloatingBackgroundToCameraButton()
    {
        GameObject cameraPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var dock = new GameObject(
            "TopLeftActionDock",
            typeof(RectTransform),
            typeof(Image),
            typeof(HorizontalLayoutGroup));
        RectTransform dockRect = dock.GetComponent<RectTransform>();
        dockRect.anchorMin = new Vector2(0f, 1f);
        dockRect.anchorMax = new Vector2(0f, 1f);
        dockRect.pivot = new Vector2(0f, 1f);
        dockRect.anchoredPosition = new Vector2(20f, -112f);
        dockRect.sizeDelta = new Vector2(241.7631f, 114.3507f);
        GameObject floating = CreateButton("Btn_Floating");
        GameObject cameraGroup = Object.Instantiate(cameraPrefab);
        cameraGroup.name = "CameraRotateButton";
        floating.transform.SetParent(dock.transform, false);
        cameraGroup.transform.SetParent(dock.transform, false);

        try
        {
            var controller = dock.AddComponent<TopBarActionDockController>();
            typeof(TopBarActionDockController).GetMethod(
                    "ApplyLayout",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, null);

            Image floatingImage = floating.GetComponent<Image>();
            Image cameraImage = cameraGroup
                .GetComponentInChildren<Button>(true)
                .targetGraphic as Image;
            Assert.That(floatingImage.sprite, Is.SameAs(cameraImage.sprite));
            Assert.That(dockRect.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(dockRect.anchorMax, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(dockRect.pivot, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(
                dockRect.anchoredPosition,
                Is.EqualTo(new Vector2(20f, -112f)));
            Assert.That(
                dockRect.sizeDelta,
                Is.EqualTo(new Vector2(241.7631f, 114.3507f)));
        }
        finally
        {
            Object.DestroyImmediate(dock);
        }
    }

    private static GameObject CreateButton(string name)
    {
        return new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
    }

    private static void SetPrivate(
        object target,
        string fieldName,
        object value)
    {
        typeof(CameraRotationButtonController).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(target, value);
    }

    private sealed class TestCameraRotationController
        : MonoBehaviour, ICameraRotationController
    {
        public readonly List<int> Directions = new();

        public bool TryRotateCamera(int stepDirection)
        {
            Directions.Add(stepDirection);
            return true;
        }
    }
}
