using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Feed
{
    public sealed class CitizenFeedService : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("Feed Data")]
        [SerializeField] private FeedSystemSettingsSO settings;
        [SerializeField] private FeedEventRuleSO[] eventRules = Array.Empty<FeedEventRuleSO>();
        [SerializeField] private FeedAuthorProfileSO[] authors = Array.Empty<FeedAuthorProfileSO>();
        [SerializeField] private FeedTemplateCollectionSO[] templateCollections = Array.Empty<FeedTemplateCollectionSO>();

        [Header("Signal Observation")]
        [SerializeField, Min(0.05f)] private float signalPollSeconds = 0.2f;
        [SerializeField, Min(0.05f)] private float signalSettleSeconds = 0.5f;

        private readonly Dictionary<CitizenFeedEventType, FeedEventRuleSO> ruleByType =
            new Dictionary<CitizenFeedEventType, FeedEventRuleSO>();
        private readonly Dictionary<CitizenFeedEventType, FeedTemplateCollectionSO> templatesByType =
            new Dictionary<CitizenFeedEventType, FeedTemplateCollectionSO>();
        private readonly Dictionary<Vector2Int, CongestionLevel> congestionByTile =
            new Dictionary<Vector2Int, CongestionLevel>();
        private readonly Dictionary<Vector2Int, SignalObservation> signalObservations =
            new Dictionary<Vector2Int, SignalObservation>();
        private readonly Dictionary<Vector2Int, JamObservation> jamObservations =
            new Dictionary<Vector2Int, JamObservation>();
        private readonly Dictionary<Vector2Int, CitizenFeedInfrastructureType> infrastructureByTile =
            new Dictionary<Vector2Int, CitizenFeedInfrastructureType>();
        private readonly Dictionary<CitizenFeedEventType, double> lastEventHour =
            new Dictionary<CitizenFeedEventType, double>();
        private readonly Dictionary<string, double> lastLocationHour = new Dictionary<string, double>();
        private readonly Dictionary<FeedAuthorProfileSO, double> lastAuthorHour =
            new Dictionary<FeedAuthorProfileSO, double>();
        private readonly Dictionary<string, double> lastTemplateHour = new Dictionary<string, double>();
        private readonly List<FeedCandidate> candidates = new List<FeedCandidate>(32);
        private readonly List<Vector2Int> signalRemovalBuffer = new List<Vector2Int>();

        private CityFlowServices services;
        private IGameCalendarService calendar;
        private ISignalControl signalControl;
        private IIntersectionFacilityService facilityService;
        private IRouteDistanceProvider routeDistanceProvider;
        private float nextSignalPollTime;
        private float lastPostRealTime = float.NegativeInfinity;
        private long currentHourBucket = long.MinValue;
        private long currentDayBucket = long.MinValue;
        private int postsThisHour;
        private int postsThisDay;
        private bool vehicleSurgeArmed = true;
        private bool initialized;

        public FeedSystemSettingsSO Settings => settings;
        public event Action<CitizenFeedPost> PostGenerated;

        private void Start()
        {
            if (initialized)
            {
                return;
            }

            CityBootstrap bootstrap = FindAnyObjectByType<CityBootstrap>();
            if (bootstrap != null && bootstrap.Services != null)
            {
                Initialize(bootstrap.Services);
            }
        }

        private void Update()
        {
            if (!initialized || Time.unscaledTime < nextSignalPollTime)
            {
                return;
            }

            nextSignalPollTime = Time.unscaledTime + signalPollSeconds;
            ObserveSignals();
            ObserveSustainedCongestion();
            ObserveVehicleSurge();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        public void Configure(
            FeedSystemSettingsSO targetSettings,
            FeedEventRuleSO[] targetRules,
            FeedAuthorProfileSO[] targetAuthors,
            FeedTemplateCollectionSO[] targetTemplateCollections)
        {
            settings = targetSettings;
            eventRules = targetRules ?? Array.Empty<FeedEventRuleSO>();
            authors = targetAuthors ?? Array.Empty<FeedAuthorProfileSO>();
            templateCollections = targetTemplateCollections ?? Array.Empty<FeedTemplateCollectionSO>();
            RebuildLookups();
        }

        public void Initialize(CityFlowServices targetServices)
        {
            if (targetServices == null || services == targetServices && initialized)
            {
                return;
            }

            Unsubscribe();
            services = targetServices;
            signalControl = services.Placement as ISignalControl;
            facilityService = services.Placement as IIntersectionFacilityService;
            routeDistanceProvider = services.Placement as IRouteDistanceProvider;
            services.Events.CongestionChanged += OnCongestionChanged;
            services.Events.InfrastructureChanged += OnInfrastructureChanged;
            services.Events.Arrival += OnArrival;
            services.GameCalendarRegistered += OnGameCalendarRegistered;
            BindCalendar(services.GameCalendar);
            RebuildLookups();
            SnapshotSignals();
            SnapshotInfrastructure();
            vehicleSurgeArmed = true;
            initialized = settings != null && ruleByType.Count > 0 && templatesByType.Count > 0;

            if (!initialized)
            {
                Debug.LogWarning(
                    "[CitizenFeed] Feed data is incomplete. Run the Green SNS V1 data generator and rebake the UI.");
                return;
            }

            Debug.Log(
                $"[CitizenFeed] Initialized with {authors.Length} authors, " +
                $"{ruleByType.Count} event rules and {templatesByType.Count} template collections.");
        }

        private void OnCongestionChanged(CongestionEvent congestionEvent)
        {
            CongestionLevel previousLevel = congestionByTile.TryGetValue(
                congestionEvent.Tile,
                out CongestionLevel knownLevel)
                ? knownLevel
                : CongestionLevel.Free;
            congestionByTile[congestionEvent.Tile] = congestionEvent.Level;

            if (congestionEvent.Level == CongestionLevel.Jam && previousLevel != CongestionLevel.Jam)
            {
                jamObservations[congestionEvent.Tile] = new JamObservation(GetAbsoluteGameHour());
            }
            else if (congestionEvent.Level != CongestionLevel.Jam)
            {
                jamObservations.Remove(congestionEvent.Tile);
            }

            CitizenFeedEventType? feedEventType = null;
            if (previousLevel == CongestionLevel.Free && congestionEvent.Level == CongestionLevel.Slow)
            {
                feedEventType = CitizenFeedEventType.CongestionSlowed;
            }
            else if (congestionEvent.Level == CongestionLevel.Jam && previousLevel != CongestionLevel.Jam)
            {
                feedEventType = CitizenFeedEventType.CongestionStarted;
            }
            else if (previousLevel == CongestionLevel.Jam && congestionEvent.Level != CongestionLevel.Jam)
            {
                feedEventType = CitizenFeedEventType.CongestionResolved;
            }

            if (!feedEventType.HasValue)
            {
                return;
            }

            float density01 = services.TileData.GetDensity01(congestionEvent.Tile);
            CitizenFeedContext context = CitizenFeedContext.ForCongestion(
                feedEventType.Value,
                congestionEvent.Tile,
                density01,
                previousLevel,
                congestionEvent.Level,
                GetGameHour());
            TryGeneratePost(context);
        }

        private bool TryGeneratePost(in CitizenFeedContext context)
        {
            if (!CanAttemptPost(context, out FeedEventRuleSO rule, out double absoluteHour))
            {
                return false;
            }

            float score = rule.CalculateScore(context);
            if (score < settings.MinimumFeedScore)
            {
                return false;
            }

            float scoreChance = ScoreToChance(score);
            if (UnityEngine.Random.value > Mathf.Clamp01(rule.BaseChance * scoreChance))
            {
                return false;
            }

            FeedCandidate selected = SelectCandidate(context.EventType, rule, absoluteHour);
            if (selected == null)
            {
                return false;
            }

            string message = CitizenFeedFormatter.Format(selected.Template.Text, context);
            message = CitizenFeedFormatter.Decorate(
                message,
                selected.Author,
                settings.DecorationChance);
            CitizenFeedPost post = new CitizenFeedPost(
                context.EventType,
                context.Tile,
                selected.Author.DisplayName,
                selected.Author.RoleLabel,
                message,
                GetTimestamp(),
                selected.Author.AvatarInitial,
                selected.Author.AccentColor);

            RecordPost(context, rule, selected, absoluteHour);
            PostGenerated?.Invoke(post);

            if (settings.LogDiagnostics)
            {
                Debug.Log(
                    $"[CitizenFeed] {context.EventType} posted by {selected.Author.DisplayName} " +
                    $"at {context.Tile}. Score={score:0.0}, Template={selected.Template.TemplateId}");
            }

            return true;
        }

        private bool CanAttemptPost(
            in CitizenFeedContext context,
            out FeedEventRuleSO rule,
            out double absoluteHour)
        {
            rule = null;
            absoluteHour = GetAbsoluteGameHour();
            if (!initialized || settings == null ||
                !ruleByType.TryGetValue(context.EventType, out rule) ||
                rule == null || !rule.RuleEnabled)
            {
                return false;
            }

            RefreshRateBuckets(absoluteHour);
            if (postsThisHour >= settings.MaximumPostsPerGameHour ||
                postsThisDay >= settings.MaximumPostsPerGameDay ||
                Time.unscaledTime - lastPostRealTime < settings.MinimumRealSecondsBetweenPosts)
            {
                return false;
            }

            if (lastEventHour.TryGetValue(context.EventType, out double eventHour) &&
                absoluteHour - eventHour < rule.CooldownGameHours)
            {
                return false;
            }

            string locationKey = CreateLocationKey(context.EventType, context.Tile);
            return !lastLocationHour.TryGetValue(locationKey, out double locationHour) ||
                   absoluteHour - locationHour >= settings.SameLocationCooldownHours;
        }

        private FeedCandidate SelectCandidate(
            CitizenFeedEventType eventType,
            FeedEventRuleSO rule,
            double absoluteHour)
        {
            candidates.Clear();
            if (!templatesByType.TryGetValue(eventType, out FeedTemplateCollectionSO collection) ||
                collection == null)
            {
                return null;
            }

            for (int authorIndex = 0; authorIndex < authors.Length; authorIndex++)
            {
                FeedAuthorProfileSO author = authors[authorIndex];
                if (author == null || author.PostingWeight <= 0f ||
                    !author.Supports(eventType) || !rule.AllowsRole(author.Role) ||
                    !author.IsActiveAtHour(GetGameHour()) ||
                    IsAuthorCoolingDown(author, absoluteHour))
                {
                    continue;
                }

                IReadOnlyList<CitizenFeedTemplateEntry> templates = collection.Templates;
                for (int templateIndex = 0; templateIndex < templates.Count; templateIndex++)
                {
                    CitizenFeedTemplateEntry template = templates[templateIndex];
                    CitizenFeedTimePeriod timePeriod = CitizenFeedFormatter.GetTimePeriod(GetGameHour());
                    if (template == null || template.Weight <= 0f ||
                        !template.Allows(author, timePeriod) ||
                        IsTemplateCoolingDown(template.TemplateId, absoluteHour))
                    {
                        continue;
                    }

                    candidates.Add(new FeedCandidate(
                        author,
                        template,
                        author.PostingWeight * template.Weight * author.GetToneWeight(template.Tone)));
                }
            }

            float totalWeight = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                totalWeight += candidates[i].Weight;
            }

            if (totalWeight <= 0f)
            {
                return null;
            }

            float roll = UnityEngine.Random.value * totalWeight;
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= candidates[i].Weight;
                if (roll <= 0f)
                {
                    return candidates[i];
                }
            }

            return candidates[candidates.Count - 1];
        }

        private void RecordPost(
            in CitizenFeedContext context,
            FeedEventRuleSO rule,
            FeedCandidate selected,
            double absoluteHour)
        {
            postsThisHour++;
            postsThisDay++;
            lastPostRealTime = Time.unscaledTime;
            lastEventHour[context.EventType] = absoluteHour;
            lastLocationHour[CreateLocationKey(context.EventType, context.Tile)] = absoluteHour;
            lastAuthorHour[selected.Author] = absoluteHour;
            lastTemplateHour[selected.Template.TemplateId] = absoluteHour;
        }

        private void ObserveSignals()
        {
            signalControl ??= services?.Placement as ISignalControl;
            if (signalControl == null)
            {
                return;
            }

            IReadOnlyList<Vector2Int> signalTiles = signalControl.SignalTiles;
            for (int i = 0; i < signalTiles.Count; i++)
            {
                Vector2Int tile = signalTiles[i];
                SignalSnapshot current = ReadSignal(tile);
                if (!signalObservations.TryGetValue(tile, out SignalObservation observation))
                {
                    signalObservations[tile] = new SignalObservation(current);
                    continue;
                }

                observation.Observe(current, Time.unscaledTime);
            }

            signalRemovalBuffer.Clear();
            foreach (KeyValuePair<Vector2Int, SignalObservation> pair in signalObservations)
            {
                if (!ContainsTile(signalTiles, pair.Key))
                {
                    signalRemovalBuffer.Add(pair.Key);
                    continue;
                }

                if (!pair.Value.TryTakeSettledChange(
                        Time.unscaledTime,
                        signalSettleSeconds,
                        out SignalSnapshot previous,
                        out SignalSnapshot current))
                {
                    continue;
                }

                CitizenFeedContext context = CitizenFeedContext.ForSignal(
                    pair.Key,
                    previous.GreenSlots,
                    current.GreenSlots,
                    previous.OffsetSlots,
                    current.OffsetSlots,
                    GetGameHour());
                TryGeneratePost(context);
            }

            for (int i = 0; i < signalRemovalBuffer.Count; i++)
            {
                signalObservations.Remove(signalRemovalBuffer[i]);
            }
        }

        private void SnapshotSignals()
        {
            signalObservations.Clear();
            if (signalControl == null)
            {
                return;
            }

            IReadOnlyList<Vector2Int> signalTiles = signalControl.SignalTiles;
            for (int i = 0; i < signalTiles.Count; i++)
            {
                Vector2Int tile = signalTiles[i];
                signalObservations[tile] = new SignalObservation(ReadSignal(tile));
            }
        }

        private SignalSnapshot ReadSignal(Vector2Int tile)
        {
            return new SignalSnapshot(
                signalControl.GetSignalGreenSlots(tile),
                signalControl.GetSignalOffsetSlots(tile));
        }

        private void ObserveSustainedCongestion()
        {
            if (settings == null || jamObservations.Count == 0)
            {
                return;
            }

            double absoluteHour = GetAbsoluteGameHour();
            foreach (KeyValuePair<Vector2Int, JamObservation> pair in jamObservations)
            {
                JamObservation observation = pair.Value;
                if (observation.PostGenerated ||
                    absoluteHour - observation.StartHour < settings.SustainedCongestionGameHours)
                {
                    continue;
                }

                observation.PostGenerated = true;
                float density01 = services.TileData.GetDensity01(pair.Key);
                CitizenFeedContext context = CitizenFeedContext.ForCongestion(
                    CitizenFeedEventType.CongestionSustained,
                    pair.Key,
                    density01,
                    CongestionLevel.Jam,
                    CongestionLevel.Jam,
                    GetGameHour());
                TryGeneratePost(context);
            }
        }


        private void OnInfrastructureChanged(InfrastructureChangedEvent infrastructureEvent)
        {
            if (!initialized || facilityService == null)
            {
                return;
            }

            infrastructureByTile.TryGetValue(
                infrastructureEvent.Tile,
                out CitizenFeedInfrastructureType previousType);
            CitizenFeedInfrastructureType currentType = ReadInfrastructureType(infrastructureEvent.Tile);
            CitizenFeedInfrastructureType affectedType = infrastructureEvent.IsRemove
                ? previousType
                : currentType;

            if (currentType == CitizenFeedInfrastructureType.None)
            {
                infrastructureByTile.Remove(infrastructureEvent.Tile);
            }
            else
            {
                infrastructureByTile[infrastructureEvent.Tile] = currentType;
            }

            if (affectedType == CitizenFeedInfrastructureType.None)
            {
                return;
            }

            CitizenFeedEventType eventType = infrastructureEvent.IsRemove
                ? CitizenFeedEventType.InfrastructureRemoved
                : CitizenFeedEventType.InfrastructurePlaced;
            CitizenFeedContext context = CitizenFeedContext.ForInfrastructure(
                eventType,
                infrastructureEvent.Tile,
                affectedType,
                infrastructureEvent.IsRemove,
                GetGameHour());
            TryGeneratePost(context);
        }

        private void OnArrival(ArrivalEvent arrivalEvent)
        {
            if (!initialized || routeDistanceProvider == null || settings == null ||
                !routeDistanceProvider.TryGetAverageRouteDistance(
                    arrivalEvent.Destination,
                    out float distanceTiles) ||
                distanceTiles < settings.NotableArrivalDistanceTiles)
            {
                return;
            }

            CitizenFeedContext context = CitizenFeedContext.ForArrival(
                arrivalEvent.Destination,
                distanceTiles,
                GetGameHour());
            TryGeneratePost(context);
        }

        private void ObserveVehicleSurge()
        {
            if (settings == null || services?.Stats == null)
            {
                return;
            }

            int activeVehicleCount = services.Stats.ActiveVehicleCount;
            if (!vehicleSurgeArmed)
            {
                int resetCount = Mathf.FloorToInt(
                    settings.VehicleSurgeCount * settings.VehicleSurgeResetRatio);
                vehicleSurgeArmed = activeVehicleCount <= resetCount;
                return;
            }

            if (activeVehicleCount < settings.VehicleSurgeCount)
            {
                return;
            }

            vehicleSurgeArmed = false;
            CitizenFeedContext context = CitizenFeedContext.ForVehicleSurge(
                activeVehicleCount,
                GetGameHour());
            TryGeneratePost(context);
        }

        private void SnapshotInfrastructure()
        {
            infrastructureByTile.Clear();
            if (facilityService == null)
            {
                return;
            }

            AddInfrastructureTiles(facilityService.SignalTiles, CitizenFeedInfrastructureType.Signal);
            AddInfrastructureTiles(
                facilityService.RoundaboutTiles,
                CitizenFeedInfrastructureType.Roundabout);
            AddInfrastructureTiles(facilityService.OverpassTiles, CitizenFeedInfrastructureType.Overpass);
            AddInfrastructureTiles(
                facilityService.PriorityRoadTiles,
                CitizenFeedInfrastructureType.PriorityRoad);
        }

        private void AddInfrastructureTiles(
            IReadOnlyList<Vector2Int> tiles,
            CitizenFeedInfrastructureType infrastructureType)
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                infrastructureByTile[tiles[i]] = infrastructureType;
            }
        }

        private CitizenFeedInfrastructureType ReadInfrastructureType(Vector2Int tile)
        {
            if (ContainsTile(facilityService.SignalTiles, tile))
            {
                return CitizenFeedInfrastructureType.Signal;
            }

            if (ContainsTile(facilityService.RoundaboutTiles, tile))
            {
                return CitizenFeedInfrastructureType.Roundabout;
            }

            if (ContainsTile(facilityService.OverpassTiles, tile))
            {
                return CitizenFeedInfrastructureType.Overpass;
            }

            return ContainsTile(facilityService.PriorityRoadTiles, tile)
                ? CitizenFeedInfrastructureType.PriorityRoad
                : CitizenFeedInfrastructureType.None;
        }

        private void RebuildLookups()
        {
            ruleByType.Clear();
            for (int i = 0; i < eventRules.Length; i++)
            {
                FeedEventRuleSO rule = eventRules[i];
                if (rule != null)
                {
                    ruleByType[rule.EventType] = rule;
                }
            }

            templatesByType.Clear();
            for (int i = 0; i < templateCollections.Length; i++)
            {
                FeedTemplateCollectionSO collection = templateCollections[i];
                if (collection != null)
                {
                    templatesByType[collection.EventType] = collection;
                }
            }
        }

        private void OnGameCalendarRegistered(IGameCalendarService gameCalendar)
        {
            BindCalendar(gameCalendar);
        }

        private void BindCalendar(IGameCalendarService gameCalendar)
        {
            calendar = gameCalendar;
        }

        private void Unsubscribe()
        {
            if (services != null)
            {
                services.Events.CongestionChanged -= OnCongestionChanged;
                services.Events.InfrastructureChanged -= OnInfrastructureChanged;
                services.Events.Arrival -= OnArrival;
                services.GameCalendarRegistered -= OnGameCalendarRegistered;
            }

            initialized = false;
        }

        private bool IsAuthorCoolingDown(FeedAuthorProfileSO author, double absoluteHour)
        {
            return lastAuthorHour.TryGetValue(author, out double previousHour) &&
                   absoluteHour - previousHour < settings.SameAuthorCooldownHours;
        }

        private bool IsTemplateCoolingDown(string templateId, double absoluteHour)
        {
            return lastTemplateHour.TryGetValue(templateId, out double previousHour) &&
                   absoluteHour - previousHour < settings.SameTemplateCooldownHours;
        }

        private void RefreshRateBuckets(double absoluteHour)
        {
            long hourBucket = (long)Math.Floor(absoluteHour);
            long dayBucket = hourBucket / 24L;
            if (hourBucket != currentHourBucket)
            {
                currentHourBucket = hourBucket;
                postsThisHour = 0;
            }

            if (dayBucket != currentDayBucket)
            {
                currentDayBucket = dayBucket;
                postsThisDay = 0;
            }
        }

        private double GetAbsoluteGameHour()
        {
            return calendar != null
                ? calendar.TotalDays * 24d + calendar.Hour
                : Time.unscaledTime;
        }

        private int GetGameHour()
        {
            return calendar != null
                ? calendar.Hour
                : Mathf.FloorToInt(Time.unscaledTime) % 24;
        }

        private string GetTimestamp()
        {
            return calendar != null
                ? CitizenFeedFormatter.FormatTimestamp(
                    calendar.Year,
                    calendar.Month,
                    calendar.Day,
                    calendar.Hour)
                : $"{GetGameHour():00}:00";
        }

        private static float ScoreToChance(float score)
        {
            if (score >= 90f) return 1f;
            if (score >= 70f) return 0.8f;
            if (score >= 50f) return 0.55f;
            return 0.3f;
        }

        private static string CreateLocationKey(CitizenFeedEventType eventType, Vector2Int tile)
        {
            return $"{eventType}:{tile.x}:{tile.y}";
        }

        private static bool ContainsTile(IReadOnlyList<Vector2Int> tiles, Vector2Int target)
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                if (tiles[i] == target)
                {
                    return true;
                }
            }

            return false;
        }


        private sealed class FeedCandidate
        {
            public FeedAuthorProfileSO Author { get; }
            public CitizenFeedTemplateEntry Template { get; }
            public float Weight { get; }

            public FeedCandidate(
                FeedAuthorProfileSO author,
                CitizenFeedTemplateEntry template,
                float weight)
            {
                Author = author;
                Template = template;
                Weight = weight;
            }
        }

        private readonly struct SignalSnapshot
        {
            public int GreenSlots { get; }
            public int OffsetSlots { get; }

            public SignalSnapshot(int greenSlots, int offsetSlots)
            {
                GreenSlots = greenSlots;
                OffsetSlots = offsetSlots;
            }

            public bool Equals(SignalSnapshot other)
            {
                return GreenSlots == other.GreenSlots && OffsetSlots == other.OffsetSlots;
            }
        }

        private sealed class SignalObservation
        {
            private SignalSnapshot current;
            private SignalSnapshot pendingStart;
            private float lastChangeTime;
            private bool hasPendingChange;

            public SignalObservation(SignalSnapshot initial)
            {
                current = initial;
                pendingStart = initial;
            }

            public void Observe(SignalSnapshot next, float currentTime)
            {
                if (current.Equals(next))
                {
                    return;
                }

                if (!hasPendingChange)
                {
                    pendingStart = current;
                    hasPendingChange = true;
                }

                current = next;
                lastChangeTime = currentTime;
            }

            public bool TryTakeSettledChange(
                float currentTime,
                float settleSeconds,
                out SignalSnapshot previous,
                out SignalSnapshot next)
            {
                previous = pendingStart;
                next = current;
                if (!hasPendingChange || currentTime - lastChangeTime < settleSeconds)
                {
                    return false;
                }

                hasPendingChange = false;
                return !previous.Equals(next);
            }
        }

        private sealed class JamObservation
        {
            public double StartHour { get; }
            public bool PostGenerated { get; set; }

            public JamObservation(double startHour)
            {
                StartHour = startHour;
            }
        }

        // Unity setup: The Green SNS baker assigns all V1 data assets automatically.
    }
}
