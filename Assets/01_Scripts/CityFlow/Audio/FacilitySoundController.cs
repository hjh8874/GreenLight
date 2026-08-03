using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Managers;
using CityFlow.View;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace CityFlow.Audio
{
    public sealed class FacilitySoundController :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        [SerializeField] private SoundManager soundManager;
        [SerializeField] private AudioMixerGroup facilityOutput;
        [Min(0.1f)]
        [SerializeField] private float hospitalPreviewSeconds = 4.8f;
        [Min(0.1f)]
        [SerializeField] private float schoolPreviewSeconds = 6f;

        private CityFlowServices services;
        private MainCityView cityView;
        private AudioSource previewSource;
        private Vector2Int? hoveredFacility;
        private float previewEndsAt;
        private bool isPinnedByClick;
        private float nextResolveTime;

        public void Initialize(CityFlowServices cityFlowServices)
        {
            services = cityFlowServices;
        }

        private void Awake()
        {
            soundManager ??= GetComponent<SoundManager>();
            previewSource = CreateSource();
        }

        private void Update()
        {
            ResolveCityView();
            if (cityView != null && cityView.IsDriveViewActive)
            {
                ClearHover(stopPlayback: true);
                return;
            }

            if (!TryGetHoveredFacility(
                    out Vector2Int tile,
                    out TileType type))
            {
                if (!isPinnedByClick)
                {
                    ClearHover(stopPlayback: true);
                }
                UpdatePreviewLifetime();
                return;
            }

            bool changed = hoveredFacility != tile;
            hoveredFacility = tile;
            if (changed)
            {
                PlayFacilityPreview(type, pinByClick: false);
            }

            if (Mouse.current != null &&
                Mouse.current.leftButton.wasPressedThisFrame)
            {
                PlayFacilityPreview(type, pinByClick: true);
            }

            UpdatePreviewLifetime();
        }

        private void ResolveCityView()
        {
            if (cityView != null || Time.unscaledTime < nextResolveTime)
            {
                return;
            }

            cityView = FindAnyObjectByType<MainCityView>();
            nextResolveTime = Time.unscaledTime + 1f;
        }

        private bool TryGetHoveredFacility(
            out Vector2Int tile,
            out TileType type)
        {
            tile = default;
            type = TileType.Empty;
            if (services?.TileData == null ||
                EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject())
            {
                return false;
            }

            Mouse mouse = Mouse.current;
            Camera camera = cityView != null
                ? cityView.ActiveViewCamera
                : Camera.main;
            if (mouse == null || camera == null)
            {
                return false;
            }

            Ray ray = camera.ScreenPointToRay(mouse.position.ReadValue());
            if (services.WorldCoordinates == null ||
                !services.WorldCoordinates.TryRayToGrid(
                    ray,
                    out tile,
                    out _))
            {
                return false;
            }

            if (services.TileData.TryGetFootprintAnchor(
                    tile,
                    out Vector2Int anchor))
            {
                tile = anchor;
            }

            type = services.TileData.GetTileType(tile);
            return type == TileType.Hospital || type == TileType.School;
        }

        private void PlayFacilityPreview(TileType type, bool pinByClick)
        {
            string id = type == TileType.Hospital
                ? SoundIds.HospitalPreview
                : SoundIds.SchoolPreview;
            if (soundManager == null ||
                !soundManager.TryGetSfx(
                    id,
                    out AudioClip clip,
                    out float volume))
            {
                return;
            }

            float duration = type == TileType.Hospital
                ? hospitalPreviewSeconds
                : schoolPreviewSeconds;
            previewSource.Stop();
            previewSource.clip = clip;
            previewSource.volume = volume;
            previewSource.Play();
            previewEndsAt = Time.unscaledTime + Mathf.Min(duration, clip.length);
            isPinnedByClick = pinByClick;
        }

        private void UpdatePreviewLifetime()
        {
            if (previewSource == null || !previewSource.isPlaying)
            {
                isPinnedByClick = false;
                return;
            }

            if (Time.unscaledTime < previewEndsAt)
            {
                return;
            }

            previewSource.Stop();
            isPinnedByClick = false;
        }

        private void ClearHover(bool stopPlayback)
        {
            hoveredFacility = null;
            if (stopPlayback && previewSource != null)
            {
                previewSource.Stop();
            }
        }

        private AudioSource CreateSource()
        {
            GameObject child = new GameObject("Facility Preview");
            child.transform.SetParent(transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.outputAudioMixerGroup = facilityOutput;
            return source;
        }

#if UNITY_EDITOR
        public void EditorConfigure(
            SoundManager manager,
            AudioMixerGroup output)
        {
            soundManager = manager;
            facilityOutput = output;
            hospitalPreviewSeconds = 4.8f;
            schoolPreviewSeconds = 6f;
        }
#endif

        // Unity setup:
        // The baked prefab resolves hospital and school tiles through CityFlow services.
    }
}
