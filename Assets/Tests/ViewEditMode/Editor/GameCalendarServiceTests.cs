using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.Gameplay.Progression;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CityFlow.Tests
{
    public sealed class GameCalendarServiceTests
    {
        [Test]
        public void DefaultSettings_UseThreeMinuteDay()
        {
            GameTimeSettingsSO settings =
                Resources.Load<GameTimeSettingsSO>(
                    "CityFlow/GameTimeSettings");

            Assert.That(settings, Is.Not.Null);
            Assert.That(
                settings.RealMinutesPerGameDay,
                Is.EqualTo(3f));
            Assert.That(
                settings.RealSecondsPerGameDay,
                Is.EqualTo(180f));
            Assert.That(
                settings.RealSecondsPerGameHour,
                Is.EqualTo(7.5f));
        }

        [Test]
        public void NewGame_StartsAtSevenInTheMorning()
        {
            GameObject owner = new GameObject("GameCalendarProgressTest");

            try
            {
                GameCalendarService calendar =
                    owner.AddComponent<GameCalendarService>();
                calendar.Initialize(CreateServices());

                Assert.That(calendar.Hour, Is.EqualTo(7));
                Assert.That(
                    calendar.TimeOfDay01,
                    Is.EqualTo(7f / 24f)
                        .Within(0.000001f));
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
                Assert.That(
                    calendar.RealSecondsPerGameHour,
                    Is.EqualTo(60f));
                Assert.That(
                    calendar.RealSecondsPerGameDay,
                    Is.EqualTo(1440f));
                Assert.That(calendar.HoursPerDay, Is.EqualTo(24));
                Assert.That(calendar.Hour, Is.EqualTo(7));
                Assert.That(
                    calendar.TimeOfDay01,
                    Is.EqualTo(7f / 24f).Within(0.000001f));
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void RestoreSnapshot_KeepsSavedHour()
        {
            GameObject owner = new GameObject("GameCalendarRestoreTest");

            try
            {
                GameCalendarService calendar =
                    owner.AddComponent<GameCalendarService>();
                calendar.Initialize(CreateServices());

                calendar.RestoreSnapshot(new GameCalendarSaveData
                {
                    Year = 2,
                    Month = 3,
                    Day = 4,
                    Hour = 21,
                    TotalMonths = 15,
                    TotalDays = 400,
                    AccumulatedRealSeconds = 0f
                });

                Assert.That(calendar.Hour, Is.EqualTo(21));
                Assert.That(
                    calendar.TimeOfDay01,
                    Is.EqualTo(21f / 24f).Within(0.000001f));
            }
            finally
            {
                Object.DestroyImmediate(owner);
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
