using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityFlow.Environment
{
    [DisallowMultipleComponent]
    public sealed class VillageBirdDepartureVisual : EnvironmentVisualModule
    {
        private enum BirdState
        {
            WaitingToArrive,
            Arriving,
            Perched,
            TakingOff,
            Departing
        }

        private enum ViewEdge
        {
            Left,
            Right,
            Bottom,
            Top
        }

        private sealed class VillageBirdAgent
        {
            public Transform Visual { get; set; }
            public BirdState State { get; set; }
            public Vector2 JourneyStart { get; set; }
            public Vector2 JourneyEnd { get; set; }
            public Vector2 ExitTarget { get; set; }
            public Vector2 Position { get; set; }
            public Vector2 ViewHalfExtents { get; set; }
            public Vector3 WorldAnchor { get; set; }
            public Quaternion WorldRotation { get; set; }
            public float StartAltitude { get; set; }
            public float EndAltitude { get; set; }
            public float StateElapsedSeconds { get; set; }
            public float StateDurationSeconds { get; set; }
            public float BirdSize { get; set; }
            public float FlapPhase { get; set; }
            public float FlapFrequency { get; set; }
            public float HeadingDegrees { get; set; }
            public ViewEdge ArrivalEdge { get; set; }
        }

        [SerializeField] private BirdFlockProfileSO profile;
        [SerializeField] private Mesh birdMesh;
        [SerializeField] private Material birdMaterial;

        private readonly List<VillageBirdAgent> birds = new();

        private Material runtimeMaterial;
        private System.Random random;
        private float elapsedTime;
        private bool wasEnvironmentVisible;

        public BirdFlockProfileSO Profile => profile;
        public int VisibleBirdCount { get; private set; }
        public bool IsBirdVisible => VisibleBirdCount > 0;

        protected override void OnModuleInitialized()
        {
            if (profile == null || birdMesh == null || birdMaterial == null)
            {
                Debug.LogError(
                    "[VillageBirdDepartureVisual] Profile, mesh, or material " +
                    "is missing. Run the environment visual baker again.",
                    this);
                SetVisualEnabled(false);
                return;
            }

            random = new System.Random(profile.DeterministicSeed + 104729);
            runtimeMaterial = new Material(birdMaterial)
            {
                name = birdMaterial.name + " (Village Birds Runtime)"
            };
            ApplyMaterialColor(runtimeMaterial, profile.BirdColor);
            CreateBirdVisuals();
            HideAllBirds();
        }

        protected override void OnVisualTick(float unscaledDeltaTime)
        {
            if (profile == null || VisualSystem == null)
            {
                return;
            }

            bool shouldShow = profile.IsVisibleAtHour(
                                  VisualSystem.CurrentHour) &&
                              (!profile.HideInDriveView ||
                               !VisualSystem.IsDriveViewActive);
            if (!shouldShow ||
                !VisualSystem.TryGetViewAnchor(
                    out Vector3 anchor,
                    out Quaternion rotation))
            {
                ResetForHiddenEnvironment();
                return;
            }

            if (!wasEnvironmentVisible)
            {
                BeginDaytimeActivity();
            }

            elapsedTime += unscaledDeltaTime;
            Vector2 halfExtents = GetViewHalfExtents();
            VisibleBirdCount = 0;
            for (int index = 0; index < birds.Count; index++)
            {
                UpdateBird(
                    birds[index],
                    halfExtents,
                    anchor,
                    rotation,
                    unscaledDeltaTime);
                if (birds[index].Visual.gameObject.activeSelf)
                {
                    VisibleBirdCount++;
                }
            }
        }

        protected override void OnVisualStateChanged(bool isEnabled)
        {
            if (!isEnabled)
            {
                ResetForHiddenEnvironment();
            }
        }

        protected override void OnModuleShutdown()
        {
            for (int index = 0; index < birds.Count; index++)
            {
                if (birds[index].Visual != null)
                {
                    Destroy(birds[index].Visual.gameObject);
                }
            }

            birds.Clear();
            if (runtimeMaterial != null)
            {
                Destroy(runtimeMaterial);
                runtimeMaterial = null;
            }
        }

        private void CreateBirdVisuals()
        {
            for (int index = 0; index < profile.VillageBirdCount; index++)
            {
                GameObject birdObject = new($"Village Bird {index + 1}");
                birdObject.transform.SetParent(transform, false);
                MeshFilter filter = birdObject.AddComponent<MeshFilter>();
                filter.sharedMesh = birdMesh;

                MeshRenderer renderer = birdObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = runtimeMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

                birds.Add(new VillageBirdAgent
                {
                    Visual = birdObject.transform,
                    BirdSize = Range(profile.MinimumSize, profile.MaximumSize),
                    FlapPhase = Range(0f, Mathf.PI * 2f),
                    FlapFrequency = Range(
                        profile.MinimumFlapFrequency,
                        profile.MaximumFlapFrequency)
                });
            }
        }

        private void BeginDaytimeActivity()
        {
            wasEnvironmentVisible = true;
            for (int index = 0; index < birds.Count; index++)
            {
                VillageBirdAgent bird = birds[index];
                bird.State = BirdState.WaitingToArrive;
                bird.StateElapsedSeconds = 0f;
                bird.StateDurationSeconds =
                    index * profile.VillageBirdArrivalStaggerSeconds +
                    Range(0f, 0.35f);
                SetBirdVisible(bird, false);
            }
        }

        private void UpdateBird(
            VillageBirdAgent bird,
            Vector2 halfExtents,
            Vector3 worldAnchor,
            Quaternion worldRotation,
            float deltaTime)
        {
            bird.StateElapsedSeconds += deltaTime;
            switch (bird.State)
            {
                case BirdState.WaitingToArrive:
                    if (bird.StateElapsedSeconds >= bird.StateDurationSeconds)
                    {
                        BeginArrival(
                            bird,
                            halfExtents,
                            worldAnchor,
                            worldRotation);
                    }
                    break;

                case BirdState.Arriving:
                    UpdateJourney(bird, true, true);
                    if (HasStateCompleted(bird))
                    {
                        BeginPerched(bird);
                    }
                    break;

                case BirdState.Perched:
                    ApplyBirdTransform(
                        bird,
                        bird.Position,
                        profile.VillageBirdPerchedAltitude,
                        0f);
                    if (HasStateCompleted(bird))
                    {
                        BeginTakeoff(bird);
                    }
                    break;

                case BirdState.TakingOff:
                    UpdateJourney(bird, true, true);
                    if (HasStateCompleted(bird))
                    {
                        BeginDeparture(bird);
                    }
                    break;

                case BirdState.Departing:
                    UpdateJourney(bird, true, false);
                    if (HasStateCompleted(bird))
                    {
                        BeginReturnWait(bird);
                    }
                    break;
            }
        }

        private void BeginArrival(
            VillageBirdAgent bird,
            Vector2 halfExtents,
            Vector3 worldAnchor,
            Quaternion worldRotation)
        {
            bird.ViewHalfExtents = halfExtents;
            bird.WorldAnchor = worldAnchor;
            bird.WorldRotation = worldRotation;
            bird.ArrivalEdge = RandomEdge();
            Vector2 landingPoint = RandomLandingPoint(halfExtents);
            ConfigureJourney(
                bird,
                BirdState.Arriving,
                OutsidePoint(halfExtents, bird.ArrivalEdge),
                landingPoint,
                Range(profile.MinimumAltitude, profile.MaximumAltitude),
                profile.VillageBirdPerchedAltitude,
                Range(
                    profile.VillageBirdMinimumArrivalSeconds,
                    profile.VillageBirdMaximumArrivalSeconds));
            SetBirdVisible(bird, true);
        }

        private void BeginPerched(VillageBirdAgent bird)
        {
            bird.State = BirdState.Perched;
            bird.Position = bird.JourneyEnd;
            bird.StateElapsedSeconds = 0f;
            bird.StateDurationSeconds = Range(
                profile.VillageBirdMinimumPerchedSeconds,
                profile.VillageBirdMaximumPerchedSeconds);
            bird.HeadingDegrees = Range(0f, 360f);
            ApplyBirdTransform(
                bird,
                bird.Position,
                profile.VillageBirdPerchedAltitude,
                0f);
        }

        private void BeginTakeoff(VillageBirdAgent bird)
        {
            ViewEdge exitEdge = RandomDifferentEdge(bird.ArrivalEdge);
            Vector2 outsideTarget = OutsidePoint(
                bird.ViewHalfExtents,
                exitEdge);
            Vector2 direction = (outsideTarget - bird.Position).normalized;
            float flightAltitude = Range(
                profile.MinimumAltitude,
                profile.MaximumAltitude);
            ConfigureJourney(
                bird,
                BirdState.TakingOff,
                bird.Position,
                bird.Position + direction * 0.45f,
                profile.VillageBirdPerchedAltitude,
                flightAltitude,
                profile.VillageBirdTakeoffSeconds);
            bird.ExitTarget = outsideTarget;
        }

        private void BeginDeparture(VillageBirdAgent bird)
        {
            Vector2 takeoffPosition = bird.Position;
            Vector2 outsideTarget = bird.ExitTarget;
            float flightAltitude = bird.EndAltitude;
            ConfigureJourney(
                bird,
                BirdState.Departing,
                takeoffPosition,
                outsideTarget,
                flightAltitude,
                flightAltitude,
                Range(
                    profile.VillageBirdMinimumDepartureSeconds,
                    profile.VillageBirdMaximumDepartureSeconds));
        }

        private void BeginReturnWait(VillageBirdAgent bird)
        {
            bird.State = BirdState.WaitingToArrive;
            bird.StateElapsedSeconds = 0f;
            bird.StateDurationSeconds = Range(
                profile.VillageBirdMinimumReturnDelaySeconds,
                profile.VillageBirdMaximumReturnDelaySeconds);
            SetBirdVisible(bird, false);
        }

        private void ConfigureJourney(
            VillageBirdAgent bird,
            BirdState state,
            Vector2 start,
            Vector2 end,
            float startAltitude,
            float endAltitude,
            float durationSeconds)
        {
            bird.State = state;
            bird.JourneyStart = start;
            bird.JourneyEnd = end;
            bird.Position = start;
            bird.StartAltitude = startAltitude;
            bird.EndAltitude = endAltitude;
            bird.StateElapsedSeconds = 0f;
            bird.StateDurationSeconds = Mathf.Max(0.1f, durationSeconds);
            Vector2 direction = end - start;
            if (direction.sqrMagnitude > 0.0001f)
            {
                bird.HeadingDegrees = Vector2.SignedAngle(
                    Vector2.up,
                    direction.normalized);
            }
        }

        private void UpdateJourney(
            VillageBirdAgent bird,
            bool useSmoothStep,
            bool changeAltitude)
        {
            float progress = Mathf.Clamp01(
                bird.StateElapsedSeconds / bird.StateDurationSeconds);
            float movementProgress = useSmoothStep
                ? Mathf.SmoothStep(0f, 1f, progress)
                : progress;
            bird.Position = Vector2.LerpUnclamped(
                bird.JourneyStart,
                bird.JourneyEnd,
                movementProgress);
            float altitude = changeAltitude
                ? Mathf.Lerp(
                    bird.StartAltitude,
                    bird.EndAltitude,
                    movementProgress)
                : bird.EndAltitude;
            ApplyBirdTransform(bird, bird.Position, altitude, 1f);
        }

        private void ApplyBirdTransform(
            VillageBirdAgent bird,
            Vector2 position,
            float altitude,
            float flightAmount)
        {
            float flapWave = Mathf.Sin(
                elapsedTime * bird.FlapFrequency * Mathf.PI * 2f +
                bird.FlapPhase);
            float bob = flapWave * profile.BobHeight * flightAmount;
            Vector3 anchorRelativePosition = new(
                position.x,
                position.y,
                -(altitude + bob));
            bird.Visual.SetPositionAndRotation(
                bird.WorldAnchor +
                bird.WorldRotation * anchorRelativePosition,
                bird.WorldRotation *
                Quaternion.Euler(0f, 0f, bird.HeadingDegrees));

            float flyingWingScale = Mathf.Lerp(
                profile.MinimumWingScale,
                1f,
                flapWave * 0.5f + 0.5f);
            float wingScale = Mathf.Lerp(
                profile.MinimumWingScale,
                flyingWingScale,
                flightAmount);
            bird.Visual.localScale = new Vector3(
                bird.BirdSize * wingScale,
                bird.BirdSize,
                bird.BirdSize);
        }

        private Vector2 RandomLandingPoint(Vector2 halfExtents)
        {
            float areaRatio = profile.VillageBirdSpawnAreaRatio;
            return new Vector2(
                Range(-halfExtents.x * areaRatio, halfExtents.x * areaRatio),
                Range(-halfExtents.y * areaRatio, halfExtents.y * areaRatio));
        }

        private Vector2 OutsidePoint(Vector2 halfExtents, ViewEdge edge)
        {
            float horizontalLimit = halfExtents.x + profile.EdgePadding;
            float verticalLimit = halfExtents.y + profile.EdgePadding;
            return edge switch
            {
                ViewEdge.Left => new Vector2(
                    -horizontalLimit,
                    Range(-halfExtents.y, halfExtents.y)),
                ViewEdge.Right => new Vector2(
                    horizontalLimit,
                    Range(-halfExtents.y, halfExtents.y)),
                ViewEdge.Bottom => new Vector2(
                    Range(-halfExtents.x, halfExtents.x),
                    -verticalLimit),
                _ => new Vector2(
                    Range(-halfExtents.x, halfExtents.x),
                    verticalLimit)
            };
        }

        private ViewEdge RandomEdge()
        {
            return (ViewEdge)random.Next(0, 4);
        }

        private ViewEdge RandomDifferentEdge(ViewEdge previousEdge)
        {
            ViewEdge edge = RandomEdge();
            while (edge == previousEdge)
            {
                edge = RandomEdge();
            }

            return edge;
        }

        private static bool HasStateCompleted(VillageBirdAgent bird)
        {
            return bird.StateElapsedSeconds >= bird.StateDurationSeconds;
        }

        private void ResetForHiddenEnvironment()
        {
            wasEnvironmentVisible = false;
            VisibleBirdCount = 0;
            HideAllBirds();
        }

        private void HideAllBirds()
        {
            for (int index = 0; index < birds.Count; index++)
            {
                SetBirdVisible(birds[index], false);
            }
        }

        private static void SetBirdVisible(
            VillageBirdAgent bird,
            bool isVisible)
        {
            if (bird.Visual != null &&
                bird.Visual.gameObject.activeSelf != isVisible)
            {
                bird.Visual.gameObject.SetActive(isVisible);
            }
        }

        private Vector2 GetViewHalfExtents()
        {
            Camera viewCamera = VisualSystem?.ActiveCamera;
            if (viewCamera == null)
            {
                return new Vector2(8f, 6f);
            }

            float verticalExtent = viewCamera.orthographic
                ? viewCamera.orthographicSize
                : 6f;
            verticalExtent *= profile.ViewAreaMultiplier;
            return new Vector2(
                verticalExtent * Mathf.Max(1f, viewCamera.aspect),
                verticalExtent);
        }

        private float Range(float minimum, float maximum)
        {
            if (random == null || Mathf.Approximately(minimum, maximum))
            {
                return minimum;
            }

            return Mathf.Lerp(
                minimum,
                maximum,
                (float)random.NextDouble());
        }

        private static void ApplyMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

#if UNITY_EDITOR
        public void EditorConfigure(
            BirdFlockProfileSO profileAsset,
            Mesh meshAsset,
            Material materialAsset)
        {
            profile = profileAsset;
            birdMesh = meshAsset;
            birdMaterial = materialAsset;
        }
#endif

        // Unity setup:
        // The environment baker assigns the profile, mesh, and material.
    }
}
