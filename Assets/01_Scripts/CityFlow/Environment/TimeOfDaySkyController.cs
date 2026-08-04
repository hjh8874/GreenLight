using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.View;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityFlow.Environment
{
    [DisallowMultipleComponent]
    public sealed class TimeOfDaySkyController :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        private static readonly int TexAId = Shader.PropertyToID("_TexA");
        private static readonly int TexBId = Shader.PropertyToID("_TexB");
        private static readonly int BlendId = Shader.PropertyToID("_Blend");
        private static readonly int ExposureAId =
            Shader.PropertyToID("_ExposureA");
        private static readonly int ExposureBId =
            Shader.PropertyToID("_ExposureB");
        private static readonly int HorizonRotationId =
            Shader.PropertyToID("_HorizonRotation");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");

        private const float SunriseHour = 6f;
        private const float SunsetHour = 18f;
        private const float HalfDayHours = 12f;

        private static TimeOfDaySkyController activeOwner;

        [Header("Sky Cycle")]
        [SerializeField] private TimeOfDaySkyProfile profile;
        [SerializeField] private Material blendSkyboxTemplate;
        [SerializeField] private Material celestialOverlayTemplate;

        [Header("Lighting")]
        [SerializeField] private Light keyLight;

        [Header("Celestial Cycle")]
        [SerializeField, Range(0f, 1f)] private float horizonViewportY =
            0.5f;
        [SerializeField, Min(0.01f)] private float celestialCameraDepth = 10f;
        [SerializeField] private Color sunVisualColor =
            new(1f, 0.82f, 0.32f, 1f);
        [SerializeField] private Color moonVisualColor =
            new(0.82f, 0.9f, 1f, 1f);
        [SerializeField, Min(0.1f)] private float sunWorldDiameter = 2.3f;
        [SerializeField, Min(0.1f)] private float moonWorldDiameter = 2.1f;
        [SerializeField, Range(0.1f, 1f)] private float minimumScreenSizeRatio =
            0.7f;
        [SerializeField, Min(0f)] private float moonHorizonIntensity = 0.1f;

        [Header("Lifecycle")]
        [SerializeField] private bool restoreRenderSettingsOnDisable = true;

        private CityFlowServices services;
        private IGameCalendarService gameCalendar;
        private Material runtimeSkybox;
        private Material runtimeCelestialMaterial;
        private Mesh runtimeCelestialMesh;
        private GameObject runtimeCelestialObject;
        private bool ownsRenderSettings;
        private bool renderSettingsCaptured;
        private bool lightStateCaptured;
        private bool missingMaterialLogged;
        private Material previousSkybox;
        private Light previousSun;
        private AmbientMode previousAmbientMode;
        private Color previousAmbientSkyColor;
        private Color previousAmbientEquatorColor;
        private Color previousAmbientGroundColor;
        private float previousAmbientIntensity;
        private bool previousLightEnabled;
        private Color previousLightColor;
        private float previousLightIntensity;
        private float previousShadowStrength;
        private Quaternion previousLightRotation;
        private bool cameraRenderingSubscribed;
        private bool hasCurrentCycle;
        private CelestialCycleState currentCycle;
        private MainCityView mainCityView;

        public TimeOfDaySkyProfile Profile => profile;
        public Material BlendSkyboxTemplate => blendSkyboxTemplate;
        public Material CelestialOverlayTemplate =>
            celestialOverlayTemplate;
        public Light KeyLight => keyLight;

        public void Initialize(CityFlowServices newServices)
        {
            if (ReferenceEquals(services, newServices))
            {
                return;
            }

            UnbindServices();
            services = newServices;
            if (services == null)
            {
                return;
            }

            services.GameCalendarRegistered +=
                OnGameCalendarRegistered;

            if (services.GameCalendar != null)
            {
                BindGameCalendar(services.GameCalendar);
            }
        }

        private void OnEnable()
        {
            ActivateRenderSettings();
        }

        private void Update()
        {
            if (!ownsRenderSettings || gameCalendar == null)
            {
                return;
            }

            ApplyGameHour(
                gameCalendar.TimeOfDay01 *
                gameCalendar.HoursPerDay,
                false);
        }

        internal void ActivateRenderSettings()
        {
            SubscribeCameraRendering();
            if (!TryAcquireRenderSettings())
            {
                return;
            }

            if (gameCalendar != null)
            {
                ApplyGameHour(gameCalendar.Hour, true);
            }
        }

        private void OnDisable()
        {
            DeactivateRenderSettings();
        }

        internal void DeactivateRenderSettings()
        {
            UnsubscribeCameraRendering();
            ReleaseRenderSettings();
        }

        private void OnDestroy()
        {
            UnbindServices();
            DeactivateRenderSettings();
        }

        private void OnGameCalendarRegistered(
            IGameCalendarService calendar)
        {
            BindGameCalendar(calendar);
        }

        private void BindGameCalendar(
            IGameCalendarService calendar)
        {
            if (ReferenceEquals(gameCalendar, calendar))
            {
                return;
            }

            if (gameCalendar != null)
            {
                gameCalendar.HourChanged -= OnHourChanged;
            }

            gameCalendar = calendar;

            if (gameCalendar == null)
            {
                return;
            }

            gameCalendar.HourChanged += OnHourChanged;
            if (isActiveAndEnabled && ownsRenderSettings)
            {
                ApplyGameHour(gameCalendar.Hour, true);
            }
        }

        private void OnHourChanged(int hour)
        {
            if (isActiveAndEnabled && ownsRenderSettings)
            {
                ApplyGameHour(hour, true);
            }
        }

        private bool TryAcquireRenderSettings()
        {
            if (ownsRenderSettings)
            {
                return true;
            }

            if (activeOwner != null &&
                activeOwner != this)
            {
                Debug.LogError(
                    "[TimeOfDaySkyController] Only one active sky controller is allowed.",
                    this);
                enabled = false;
                return false;
            }

            if (profile == null)
            {
                Debug.LogError(
                    "[TimeOfDaySkyController] A sky cycle profile is required.",
                    this);
                enabled = false;
                return false;
            }

            if (!EnsureRuntimeMaterial())
            {
                enabled = false;
                return false;
            }

            if (!EnsureCelestialVisual())
            {
                enabled = false;
                return false;
            }

            activeOwner = this;
            ownsRenderSettings = true;
            CaptureRenderSettings();
            return true;
        }

        private bool EnsureRuntimeMaterial()
        {
            if (runtimeSkybox != null)
            {
                return true;
            }

            if (blendSkyboxTemplate == null ||
                blendSkyboxTemplate.shader == null ||
                !blendSkyboxTemplate.HasProperty(TexAId) ||
                !blendSkyboxTemplate.HasProperty(TexBId) ||
                !blendSkyboxTemplate.HasProperty(BlendId) ||
                !blendSkyboxTemplate.HasProperty(
                    HorizonRotationId))
            {
                if (!missingMaterialLogged)
                {
                    Debug.LogError(
                        "[TimeOfDaySkyController] A valid skybox blend material is required.",
                        this);
                    missingMaterialLogged = true;
                }

                return false;
            }

            runtimeSkybox = new Material(blendSkyboxTemplate)
            {
                name = $"{blendSkyboxTemplate.name} (Runtime)",
                hideFlags = HideFlags.DontSave
            };

            if (runtimeSkybox.GetTexture(TexAId) == null ||
                runtimeSkybox.GetTexture(TexBId) == null)
            {
                Debug.LogError(
                    "[TimeOfDaySkyController] The fixed skybox material must contain a cubemap.",
                    this);
                DestroyRuntimeSkybox();
                return false;
            }

            return true;
        }

        private bool EnsureCelestialVisual()
        {
            if (runtimeCelestialObject != null)
            {
                return true;
            }

            if (celestialOverlayTemplate == null ||
                celestialOverlayTemplate.shader == null ||
                !celestialOverlayTemplate.HasProperty(ColorId))
            {
                Debug.LogError(
                    "[TimeOfDaySkyController] A valid celestial overlay material is required.",
                    this);
                return false;
            }

            runtimeCelestialMaterial =
                new Material(celestialOverlayTemplate)
                {
                    name = $"{celestialOverlayTemplate.name} (Runtime)",
                    hideFlags = HideFlags.DontSave
                };
            runtimeCelestialMesh = CreateCelestialQuad();
            runtimeCelestialObject =
                new GameObject("TimeOfDayCelestialBody")
                {
                    hideFlags = HideFlags.DontSave
                };
            runtimeCelestialObject.transform.SetParent(
                transform,
                false);
            MeshFilter filter =
                runtimeCelestialObject.AddComponent<MeshFilter>();
            filter.sharedMesh = runtimeCelestialMesh;
            MeshRenderer renderer =
                runtimeCelestialObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = runtimeCelestialMaterial;
            renderer.shadowCastingMode =
                ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            runtimeCelestialObject.SetActive(false);
            return true;
        }

        private static Mesh CreateCelestialQuad()
        {
            Mesh mesh = new()
            {
                name = "Time Of Day Celestial Quad",
                hideFlags = HideFlags.DontSave,
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f),
                    new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f),
                    new Vector3(-0.5f, 0.5f, 0f)
                },
                uv = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, 1f)
                },
                triangles = new[] { 0, 2, 1, 0, 3, 2 }
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private void CaptureRenderSettings()
        {
            if (!renderSettingsCaptured)
            {
                previousSkybox = RenderSettings.skybox;
                previousSun = RenderSettings.sun;
                previousAmbientMode = RenderSettings.ambientMode;
                previousAmbientSkyColor =
                    RenderSettings.ambientSkyColor;
                previousAmbientEquatorColor =
                    RenderSettings.ambientEquatorColor;
                previousAmbientGroundColor =
                    RenderSettings.ambientGroundColor;
                previousAmbientIntensity =
                    RenderSettings.ambientIntensity;
                renderSettingsCaptured = true;
            }

            if (keyLight != null && !lightStateCaptured)
            {
                previousLightEnabled = keyLight.enabled;
                previousLightColor = keyLight.color;
                previousLightIntensity = keyLight.intensity;
                previousShadowStrength =
                    keyLight.shadowStrength;
                previousLightRotation =
                    keyLight.transform.rotation;
                lightStateCaptured = true;
            }
        }

        private void ApplyGameHour(
            float gameHour,
            bool forceEnvironmentUpdate)
        {
            if (profile == null ||
                runtimeSkybox == null)
            {
                return;
            }

            runtimeSkybox.SetFloat(BlendId, 0f);
            ApplyHorizonCorrection(Camera.main);

            CelestialCycleState cycle =
                EvaluateCelestialCycle(gameHour);
            currentCycle = cycle;
            hasCurrentCycle = true;
            ApplySkyExposure(cycle);
            ApplyCelestialVisual(
                cycle,
                Camera.main);

            if (RenderSettings.skybox != runtimeSkybox)
            {
                RenderSettings.skybox = runtimeSkybox;
            }

            ApplyLighting(cycle);

            if (forceEnvironmentUpdate)
            {
                DynamicGI.UpdateEnvironment();
            }
        }

        internal static CelestialCycleState EvaluateCelestialCycle(
            float gameHour)
        {
            float normalizedHour = Mathf.Repeat(
                gameHour,
                TimeOfDaySkyProfile.HoursPerDay);
            bool isSun =
                normalizedHour >= SunriseHour &&
                normalizedHour < SunsetHour;
            float elapsedHours = isSun
                ? normalizedHour - SunriseHour
                : Mathf.Repeat(
                    normalizedHour - SunsetHour,
                    TimeOfDaySkyProfile.HoursPerDay);
            float progress = Mathf.Clamp01(
                elapsedHours / HalfDayHours);
            float altitude = Mathf.Max(
                0f,
                Mathf.Sin(progress * Mathf.PI));
            float eastWeight = Mathf.Cos(
                progress * Mathf.PI);

            return new CelestialCycleState(
                isSun,
                progress,
                altitude,
                eastWeight);
        }

        private void ApplyCelestialVisual(
            CelestialCycleState cycle,
            Camera targetCamera)
        {
            if (runtimeCelestialObject == null ||
                runtimeCelestialMaterial == null ||
                targetCamera == null)
            {
                if (runtimeCelestialObject != null)
                {
                    runtimeCelestialObject.SetActive(false);
                }

                return;
            }

            Transform bodyTransform =
                runtimeCelestialObject.transform;
            float baseWorldDiameter = cycle.IsSun
                ? sunWorldDiameter
                : moonWorldDiameter;
            float worldDiameter = CalculateCelestialDisplayDiameter(
                baseWorldDiameter,
                targetCamera.orthographicSize,
                ResolveMaximumOrthographicSize(targetCamera),
                minimumScreenSizeRatio);
            bodyTransform.position =
                CalculateCelestialCameraPosition(
                    targetCamera,
                    horizonViewportY,
                    celestialCameraDepth,
                    worldDiameter,
                    cycle);
            bodyTransform.rotation =
                targetCamera.transform.rotation;
            bodyTransform.localScale =
                new Vector3(
                    worldDiameter,
                    worldDiameter,
                    1f);
            runtimeCelestialMaterial.SetColor(
                ColorId,
                cycle.IsSun
                    ? sunVisualColor
                    : moonVisualColor);
            runtimeCelestialObject.SetActive(true);
        }

        private float ResolveMaximumOrthographicSize(
            Camera targetCamera)
        {
            if (mainCityView == null)
            {
                mainCityView = FindAnyObjectByType<MainCityView>();
            }

            float currentSize = Mathf.Max(
                0.1f,
                targetCamera.orthographicSize);
            return mainCityView != null
                ? Mathf.Max(
                    currentSize,
                    mainCityView.MaximumOrthographicSize)
                : currentSize;
        }

        internal static float CalculateCelestialDisplayDiameter(
            float baseWorldDiameter,
            float currentOrthographicSize,
            float maximumOrthographicSize,
            float minimumScreenRatio)
        {
            float safeMaximumSize = Mathf.Max(
                0.1f,
                maximumOrthographicSize);
            float zoomRatio = Mathf.Clamp01(
                Mathf.Max(0.1f, currentOrthographicSize) /
                safeMaximumSize);
            float screenSizeRatio = Mathf.Lerp(
                Mathf.Clamp01(minimumScreenRatio),
                1f,
                zoomRatio);
            return Mathf.Max(0f, baseWorldDiameter) *
                   zoomRatio * screenSizeRatio;
        }

        private void ResolveLightDirections(
            out Vector3 eastDirection,
            out Vector3 upDirection)
        {
            IWorldCoordinateSpace coordinates =
                services?.WorldCoordinates;
            if (coordinates == null)
            {
                eastDirection =
                    (Vector3.right + Vector3.forward).normalized;
                upDirection = Vector3.up;
                return;
            }

            eastDirection =
                (coordinates.GridXAxis +
                 coordinates.GridYAxis).normalized;
            upDirection = coordinates.GroundNormal.normalized;
        }

        internal static Vector3 CalculateCelestialCameraPosition(
            Camera targetCamera,
            float horizonY,
            float cameraDepth,
            float bodyWorldDiameter,
            CelestialCycleState cycle)
        {
            if (targetCamera == null)
            {
                return Vector3.zero;
            }

            float verticalRadius = targetCamera.orthographic
                ? Mathf.Max(0f, bodyWorldDiameter) /
                  (4f * Mathf.Max(0.1f, targetCamera.orthographicSize))
                : 0f;
            float horizontalRadius = verticalRadius /
                Mathf.Max(0.01f, targetCamera.aspect);
            float viewportX =
                0.5f + cycle.EastWeight *
                (0.5f - horizontalRadius);
            float safeHorizonY = Mathf.Clamp01(horizonY);
            float viewportY = Mathf.Lerp(
                safeHorizonY,
                1f - verticalRadius,
                cycle.Altitude);
            float depth = Mathf.Clamp(
                cameraDepth,
                targetCamera.nearClipPlane + 0.01f,
                targetCamera.farClipPlane - 0.01f);

            return targetCamera.ViewportToWorldPoint(
                new Vector3(viewportX, viewportY, depth));
        }

        private void ApplySkyExposure(
            CelestialCycleState cycle)
        {
            if (runtimeSkybox == null ||
                !TryGetLightingKeyframes(
                    out TimeOfDaySkyKeyframe midnight,
                    out TimeOfDaySkyKeyframe dawn,
                    out TimeOfDaySkyKeyframe noon,
                    out TimeOfDaySkyKeyframe dusk))
            {
                return;
            }

            float heightWeight = Mathf.SmoothStep(
                0f,
                1f,
                cycle.Altitude);
            TimeOfDaySkyKeyframe horizon =
                cycle.Progress < 0.5f
                    ? dawn
                    : dusk;
            float exposure = cycle.IsSun
                ? Mathf.Lerp(
                    horizon.SkyExposure,
                    noon.SkyExposure,
                    heightWeight)
                : Mathf.Lerp(
                    horizon.SkyExposure,
                    midnight.SkyExposure,
                    heightWeight);
            runtimeSkybox.SetFloat(ExposureAId, exposure);
            runtimeSkybox.SetFloat(ExposureBId, exposure);
        }

        internal void ApplyHorizonCorrection(
            Camera targetCamera)
        {
            if (runtimeSkybox == null)
            {
                return;
            }

            Quaternion correction =
                CalculateHorizonCorrection(targetCamera);
            runtimeSkybox.SetVector(
                HorizonRotationId,
                new Vector4(
                    correction.x,
                    correction.y,
                    correction.z,
                    correction.w));
        }

        internal static Quaternion CalculateHorizonCorrection(
            Camera targetCamera)
        {
            if (targetCamera == null)
            {
                return Quaternion.identity;
            }

            Transform cameraTransform =
                targetCamera.transform;
            Vector3 projectedWorldUp =
                Vector3.ProjectOnPlane(
                    Vector3.up,
                    cameraTransform.forward);
            if (projectedWorldUp.sqrMagnitude <= 0.000001f)
            {
                return Quaternion.identity;
            }

            float correctionAngle =
                Vector3.SignedAngle(
                    cameraTransform.up,
                    projectedWorldUp.normalized,
                    cameraTransform.forward);
            return Quaternion.AngleAxis(
                correctionAngle,
                cameraTransform.forward);
        }

        private void SubscribeCameraRendering()
        {
            if (cameraRenderingSubscribed)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering +=
                OnBeginCameraRendering;
            cameraRenderingSubscribed = true;
        }

        private void UnsubscribeCameraRendering()
        {
            if (!cameraRenderingSubscribed)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering -=
                OnBeginCameraRendering;
            cameraRenderingSubscribed = false;
        }

        private void OnBeginCameraRendering(
            ScriptableRenderContext context,
            Camera targetCamera)
        {
            if (!ownsRenderSettings ||
                targetCamera == null ||
                targetCamera.cameraType != CameraType.Game)
            {
                return;
            }

            ApplyHorizonCorrection(targetCamera);
            if (hasCurrentCycle)
            {
                ApplyCelestialVisual(
                    currentCycle,
                    targetCamera);
            }
        }

        private void ApplyLighting(
            CelestialCycleState cycle)
        {
            if (!TryGetLightingKeyframes(
                    out TimeOfDaySkyKeyframe midnight,
                    out TimeOfDaySkyKeyframe dawn,
                    out TimeOfDaySkyKeyframe noon,
                    out TimeOfDaySkyKeyframe dusk))
            {
                return;
            }

            float heightWeight = Mathf.SmoothStep(
                0f,
                1f,
                cycle.Altitude);
            TimeOfDaySkyKeyframe horizon =
                cycle.Progress < 0.5f
                    ? dawn
                    : dusk;

            Color lightColor;
            float lightIntensity;
            float shadowStrength;
            Color ambientSkyColor;
            Color ambientEquatorColor;
            Color ambientGroundColor;
            float ambientIntensity;

            if (cycle.IsSun)
            {
                lightColor = Color.Lerp(
                    horizon.LightColor,
                    noon.LightColor,
                    heightWeight);
                lightIntensity = Mathf.Lerp(
                    horizon.LightIntensity,
                    noon.LightIntensity,
                    heightWeight);
                shadowStrength = Mathf.Lerp(
                    horizon.ShadowStrength,
                    noon.ShadowStrength,
                    heightWeight);
                ambientSkyColor = Color.Lerp(
                    horizon.AmbientSkyColor,
                    noon.AmbientSkyColor,
                    heightWeight);
                ambientEquatorColor = Color.Lerp(
                    horizon.AmbientEquatorColor,
                    noon.AmbientEquatorColor,
                    heightWeight);
                ambientGroundColor = Color.Lerp(
                    horizon.AmbientGroundColor,
                    noon.AmbientGroundColor,
                    heightWeight);
                ambientIntensity = Mathf.Lerp(
                    horizon.AmbientIntensity,
                    noon.AmbientIntensity,
                    heightWeight);
            }
            else
            {
                TimeOfDaySkyKeyframe nightHorizon =
                    cycle.Progress < 0.5f
                        ? dusk
                        : dawn;
                lightColor = midnight.LightColor;
                lightIntensity = Mathf.Lerp(
                    moonHorizonIntensity,
                    midnight.LightIntensity,
                    heightWeight);
                shadowStrength = Mathf.Lerp(
                    0.1f,
                    midnight.ShadowStrength,
                    heightWeight);
                ambientSkyColor = Color.Lerp(
                    nightHorizon.AmbientSkyColor,
                    midnight.AmbientSkyColor,
                    heightWeight);
                ambientEquatorColor = Color.Lerp(
                    nightHorizon.AmbientEquatorColor,
                    midnight.AmbientEquatorColor,
                    heightWeight);
                ambientGroundColor = Color.Lerp(
                    nightHorizon.AmbientGroundColor,
                    midnight.AmbientGroundColor,
                    heightWeight);
                ambientIntensity = Mathf.Lerp(
                    nightHorizon.AmbientIntensity,
                    midnight.AmbientIntensity,
                    heightWeight);
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor =
                ambientSkyColor;
            RenderSettings.ambientEquatorColor =
                ambientEquatorColor;
            RenderSettings.ambientGroundColor =
                ambientGroundColor;
            RenderSettings.ambientIntensity =
                ambientIntensity;

            if (keyLight == null)
            {
                return;
            }

            keyLight.enabled = true;
            keyLight.color = lightColor;
            keyLight.intensity = lightIntensity;
            keyLight.shadowStrength = shadowStrength;
            ResolveLightDirections(
                out Vector3 eastDirection,
                out Vector3 upDirection);
            Vector3 bodyDirection =
                (eastDirection * cycle.EastWeight +
                 upDirection * cycle.Altitude).normalized;
            Vector3 orbitNorth = Vector3.Cross(
                upDirection,
                eastDirection).normalized;
            keyLight.transform.rotation = Quaternion.LookRotation(
                -bodyDirection,
                orbitNorth);
            RenderSettings.sun = keyLight;
        }

        private bool TryGetLightingKeyframes(
            out TimeOfDaySkyKeyframe midnight,
            out TimeOfDaySkyKeyframe dawn,
            out TimeOfDaySkyKeyframe noon,
            out TimeOfDaySkyKeyframe dusk)
        {
            midnight = null;
            dawn = null;
            noon = null;
            dusk = null;
            bool valid =
                profile.TryGetKeyframe(0f, out midnight) &&
                profile.TryGetKeyframe(6f, out dawn) &&
                profile.TryGetKeyframe(12f, out noon) &&
                profile.TryGetKeyframe(18f, out dusk);
            if (!valid && !missingMaterialLogged)
            {
                Debug.LogError(
                    "[TimeOfDaySkyController] Lighting profile requires 0, 6, 12, and 18 hour keyframes.",
                    this);
                missingMaterialLogged = true;
            }

            return valid;
        }

        private void ReleaseRenderSettings()
        {
            if (!ownsRenderSettings)
            {
                return;
            }

            if (restoreRenderSettingsOnDisable &&
                renderSettingsCaptured)
            {
                RenderSettings.skybox = previousSkybox;
                RenderSettings.sun = previousSun;
                RenderSettings.ambientMode =
                    previousAmbientMode;
                RenderSettings.ambientSkyColor =
                    previousAmbientSkyColor;
                RenderSettings.ambientEquatorColor =
                    previousAmbientEquatorColor;
                RenderSettings.ambientGroundColor =
                    previousAmbientGroundColor;
                RenderSettings.ambientIntensity =
                    previousAmbientIntensity;
            }

            if (restoreRenderSettingsOnDisable &&
                keyLight != null &&
                lightStateCaptured)
            {
                keyLight.enabled = previousLightEnabled;
                keyLight.color = previousLightColor;
                keyLight.intensity = previousLightIntensity;
                keyLight.shadowStrength =
                    previousShadowStrength;
                keyLight.transform.rotation =
                    previousLightRotation;
            }

            if (activeOwner == this)
            {
                activeOwner = null;
            }

            ownsRenderSettings = false;
            renderSettingsCaptured = false;
            lightStateCaptured = false;
            hasCurrentCycle = false;
            DestroyRuntimeSkybox();
            DestroyCelestialVisual();
        }

        private void DestroyRuntimeSkybox()
        {
            if (runtimeSkybox == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(runtimeSkybox);
            }
            else
            {
                DestroyImmediate(runtimeSkybox);
            }

            runtimeSkybox = null;
        }

        private void DestroyCelestialVisual()
        {
            DestroyRuntimeObject(runtimeCelestialObject);
            DestroyRuntimeObject(runtimeCelestialMesh);
            DestroyRuntimeObject(runtimeCelestialMaterial);
            runtimeCelestialObject = null;
            runtimeCelestialMesh = null;
            runtimeCelestialMaterial = null;
        }

        private static void DestroyRuntimeObject(
            Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private void UnbindServices()
        {
            if (gameCalendar != null)
            {
                gameCalendar.HourChanged -= OnHourChanged;
                gameCalendar = null;
            }

            if (services != null)
            {
                services.GameCalendarRegistered -=
                    OnGameCalendarRegistered;
                services = null;
            }

            mainCityView = null;
        }

        internal readonly struct CelestialCycleState
        {
            public CelestialCycleState(
                bool isSun,
                float progress,
                float altitude,
                float eastWeight)
            {
                IsSun = isSun;
                Progress = progress;
                Altitude = altitude;
                EastWeight = eastWeight;
            }

            public bool IsSun { get; }
            public float Progress { get; }
            public float Altitude { get; }
            public float EastWeight { get; }
        }
    }
}
