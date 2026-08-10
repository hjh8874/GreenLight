using System.Reflection;
using CityFlow.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public class TitleSceneEnhancementsTests
{
    private const string PrefabPath =
        "Assets/Resources/CityFlow/UI/" +
        "UI_TitleSceneEnhancements.prefab";

    [Test]
    public void Prefab_HasLogoBackdropAndBgmOnlySettings()
    {
        GameObject prefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null);
        Assert.That(
            prefab.GetComponent<TitleSceneEnhancementsView>(),
            Is.Not.Null);
        Assert.That(
            prefab.transform.Find("LogoBackdropLayer/LogoBackdrop"),
            Is.Not.Null);
        Transform backdrop =
            prefab.transform.Find("LogoBackdropLayer/LogoBackdrop");
        Image backdropImage = backdrop.GetComponent<Image>();
        float backdropLuminance =
            0.2126f * backdropImage.color.r +
            0.7152f * backdropImage.color.g +
            0.0722f * backdropImage.color.b;
        Assert.That(backdropLuminance, Is.GreaterThan(0.85f));
        Assert.That(backdropImage.color.a, Is.GreaterThan(0.9f));
        Assert.That(backdrop.GetComponent<Shadow>(), Is.Not.Null);
        Assert.That(backdrop.GetComponent<Outline>(), Is.Not.Null);
        Transform settings = prefab.transform.Find(
            "TitleSettingsLayer/TitleSettingsPanel");
        Assert.That(settings, Is.Not.Null);
        Assert.That(
            settings.Find("UI_AudioSettings_Title/BGM_Group"),
            Is.Not.Null);
        Assert.That(
            settings.Find("UI_AudioSettings_Title/SFX_Group"),
            Is.Null);
        Assert.That(settings.Find("CloseButton"), Is.Not.Null);
        Assert.That(FindDescendant(settings, "Btn_Quit"), Is.Null);
    }

    [Test]
    public void Controller_DisablesLanguageAndTogglesSettings()
    {
        var canvasObject = new GameObject(
            "ArbitraryTitleSurface",
            typeof(RectTransform),
            typeof(Canvas));
        var menu = new GameObject("MenuContainer", typeof(RectTransform));
        var language = new GameObject(
            "ArbitraryLanguageAction",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button));
        var controllerObject = new GameObject("TitleController");
        var popupObject = new GameObject("ConfirmPopup");

        try
        {
            menu.transform.SetParent(canvasObject.transform, false);
            language.transform.SetParent(menu.transform, false);
            Button languageButton = language.GetComponent<Button>();
            TitleSceneController controller =
                controllerObject.AddComponent<TitleSceneController>();
            ConfirmPopupController popup =
                popupObject.AddComponent<ConfirmPopupController>();
            popupObject.SetActive(false);
            SetPrivateField(controller, "confirmPopup", popup);
            SetPrivateField(
                controller,
                "titleCanvas",
                canvasObject.GetComponent<Canvas>());
            SetPrivateField(controller, "languageButton", languageButton);

            InvokePrivate(controller, "DisableLanguageButton");
            Assert.That(languageButton.interactable, Is.False);

            InvokePrivate(controller, "InstallTitleEnhancements");
            TitleSceneEnhancementsView view =
                Object.FindFirstObjectByType<TitleSceneEnhancementsView>();
            Assert.That(view, Is.Not.Null);
            Assert.That(view.IsLogoBackdropVisible, Is.True);
            Assert.That(view.IsSettingsVisible, Is.False);

            controller.OnSettings();
            Assert.That(view.IsSettingsVisible, Is.True);
            controller.OnSettings();
            Assert.That(view.IsSettingsVisible, Is.False);

            controller.OnSettings();
            Assert.That(view.IsSettingsVisible, Is.True);
            controller.OnQuit();
            Assert.That(view.IsSettingsVisible, Is.False);
            Assert.That(popupObject.activeSelf, Is.True);

            controller.OnSettings();
            Assert.That(popupObject.activeSelf, Is.False);
            Assert.That(view.IsSettingsVisible, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(popupObject);
            Object.DestroyImmediate(controllerObject);
            Object.DestroyImmediate(canvasObject);
        }
    }

    [Test]
    public void Controller_UsesReferencedUiInsteadOfHierarchyNames()
    {
        var intendedCanvasObject = new GameObject(
            "RenamedTitleSurface",
            typeof(RectTransform),
            typeof(Canvas));
        var competingCanvasObject = new GameObject(
            "TitleCanvas",
            typeof(RectTransform),
            typeof(Canvas));
        var popupObject = new GameObject("RenamedConfirmation");
        var languageObject = new GameObject(
            "RenamedLanguageAction",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button));
        var controllerObject = new GameObject("RenamedTitleController");

        try
        {
            popupObject.transform.SetParent(
                intendedCanvasObject.transform,
                false);
            languageObject.transform.SetParent(
                intendedCanvasObject.transform,
                false);

            ConfirmPopupController popup =
                popupObject.AddComponent<ConfirmPopupController>();
            TitleSceneController controller =
                controllerObject.AddComponent<TitleSceneController>();
            Button languageButton = languageObject.GetComponent<Button>();
            UnityEventTools.AddPersistentListener(
                languageButton.onClick,
                controller.OnLanguageClicked);
            SetPrivateField(controller, "confirmPopup", popup);

            InvokePrivate(controller, "InstallTitleEnhancements");
            InvokePrivate(controller, "DisableLanguageButton");

            TitleSceneEnhancementsView view =
                intendedCanvasObject.GetComponentInChildren<
                    TitleSceneEnhancementsView>(true);
            Assert.That(view, Is.Not.Null);
            Assert.That(
                competingCanvasObject.GetComponentInChildren<
                    TitleSceneEnhancementsView>(true),
                Is.Null);
            Assert.That(languageButton.interactable, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(controllerObject);
            Object.DestroyImmediate(competingCanvasObject);
            Object.DestroyImmediate(intendedCanvasObject);
        }
    }

    [Test]
    public void View_MovesBehindLogoAndInFrontForSettings()
    {
        GameObject prefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var canvasObject = new GameObject(
            "SortingTestCanvas",
            typeof(RectTransform),
            typeof(Canvas));

        try
        {
            var existingLogo = new GameObject(
                "ExistingLogo",
                typeof(RectTransform));
            existingLogo.transform.SetParent(canvasObject.transform, false);
            GameObject instance = Object.Instantiate(
                prefab,
                canvasObject.transform,
                false);
            TitleSceneEnhancementsView view =
                instance.GetComponent<TitleSceneEnhancementsView>();
            view.Initialize(true);

            Assert.That(instance.transform.GetSiblingIndex(), Is.Zero);
            Assert.That(
                existingLogo.transform.GetSiblingIndex(),
                Is.GreaterThan(instance.transform.GetSiblingIndex()));
            Transform settings = instance.transform.Find(
                "TitleSettingsLayer");
            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.parent, Is.EqualTo(instance.transform));

            view.SetSettingsVisible(true);
            Assert.That(instance.transform.GetSiblingIndex(), Is.Zero);
            Assert.That(settings.parent, Is.EqualTo(canvasObject.transform));
            Assert.That(
                settings.GetSiblingIndex(),
                Is.EqualTo(canvasObject.transform.childCount - 1));

            view.SetSettingsVisible(false);
            Assert.That(instance.transform.GetSiblingIndex(), Is.Zero);
            Assert.That(settings.parent, Is.EqualTo(instance.transform));
        }
        finally
        {
            Object.DestroyImmediate(canvasObject);
        }
    }

    private static void InvokePrivate(object target, string methodName)
    {
        target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(target, null);
    }

    private static void SetPrivateField(
        object target,
        string fieldName,
        object value)
    {
        target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(target, value);
    }

    private static Transform FindDescendant(
        Transform parent,
        string targetName)
    {
        for (int index = 0; index < parent.childCount; index++)
        {
            Transform child = parent.GetChild(index);
            if (child.name == targetName)
            {
                return child;
            }

            Transform nested = FindDescendant(child, targetName);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }
}
