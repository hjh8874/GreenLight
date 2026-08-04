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
        [SerializeField] private float previewSeconds = 10f;
        [Min(0.1f)]
        [SerializeField] private float fadeStartsAtSeconds = 6f;

        private static readonly string[] HospitalSounds =
            { SoundIds.HospitalPreview };
        private static readonly string[] SchoolSounds =
            { SoundIds.SchoolPreview };
        private static readonly string[] HouseSounds =
            { SoundIds.HousePreview };
        private static readonly string[] OfficeSounds =
            { SoundIds.OfficePreview };
        private static readonly string[] MallSounds =
            { SoundIds.MallPreview };
        private static readonly string[] PetrolStationSounds =
            { SoundIds.PetrolStationPreview };
        private static readonly string[] PoliceStationSounds =
            { SoundIds.PoliceStationPreview };
        private static readonly string[] VideoStoreSounds =
        {
            SoundIds.VideoStoreLeverPreview,
            SoundIds.VideoStoreProjectorPreview
        };
        private static readonly string[] PharmacySounds =
            { SoundIds.PharmacyPreview };
        private static readonly string[] CoffeeShopSounds =
            { SoundIds.CoffeeShopPreview };
        private static readonly string[] CinemaSounds =
        {
            SoundIds.CinemaEpicPreview,
            SoundIds.CinemaRevealPreview,
            SoundIds.CinemaComedyPreview,
            SoundIds.CinemaOrientalPreview,
            SoundIds.CinemaCalmPreview
        };
        private static readonly string[] AutoRepairSounds =
            { SoundIds.AutoRepairPreview };

        private CityFlowServices services;
        private MainCityView cityView;
        private AudioSource previewSource;
        private Vector2Int? hoveredFacility;
        private float previewStartedAt;
        private float previewEndsAt;
        private float previewBaseVolume;
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
                    out string[] soundIds))
            {
                ClearHover(stopPlayback: !isPinnedByClick);
                UpdatePreviewLifetime();
                return;
            }

            bool changed = hoveredFacility != tile;
            hoveredFacility = tile;
            if (changed)
            {
                PlayFacilityPreview(soundIds, pinByClick: false);
            }

            if (Mouse.current != null &&
                Mouse.current.leftButton.wasPressedThisFrame)
            {
                PlayFacilityPreview(soundIds, pinByClick: true);
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
            out string[] soundIds)
        {
            tile = default;
            soundIds = null;
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

            TileType type = services.TileData.GetTileType(tile);
            return TryGetFacilitySounds(tile, type, out soundIds);
        }

        private bool TryGetFacilitySounds(
            Vector2Int tile,
            TileType type,
            out string[] soundIds)
        {
            soundIds = type switch
            {
                TileType.House => HouseSounds,
                TileType.Office => OfficeSounds,
                TileType.Hospital => HospitalSounds,
                TileType.School => SchoolSounds,
                _ => null
            };
            if (soundIds != null)
            {
                return true;
            }

            if (type != TileType.SpecialBuilding ||
                services?.SpecialBuildings == null ||
                !services.SpecialBuildings.TryGetBuilding(
                    tile,
                    out SpecialBuildingInstance building))
            {
                return false;
            }

            soundIds = building.BuildingId switch
            {
                "mall" => MallSounds,
                "petrol_station" => PetrolStationSounds,
                "police_station" => PoliceStationSounds,
                "video_store" => VideoStoreSounds,
                "pharmacy" => PharmacySounds,
                "coffee_shop" => CoffeeShopSounds,
                "cinema" => CinemaSounds,
                "auto_repair" => AutoRepairSounds,
                _ => null
            };
            return soundIds != null;
        }

        private void PlayFacilityPreview(
            string[] soundIds,
            bool pinByClick)
        {
            if (!TryResolveRandomClip(
                    soundIds,
                    out AudioClip clip,
                    out float volume))
            {
                StopPreview();
                return;
            }

            previewSource.Stop();
            previewSource.clip = clip;
            previewBaseVolume = volume;
            previewSource.volume = previewBaseVolume;
            previewSource.Play();
            previewStartedAt = Time.unscaledTime;
            previewEndsAt = previewStartedAt + previewSeconds;
            isPinnedByClick = pinByClick;
        }

        private bool TryResolveRandomClip(
            string[] soundIds,
            out AudioClip clip,
            out float volume)
        {
            clip = null;
            volume = 0f;
            if (soundManager == null ||
                soundIds == null ||
                soundIds.Length == 0)
            {
                return false;
            }

            int startIndex = Random.Range(0, soundIds.Length);
            for (int offset = 0; offset < soundIds.Length; offset++)
            {
                string soundId = soundIds[
                    (startIndex + offset) % soundIds.Length];
                if (soundManager.TryGetSfx(soundId, out clip, out volume))
                {
                    return true;
                }
            }

            return false;
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
                float fadeStart = previewStartedAt + fadeStartsAtSeconds;
                if (Time.unscaledTime >= fadeStart &&
                    previewEndsAt > fadeStart)
                {
                    float fade = 1f - Mathf.InverseLerp(
                        fadeStart,
                        previewEndsAt,
                        Time.unscaledTime);
                    previewSource.volume = previewBaseVolume * fade;
                }
                return;
            }

            StopPreview();
        }

        private void ClearHover(bool stopPlayback)
        {
            hoveredFacility = null;
            if (stopPlayback && previewSource != null)
            {
                StopPreview();
            }
        }

        private void StopPreview()
        {
            if (previewSource != null)
            {
                previewSource.Stop();
                previewSource.clip = null;
            }

            previewBaseVolume = 0f;
            previewStartedAt = 0f;
            previewEndsAt = 0f;
            isPinnedByClick = false;
        }

        private AudioSource CreateSource()
        {
            GameObject child = new GameObject("Facility Preview");
            child.transform.SetParent(transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
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
            previewSeconds = 10f;
            fadeStartsAtSeconds = 6f;
        }
#endif

        // Unity setup:
        // The baked prefab resolves standard and special facilities through CityFlow services.
    }
}
