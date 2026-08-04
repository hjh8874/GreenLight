using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Environment
{
    [Serializable]
    public sealed class TimeOfDaySkyKeyframe
    {
        [SerializeField, Range(0f, 23.999f)] private float hour;
        [SerializeField] private Material skyboxMaterial;
        [SerializeField, Range(0f, 360f)] private float skyRotation;
        [SerializeField, Min(0f)] private float skyExposure = 1f;
        [Tooltip("Duration before this keyframe in game hours. The previous sky remains stable outside this window.")]
        [SerializeField, Min(0f)] private float transitionHours = 0.5f;

        [Header("Directional Light")]
        [SerializeField] private Color lightColor = Color.white;
        [SerializeField, Min(0f)] private float lightIntensity = 1f;
        [SerializeField] private Vector3 lightEuler = new(50f, -30f, 0f);
        [SerializeField, Range(0f, 1f)] private float shadowStrength = 0.8f;

        [Header("Ambient Trilight")]
        [SerializeField] private Color ambientSkyColor = Color.gray;
        [SerializeField] private Color ambientEquatorColor = Color.gray;
        [SerializeField] private Color ambientGroundColor = Color.gray;
        [SerializeField, Min(0f)] private float ambientIntensity = 1f;

        public float Hour => NormalizeHour(hour);
        public Material SkyboxMaterial => skyboxMaterial;
        public float SkyRotation => skyRotation;
        public float SkyExposure => skyExposure;
        public float TransitionHours => transitionHours;
        public Color LightColor => lightColor;
        public float LightIntensity => lightIntensity;
        public Vector3 LightEuler => lightEuler;
        public float ShadowStrength => shadowStrength;
        public Color AmbientSkyColor => ambientSkyColor;
        public Color AmbientEquatorColor => ambientEquatorColor;
        public Color AmbientGroundColor => ambientGroundColor;
        public float AmbientIntensity => ambientIntensity;

        internal void Sanitize()
        {
            hour = Mathf.Clamp(hour, 0f, 23.999f);
            skyRotation = Mathf.Repeat(skyRotation, 360f);
            skyExposure = Mathf.Max(0f, skyExposure);
            transitionHours = Mathf.Max(0f, transitionHours);
            lightIntensity = Mathf.Max(0f, lightIntensity);
            shadowStrength = Mathf.Clamp01(shadowStrength);
            ambientIntensity = Mathf.Max(0f, ambientIntensity);
        }

        private static float NormalizeHour(float value)
        {
            return Mathf.Repeat(value, TimeOfDaySkyProfile.HoursPerDay);
        }
    }

    public readonly struct TimeOfDaySkyEvaluation
    {
        public TimeOfDaySkyEvaluation(
            TimeOfDaySkyKeyframe current,
            TimeOfDaySkyKeyframe next,
            float segmentProgress,
            float skyBlend)
        {
            Current = current;
            Next = next;
            SegmentProgress = segmentProgress;
            SkyBlend = skyBlend;
        }

        public TimeOfDaySkyKeyframe Current { get; }
        public TimeOfDaySkyKeyframe Next { get; }
        public float SegmentProgress { get; }
        public float SkyBlend { get; }
    }

    [CreateAssetMenu(
        fileName = "TimeOfDaySkyProfile",
        menuName = "CityFlow/Environment/Time Of Day Sky Profile")]
    public sealed class TimeOfDaySkyProfile : ScriptableObject
    {
        public const float HoursPerDay = 24f;

        [SerializeField] private List<TimeOfDaySkyKeyframe> keyframes = new();

        public IReadOnlyList<TimeOfDaySkyKeyframe> Keyframes => keyframes;

        public bool TryGetKeyframe(
            float hour,
            out TimeOfDaySkyKeyframe keyframe)
        {
            keyframe = null;
            if (keyframes == null)
            {
                return false;
            }

            float normalizedHour = Mathf.Repeat(hour, HoursPerDay);
            for (int i = 0; i < keyframes.Count; i++)
            {
                TimeOfDaySkyKeyframe candidate = keyframes[i];
                if (candidate != null &&
                    Mathf.Approximately(
                        candidate.Hour,
                        normalizedHour))
                {
                    keyframe = candidate;
                    return true;
                }
            }

            return false;
        }

        public bool TryEvaluate(
            float gameHour,
            out TimeOfDaySkyEvaluation evaluation)
        {
            evaluation = default;
            if (keyframes == null || keyframes.Count == 0)
            {
                return false;
            }

            float normalizedHour = Mathf.Repeat(gameHour, HoursPerDay);
            TimeOfDaySkyKeyframe current = null;
            TimeOfDaySkyKeyframe next = null;
            float currentHour = float.NegativeInfinity;
            float nextHour = float.PositiveInfinity;
            float latestHour = float.NegativeInfinity;
            float earliestHour = float.PositiveInfinity;
            TimeOfDaySkyKeyframe latest = null;
            TimeOfDaySkyKeyframe earliest = null;

            for (int i = 0; i < keyframes.Count; i++)
            {
                TimeOfDaySkyKeyframe keyframe = keyframes[i];
                if (keyframe == null)
                {
                    continue;
                }

                float hour = keyframe.Hour;
                if (hour >= latestHour)
                {
                    latestHour = hour;
                    latest = keyframe;
                }

                if (hour < earliestHour)
                {
                    earliestHour = hour;
                    earliest = keyframe;
                }

                if (hour <= normalizedHour && hour >= currentHour)
                {
                    currentHour = hour;
                    current = keyframe;
                }
                else if (hour > normalizedHour && hour < nextHour)
                {
                    nextHour = hour;
                    next = keyframe;
                }
            }

            current ??= latest;
            next ??= earliest;
            if (current == null || next == null)
            {
                return false;
            }

            float segmentHours = ForwardDistance(current.Hour, next.Hour);
            if (segmentHours <= Mathf.Epsilon)
            {
                evaluation = new TimeOfDaySkyEvaluation(
                    current,
                    current,
                    0f,
                    0f);
                return true;
            }

            float elapsedHours = ForwardDistance(
                current.Hour,
                normalizedHour);
            float segmentProgress = Mathf.Clamp01(
                elapsedHours / segmentHours);
            segmentProgress = Mathf.SmoothStep(
                0f,
                1f,
                segmentProgress);

            float transitionHours = Mathf.Min(
                next.TransitionHours,
                segmentHours);
            float skyBlend = 0f;
            if (transitionHours > Mathf.Epsilon)
            {
                float transitionStart = segmentHours - transitionHours;
                float transitionProgress = Mathf.Clamp01(
                    (elapsedHours - transitionStart) /
                    transitionHours);
                skyBlend = Mathf.SmoothStep(
                    0f,
                    1f,
                    transitionProgress);
            }

            evaluation = new TimeOfDaySkyEvaluation(
                current,
                next,
                segmentProgress,
                skyBlend);
            return true;
        }

        private static float ForwardDistance(float fromHour, float toHour)
        {
            return Mathf.Repeat(
                toHour - fromHour,
                HoursPerDay);
        }

        private void OnValidate()
        {
            if (keyframes == null)
            {
                keyframes = new List<TimeOfDaySkyKeyframe>();
                return;
            }

            keyframes.RemoveAll(keyframe => keyframe == null);
            for (int i = 0; i < keyframes.Count; i++)
            {
                keyframes[i].Sanitize();
            }

            keyframes.Sort(
                (left, right) =>
                    left.Hour.CompareTo(right.Hour));
        }
    }
}
