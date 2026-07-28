using CityFlow.Bootstrap;
using CityFlow.Contracts;
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
        private static readonly int RotationAId =
            Shader.PropertyToID("_RotationA");
        private static readonly int RotationBId =
            Shader.PropertyToID("_RotationB");
        private static readonly int ExposureAId =
            Shader.PropertyToID("_ExposureA");
        private static readonly int ExposureBId =
            Shader.PropertyToID("_ExposureB");
        private static readonly int HorizonRotationId =
            Shader.PropertyToID("_HorizonRotation");

        private static TimeOfDaySkyController activeOwner;

        [Header("Sky Cycle")]
        [SerializeField] private TimeOfDaySkyProfile profile;
        [SerializeField] private Material blendSkyboxTemplate;

        [Header("Lighting")]
        [SerializeField] private Light keyLight;

        [Header("Lifecycle")]
        [SerializeField] private bool restoreRenderSettingsOnDisable = true;

        private CityFlowServices services;
        private IGameCalendarService gameCalendar;
        private Material runtimeSkybox;
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
        private Material lastCurrentSource;
        private bool cameraRenderingSubscribed;

        public TimeOfDaySkyProfile Profile => profile;
        public Material BlendSkyboxTemplate => blendSkyboxTemplate;
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
                ApplyGameHour(hour, false);
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
            return true;
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
                runtimeSkybox == null ||
                !profile.TryEvaluate(
                    gameHour,
                    out TimeOfDaySkyEvaluation evaluation))
            {
                return;
            }

            Material currentSource =
                evaluation.Current.SkyboxMaterial;
            if (!TryGetCubemap(
                    currentSource,
                    out Cubemap currentTexture))
            {
                if (!missingMaterialLogged)
                {
                    Debug.LogError(
                        "[TimeOfDaySkyController] Every keyframe must reference an AllSky cubemap material with a _Tex property.",
                        this);
                    missingMaterialLogged = true;
                }

                return;
            }

            runtimeSkybox.SetTexture(TexAId, currentTexture);
            runtimeSkybox.SetTexture(TexBId, currentTexture);
            float exposure =
                SourceExposure(currentSource) *
                evaluation.Current.SkyExposure;
            runtimeSkybox.SetFloat(ExposureAId, exposure);
            runtimeSkybox.SetFloat(ExposureBId, exposure);
            runtimeSkybox.SetFloat(
                RotationAId,
                evaluation.Current.SkyRotation);
            runtimeSkybox.SetFloat(
                RotationBId,
                evaluation.Current.SkyRotation);
            runtimeSkybox.SetFloat(BlendId, 0f);
            ApplyHorizonCorrection(Camera.main);

            if (RenderSettings.skybox != runtimeSkybox)
            {
                RenderSettings.skybox = runtimeSkybox;
            }

            ApplyLighting(evaluation);

            bool sourceChanged =
                lastCurrentSource != currentSource;
            if (forceEnvironmentUpdate || sourceChanged)
            {
                DynamicGI.UpdateEnvironment();
                lastCurrentSource = currentSource;
            }
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
        }

        private void ApplyLighting(
            TimeOfDaySkyEvaluation evaluation)
        {
            TimeOfDaySkyKeyframe current =
                evaluation.Current;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor =
                current.AmbientSkyColor;
            RenderSettings.ambientEquatorColor =
                current.AmbientEquatorColor;
            RenderSettings.ambientGroundColor =
                current.AmbientGroundColor;
            RenderSettings.ambientIntensity =
                current.AmbientIntensity;

            if (keyLight == null)
            {
                return;
            }

            keyLight.enabled = true;
            keyLight.color = current.LightColor;
            keyLight.intensity = current.LightIntensity;
            keyLight.shadowStrength =
                current.ShadowStrength;
            keyLight.transform.rotation =
                Quaternion.Euler(current.LightEuler);
            RenderSettings.sun = keyLight;
        }

        private static bool TryGetCubemap(
            Material source,
            out Cubemap texture)
        {
            texture = null;
            if (source == null ||
                !source.HasProperty("_Tex"))
            {
                return false;
            }

            texture = source.GetTexture("_Tex") as Cubemap;
            return texture != null;
        }

        private static float SourceExposure(Material source)
        {
            if (source != null &&
                source.HasProperty("_Exposure"))
            {
                return Mathf.Max(
                    0f,
                    source.GetFloat("_Exposure"));
            }

            return 1f;
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
            lastCurrentSource = null;

            if (runtimeSkybox != null)
            {
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
        }
    }
}
