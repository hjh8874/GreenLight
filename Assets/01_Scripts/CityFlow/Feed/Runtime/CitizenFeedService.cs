using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Feed
{
    public sealed class CitizenFeedService :
        MonoBehaviour,
        ICityFlowServiceConsumer,
        ICitizenFeedSaveSource
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
        private readonly CitizenConcernLedger ledger = new CitizenConcernLedger();
        // 장부는 작성자를 이름으로 기억한다(FeedAuthorProfileSO에 안정적 id가 없다).
        // 후속 글에서 이름 → 프로필로 되돌리기 위한 역인덱스.
        private readonly Dictionary<string, FeedAuthorProfileSO> authorByName =
            new Dictionary<string, FeedAuthorProfileSO>();

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
        // null = 아직 기록 전. 로드 직후 첫 관측은 기록만 하고 발행하지 않는다 —
        // 안 그러면 세이브를 열 때마다 "좋은 아침"이 튀어나온다.
        private CitizenFeedTimePeriod? lastTimePeriod;

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
            ObserveTimePeriod();
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

        /// <summary>
        /// 목록의 항목 중 **아직 없는 것만** 더한다. 씬에 직렬화된 참조는 그대로 두므로
        /// 손으로 맞춘 구성이 덮이지 않는다.
        ///
        /// 씬의 직렬화 배열만으로는 통합이 깨지기 때문에 존재한다 — 규칙이 늘거나
        /// 에셋이 재생성되면 배열에 안 들어가거나 참조가 끊기고, 해당 이벤트가
        /// 에러 없이 조용히 무동작이 된다.
        /// </summary>
        /// <returns>실제로 더해진 항목 수.</returns>
        public int MergeFrom(FeedCatalogSO catalog)
        {
            if (catalog == null) return 0;

            int added = 0;
            if (settings == null && catalog.Settings != null)
            {
                settings = catalog.Settings;
                added++;
            }

            added += MergeRules(catalog.Rules);
            added += MergeTemplates(catalog.TemplateCollections);
            added += MergeAuthors(catalog.Authors);

            if (added > 0)
            {
                RebuildLookups();
                // 데이터가 비어 있어 초기화에 실패했던 경우를 여기서 되살린다.
                initialized = initialized ||
                    services != null && settings != null &&
                    ruleByType.Count > 0 && templatesByType.Count > 0;
            }

            return added;
        }

        private int MergeRules(FeedEventRuleSO[] incoming)
        {
            if (incoming == null || incoming.Length == 0) return 0;

            var merged = new List<FeedEventRuleSO>(eventRules);
            var present = new HashSet<CitizenFeedEventType>();
            foreach (FeedEventRuleSO existing in merged)
            {
                if (existing != null) present.Add(existing.EventType);
            }

            int added = 0;
            foreach (FeedEventRuleSO rule in incoming)
            {
                if (rule == null || !present.Add(rule.EventType)) continue;
                merged.Add(rule);
                added++;
            }

            if (added > 0) eventRules = merged.ToArray();
            return added;
        }

        private int MergeTemplates(FeedTemplateCollectionSO[] incoming)
        {
            if (incoming == null || incoming.Length == 0) return 0;

            var merged = new List<FeedTemplateCollectionSO>(templateCollections);
            var present = new HashSet<CitizenFeedEventType>();
            foreach (FeedTemplateCollectionSO existing in merged)
            {
                if (existing != null) present.Add(existing.EventType);
            }

            int added = 0;
            foreach (FeedTemplateCollectionSO collection in incoming)
            {
                if (collection == null || !present.Add(collection.EventType)) continue;
                merged.Add(collection);
                added++;
            }

            if (added > 0) templateCollections = merged.ToArray();
            return added;
        }

        private int MergeAuthors(FeedAuthorProfileSO[] incoming)
        {
            if (incoming == null || incoming.Length == 0) return 0;

            var merged = new List<FeedAuthorProfileSO>(authors);
            var present = new HashSet<string>();
            foreach (FeedAuthorProfileSO existing in merged)
            {
                if (existing != null && !string.IsNullOrEmpty(existing.DisplayName))
                {
                    present.Add(existing.DisplayName);
                }
            }

            int added = 0;
            foreach (FeedAuthorProfileSO author in incoming)
            {
                if (author == null || string.IsNullOrEmpty(author.DisplayName)) continue;
                if (!present.Add(author.DisplayName)) continue;
                merged.Add(author);
                added++;
            }

            if (added > 0) authors = merged.ToArray();
            return added;
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
            services.Events.JobChanged += OnJobChanged;
            // 이미 발행되고 있었으나 듣지 않던 신호들.
            // ⚠️ 이벤트 이름과 타입 이름이 다르다 — EmergencyIncidentAlertEvent(타입) /
            //    EmergencyIncidentAlerted(이벤트). 타입명으로 쓰면 컴파일이 안 된다.
            services.Events.FlowBurst += OnFlowBurst;
            services.Events.Placed += OnPlaced;
            services.Events.EmergencyIncidentAlerted += OnEmergencyAlerted;
            services.Events.EmergencyIncidentOutcomeReported += OnEmergencyOutcomeReported;
            services.GameCalendarRegistered += OnGameCalendarRegistered;
            // 반드시 세이브 소스 등록보다 먼저다. 이미 세이브를 불러온 상태라면
            // RegisterCitizenFeedSaveSource가 등록 즉시 RestoreSnapshot을 부르고,
            // 그 안에서 GetAbsoluteGameHour()로 24시간 만료를 판정한다.
            // 달력이 아직 null이면 저장된 시각이 아니라 폴백 런타임 시각을 쓴다.
            BindCalendar(services.GameCalendar);
            services.RegisterCitizenFeedSaveSource(this);
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
            // 후속 글 여부를 먼저 안다. 상한 판단이 여기에 걸리기 때문이다.
            // 소비형 TryResolve가 아니라 TryPeek을 쓰는 이유 — 이 시점엔 글이 나갈지 모른다.
            double peekHour = GetAbsoluteGameHour();
            string pinnedName = null;
            bool isFollowUp =
                TryGetResolveKind(context.EventType, out CitizenFeedConcernKind resolveKind) &&
                ledger.TryPeek(context.Tile, resolveKind, peekHour, out pinnedName);

            if (!CanAttemptPost(
                    context, isFollowUp, out FeedEventRuleSO rule, out double absoluteHour))
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

            // 이어받는 글은 그 사람이 써야 의미가 있다. 그 사람으로 글을 못 만들면
            // (템플릿이 없거나 하면) 아무나 쓰는 일반 글로 조용히 떨어진다.
            FeedAuthorProfileSO pinnedAuthor = isFollowUp ? FindAuthor(pinnedName) : null;
            FeedCandidate selected =
                SelectCandidate(context.EventType, rule, absoluteHour, pinnedAuthor);
            if (selected == null && pinnedAuthor != null)
            {
                selected = SelectCandidate(context.EventType, rule, absoluteHour, null);
            }

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

            // 장부 갱신은 글이 실제로 나간 뒤에만 한다. 점수·확률 게이트에서 걸러진
            // 이벤트는 시민이 말한 적이 없으므로 이어받을 것도, 지울 것도 없다.
            if (isFollowUp)
            {
                ledger.TryResolve(context.Tile, resolveKind, absoluteHour, out _);
            }
            else if (TryGetConcernKind(context.EventType, out CitizenFeedConcernKind openKind))
            {
                ledger.Open(selected.Author.DisplayName, context.Tile, openKind, absoluteHour);
            }

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
            bool isFollowUp,
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

            // 시간당·일간 상한 **양쪽**에 후속 글용 1칸씩 예약한다. 이게 없으면
            // 플레이어가 문제를 고쳤는데 아무도 알아보지 않는, 이 기능이 존재할
            // 이유가 사라지는 결과가 난다. 시간당만 열어두면 일간 상한(12개)에서
            // 같은 문제가 그대로 재현된다.
            // 후속 글은 장부에 짝이 있을 때만 생기므로 이 예외로 도배가 될 수 없다.
            int hourlyCap = isFollowUp
                ? settings.MaximumPostsPerGameHour + 1
                : settings.MaximumPostsPerGameHour;
            int dailyCap = isFollowUp
                ? settings.MaximumPostsPerGameDay + 1
                : settings.MaximumPostsPerGameDay;

            if (postsThisHour >= hourlyCap ||
                postsThisDay >= dailyCap ||
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
            double absoluteHour,
            FeedAuthorProfileSO pinnedAuthor)
        {
            candidates.Clear();
            if (!templatesByType.TryGetValue(eventType, out FeedTemplateCollectionSO collection) ||
                collection == null)
            {
                return null;
            }

            // 이어받기 상황에서는 후속 전용 문구가 있으면 **그것만** 후보로 삼는다.
            // 가중치 경쟁에 맡기면(4 vs 일반 7종) 절반 가까이 평범한 문구가 뽑혀
            // 같은 시민이 써도 인과가 글에 드러나지 않는다. 실측 5/9였다.
            bool preferFollowUpOnly =
                pinnedAuthor != null && HasFollowUpTemplate(collection);

            for (int authorIndex = 0; authorIndex < authors.Length; authorIndex++)
            {
                FeedAuthorProfileSO author = authors[authorIndex];
                if (author == null || author.PostingWeight <= 0f) continue;

                // 이어받는 글은 지정된 사람만 쓴다. 그 사람에겐 작성자 쿨다운과
                // 활동시간을 적용하지 않는다 — 방금 불평한 사람이라 쿨다운에 걸려 있고,
                // 그걸 지키면 후속 글이 영영 안 나간다.
                if (pinnedAuthor != null)
                {
                    if (author != pinnedAuthor) continue;
                }
                else if (!author.IsActiveAtHour(GetGameHour()) ||
                         IsAuthorCoolingDown(author, absoluteHour))
                {
                    continue;
                }

                if (!author.Supports(eventType) || !rule.AllowsRole(author.Role))
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

                    // "제가 저번에 말한 그곳" 류는 앞선 불만이 있어야 성립한다.
                    // 이어받기가 아닌데 새어나가면 하지도 않은 말을 전제하게 된다.
                    if (template.FollowUpOnly && pinnedAuthor == null)
                    {
                        continue;
                    }

                    // 반대로 이어받기인데 후속 문구가 준비돼 있으면 일반 문구는 뺀다.
                    if (preferFollowUpOnly && !template.FollowUpOnly)
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

        private void OnJobChanged(JobChangedEvent jobChangedEvent)
        {
            if (!initialized)
            {
                return;
            }

            CitizenFeedContext context = CitizenFeedContext.ForJobChanged(
                jobChangedEvent.Home,
                jobChangedEvent.OldWork,
                jobChangedEvent.NewWork,
                GetGameHour());
            TryGeneratePost(context);
        }

        private void OnFlowBurst(FlowBurstEvent flowBurstEvent)
        {
            if (!initialized) return;

            TryGeneratePost(CitizenFeedContext.ForTile(
                CitizenFeedEventType.FlowBurst,
                flowBurstEvent.Tile,
                GetGameHour()));
        }

        private void OnPlaced(PlacedEvent placedEvent)
        {
            if (!initialized) return;

            if (placedEvent.IsRemove)
            {
                // 타일이 사라지면 그 자리를 가리키던 관심사도 버린다.
                // 안 그러면 없어진 도로를 두고 "이제 괜찮네요"가 나간다.
                ledger.DropTile(placedEvent.Tile);
                return;
            }

            // PlacedEvent는 도로에도, 공사 시작(UnderConstruction)에도 발행된다.
            // 거르지 않으면 도로를 깔 때마다 "건물이 들어섰다"는 글이 나가고,
            // 건설형 건물은 공사 시작과 완공에 두 번 나간다.
            if (!IsCompletedBuilding(placedEvent.Type))
            {
                return;
            }

            TryGeneratePost(CitizenFeedContext.ForTile(
                CitizenFeedEventType.BuildingPlaced,
                placedEvent.Tile,
                GetGameHour()));
        }

        private void OnEmergencyAlerted(EmergencyIncidentAlertEvent alertEvent)
        {
            if (!initialized) return;

            TryGeneratePost(CitizenFeedContext.ForTile(
                CitizenFeedEventType.EmergencyAlert,
                alertEvent.Location,
                GetGameHour()));
        }

        private void OnEmergencyOutcomeReported(EmergencyIncidentOutcomeEvent outcomeEvent)
        {
            if (!initialized) return;
            // 실패한 결말까지 "다행이다"로 말하면 안 된다. 해결된 것만 이어받는다.
            // 실패는 장부에 관심사를 남겨둬서 나중에 해결되면 그때 알아본다.
            if (outcomeEvent.Outcome != EmergencyIncidentOutcome.Resolved) return;

            TryGeneratePost(CitizenFeedContext.ForTile(
                CitizenFeedEventType.EmergencyResolved,
                outcomeEvent.Location,
                GetGameHour()));
        }

        /// <summary>
        /// 아무 사건이 없어도 도시가 말하게 하는 유일한 훅. 출근·퇴근 시간대 진입에만 쏜다 —
        /// 전 시간대를 열면 잡담이 진짜 정보를 밀어낸다.
        /// </summary>
        private void ObserveTimePeriod()
        {
            if (!initialized) return;

            CitizenFeedTimePeriod current =
                CitizenFeedFormatter.GetTimePeriod(GetGameHour());
            if (lastTimePeriod == null)
            {
                lastTimePeriod = current;
                return;
            }

            if (lastTimePeriod.Value == current) return;
            lastTimePeriod = current;

            if (current != CitizenFeedTimePeriod.MorningRush &&
                current != CitizenFeedTimePeriod.EveningRush)
            {
                return;
            }

            TryGeneratePost(CitizenFeedContext.ForTimePeriod(GetGameHour()));
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

            // 장부 노브도 설정 SO를 따른다 — 튜닝 지점이 두 곳으로 갈리지 않게.
            if (settings != null)
            {
                ledger.ApplyLimits(
                    settings.ConcernExpiryGameHours,
                    settings.ConcernCapacity);
            }

            authorByName.Clear();
            for (int i = 0; i < authors.Length; i++)
            {
                FeedAuthorProfileSO author = authors[i];
                if (author == null || string.IsNullOrEmpty(author.DisplayName)) continue;
                // 이름이 겹치면 먼저 등록된 쪽을 남긴다. 장부는 이름으로만 짝짓기 때문에
                // 동명이인이 있으면 어느 쪽이든 하나로 고정돼야 결과가 일관된다.
                if (!authorByName.ContainsKey(author.DisplayName))
                {
                    authorByName.Add(author.DisplayName, author);
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
                services.Events.JobChanged -= OnJobChanged;
                services.Events.FlowBurst -= OnFlowBurst;
                services.Events.Placed -= OnPlaced;
                services.Events.EmergencyIncidentAlerted -= OnEmergencyAlerted;
                services.Events.EmergencyIncidentOutcomeReported -= OnEmergencyOutcomeReported;
                services.GameCalendarRegistered -= OnGameCalendarRegistered;
            }

            initialized = false;
        }

        /// <summary>
        /// 이 묶음에 이어받기 전용 문구가 하나라도 있는가.
        /// 없으면(예: 데이터를 아직 안 채운 이벤트) 일반 문구로 떨어져야 하므로
        /// 후속 전용 필터를 걸면 안 된다 — 걸면 글이 아예 안 나간다.
        /// </summary>
        private static bool HasFollowUpTemplate(FeedTemplateCollectionSO collection)
        {
            IReadOnlyList<CitizenFeedTemplateEntry> templates = collection.Templates;
            for (int i = 0; i < templates.Count; i++)
            {
                if (templates[i] != null && templates[i].FollowUpOnly) return true;
            }

            return false;
        }

        /// <summary>
        /// 시민이 "뭔가 새로 생겼다"고 말할 만한 완성된 건물인가.
        /// 도로(Road)는 인프라 이벤트가 따로 다루고, 공사 중(UnderConstruction)은
        /// 아직 건물이 아니다 — 완공 시 실제 타입으로 다시 발행된다.
        /// </summary>
        private static bool IsCompletedBuilding(TileType type)
        {
            switch (type)
            {
                case TileType.House:
                case TileType.Office:
                case TileType.School:
                case TileType.Hospital:
                case TileType.SpecialBuilding:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 어떤 이벤트가 관심사를 여는가. 여기 없는 이벤트는 장부를 건드리지 않는다.
        /// </summary>
        private static bool TryGetConcernKind(
            CitizenFeedEventType eventType,
            out CitizenFeedConcernKind kind)
        {
            switch (eventType)
            {
                case CitizenFeedEventType.CongestionStarted:
                case CitizenFeedEventType.CongestionSustained:
                    kind = CitizenFeedConcernKind.Congestion;
                    return true;
                case CitizenFeedEventType.EmergencyAlert:
                    kind = CitizenFeedConcernKind.Emergency;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }

        /// <summary>
        /// 어떤 이벤트가 관심사를 닫는가. 위 표와 짝을 이룬다.
        /// </summary>
        private static bool TryGetResolveKind(
            CitizenFeedEventType eventType,
            out CitizenFeedConcernKind kind)
        {
            switch (eventType)
            {
                case CitizenFeedEventType.CongestionResolved:
                    kind = CitizenFeedConcernKind.Congestion;
                    return true;
                case CitizenFeedEventType.EmergencyResolved:
                    kind = CitizenFeedConcernKind.Emergency;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }

        private FeedAuthorProfileSO FindAuthor(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return null;
            return authorByName.TryGetValue(displayName, out FeedAuthorProfileSO author)
                ? author
                : null;
        }

        public CitizenFeedSaveData CreateSnapshot() => ledger.ToSaveData();

        public void RestoreSnapshot(CitizenFeedSaveData snapshot)
        {
            ledger.RestoreFrom(snapshot, GetAbsoluteGameHour());
            // 로드 직후 첫 시간대 관측은 발행 없이 기록만 하도록 되돌린다.
            lastTimePeriod = null;
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
            if (calendar == null)
            {
                return $"{GetGameHour():00}:00";
            }

            // 달력은 분을 노출하지 않지만 TimeOfDay01이 있다.
            // 하루 안 진행도에서 시를 빼면 그 시각의 분이 나온다.
            float hoursIntoDay = calendar.TimeOfDay01 * calendar.HoursPerDay;
            int minute = Mathf.Clamp((int)((hoursIntoDay - calendar.Hour) * 60f), 0, 59);
            return CitizenFeedFormatter.FormatTimestamp(calendar.Hour, minute);
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
