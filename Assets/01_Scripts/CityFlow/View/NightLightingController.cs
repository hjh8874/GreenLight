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

        private readonly List<Light> headlights = new();
        private CityFlowServices services;
        private IGameCalendarService calendar;
        private bool isMoving;
        private Vector3 localForward = Vector3.right;

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

            isMoving = moving;
            ApplyHour(calendar?.Hour ?? 12);
        }

        private void EnsureHeadlights()
        {
            if (headlights.Count > 0 ||
                !TryCalculateLocalBounds(out Bounds bounds))
            {
                return;
            }

            var lightRoot = new GameObject("NightHeadlights");
            lightRoot.transform.SetParent(transform, false);

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
            float worldLength = transform.TransformVector(
                forward * length).magnitude;
            Vector3 front =
                bounds.center +
                forward *
                (halfLength + length * 0.025f);
            float sideOffset = width * 0.3f;
            float heightZ = Mathf.Lerp(
                bounds.center.z,
                bounds.max.z,
                0.25f);
            front.z = heightZ;

            CreateHeadlight(
                lightRoot.transform,
                "Headlight_Left",
                front + side * sideOffset,
                worldLength,
                forward);
            CreateHeadlight(
                lightRoot.transform,
                "Headlight_Right",
                front - side * sideOffset,
                worldLength,
                forward);
        }

        private void CreateHeadlight(
            Transform parent,
            string lightName,
            Vector3 localPosition,
            float worldLength,
            Vector3 forward)
        {
            var lightObject = new GameObject(lightName);
            lightObject.hideFlags =
                HideFlags.HideInHierarchy |
                HideFlags.DontSave;
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.localPosition = localPosition;
            lightObject.transform.localRotation =
                Quaternion.LookRotation(
                    forward,
                    Vector3.back);

            Light headlight = lightObject.AddComponent<Light>();
            headlight.type = LightType.Spot;
            headlight.color = new Color(1f, 0.9f, 0.72f);
            headlight.intensity = 1.35f;
            headlight.range = Mathf.Max(1.2f, worldLength * 6f);
            headlight.spotAngle = 46f;
            headlight.innerSpotAngle = 24f;
            headlight.shadows = LightShadows.None;
            headlight.renderMode = LightRenderMode.Auto;
            headlights.Add(headlight);
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
            bool isOn = isMoving && IsHeadlightHour(hour);
            for (int index = 0; index < headlights.Count; index++)
            {
                if (headlights[index] != null)
                {
                    headlights[index].enabled = isOn;
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
