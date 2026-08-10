using System.Reflection;
using CityFlow.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public class FloatingPanelControllerTests
{
    [Test]
    public void ApplyFloatingToggleReadability_UsesHighContrastVisuals()
    {
        var owner = new GameObject("FloatingPanel");
        var toggleObject = new GameObject("Floating", typeof(Toggle));
        var backgroundObject = new GameObject(
            "Background",
            typeof(RectTransform),
            typeof(Image));
        var checkmarkObject = new GameObject(
            "Checkmark",
            typeof(RectTransform),
            typeof(Image));
        var labelObject = new GameObject(
            "Label",
            typeof(RectTransform),
            typeof(TextMeshProUGUI));

        try
        {
            toggleObject.transform.SetParent(owner.transform, false);
            backgroundObject.transform.SetParent(toggleObject.transform, false);
            checkmarkObject.transform.SetParent(
                backgroundObject.transform,
                false);
            labelObject.transform.SetParent(toggleObject.transform, false);

            Toggle toggle = toggleObject.GetComponent<Toggle>();
            Image background = backgroundObject.GetComponent<Image>();
            Image checkmark = checkmarkObject.GetComponent<Image>();
            toggle.targetGraphic = background;
            toggle.graphic = checkmark;

            FloatingPanelController controller =
                owner.AddComponent<FloatingPanelController>();
            SetPrivate(controller, "tglFloatingMode", toggle);
            typeof(FloatingPanelController).GetMethod(
                    "ApplyFloatingToggleReadability",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, null);

            TMP_Text label = labelObject.GetComponent<TMP_Text>();
            Assert.That(label.color, Is.EqualTo(Color.white));
            Assert.That(label.fontSize, Is.EqualTo(16f));
            Assert.That(label.fontStyle, Is.EqualTo(FontStyles.Bold));
            Assert.That(background.GetComponent<Outline>(), Is.Not.Null);
            Assert.That(checkmark.color.g, Is.GreaterThan(0.9f));
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    private static void SetPrivate(
        object target,
        string fieldName,
        object value)
    {
        target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(target, value);
    }
}
