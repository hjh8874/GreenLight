using System.Reflection;
using CityFlow.UI;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public class SettingsPanelControllerTests
{
    [Test]
    public void RemoveRedundantCongestionToggle_DisablesSettingsChildOnly()
    {
        var hud = new GameObject("TopHud");
        var hudToggle = new GameObject("CongestionViewToggle");
        hudToggle.transform.SetParent(hud.transform, false);

        var settings = new GameObject("Settings");
        var settingsToggle = new GameObject("CongestionViewToggle");
        settingsToggle.transform.SetParent(settings.transform, false);

        try
        {
            var controller = settings.AddComponent<SettingsPanelController>();
            typeof(SettingsPanelController).GetMethod(
                    "RemoveRedundantCongestionToggle",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, null);

            Assert.IsFalse(settingsToggle.activeSelf);
            Assert.IsTrue(hudToggle.activeSelf);
        }
        finally
        {
            Object.DestroyImmediate(settings);
            Object.DestroyImmediate(hud);
        }
    }
}
