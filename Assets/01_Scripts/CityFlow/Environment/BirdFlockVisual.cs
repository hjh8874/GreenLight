using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityFlow.Environment
{
    [DisallowMultipleComponent]
    public sealed class BirdFlockVisual : EnvironmentVisualModule
    {
        private enum FlockFlightState
        {
            Dormant,
            Crossing
        }

        [Serializable]
        private sealed class BirdAgent
        {
            public Transform Transform { get; set; }
            public float FormationLateral { get; set; }
            public float FormationBehind { get; set; }
            public Vector2 FormationJitter { get; set; }
            public float Altitude { get; set; }
            public float Size { get; set; }
            public float FlapPhase { get; set; }
            public float FlapFrequency { get; set; }
        }

        [SerializeField] private BirdFlockProfileSO profile;
        [SerializeField] private Mesh birdMesh;
        [SerializeField] private Material birdMaterial;

        private BirdAgent[] birds = Array.Empty<BirdAgent>();
        private Material runtimeMaterial;
        private System.Random random;
        private FlockFlightState flightState;
        private Vector2 flockPosition;
        private Vector2 flightDirection = Vector2.right;
        private float flightSpeed;
        private float elapsedTime;
        private int visibleBirdCount;
        private long nextFlightGameHour = long.MinValue;
        private Vector2 crossingHalfExtents;
        private bool wasEnvironmentVisible;
        private bool nextFlightStartsFromLeft = true;

        public BirdFlockProfileSO Profile => profile;
        public int VisibleBirdCount => visibleBirdCount;

        public void RespawnFlock()
        {
            if (profile == null || birds.Length == 0)
            {
                return;
            }

            random = new System.Random(profile.DeterministicSeed);
            nextFlightStartsFromLeft = true;
            flightState = FlockFlightState.Dormant;
            nextFlightGameHour = long.MinValue;
            wasEnvironmentVisible = false;
            SetPoolVisible(0);
        }

        protected override void OnModuleInitialized()
        {
            if (profile == null || birdMesh == null || birdMaterial == null)
            {
                Debug.LogError(
                    "[BirdFlockVisual] Profile, mesh, or material is missing. " +
                    "Run the environment visual baker again.",
                    this);
                SetVisualEnabled(false);
                return;
            }

            random = new System.Random(profile.DeterministicSeed);
            runtimeMaterial = new Material(birdMaterial)
            {
                name = birdMaterial.name + " (Flock Runtime)"
            };
            ApplyMaterialColor(runtimeMaterial, profile.BirdColor);
            BuildBirdPool();
            RespawnFlock();
            Debug.Log(
                $"[BirdFlockVisual] Prepared {birds.Length} pooled birds.",
                this);
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
                HideFlock();
                return;
            }

            long currentGameHour = VisualSystem.CurrentGameHourIndex;
            if (!wasEnvironmentVisible)
            {
                wasEnvironmentVisible = true;
                nextFlightGameHour = currentGameHour;
            }

            int targetCount = profile.EvaluateVisibleBirdCount(
                VisualSystem.NormalizedZoom01);
            elapsedTime += unscaledDeltaTime;

            if (flightState == FlockFlightState.Dormant)
            {
                SetPoolVisible(0);
                if (currentGameHour >= nextFlightGameHour)
                {
                    CaptureWorldAnchor(anchor, rotation);
                    crossingHalfExtents = GetViewHalfExtents();
                    BeginCrossing(crossingHalfExtents, targetCount);
                }

                return;
            }

            SetPoolVisible(targetCount);
            flockPosition += flightDirection *
                             (flightSpeed * unscaledDeltaTime);
            UpdateBirdTransforms();
            if (!HasFinishedCrossing(crossingHalfExtents))
            {
                return;
            }

            flightState = FlockFlightState.Dormant;
            nextFlightGameHour = currentGameHour +
                                 profile.FlockIntervalGameHours;
            SetPoolVisible(0);
        }

        protected override void OnVisualStateChanged(bool isEnabled)
        {
            if (!isEnabled)
            {
                HideFlock();
            }
        }

        protected override void OnModuleShutdown()
        {
            for (int index = 0; index < birds.Length; index++)
            {
                Transform birdTransform = birds[index]?.Transform;
                if (birdTransform != null)
                {
                    Destroy(birdTransform.gameObject);
                }
            }

            birds = Array.Empty<BirdAgent>();
            visibleBirdCount = 0;
            if (runtimeMaterial != null)
            {
                Destroy(runtimeMaterial);
                runtimeMaterial = null;
            }
        }

        private void BuildBirdPool()
        {
            birds = new BirdAgent[profile.MaximumBirds];
            for (int index = 0; index < birds.Length; index++)
            {
                GameObject birdObject = new($"Bird {index + 1:00}");
                birdObject.transform.SetParent(transform, false);
                MeshFilter filter = birdObject.AddComponent<MeshFilter>();
                filter.sharedMesh = birdMesh;

                MeshRenderer renderer = birdObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = runtimeMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

                birds[index] = new BirdAgent
                {
                    Transform = birdObject.transform
                };
                birdObject.SetActive(false);
            }
        }

        private void BeginCrossing(Vector2 halfExtents, int targetCount)
        {
            PrepareFormation();
            flightState = FlockFlightState.Crossing;

            bool startsFromLeft = nextFlightStartsFromLeft;
            nextFlightStartsFromLeft = !nextFlightStartsFromLeft;
            float horizontalSign = startsFromLeft ? 1f : -1f;
            flightDirection = new Vector2(
                horizontalSign,
                Range(-0.18f, 0.18f)).normalized;

            float paddedHorizontal = halfExtents.x + profile.EdgePadding;
            flockPosition = new Vector2(
                startsFromLeft ? -paddedHorizontal : paddedHorizontal,
                Range(-halfExtents.y * 0.65f, halfExtents.y * 0.65f));

            float crossingSeconds = Range(
                profile.MinimumCrossingSeconds,
                profile.MaximumCrossingSeconds);
            float horizontalDistance = paddedHorizontal * 2f;
            flightSpeed = horizontalDistance /
                          Mathf.Max(0.5f, crossingSeconds) /
                          Mathf.Max(0.1f, Mathf.Abs(flightDirection.x));
            SetPoolVisible(targetCount);
            UpdateBirdTransforms();
        }

        private void PrepareFormation()
        {
            for (int index = 0; index < birds.Length; index++)
            {
                BirdAgent bird = birds[index];
                if (index == 0)
                {
                    bird.FormationLateral = 0f;
                    bird.FormationBehind = 0f;
                }
                else
                {
                    int row = (index + 1) / 2;
                    float side = index % 2 == 1 ? -1f : 1f;
                    bird.FormationLateral =
                        side * row * profile.FormationSpacing;
                    bird.FormationBehind =
                        -row * profile.FormationSpacing;
                }

                bird.FormationJitter = new Vector2(
                    Range(-profile.FormationJitter, profile.FormationJitter),
                    Range(-profile.FormationJitter, profile.FormationJitter));
                bird.Altitude = Range(
                    profile.MinimumAltitude,
                    profile.MaximumAltitude);
                bird.Size = Range(profile.MinimumSize, profile.MaximumSize);
                bird.FlapPhase = Range(0f, Mathf.PI * 2f);
                bird.FlapFrequency = Range(
                    profile.MinimumFlapFrequency,
                    profile.MaximumFlapFrequency);
            }
        }

        private void UpdateBirdTransforms()
        {
            Vector2 lateralAxis = new(
                -flightDirection.y,
                flightDirection.x);
            float headingDegrees = Vector2.SignedAngle(
                Vector2.up,
                flightDirection);

            for (int index = 0; index < visibleBirdCount; index++)
            {
                BirdAgent bird = birds[index];
                Vector2 position = flockPosition +
                                   lateralAxis * bird.FormationLateral +
                                   flightDirection * bird.FormationBehind +
                                   bird.FormationJitter;
                float flapWave = Mathf.Sin(
                    elapsedTime * bird.FlapFrequency * Mathf.PI * 2f +
                    bird.FlapPhase);
                bird.Transform.localPosition = new Vector3(
                    position.x,
                    position.y,
                    -(bird.Altitude + flapWave * profile.BobHeight));
                bird.Transform.localRotation = Quaternion.Euler(
                    0f,
                    0f,
                    headingDegrees);

                float wingScale = Mathf.Lerp(
                    profile.MinimumWingScale,
                    1f,
                    flapWave * 0.5f + 0.5f);
                bird.Transform.localScale = new Vector3(
                    bird.Size * wingScale,
                    bird.Size,
                    bird.Size);
            }
        }

        private bool HasFinishedCrossing(Vector2 halfExtents)
        {
            float exit = halfExtents.x + profile.EdgePadding;
            return flightDirection.x > 0f
                ? flockPosition.x > exit
                : flockPosition.x < -exit;
        }

        private void HideFlock()
        {
            wasEnvironmentVisible = false;
            flightState = FlockFlightState.Dormant;
            nextFlightGameHour = long.MinValue;
            SetPoolVisible(0);
        }

        private void CaptureWorldAnchor(
            Vector3 anchorPosition,
            Quaternion anchorRotation)
        {
            transform.SetPositionAndRotation(anchorPosition, anchorRotation);
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

        private void SetPoolVisible(int targetCount)
        {
            int clampedCount = Mathf.Clamp(targetCount, 0, birds.Length);
            if (visibleBirdCount == clampedCount)
            {
                return;
            }

            for (int index = 0; index < birds.Length; index++)
            {
                bool shouldBeActive = index < clampedCount;
                GameObject birdObject = birds[index].Transform.gameObject;
                if (birdObject.activeSelf != shouldBeActive)
                {
                    birdObject.SetActive(shouldBeActive);
                }
            }

            visibleBirdCount = clampedCount;
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
