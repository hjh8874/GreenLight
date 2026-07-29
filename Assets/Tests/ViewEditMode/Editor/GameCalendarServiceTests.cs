using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Gameplay.Progression;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CityFlow.Tests
{
    public sealed class GameCalendarServiceTests
    {
        [Test]
        public void DefaultSettings_UseTwelveMinuteDay()
        {
            GameTimeSettingsSO settings =
                Resources.Load<GameTimeSettingsSO>(
                    "CityFlow/GameTimeSettings");

            Assert.That(settings, Is.Not.Null);
            Assert.That(
                settings.RealMinutesPerGameDay,
                Is.EqualTo(12f));
            Assert.That(
                settings.RealSecondsPerGameDay,
                Is.EqualTo(720f));
            Assert.That(
                settings.RealSecondsPerGameHour,
                Is.EqualTo(30f));
        }

        [Test]
        public void OfflineAdvance_TwelveMinutesAdvancesOneDay()
        {
            GameObject owner = new GameObject("GameCalendarTest");

            try
            {
                GameCalendarService calendar =
                    owner.AddComponent<GameCalendarService>();
                calendar.Initialize(CreateServices());

                calendar.AdvanceOffline(720d);

                Assert.That(calendar.Year, Is.EqualTo(1));
                Assert.That(calendar.Month, Is.EqualTo(1));
                Assert.That(calendar.Day, Is.EqualTo(2));
                Assert.That(calendar.Hour, Is.EqualTo(0));
                Assert.That(calendar.TotalDays, Is.EqualTo(1L));
                Assert.That(calendar.TimeOfDay01, Is.EqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void InspectorOverride_ControlsHourDurationAndSkyApi()
        {
            GameObject owner = new GameObject("GameCalendarOverrideTest");
            GameTimeSettingsSO settings = CreateSettings(24f);

            try
            {
                GameCalendarService calendar =
                    owner.AddComponent<GameCalendarService>();
                var serializedCalendar = new SerializedObject(calendar);
                serializedCalendar.FindProperty("timeSettings")
                    .objectReferenceValue = settings;
                serializedCalendar.ApplyModifiedPropertiesWithoutUndo();

                calendar.Initialize(CreateServices());
                calendar.AdvanceOffline(360d);

                Assert.That(
                    calendar.RealSecondsPerGameHour,
                    Is.EqualTo(60f));
                Assert.That(
                    calendar.RealSecondsPerGameDay,
                    Is.EqualTo(1440f));
                Assert.That(calendar.HoursPerDay, Is.EqualTo(24));
                Assert.That(calendar.Hour, Is.EqualTo(6));
                Assert.That(calendar.TimeOfDay01, Is.EqualTo(0.25f));
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }

        private static CityFlowServices CreateServices()
        {
            return new CityFlowServices(
                new SimEventHub(),
                null,
                null);
        }

        private static GameTimeSettingsSO CreateSettings(
            float realMinutesPerGameDay)
        {
            GameTimeSettingsSO settings =
                ScriptableObject.CreateInstance<GameTimeSettingsSO>();
            var serializedSettings = new SerializedObject(settings);
            serializedSettings.FindProperty("realMinutesPerGameDay")
                .floatValue = realMinutesPerGameDay;
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            return settings;
        }
    }
}
