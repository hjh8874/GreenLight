using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CityFlow.Managers;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace CityFlow.Audio.Editor
{
    public static class SoundSystemBaker
    {
        private const string AudioRoot = "Assets/04_Audio";
        private const float FacilityPreviewVolume = 0.2f;
        private const float EmergencyFacilityPreviewVolume = 0.08f;
        private const string ConfigRoot = AudioRoot + "/Configs";
        private const string MixerRoot = AudioRoot + "/Mixers";
        private const string PrefabRoot = AudioRoot + "/Prefabs";
        private const string CatalogPath = ConfigRoot + "/SoundCatalog.asset";
        private const string AmbienceProfilePath =
            ConfigRoot + "/CityAmbienceProfile.asset";
        private const string BgmPlaylistPath = ConfigRoot + "/BgmPlaylist.asset";
        private const string MixerPath = MixerRoot + "/CityAudioMixer.mixer";
        private const string PrefabPath = PrefabRoot + "/SoundSystem.prefab";

        private const string DownloadRoot = "Assets/99_Download";
        private const string UiPack = DownloadRoot + "/Casual Game UI Sound";
        private const string CityNoisePack = DownloadRoot + "/City_noise";
        private const string BackgroundPack = DownloadRoot +
            "/Gamemaster Audio - Pro Sound Collection/Backgrounds";
        private const string FacilityMusicPack = DownloadRoot +
            "/Gamemaster Audio - Pro Sound Collection/" +
            "\u03A9_Bonus_Music_16bit44kOnly";

        private static readonly string PlacementSuccessPath =
            UiPack + "/ITEM/ITEM_Gear_Wood_Put.wav";
        private static readonly string PlacementRejectedPath =
            UiPack + "/USER_INTERFACE/USER_INTERFACE_Click_12.wav";
        private static readonly string DemolitionPath =
            UiPack + "/ITEM/ITEM_Soil_Put.wav";
        private static readonly string CoinPath = DownloadRoot +
            "/Cozy_Sound_Pack/Coin/Coin_Handling.wav";
        private static readonly string UiClickPath = DownloadRoot +
            "/Cyberleaf - Modern UI SFX/Buttons/ClickyButton4.wav";
        private static readonly string PositiveNotificationPath =
            UiPack +
            "/NOTIFICATION/NOTIFICATION_Positive_Notification_09.wav";
        private static readonly string LargePositiveNotificationPath =
            UiPack +
            "/NOTIFICATION/NOTIFICATION_Positive_Notification_10.wav";
        private static readonly string HospitalPath = DownloadRoot +
            "/Gamemaster Audio - Pro Sound Collection/" +
            "Alarms_Beeps_Siren/alarm_siren_loop_01.wav";
        private static readonly string SchoolPath = CityNoisePack +
            "/geralt-the-big-break-at-half-past-nine-220150.mp3";
        private static readonly string HousePath = CityNoisePack +
            "/freesound_community-going-through-a-zoo-with-lots-of-" +
            "people-atmo-24895.mp3";
        private static readonly string OfficePath = CityNoisePack +
            "/virtual_vibes-office-desk-keystrokes-423439.mp3";
        private static readonly string MallPath = CityNoisePack +
            "/dragon-studio-large-shopping-mall-on-christmas-eve-451860.mp3";
        private static readonly string PetrolStationPath = BackgroundPack +
            "/background_construction_building_loop.wav";
        private static readonly string PoliceStationPath = CityNoisePack +
            "/freesound_community-police-radio-chatter-30048.mp3";
        private static readonly string VideoStoreLeverPath = DownloadRoot +
            "/Gamemaster Audio - Pro Sound Collection/" +
            "Switches_Buttons_Gears_Levers/" +
            "lever_turn_push_crank_handle_small_08.wav";
        private static readonly string VideoStoreProjectorPath = DownloadRoot +
            "/Gamemaster Audio - Pro Sound Collection/Miscellaneous/" +
            "Film Camera Movie Projector Small/" +
            "movie_camera_vintage_shutter_loop_3.wav";
        private static readonly string PharmacyPath = CityNoisePack +
            "/ribhavagrawal-coughing-sound-effect-type-06-294181.mp3";
        private static readonly string CoffeeShopPath = BackgroundPack +
            "/background_people_restaurant_cafe_noisy_chatter_talk_loop_01.wav";
        private static readonly string CinemaEpicPath = FacilityMusicPack +
            "/music_epic_fallen_empire.wav";
        private static readonly string CinemaRevealPath = FacilityMusicPack +
            "/music_cinematic_reveal.wav";
        private static readonly string CinemaComedyPath = FacilityMusicPack +
            "/music_comedy_quirky_fun_knockout.wav";
        private static readonly string CinemaOrientalPath = FacilityMusicPack +
            "/music_oriental_sunrise.wav";
        private static readonly string CinemaCalmPath = FacilityMusicPack +
            "/music_calm_green_lake_serenade.wav";
        private static readonly string AutoRepairPath = BackgroundPack +
            "/background_construction_factory_warehouse_machine_loop_01.wav";
        private static readonly string CongestionPath = CityNoisePack +
            "/" +
            "freesound_community-downtown-traffic-and-crowd-noises-14734.mp3";
        private static readonly string[] CongestionHornPaths =
        {
            CityNoisePack + "/99647C365D0D18E402.mp3",
            CityNoisePack + "/993FF73C5D0D195402.mp3"
        };
        private static readonly string RoomTonePath =
            BackgroundPack + "/background_room_tone_loop_01.wav";
        private const string BgmFolder = DownloadRoot + "/BGM";

        private static readonly string[] DayPaths =
        {
            BackgroundPack + "/background_crowd_people_chatter_loop_01.wav",
            BackgroundPack + "/background_crowd_people_chatter_loop_02.wav",
            BackgroundPack +
            "/background_people_crowd_noisy_chatter_talking_mumble_loop_01.wav",
            BackgroundPack +
            "/background_people_crowd_noisy_chatter_talking_mumble_loop_02.wav"
        };

        private static readonly string[] NightPaths =
        {
            BackgroundPack + "/background_quiet_urban_park_loop_01.wav",
            BackgroundPack + "/background_quiet_urban_park_loop_02.wav"
        };

        private static readonly string[] ShortSfxPaths =
        {
            PlacementSuccessPath,
            PlacementRejectedPath,
            DemolitionPath,
            CoinPath,
            UiClickPath,
            PositiveNotificationPath,
            LargePositiveNotificationPath,
            HospitalPath,
            OfficePath,
            MallPath,
            PoliceStationPath,
            VideoStoreLeverPath,
            VideoStoreProjectorPath,
            PharmacyPath,
            CongestionHornPaths[0],
            CongestionHornPaths[1]
        };

        private static readonly string[] FacilityStreamingPaths =
        {
            SchoolPath,
            HousePath,
            PetrolStationPath,
            CoffeeShopPath,
            CinemaEpicPath,
            CinemaRevealPath,
            CinemaComedyPath,
            CinemaOrientalPath,
            CinemaCalmPath,
            AutoRepairPath
        };

        [MenuItem("Tools/GreenLight/Audio/Bake Sound System")]
        public static void BakeSoundSystem()
        {
            EnsureFolder(ConfigRoot);
            EnsureFolder(MixerRoot);
            EnsureFolder(PrefabRoot);

            ConfigureImportSettings();

            SoundCatalog catalog = LoadOrCreateAsset<SoundCatalog>(CatalogPath);
            CityAmbienceProfileSO ambienceProfile =
                LoadOrCreateAsset<CityAmbienceProfileSO>(AmbienceProfilePath);
            BgmPlaylistSO bgmPlaylist =
                LoadOrCreateAsset<BgmPlaylistSO>(BgmPlaylistPath);

            ConfigureCatalog(catalog);
            ConfigureAmbienceProfile(ambienceProfile);
            ConfigureBgmPlaylist(bgmPlaylist);

            AudioMixer mixer = LoadOrCreateMixer();
            Dictionary<string, AudioMixerGroup> groups =
                EnsureMixerGroups(mixer);
            BakePrefab(catalog, ambienceProfile, bgmPlaylist, groups);

            EditorUtility.SetDirty(catalog);
            EditorUtility.SetDirty(ambienceProfile);
            EditorUtility.SetDirty(bgmPlaylist);
            if (mixer != null)
            {
                EditorUtility.SetDirty(mixer);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabPath);
            Debug.Log(
                "[SoundSystemBaker] Sound system baked successfully. " +
                $"Place {PrefabPath} in a feature or integration scene.");
        }

        private static void ConfigureCatalog(SoundCatalog catalog)
        {
            List<SoundCatalog.SoundEntry> entries = new()
            {
                Entry(SoundIds.PlacementSuccess, PlacementSuccessPath, 0.7f, 0.06f),
                Entry(SoundIds.PlacementRejected, PlacementRejectedPath, 0.7f, 0.15f),
                Entry(SoundIds.DemolitionSuccess, DemolitionPath, 0.8f, 0.08f),
                Entry(SoundIds.CoinTransaction, CoinPath, 0.7f, 0.08f),
                Entry(
                    SoundIds.HarvestRewardStandard,
                    PositiveNotificationPath,
                    0.75f,
                    0f),
                Entry(
                    SoundIds.HarvestRewardLarge,
                    LargePositiveNotificationPath,
                    0.75f,
                    0f),
                Entry(SoundIds.UiClick, UiClickPath, 0.45f, 0.03f),
                Entry(
                    SoundIds.PositiveNotification,
                    PositiveNotificationPath,
                    0.75f,
                    1f),
                Entry(SoundIds.FlowBurst, PositiveNotificationPath, 0.65f, 0.5f),
                Entry(
                    SoundIds.HospitalPreview,
                    HospitalPath,
                    EmergencyFacilityPreviewVolume,
                    0f),
                Entry(
                    SoundIds.SchoolPreview,
                    SchoolPath,
                    FacilityPreviewVolume,
                    0f),
                Entry(
                    SoundIds.HousePreview,
                    HousePath,
                    FacilityPreviewVolume,
                    0f),
                Entry(
                    SoundIds.OfficePreview,
                    OfficePath,
                    FacilityPreviewVolume,
                    0f),
                Entry(
                    SoundIds.MallPreview,
                    MallPath,
                    FacilityPreviewVolume,
                    0f),
                Entry(
                    SoundIds.PetrolStationPreview,
                    PetrolStationPath,
                    FacilityPreviewVolume,
                    0f),
                Entry(
                    SoundIds.PoliceStationPreview,
                    PoliceStationPath,
                    EmergencyFacilityPreviewVolume,
                    0f),
                Entry(
                    SoundIds.VideoStoreLeverPreview,
                    VideoStoreLeverPath,
                    FacilityPreviewVolume,
                    0f),
                Entry(
                    SoundIds.VideoStoreProjectorPreview,
                    VideoStoreProjectorPath,
                    FacilityPreviewVolume,
                    0f),
                Entry(
                    SoundIds.PharmacyPreview,
                    PharmacyPath,
                    FacilityPreviewVolume,
                    0f),
                Entry(
                    SoundIds.CoffeeShopPreview,
                    CoffeeShopPath,
                    FacilityPreviewVolume,
                    0f),
                Entry(
                    SoundIds.CinemaEpicPreview,
                    CinemaEpicPath,
                    FacilityPreviewVolume,
                    0f),
                Entry(
                    SoundIds.CinemaRevealPreview,
                    CinemaRevealPath,
                    FacilityPreviewVolume,
                    0f),
                Entry(
                    SoundIds.CinemaComedyPreview,
                    CinemaComedyPath,
                    FacilityPreviewVolume,
                    0f),
                Entry(
                    SoundIds.CinemaOrientalPreview,
                    CinemaOrientalPath,
                    FacilityPreviewVolume,
                    0f),
                Entry(
                    SoundIds.CinemaCalmPreview,
                    CinemaCalmPath,
                    FacilityPreviewVolume,
                    0f),
                Entry(
                    SoundIds.AutoRepairPreview,
                    AutoRepairPath,
                    FacilityPreviewVolume,
                    0f)
            };
            catalog.EditorSetSounds(entries);
        }

        private static SoundCatalog.SoundEntry Entry(
            string id,
            string path,
            float volume,
            float cooldown)
        {
            return new SoundCatalog.SoundEntry(
                id,
                SoundType.Sfx,
                LoadClip(path),
                volume,
                cooldown,
                preload: true);
        }

        private static void ConfigureAmbienceProfile(
            CityAmbienceProfileSO profile)
        {
            profile.EditorConfigure(
                DayPaths.Select(LoadClip).Where(clip => clip != null).ToArray(),
                NightPaths.Select(LoadClip).Where(clip => clip != null).ToArray(),
                LoadClip(RoomTonePath),
                LoadClip(CongestionPath),
                CongestionHornPaths
                    .Select(LoadClip)
                    .Where(clip => clip != null)
                    .ToArray());
        }

        private static void ConfigureBgmPlaylist(BgmPlaylistSO playlist)
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:AudioClip",
                new[] { BgmFolder });
            AudioClip[] clips = guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(LoadClip)
                .Where(clip => clip != null)
                .ToArray();

            if (clips.Length == 0)
            {
                Debug.LogWarning(
                    $"[SoundSystemBaker] No BGM clips were found under {BgmFolder}.");
            }

            playlist.EditorConfigure(clips);
        }

        private static void ConfigureImportSettings()
        {
            for (int index = 0; index < ShortSfxPaths.Length; index++)
            {
                ConfigureImporter(
                    ShortSfxPaths[index],
                    AudioClipLoadType.DecompressOnLoad,
                    AudioCompressionFormat.PCM,
                    preload: true,
                    loadInBackground: false,
                    quality: 1f);
            }

            List<string> streamingPaths = new(DayPaths);
            streamingPaths.AddRange(NightPaths);
            streamingPaths.Add(RoomTonePath);
            streamingPaths.Add(CongestionPath);
            streamingPaths.AddRange(FacilityStreamingPaths);

            string[] bgmGuids = AssetDatabase.FindAssets(
                "t:AudioClip",
                new[] { BgmFolder });
            for (int index = 0; index < bgmGuids.Length; index++)
            {
                streamingPaths.Add(
                    AssetDatabase.GUIDToAssetPath(bgmGuids[index]));
            }

            for (int index = 0; index < streamingPaths.Count; index++)
            {
                ConfigureImporter(
                    streamingPaths[index],
                    AudioClipLoadType.Streaming,
                    AudioCompressionFormat.Vorbis,
                    preload: false,
                    loadInBackground: true,
                    quality: 0.7f);
            }
        }

        private static void ConfigureImporter(
            string path,
            AudioClipLoadType loadType,
            AudioCompressionFormat format,
            bool preload,
            bool loadInBackground,
            float quality)
        {
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[SoundSystemBaker] Missing audio asset: {path}");
                return;
            }

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            bool changed = settings.loadType != loadType ||
                           settings.compressionFormat != format ||
                           !Mathf.Approximately(settings.quality, quality) ||
                           settings.preloadAudioData != preload ||
                           importer.loadInBackground != loadInBackground;
            if (!changed)
            {
                return;
            }

            settings.loadType = loadType;
            settings.compressionFormat = format;
            settings.quality = quality;
            settings.preloadAudioData = preload;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = loadInBackground;
            importer.SaveAndReimport();
        }

        private static AudioClip LoadClip(string path)
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                Debug.LogWarning($"[SoundSystemBaker] Audio clip not found: {path}");
            }

            return clip;
        }

        private static AudioMixer LoadOrCreateMixer()
        {
            AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (mixer != null)
            {
                return mixer;
            }

            Type controllerType = AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(assembly =>
                    assembly.GetType("UnityEditor.Audio.AudioMixerController"))
                .FirstOrDefault(type => type != null);
            MethodInfo createMethod = controllerType?.GetMethod(
                "CreateMixerControllerAtPath",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (createMethod == null)
            {
                Debug.LogError(
                    "[SoundSystemBaker] Unity AudioMixer creation API was not found.");
                return null;
            }

            mixer = createMethod.Invoke(null, new object[] { MixerPath }) as AudioMixer;
            AssetDatabase.SaveAssets();
            return mixer;
        }

        private static Dictionary<string, AudioMixerGroup> EnsureMixerGroups(
            AudioMixer mixer)
        {
            string[] names =
            {
                "Music",
                "Ambience",
                "Congestion",
                "Facility",
                "GameplaySFX",
                "UI",
                "Radio"
            };
            Dictionary<string, AudioMixerGroup> result = new();
            if (mixer == null)
            {
                for (int index = 0; index < names.Length; index++)
                {
                    result[names[index]] = null;
                }
                return result;
            }

            for (int index = 0; index < names.Length; index++)
            {
                string name = names[index];
                AudioMixerGroup group = FindExactGroup(mixer, name);
                if (group == null)
                {
                    group = CreateMixerGroup(mixer, name);
                }

                if (group != null)
                {
                    AttachMixerGroupToMaster(mixer, group);
                }

                result[name] = group ??
                    mixer.FindMatchingGroups("Master").FirstOrDefault();
            }

            return result;
        }

        private static AudioMixerGroup FindExactGroup(
            AudioMixer mixer,
            string name)
        {
            return AssetDatabase.LoadAllAssetsAtPath(MixerPath)
                .OfType<AudioMixerGroup>()
                .FirstOrDefault(group => group.name == name);
        }

        private static AudioMixerGroup CreateMixerGroup(
            AudioMixer mixer,
            string name)
        {
            MethodInfo method = mixer.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance)
                .FirstOrDefault(candidate =>
                    candidate.Name == "CreateNewGroup" &&
                    candidate.GetParameters().Length == 2 &&
                    candidate.GetParameters()[0].ParameterType == typeof(string));
            if (method == null)
            {
                Debug.LogWarning(
                    $"[SoundSystemBaker] Could not create AudioMixer group {name}.");
                return null;
            }

            AudioMixerGroup group = method.Invoke(
                mixer,
                new object[] { name, false }) as AudioMixerGroup;
            EditorUtility.SetDirty(mixer);
            AssetDatabase.SaveAssets();
            return group;
        }

        private static void AttachMixerGroupToMaster(
            AudioMixer mixer,
            AudioMixerGroup group)
        {
            AudioMixerGroup master = mixer.FindMatchingGroups("Master")
                .FirstOrDefault(candidate => candidate.name == "Master");
            MethodInfo method = mixer.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance)
                .FirstOrDefault(candidate =>
                    candidate.Name == "AddChildToParent" &&
                    candidate.GetParameters().Length == 2);
            if (master == null || method == null)
            {
                Debug.LogWarning(
                    $"[SoundSystemBaker] Could not attach mixer group " +
                    $"{group.name} to Master.");
                return;
            }

            method.Invoke(mixer, new object[] { group, master });
            EditorUtility.SetDirty(mixer);
            AssetDatabase.SaveAssets();
        }

        private static void BakePrefab(
            SoundCatalog catalog,
            CityAmbienceProfileSO ambienceProfile,
            BgmPlaylistSO bgmPlaylist,
            IReadOnlyDictionary<string, AudioMixerGroup> groups)
        {
            GameObject root = new("SoundSystem");
            try
            {
                SoundManager manager = root.AddComponent<SoundManager>();
                manager.EditorConfigure(
                    catalog,
                    groups["Music"],
                    groups["GameplaySFX"]);

                AudioListenerFollower listenerFollower =
                    root.AddComponent<AudioListenerFollower>();
                listenerFollower.EditorConfigure(root.GetComponent<AudioListener>());
                CityAmbienceController ambience =
                    root.AddComponent<CityAmbienceController>();
                ambience.EditorConfigure(
                    ambienceProfile,
                    groups["Ambience"],
                    groups["Congestion"]);

                BgmPlaylistController bgm =
                    root.AddComponent<BgmPlaylistController>();
                bgm.EditorConfigure(bgmPlaylist, groups["Music"]);

                GameplaySoundController gameplay =
                    root.AddComponent<GameplaySoundController>();
                gameplay.EditorConfigure(manager);

                UiSoundController ui = root.AddComponent<UiSoundController>();
                ui.EditorConfigure(manager, groups["UI"]);

                FacilitySoundController facility =
                    root.AddComponent<FacilitySoundController>();
                facility.EditorConfigure(manager, groups["Facility"]);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static T LoadOrCreateAsset<T>(string path)
            where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[index]);
                }
                current = next;
            }
        }

        // Unity setup:
        // Run Tools > GreenLight > Audio > Bake Sound System.
        // The generated prefab is ready for scene placement without extra wiring.
    }
}
