using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityFlow.View
{
    public enum BuildingNightLightProfile
    {
        House = 1,
        Office = 2,
        StudioHorizonCivic = 3
    }

    [DisallowMultipleComponent]
    public sealed class VehicleNightLighting : MonoBehaviour
    {
        private const int HeadlightStartHour = 18;
        private const int HeadlightEndHour = 6;
        private const float StandardHeadlightHeight = 0.65f;
        private const float TallVehicleHeadlightHeight = 0.8f;
        private const float HeadlightFrontClearance = 0.01f;
        private const float HeadlightSideOffset = 0.18f;
        private const float HeadlightAimDistance = 0.24f;
        private const float TallVehicleHeadlightAimDistance = 0.24f;
        private const float StandardHeadlightIntensity = 0.75f;
        private const float TallVehicleHeadlightIntensity = 3.40f;
        private const float StandardHeadlightInnerSpotAngle = 20f;
        private const float TallVehicleHeadlightInnerSpotAngle = 20f;
        private const float StandardRoadClearance = 0.30f;
        private const float TallVehicleRoadClearance = 0.30f;
        private const float HeadlightLensRoadClearance = 0.30f;
        private const float HeadlightLensDepth = 0.015f;
        private const float HeadlightLensWidth = 0.12f;
        private const float HeadlightLensHeight = 0.08f;
        private static readonly Color HeadlightColor =
            new(1f, 0.9f, 0.72f, 1f);
        private static Mesh headlightLensMesh;
        private static Material headlightLensMaterial;

        private readonly List<Light> headlights = new();
        private readonly List<Renderer> headlightLenses = new();
        private CityFlowServices services;
        private IGameCalendarService calendar;
        private bool isMoving;
        private Vector3 localForward = Vector3.right;
        private float headlightHeight = StandardHeadlightHeight;

        public static VehicleNightLighting Attach(
            GameObject vehicleRoot,
            CityFlowServices services)
        {
            return Attach(
                vehicleRoot,
                services,
                Vector3.right);
        }

        public static VehicleNightLighting Attach(
            GameObject vehicleRoot,
            CityFlowServices services,
            Vector3 localForward)
        {
            return Attach(
                vehicleRoot,
                services,
                localForward,
                StandardHeadlightHeight);
        }

        public static VehicleNightLighting AttachTallVehicle(
            GameObject vehicleRoot,
            CityFlowServices services,
            Vector3 localForward)
        {
            return Attach(
                vehicleRoot,
                services,
                localForward,
                TallVehicleHeadlightHeight);
        }

        private static VehicleNightLighting Attach(
            GameObject vehicleRoot,
            CityFlowServices services,
            Vector3 localForward,
            float headlightHeight)
        {
            if (vehicleRoot == null)
            {
                return null;
            }

            VehicleNightLighting lighting =
                vehicleRoot.GetComponent<VehicleNightLighting>() ??
                vehicleRoot.AddComponent<VehicleNightLighting>();
            lighting.localForward =
                localForward.sqrMagnitude > 0.0001f
                    ? localForward.normalized
                    : Vector3.right;
            lighting.headlightHeight = Mathf.Clamp01(headlightHeight);
            lighting.Initialize(services);
            return lighting;
        }

        public void Initialize(CityFlowServices cityServices)
        {
            if (!ReferenceEquals(services, cityServices))
            {
                if (services != null)
                {
                    services.GameCalendarRegistered -=
                        OnGameCalendarRegistered;
                }

                services = cityServices;
                if (services != null)
                {
                    services.GameCalendarRegistered +=
                        OnGameCalendarRegistered;
                }
            }

            EnsureHeadlights();
            BindCalendar(services?.GameCalendar);
        }

        internal static bool IsHeadlightHour(int hour)
        {
            int normalized = ((hour % 24) + 24) % 24;
            return normalized >= HeadlightStartHour ||
                   normalized < HeadlightEndHour;
        }

        public void SetMoving(bool moving)
        {
            if (isMoving == moving)
            {
                return;
            }

            if (moving)
            {
                EnsureHeadlights();
            }

            isMoving = moving;
            ApplyHour(calendar?.Hour ?? 12);
        }

        private void EnsureHeadlights()
        {
            if (!TryCalculateLocalBounds(out Bounds bounds))
            {
                return;
            }

            Transform lightRoot;
            if (headlights.Count == 0)
            {
                var lightRootObject =
                    new GameObject("NightHeadlights");
                lightRootObject.layer = gameObject.layer;
                lightRootObject.transform.SetParent(transform, false);
                lightRoot = lightRootObject.transform;
            }
            else
            {
                lightRoot = headlights[0].transform.parent;
            }

            Vector3 forward = localForward;
            Vector3 side = new(
                -forward.y,
                forward.x,
                0f);
            float halfLength =
                Mathf.Abs(forward.x) * bounds.extents.x +
                Mathf.Abs(forward.y) * bounds.extents.y;
            float halfWidth =
                Mathf.Abs(side.x) * bounds.extents.x +
                Mathf.Abs(side.y) * bounds.extents.y;
            float length = Mathf.Max(0.01f, halfLength * 2f);
            float width = Mathf.Max(0.01f, halfWidth * 2f);
            bool usesTallVehicleProfile =
                headlightHeight >= TallVehicleHeadlightHeight;
            float worldLength = transform.TransformVector(
                forward * length).magnitude;
            Vector3 front =
                bounds.center +
                forward *
                (halfLength + length * HeadlightFrontClearance);
            float sideOffset = width * HeadlightSideOffset;
            float aimDistance = Mathf.Max(
                0.05f,
                length * (usesTallVehicleProfile
                    ? TallVehicleHeadlightAimDistance
                    : HeadlightAimDistance));
            float headlightIntensity = usesTallVehicleProfile
                ? TallVehicleHeadlightIntensity
                : StandardHeadlightIntensity;
            float innerSpotAngle = usesTallVehicleProfile
                ? TallVehicleHeadlightInnerSpotAngle
                : StandardHeadlightInnerSpotAngle;
            // Vehicle roots are placed on the sampled road surface. Generated
            // wrappers normalize their ground contact to local Z=0, and the
            // procedural fallback uses the same root-ground contract. Keeping
            // this plane local makes initialization independent of road pose.
            const float roadHeight = 0f;
            float clearance =
                usesTallVehicleProfile
                    ? TallVehicleRoadClearance
                    : StandardRoadClearance;
            float heightZ =
                roadHeight - bounds.size.z * clearance;

            front.z = heightZ;
            float lensHeight =
                roadHeight -
                bounds.size.z * HeadlightLensRoadClearance;
            float headlightRange =
                Mathf.Max(1.2f, worldLength * 6f);

            if (headlights.Count == 0)
            {
                CreateHeadlight(
                    lightRoot,
                    "Headlight_Left",
                    front + side * sideOffset,
                    headlightRange,
                    headlightIntensity,
                    innerSpotAngle,
                    forward,
                    aimDistance,
                    roadHeight);
                CreateHeadlight(
                    lightRoot,
                    "Headlight_Right",
                    front - side * sideOffset,
                    headlightRange,
                    headlightIntensity,
                    innerSpotAngle,
                    forward,
                    aimDistance,
                    roadHeight);
            }
            else
            {
                headlights[0].intensity = headlightIntensity;
                headlights[1].intensity = headlightIntensity;
                headlights[0].range = headlightRange;
                headlights[1].range = headlightRange;
                headlights[0].spotAngle = 60f;
                headlights[1].spotAngle = 60f;
                headlights[0].innerSpotAngle = innerSpotAngle;
                headlights[1].innerSpotAngle = innerSpotAngle;
                ConfigureHeadlight(
                    headlights[0],
                    front + side * sideOffset,
                    forward,
                    aimDistance,
                    roadHeight);
                ConfigureHeadlight(
                    headlights[1],
                    front - side * sideOffset,
                    forward,
                    aimDistance,
                    roadHeight);
            }

            EnsureHeadlightLenses(
                lightRoot,
                front,
                side,
                sideOffset,
                forward,
                length,
                width,
                bounds.size.z,
                lensHeight);
        }

        private void CreateHeadlight(
            Transform parent,
            string lightName,
            Vector3 localPosition,
            float range,
            float intensity,
            float innerSpotAngle,
            Vector3 forward,
            float aimDistance,
            float roadHeight)
        {
            var lightObject = new GameObject(lightName);
            lightObject.hideFlags =
                HideFlags.HideInHierarchy |
                HideFlags.DontSave;
            lightObject.transform.SetParent(parent, false);

            Light headlight = lightObject.AddComponent<Light>();
            headlight.type = LightType.Spot;
            headlight.color = HeadlightColor;
            headlight.intensity = intensity;
            headlight.range = range;
            headlight.spotAngle = 60f;
            headlight.innerSpotAngle = innerSpotAngle;
            headlight.shadows = LightShadows.None;
            headlight.renderMode = LightRenderMode.Auto;
            headlights.Add(headlight);
            ConfigureHeadlight(
                headlight,
                localPosition,
                forward,
                aimDistance,
                roadHeight);
        }

        private void EnsureHeadlightLenses(
            Transform parent,
            Vector3 front,
            Vector3 side,
            float sideOffset,
            Vector3 forward,
            float length,
            float width,
            float height,
            float lensHeight)
        {
            while (headlightLenses.Count < 2)
            {
                string lensName = headlightLenses.Count == 0
                    ? "HeadlightLens_Left"
                    : "HeadlightLens_Right";
                headlightLenses.Add(
                    CreateHeadlightLens(parent, lensName));
            }

            Vector3 leftPosition = front + side * sideOffset;
            Vector3 rightPosition = front - side * sideOffset;
            leftPosition.z = lensHeight;
            rightPosition.z = lensHeight;
            ConfigureHeadlightLens(
                headlightLenses[0],
                leftPosition,
                forward,
                length,
                width,
                height);
            ConfigureHeadlightLens(
                headlightLenses[1],
                rightPosition,
                forward,
                length,
                width,
                height);
        }

        private static Renderer CreateHeadlightLens(
            Transform parent,
            string lensName)
        {
            var lensObject = new GameObject(lensName);
            lensObject.hideFlags =
                HideFlags.HideInHierarchy |
                HideFlags.DontSave;
            lensObject.layer = parent.gameObject.layer;
            lensObject.transform.SetParent(parent, false);

            MeshFilter lensFilter =
                lensObject.AddComponent<MeshFilter>();
            lensFilter.sharedMesh = GetHeadlightLensMesh();
            Renderer lensRenderer =
                lensObject.AddComponent<MeshRenderer>();
            lensRenderer.sharedMaterial = GetHeadlightLensMaterial();
            lensRenderer.shadowCastingMode = ShadowCastingMode.Off;
            lensRenderer.receiveShadows = false;
            lensRenderer.lightProbeUsage = LightProbeUsage.Off;
            lensRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            lensRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            return lensRenderer;
        }

        private static Mesh GetHeadlightLensMesh()
        {
            if (headlightLensMesh != null)
            {
                return headlightLensMesh;
            }

            headlightLensMesh = new Mesh
            {
                name = "Vehicle Headlight Lens Cube (Runtime)",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, -0.5f),
                    new Vector3(0.5f, -0.5f, -0.5f),
                    new Vector3(0.5f, 0.5f, -0.5f),
                    new Vector3(-0.5f, 0.5f, -0.5f),
                    new Vector3(-0.5f, -0.5f, 0.5f),
                    new Vector3(0.5f, -0.5f, 0.5f),
                    new Vector3(0.5f, 0.5f, 0.5f),
                    new Vector3(-0.5f, 0.5f, 0.5f)
                },
                triangles = new[]
                {
                    0, 2, 1, 0, 3, 2,
                    4, 5, 6, 4, 6, 7,
                    0, 4, 7, 0, 7, 3,
                    1, 2, 6, 1, 6, 5,
                    0, 1, 5, 0, 5, 4,
                    3, 7, 6, 3, 6, 2
                }
            };
            headlightLensMesh.RecalculateBounds();
            return headlightLensMesh;
        }

        private static void ConfigureHeadlightLens(
            Renderer lensRenderer,
            Vector3 localPosition,
            Vector3 forward,
            float length,
            float width,
            float height)
        {
            if (lensRenderer == null)
            {
                return;
            }

            Transform lensTransform = lensRenderer.transform;
            lensTransform.localPosition = localPosition;
            lensTransform.localRotation = Quaternion.AngleAxis(
                Mathf.Atan2(forward.y, forward.x) * Mathf.Rad2Deg,
                Vector3.forward);
            lensTransform.localScale = new Vector3(
                Mathf.Max(0.001f, length * HeadlightLensDepth),
                Mathf.Max(0.001f, width * HeadlightLensWidth),
                Mathf.Max(0.001f, height * HeadlightLensHeight));
        }

        private static Material GetHeadlightLensMaterial()
        {
            if (headlightLensMaterial != null)
            {
                return headlightLensMaterial;
            }

            Shader lensShader =
                Resources.Load<Shader>("CityFlowHeadlightLens") ??
                Shader.Find("GreenLight/CityFlow Headlight Lens");
            if (lensShader == null)
            {
                return null;
            }

            headlightLensMaterial = new Material(lensShader)
            {
                name = "Vehicle Headlight Lens (Runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
            if (headlightLensMaterial.HasProperty("_LensColor"))
            {
                headlightLensMaterial.SetColor(
                    "_LensColor",
                    HeadlightColor);
            }

            return headlightLensMaterial;
        }

        private static void ConfigureHeadlight(
            Light headlight,
            Vector3 localPosition,
            Vector3 forward,
            float aimDistance,
            float roadHeight)
        {
            headlight.transform.localPosition = localPosition;
            Vector3 localAimPoint =
                localPosition + forward * aimDistance;
            localAimPoint.z = roadHeight;
            Transform parent = headlight.transform.parent;
            Vector3 worldOrigin =
                parent.TransformPoint(localPosition);
            Vector3 worldAimPoint =
                parent.TransformPoint(localAimPoint);
            headlight.transform.rotation =
                Quaternion.LookRotation(
                    worldAimPoint - worldOrigin,
                    parent.TransformDirection(Vector3.back));
        }

        private bool TryCalculateLocalBounds(out Bounds bounds)
        {
            BoxCollider bodyCollider = GetComponent<BoxCollider>();
            if (bodyCollider != null &&
                bodyCollider.size.x > 0.0001f &&
                bodyCollider.size.y > 0.0001f &&
                bodyCollider.size.z > 0.0001f)
            {
                bounds = new Bounds(
                    bodyCollider.center,
                    bodyCollider.size);
                return true;
            }

            Renderer[] renderers =
                GetComponentsInChildren<Renderer>(true);
            bool found = false;
            bounds = default;

            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (!IsVehicleGeometryRenderer(renderer))
                {
                    continue;
                }

                Bounds rendererBounds = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 rendererPoint = new(
                        (corner & 1) == 0
                            ? rendererBounds.min.x
                            : rendererBounds.max.x,
                        (corner & 2) == 0
                            ? rendererBounds.min.y
                            : rendererBounds.max.y,
                        (corner & 4) == 0
                            ? rendererBounds.min.z
                            : rendererBounds.max.z);
                    Vector3 worldPoint =
                        renderer.transform.TransformPoint(rendererPoint);
                    Vector3 localPoint =
                        transform.InverseTransformPoint(worldPoint);
                    if (!found)
                    {
                        bounds = new Bounds(
                            localPoint,
                            Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(localPoint);
                    }
                }
            }

            return found;
        }

        private static bool IsVehicleGeometryRenderer(Renderer renderer)
        {
            if (renderer == null ||
                (renderer.gameObject.hideFlags & HideFlags.DontSave) != 0 ||
                renderer is ParticleSystemRenderer ||
                renderer is TrailRenderer ||
                renderer is LineRenderer ||
                renderer.GetComponent<TextMesh>() != null)
            {
                return false;
            }

            return renderer is MeshRenderer ||
                   renderer is SkinnedMeshRenderer;
        }

        private void OnGameCalendarRegistered(
            IGameCalendarService gameCalendar)
        {
            BindCalendar(gameCalendar);
        }

        private void BindCalendar(IGameCalendarService gameCalendar)
        {
            if (ReferenceEquals(calendar, gameCalendar))
            {
                ApplyHour(calendar?.Hour ?? 12);
                return;
            }

            if (calendar != null)
            {
                calendar.HourChanged -= ApplyHour;
            }

            calendar = gameCalendar;
            if (calendar != null)
            {
                calendar.HourChanged += ApplyHour;
                ApplyHour(calendar.Hour);
            }
            else
            {
                ApplyHour(12);
            }
        }

        private void ApplyHour(int hour)
        {
            bool isOn = isMoving && IsHeadlightHour(hour);
            for (int index = 0; index < headlights.Count; index++)
            {
                if (headlights[index] != null)
                {
                    headlights[index].enabled = isOn;
                }
            }
            for (int index = 0; index < headlightLenses.Count; index++)
            {
                if (headlightLenses[index] != null)
                {
                    headlightLenses[index].forceRenderingOff = !isOn;
                }
            }
        }

        private void OnDestroy()
        {
            if (services != null)
            {
                services.GameCalendarRegistered -=
                    OnGameCalendarRegistered;
            }
            if (calendar != null)
            {
                calendar.HourChanged -= ApplyHour;
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class BuildingNightLighting : MonoBehaviour
    {
        private const int BuildingLightStartHour = 18;
        private const int BuildingLightEndHour = 24;
        private static readonly int EmissionColorId =
            Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");
        private static readonly Color WindowLightColor =
            new(1f, 0.9f, 0.72f, 1f);

        private sealed class RuntimeMaterialState
        {
            public Renderer Renderer;
            public Material[] OriginalMaterials;
            public Material[] RuntimeMaterials;
        }

        private sealed class EmissionState
        {
            public Material Material;
            public Color NightEmission;
        }

        private sealed class OverlayMaterialState
        {
            public GameObject Object;
            public Material Material;
        }

        private readonly List<RuntimeMaterialState> rendererStates =
            new();
        private readonly List<EmissionState> emissionStates = new();
        private readonly List<OverlayMaterialState> overlayStates = new();
        private CityFlowServices services;
        private IGameCalendarService calendar;
        private bool materialsPrepared;
        private BuildingNightLightProfile profile;

        public static BuildingNightLighting Attach(
            GameObject buildingRoot,
            CityFlowServices services,
            BuildingNightLightProfile profile)
        {
            if (buildingRoot == null)
            {
                return null;
            }

            BuildingNightLighting lighting =
                buildingRoot.GetComponent<BuildingNightLighting>() ??
                buildingRoot.AddComponent<BuildingNightLighting>();
            lighting.Initialize(
                services,
                profile);
            return lighting;
        }

        public void Initialize(
            CityFlowServices cityServices,
            BuildingNightLightProfile lightProfile)
        {
            profile = lightProfile;
            if (!ReferenceEquals(services, cityServices))
            {
                if (services != null)
                {
                    services.GameCalendarRegistered -=
                        OnGameCalendarRegistered;
                }

                services = cityServices;
                if (services != null)
                {
                    services.GameCalendarRegistered +=
                        OnGameCalendarRegistered;
                }
            }

            PrepareRuntimeMaterials();
            BindCalendar(services?.GameCalendar);
        }

        internal static bool IsBuildingLightHour(int hour)
        {
            int normalized = ((hour % 24) + 24) % 24;
            return normalized >= BuildingLightStartHour &&
                   normalized < BuildingLightEndHour;
        }

        private void PrepareRuntimeMaterials()
        {
            if (materialsPrepared)
            {
                return;
            }

            materialsPrepared = true;
            Renderer[] renderers =
                GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                Material[] originals = renderer.sharedMaterials;
                var runtimeMaterials =
                    new Material[originals.Length];
                bool replacedAny = false;

                for (int materialIndex = 0;
                     materialIndex < originals.Length;
                     materialIndex++)
                {
                    Material original = originals[materialIndex];
                    bool isWindow = original != null &&
                                    IsWindowMaterial(
                                        renderer.name,
                                        original.name);
                    if (!isWindow ||
                        !original.HasProperty(EmissionColorId))
                    {
                        runtimeMaterials[materialIndex] = original;
                        continue;
                    }

                    var runtime = new Material(original)
                    {
                        name = original.name + " (Night Lighting)",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    runtime.EnableKeyword("_EMISSION");
                    runtime.globalIlluminationFlags =
                        MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    runtime.SetColor(EmissionColorId, Color.black);
                    runtimeMaterials[materialIndex] = runtime;
                    replacedAny = true;

                    emissionStates.Add(
                        new EmissionState
                        {
                            Material = runtime,
                            NightEmission = CalculateNightEmission()
                        });
                }

                if (!replacedAny)
                {
                    continue;
                }

                rendererStates.Add(
                    new RuntimeMaterialState
                    {
                        Renderer = renderer,
                        OriginalMaterials = originals,
                        RuntimeMaterials = runtimeMaterials
                    });
                renderer.sharedMaterials = runtimeMaterials;
            }

            if (emissionStates.Count == 0)
            {
                CreateWindowOverlayMaterials();
            }
        }

        private static bool IsWindowMaterial(
            string rendererName,
            string materialName)
        {
            string combined =
                $"{rendererName} {materialName}";
            return combined.Contains(
                       "window",
                       StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains(
                       "glass",
                       StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains(
                       "light",
                       StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains(
                       "lamp",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static Color CalculateNightEmission()
        {
            return WindowLightColor * 0.65f;
        }

        private void CreateWindowOverlayMaterials()
        {
            if (!TryCalculateLocalBounds(out Bounds buildingBounds))
            {
                return;
            }

            MeshRenderer[] renderers =
                GetComponentsInChildren<MeshRenderer>(true);
            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                MeshRenderer renderer = renderers[rendererIndex];
                Material[] originals = renderer.sharedMaterials;
                MeshFilter sourceFilter =
                    renderer.GetComponent<MeshFilter>();
                if (sourceFilter == null ||
                    sourceFilter.sharedMesh == null ||
                    originals.Length != 1 ||
                    originals[0] == null)
                {
                    continue;
                }

                Material overlay = CreateProceduralWindowMaterial(
                    originals[0],
                    buildingBounds);
                if (overlay == null)
                {
                    continue;
                }

                var overlayObject = new GameObject(
                    renderer.name + "_NightWindowOverlay");
                overlayObject.layer = renderer.gameObject.layer;
                overlayObject.transform.SetParent(
                    renderer.transform,
                    false);
                overlayObject.AddComponent<MeshFilter>()
                    .sharedMesh = sourceFilter.sharedMesh;
                MeshRenderer overlayRenderer =
                    overlayObject.AddComponent<MeshRenderer>();
                overlayRenderer.sharedMaterial = overlay;
                overlayRenderer.shadowCastingMode =
                    ShadowCastingMode.Off;
                overlayRenderer.receiveShadows = false;
                overlayObject.SetActive(false);
                overlayStates.Add(
                    new OverlayMaterialState
                    {
                        Object = overlayObject,
                        Material = overlay
                    });
            }
        }

        private Material CreateProceduralWindowMaterial(
            Material sourceMaterial,
            Bounds buildingBounds)
        {
            Shader shader = Resources.Load<Shader>(
                "CityFlowNightWindowOverlay");
            Texture sourceTexture = sourceMaterial?.mainTexture;
            if (shader == null || sourceTexture == null)
            {
                return null;
            }

            var material = new Material(shader)
            {
                name = "Night Window Light (Runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };

            material.SetTexture(BaseMapId, sourceTexture);
            material.SetTextureScale(
                "_BaseMap",
                sourceMaterial.mainTextureScale);
            material.SetTextureOffset(
                "_BaseMap",
                sourceMaterial.mainTextureOffset);
            material.SetFloat("_Enabled", 0f);
            material.SetFloat(
                "_WindowMaskProfile",
                (float)profile);
            material.SetFloat("_EmissionIntensity", 1.35f);
            if (material.HasProperty(BaseColorId))
            {
                material.SetColor(
                    BaseColorId,
                    WindowLightColor);
            }
            if (material.HasProperty(ColorId))
            {
                material.SetColor(
                    ColorId,
                    WindowLightColor);
            }
            if (material.HasProperty(EmissionColorId))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor(
                    EmissionColorId,
                    WindowLightColor * 0.65f);
            }
            Vector3 position = transform.position;
            float seed = Mathf.Abs(
                position.x * 12.9898f +
                position.y * 78.233f +
                position.z * 37.719f);
            material.SetFloat("_BuildingSeed", seed);
            Vector3 buildingBottom = transform.TransformPoint(
                new Vector3(
                    buildingBounds.center.x,
                    buildingBounds.center.y,
                    buildingBounds.min.z));
            float buildingHeight = transform.TransformVector(
                Vector3.forward * buildingBounds.size.z).magnitude;
            material.SetFloat(
                "_BuildingBottom",
                buildingBottom.z);
            material.SetFloat(
                "_BuildingHeight",
                Mathf.Max(0.0001f, buildingHeight));

            return material;
        }

        private bool TryCalculateLocalBounds(out Bounds bounds)
        {
            Renderer[] renderers =
                GetComponentsInChildren<Renderer>(true);
            bool found = false;
            bounds = default;

            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                Bounds worldBounds = renderers[rendererIndex].bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 worldPoint = new(
                        (corner & 1) == 0
                            ? worldBounds.min.x
                            : worldBounds.max.x,
                        (corner & 2) == 0
                            ? worldBounds.min.y
                            : worldBounds.max.y,
                        (corner & 4) == 0
                            ? worldBounds.min.z
                            : worldBounds.max.z);
                    Vector3 localPoint =
                        transform.InverseTransformPoint(worldPoint);
                    if (!found)
                    {
                        bounds = new Bounds(
                            localPoint,
                            Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(localPoint);
                    }
                }
            }

            return found;
        }

        private void OnGameCalendarRegistered(
            IGameCalendarService gameCalendar)
        {
            BindCalendar(gameCalendar);
        }

        private void BindCalendar(IGameCalendarService gameCalendar)
        {
            if (ReferenceEquals(calendar, gameCalendar))
            {
                ApplyHour(calendar?.Hour ?? 12);
                return;
            }

            if (calendar != null)
            {
                calendar.HourChanged -= ApplyHour;
            }

            calendar = gameCalendar;
            if (calendar != null)
            {
                calendar.HourChanged += ApplyHour;
                ApplyHour(calendar.Hour);
            }
            else
            {
                ApplyHour(12);
            }
        }

        private void ApplyHour(int hour)
        {
            bool isOn = IsBuildingLightHour(hour);
            for (int index = 0; index < overlayStates.Count; index++)
            {
                OverlayMaterialState state = overlayStates[index];
                Material overlay = state.Material;
                if (overlay != null)
                {
                    overlay.SetFloat("_Enabled", isOn ? 1f : 0f);
                }
                if (state.Object != null)
                {
                    state.Object.SetActive(isOn);
                }
            }

            for (int index = 0;
                 index < emissionStates.Count;
                 index++)
            {
                EmissionState state = emissionStates[index];
                if (state.Material != null)
                {
                    state.Material.SetColor(
                        EmissionColorId,
                        isOn
                            ? state.NightEmission
                            : Color.black);
                }
            }

        }

        private void OnDestroy()
        {
            if (services != null)
            {
                services.GameCalendarRegistered -=
                    OnGameCalendarRegistered;
            }
            if (calendar != null)
            {
                calendar.HourChanged -= ApplyHour;
            }

            for (int stateIndex = 0;
                 stateIndex < rendererStates.Count;
                 stateIndex++)
            {
                RuntimeMaterialState state =
                    rendererStates[stateIndex];
                if (state.Renderer != null)
                {
                    state.Renderer.sharedMaterials =
                        state.OriginalMaterials;
                }

                for (int materialIndex = 0;
                     materialIndex < state.RuntimeMaterials.Length;
                     materialIndex++)
                {
                    Material runtime =
                        state.RuntimeMaterials[materialIndex];
                    Material original =
                        state.OriginalMaterials[materialIndex];
                    if (runtime == null ||
                        ReferenceEquals(runtime, original))
                    {
                        continue;
                    }

                    if (Application.isPlaying)
                    {
                        Destroy(runtime);
                    }
                    else
                    {
                        DestroyImmediate(runtime);
                    }
                }
            }

            for (int index = 0; index < overlayStates.Count; index++)
            {
                OverlayMaterialState state = overlayStates[index];
                if (state.Object != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(state.Object);
                    }
                    else
                    {
                        DestroyImmediate(state.Object);
                    }
                }

                if (state.Material == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(state.Material);
                }
                else
                {
                    DestroyImmediate(state.Material);
                }
            }
        }
    }
}
