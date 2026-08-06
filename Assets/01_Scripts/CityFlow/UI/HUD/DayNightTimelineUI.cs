using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    /// <summary>
    /// Displays the current in-game time as a moving sun or moon below the
    /// main HUD top bar. The sun runs from 06:00 to 18:00, and the moon
    /// runs from 18:00 to 06:00. Each icon crosses the center after six hours.
    /// </summary>
    public sealed class DayNightTimelineUI : MonoBehaviour
    {
        private const string RootName = "DayNightTimeline";
        private const string CelestialShaderName =
            "CityFlow/Celestial Overlay";
        private const float DefaultBarHeight = 60f;
        private const float SunriseHour = 6f;
        private const float SunsetHour = 18f;

        private static readonly Color PanelColor =
            new Color(0.055f, 0.075f, 0.095f, 0.94f);
        private static readonly Color DividerColor =
            new Color(0.92f, 0.12f, 0.14f, 1f);
        private static readonly Color SunColor =
            new Color(1f, 0.82f, 0.32f, 1f);
        private static readonly Color MoonColor =
            new Color(0.82f, 0.9f, 1f, 1f);

        private CityFlowServices services;
        private IGameCalendarService calendar;
        private RectTransform markerRect;
        private float iconSize;
        private GameObject sunIcon;
        private GameObject moonIcon;
        private Material sunMaterial;
        private Material moonMaterial;

        public static void Ensure(
            RectTransform topBar,
            CityFlowServices cityFlowServices)
        {
            if (topBar == null || cityFlowServices == null ||
                topBar.name != "HUD_TopBar")
            {
                return;
            }

            Transform parent = topBar.parent;
            if (parent == null)
            {
                return;
            }

            Transform existing = parent.Find(RootName);
            DayNightTimelineUI timeline = existing != null
                ? existing.GetComponent<DayNightTimelineUI>()
                : Create(topBar, parent);

            timeline?.Initialize(cityFlowServices);
        }

        public static float CalculateTrackPosition(float timeOfDay01)
        {
            return CalculateCycleProgress(timeOfDay01);
        }

        public static float CalculateTrackOffset(
            float timeOfDay01,
            float diameter)
        {
            float position = CalculateTrackPosition(timeOfDay01);
            return Mathf.Lerp(diameter * 0.5f, -diameter * 0.5f, position);
        }

        public static float CalculateCycleProgress(float timeOfDay01)
        {
            float hour = Mathf.Repeat(timeOfDay01, 1f) * 24f;
            if (hour >= SunriseHour && hour < SunsetHour)
            {
                return Mathf.InverseLerp(SunriseHour, SunsetHour, hour);
            }

            float hoursAfterSunset = hour >= SunsetHour
                ? hour - SunsetHour
                : hour + SunriseHour;
            return Mathf.Clamp01(hoursAfterSunset / 12f);
        }

        public static bool IsSunTime(float timeOfDay01)
        {
            float hour = Mathf.Repeat(timeOfDay01, 1f) * 24f;
            return hour >= SunriseHour && hour < SunsetHour;
        }

        private static DayNightTimelineUI Create(
            RectTransform topBar,
            Transform parent)
        {
            float height = Mathf.Abs(topBar.sizeDelta.y);
            if (height < 1f)
            {
                height = topBar.rect.height;
            }

            if (height < 1f)
            {
                height = DefaultBarHeight;
            }

            GameObject root = new GameObject(
                RootName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup),
                typeof(DayNightTimelineUI));
            root.transform.SetParent(parent, false);
            root.layer = topBar.gameObject.layer;

            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(1f, 1f);
            rootRect.pivot = new Vector2(0.5f, 1f);
            rootRect.anchoredPosition = new Vector2(0f, -height);
            rootRect.sizeDelta = new Vector2(0f, height);

            Image background = root.GetComponent<Image>();
            background.color = PanelColor;
            Graphic topBarGraphic = topBar.GetComponent<Graphic>();
            background.raycastTarget =
                topBarGraphic == null || topBarGraphic.raycastTarget;

            CanvasGroup sourceGroup = topBar.GetComponent<CanvasGroup>();
            CanvasGroup timelineGroup = root.GetComponent<CanvasGroup>();
            timelineGroup.interactable =
                sourceGroup == null || sourceGroup.interactable;
            timelineGroup.blocksRaycasts =
                sourceGroup == null || sourceGroup.blocksRaycasts;
            timelineGroup.ignoreParentGroups =
                sourceGroup != null && sourceGroup.ignoreParentGroups;

            int targetSibling = Mathf.Min(
                topBar.GetSiblingIndex() + 1,
                parent.childCount - 1);
            root.transform.SetSiblingIndex(targetSibling);

            DayNightTimelineUI timeline =
                root.GetComponent<DayNightTimelineUI>();
            timeline.BuildVisuals(rootRect);
            return timeline;
        }

        private void Initialize(CityFlowServices cityFlowServices)
        {
            if (ReferenceEquals(services, cityFlowServices))
            {
                BindCalendar(cityFlowServices.GameCalendar);
                return;
            }

            if (services != null)
            {
                services.GameCalendarRegistered -= OnCalendarRegistered;
            }

            services = cityFlowServices;
            services.GameCalendarRegistered += OnCalendarRegistered;
            BindCalendar(services.GameCalendar);
        }

        private void BuildVisuals(RectTransform rootRect)
        {
            iconSize = Mathf.Abs(rootRect.sizeDelta.y);
            if (iconSize < 1f)
            {
                iconSize = rootRect.rect.height;
            }

            if (iconSize < 1f)
            {
                iconSize = DefaultBarHeight;
            }

            GameObject marker = new GameObject(
                "CelestialMarker",
                typeof(RectTransform));
            marker.transform.SetParent(rootRect, false);
            marker.layer = gameObject.layer;

            markerRect = marker.GetComponent<RectTransform>();
            markerRect.anchorMin = new Vector2(0f, 0.5f);
            markerRect.anchorMax = new Vector2(0f, 0.5f);
            markerRect.pivot = new Vector2(0.5f, 0.5f);
            markerRect.anchoredPosition = Vector2.zero;
            markerRect.sizeDelta = new Vector2(iconSize, iconSize);

            sunIcon = CreateCelestialIcon(
                "Sun",
                markerRect,
                SunColor,
                out sunMaterial);
            moonIcon = CreateCelestialIcon(
                "Moon",
                markerRect,
                MoonColor,
                out moonMaterial);
            CreateDivider(rootRect);
            Refresh();
        }

        private static void CreateDivider(RectTransform parent)
        {
            GameObject divider = new GameObject(
                "NoonDivider",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            divider.transform.SetParent(parent, false);
            divider.layer = parent.gameObject.layer;

            RectTransform rect = divider.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(2f, 0f);

            Image image = divider.GetComponent<Image>();
            image.color = DividerColor;
            image.raycastTarget = false;
        }

        private static GameObject CreateCelestialIcon(
            string name,
            RectTransform parent,
            Color color,
            out Material material)
        {
            GameObject root = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RawImage));
            root.transform.SetParent(parent, false);
            root.layer = parent.gameObject.layer;

            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;

            Shader shader = Shader.Find(CelestialShaderName);
            if (shader == null)
            {
                Debug.LogError(
                    $"[DayNightTimelineUI] Shader not found: " +
                    CelestialShaderName);
                material = null;
                root.SetActive(false);
                return root;
            }

            material = new Material(shader)
            {
                name = $"{name} Celestial Overlay (Runtime)",
                hideFlags = HideFlags.DontSave
            };
            material.SetColor("_Color", color);

            RawImage image = root.GetComponent<RawImage>();
            image.material = material;
            image.color = Color.white;
            image.raycastTarget = false;
            return root;
        }

        private void OnCalendarRegistered(IGameCalendarService gameCalendar)
        {
            BindCalendar(gameCalendar);
        }

        private void BindCalendar(IGameCalendarService gameCalendar)
        {
            calendar = gameCalendar;
            Refresh();
        }

        private void Update()
        {
            Refresh();
        }

        private void Refresh()
        {
            if (markerRect == null)
            {
                return;
            }

            float timeOfDay01 = calendar?.TimeOfDay01 ?? 0f;
            float position = CalculateTrackPosition(timeOfDay01);
            markerRect.anchorMin = markerRect.anchorMax =
                new Vector2(position, 0.5f);
            markerRect.anchoredPosition = new Vector2(
                CalculateTrackOffset(timeOfDay01, iconSize),
                0f);

            bool showSun = IsSunTime(timeOfDay01);
            if (sunIcon != null && sunIcon.activeSelf != showSun)
            {
                sunIcon.SetActive(showSun);
            }

            if (moonIcon != null && moonIcon.activeSelf == showSun)
            {
                moonIcon.SetActive(!showSun);
            }
        }

        private void OnDestroy()
        {
            if (services != null)
            {
                services.GameCalendarRegistered -= OnCalendarRegistered;
            }

            DestroyRuntimeMaterial(sunMaterial);
            DestroyRuntimeMaterial(moonMaterial);
        }

        private static void DestroyRuntimeMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(material);
            }
            else
            {
                DestroyImmediate(material);
            }
        }
    }
}
