using CityFlow.UI;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Tests.ViewEditMode
{
    public sealed class AudioSettingsPanelControllerTests
    {
        private const string TestPreferenceKey =
            "Codex_AudioSettingsPanelControllerTests_Volume";

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(TestPreferenceKey);
        }

        [Test]
        public void LoadVolumePreference_UsesThirtyPercentForFirstRun()
        {
            PlayerPrefs.DeleteKey(TestPreferenceKey);

            Assert.That(
                AudioSettingsPanelController.LoadVolumePreference(
                    TestPreferenceKey),
                Is.EqualTo(0.3f).Within(0.0001f));
        }

        [Test]
        public void LoadVolumePreference_PreservesPreviouslySavedValue()
        {
            PlayerPrefs.SetFloat(TestPreferenceKey, 0.72f);

            Assert.That(
                AudioSettingsPanelController.LoadVolumePreference(
                    TestPreferenceKey),
                Is.EqualTo(0.72f).Within(0.0001f));
        }
    }
}
