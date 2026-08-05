using System;
using CityFlow.Feed;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CityFlow.EditorTools
{
    internal sealed class CitizenFeedV1Assets
    {
        public FeedSystemSettingsSO Settings { get; }
        public FeedEventRuleSO[] Rules { get; }
        public FeedAuthorProfileSO[] Authors { get; }
        public FeedTemplateCollectionSO[] TemplateCollections { get; }

        public CitizenFeedV1Assets(
            FeedSystemSettingsSO settings,
            FeedEventRuleSO[] rules,
            FeedAuthorProfileSO[] authors,
            FeedTemplateCollectionSO[] templateCollections)
        {
            Settings = settings;
            Rules = rules;
            Authors = authors;
            TemplateCollections = templateCollections;
        }
    }

    public static class CitizenFeedDataGenerator
    {
        private const string RootFolder = "Assets/05_ScriptableObjects/Feed";

        [MenuItem("Tools/GreenLight/Feed/Create or Upgrade Feed Data")]
        public static void CreateOrUpgradeDataMenu()
        {
            CitizenFeedV1Assets assets = CreateOrLoadDefaults();
            ApplyToActiveScene(assets);
            Selection.activeObject = assets.Settings;
            EditorGUIUtility.PingObject(assets.Settings);
            Debug.Log(
                "[CitizenFeedDataGenerator] Green SNS data is ready. " +
                "Existing assets were preserved.");
        }

        internal static CitizenFeedV1Assets CreateOrLoadDefaults()
        {
            EnsureFolder(RootFolder);

            FeedSystemSettingsSO settings = LoadOrCreate<FeedSystemSettingsSO>(
                $"{RootFolder}/FeedSystemSettings.asset",
                asset => asset.ConfigureDefaults());

            FeedEventRuleSO congestionStartedRule = LoadOrCreate<FeedEventRuleSO>(
                $"{RootFolder}/Rule_CongestionStarted.asset",
                asset => asset.Configure(
                    CitizenFeedEventType.CongestionStarted,
                    0.9f,
                    1f,
                    30f,
                    65f,
                    AllRoles()));
            FeedEventRuleSO congestionResolvedRule = LoadOrCreate<FeedEventRuleSO>(
                $"{RootFolder}/Rule_CongestionResolved.asset",
                asset => asset.Configure(
                    CitizenFeedEventType.CongestionResolved,
                    0.9f,
                    1f,
                    75f,
                    0f,
                    AllRoles()));
            FeedEventRuleSO signalChangedRule = LoadOrCreate<FeedEventRuleSO>(
                $"{RootFolder}/Rule_SignalChanged.asset",
                asset => asset.Configure(
                    CitizenFeedEventType.SignalChanged,
                    1f,
                    1f,
                    95f,
                    0f,
                    AllRoles()));
            FeedEventRuleSO congestionSlowedRule = LoadOrCreate<FeedEventRuleSO>(
                $"{RootFolder}/Rule_CongestionSlowed.asset",
                asset => asset.Configure(
                    CitizenFeedEventType.CongestionSlowed,
                    0.75f,
                    1f,
                    45f,
                    45f,
                    AllRoles()));
            FeedEventRuleSO congestionSustainedRule = LoadOrCreate<FeedEventRuleSO>(
                $"{RootFolder}/Rule_CongestionSustained.asset",
                asset => asset.Configure(
                    CitizenFeedEventType.CongestionSustained,
                    0.9f,
                    3f,
                    75f,
                    25f,
                    AllRoles()));

            FeedEventRuleSO infrastructurePlacedRule = LoadOrCreate<FeedEventRuleSO>(
                $"{RootFolder}/Rule_InfrastructurePlaced.asset",
                asset => asset.Configure(
                    CitizenFeedEventType.InfrastructurePlaced,
                    1f,
                    0.5f,
                    90f,
                    0f,
                    AllRoles()));
            FeedEventRuleSO infrastructureRemovedRule = LoadOrCreate<FeedEventRuleSO>(
                $"{RootFolder}/Rule_InfrastructureRemoved.asset",
                asset => asset.Configure(
                    CitizenFeedEventType.InfrastructureRemoved,
                    1f,
                    0.5f,
                    90f,
                    0f,
                    AllRoles()));
            FeedEventRuleSO notableArrivalRule = LoadOrCreate<FeedEventRuleSO>(
                $"{RootFolder}/Rule_NotableArrival.asset",
                asset => asset.Configure(
                    CitizenFeedEventType.NotableArrival,
                    0.45f,
                    1f,
                    35f,
                    0f,
                    AllRoles(),
                    targetRouteDistanceMultiplier: 4f));
            FeedEventRuleSO vehicleSurgeRule = LoadOrCreate<FeedEventRuleSO>(
                $"{RootFolder}/Rule_VehicleSurge.asset",
                asset => asset.Configure(
                    CitizenFeedEventType.VehicleSurge,
                    0.8f,
                    4f,
                    50f,
                    0f,
                    AllRoles(),
                    targetVehicleCountMultiplier: 2f));

            // 흐름이 터지는 순간. 자주 일어나므로 쿨다운을 길게, 확률을 낮게 잡는다.
            FeedEventRuleSO flowBurstRule = LoadOrCreate<FeedEventRuleSO>(
                $"{RootFolder}/Rule_FlowBurst.asset",
                asset => asset.Configure(
                    CitizenFeedEventType.FlowBurst,
                    0.35f,
                    3f,
                    45f,
                    0f,
                    AllRoles()));
            FeedEventRuleSO buildingPlacedRule = LoadOrCreate<FeedEventRuleSO>(
                $"{RootFolder}/Rule_BuildingPlaced.asset",
                asset => asset.Configure(
                    CitizenFeedEventType.BuildingPlaced,
                    0.55f,
                    1f,
                    55f,
                    0f,
                    AllRoles()));
            // 구급 출동은 드물고 눈에 띄는 사건이다 — 확률·점수를 높게 준다.
            FeedEventRuleSO emergencyAlertRule = LoadOrCreate<FeedEventRuleSO>(
                $"{RootFolder}/Rule_EmergencyAlert.asset",
                asset => asset.Configure(
                    CitizenFeedEventType.EmergencyAlert,
                    1f,
                    0.5f,
                    85f,
                    0f,
                    AllRoles()));
            // 해결 글은 반드시 나가야 인과가 보인다. 확률 1.0, 쿨다운 0.
            FeedEventRuleSO emergencyResolvedRule = LoadOrCreate<FeedEventRuleSO>(
                $"{RootFolder}/Rule_EmergencyResolved.asset",
                asset => asset.Configure(
                    CitizenFeedEventType.EmergencyResolved,
                    1f,
                    0f,
                    90f,
                    0f,
                    AllRoles()));
            // 시간대 훅은 "아무 일 없어도 도시가 말하게" 하는 용도라 존재감이 약해야 한다.
            FeedEventRuleSO timePeriodRule = LoadOrCreate<FeedEventRuleSO>(
                $"{RootFolder}/Rule_TimePeriodChanged.asset",
                asset => asset.Configure(
                    CitizenFeedEventType.TimePeriodChanged,
                    0.5f,
                    5f,
                    40f,
                    0f,
                    AllRoles()));

            CitizenFeedEventType[] allEvents = AllEvents();
            FeedAuthorProfileSO officeWorker = LoadOrCreate<FeedAuthorProfileSO>(
                $"{RootFolder}/Author_OfficeWorker.asset",
                asset => asset.Configure(
                    "김대리",
                    "김",
                    "직장인",
                    new Color(0.30f, 0.75f, 0.92f, 1f),
                    CitizenFeedRole.OfficeWorker,
                    CitizenFeedPersonality.Complainer,
                    1.2f,
                    allEvents,
                    6,
                    22,
                    1.4f,
                    0.6f,
                    0.8f,
                    new[] { ":(", "..." },
                    new[] { "#출근길", "#퇴근길" }));
            FeedAuthorProfileSO parent = LoadOrCreate<FeedAuthorProfileSO>(
                $"{RootFolder}/Author_Parent.asset",
                asset => asset.Configure(
                    "한서윤",
                    "한",
                    "학부모",
                    new Color(0.93f, 0.68f, 0.35f, 1f),
                    CitizenFeedRole.Parent,
                    CitizenFeedPersonality.Emotional,
                    1f,
                    allEvents,
                    6,
                    22,
                    1.2f,
                    0.7f,
                    1.2f,
                    new[] { ":)", ":(" },
                    new[] { "#우리동네", "#등굣길" }));
            FeedAuthorProfileSO taxiDriver = LoadOrCreate<FeedAuthorProfileSO>(
                $"{RootFolder}/Author_TaxiDriver.asset",
                asset => asset.Configure(
                    "박기사",
                    "박",
                    "택시기사",
                    new Color(0.35f, 0.83f, 0.56f, 1f),
                    CitizenFeedRole.TaxiDriver,
                    CitizenFeedPersonality.Helpful,
                    1.25f,
                    allEvents,
                    5,
                    24,
                    0.9f,
                    0.7f,
                    1f,
                    new[] { "!", ":)" },
                    new[] { "#교통제보", "#우회하세요" }));
            FeedAuthorProfileSO merchant = LoadOrCreate<FeedAuthorProfileSO>(
                $"{RootFolder}/Author_Merchant.asset",
                asset => asset.Configure(
                    "최사장",
                    "최",
                    "가게 사장",
                    new Color(0.83f, 0.48f, 0.42f, 1f),
                    CitizenFeedRole.Merchant,
                    CitizenFeedPersonality.Observer,
                    0.9f,
                    allEvents,
                    7,
                    23,
                    1.1f,
                    0.7f,
                    1.1f,
                    new[] { "...", ":|" },
                    new[] { "#동네상권", "#납품" }));
            FeedAuthorProfileSO trafficEnthusiast = LoadOrCreate<FeedAuthorProfileSO>(
                $"{RootFolder}/Author_TrafficEnthusiast.asset",
                asset => asset.Configure(
                    "도로는내친구",
                    "도",
                    "교통 덕후",
                    new Color(0.63f, 0.56f, 0.91f, 1f),
                    CitizenFeedRole.TrafficEnthusiast,
                    CitizenFeedPersonality.Analytical,
                    1.1f,
                    allEvents,
                    0,
                    24,
                    0.8f,
                    0.5f,
                    0.9f,
                    new[] { "[분석]", "[기록]" },
                    new[] { "#교통분석", "#신호주기" }));

            FeedAuthorProfileSO deliveryDriver = LoadOrCreate<FeedAuthorProfileSO>(
                $"{RootFolder}/Author_DeliveryDriver.asset",
                asset => asset.Configure(
                    "이배달",
                    "이",
                    "배달기사",
                    new Color(0.95f, 0.55f, 0.24f, 1f),
                    CitizenFeedRole.DeliveryDriver,
                    CitizenFeedPersonality.Humorous,
                    1.1f,
                    allEvents,
                    7,
                    24,
                    1f,
                    1.5f,
                    0.8f,
                    new[] { ":D", "!" },
                    new[] { "#배달중", "#길찾기" }));
            FeedAuthorProfileSO student = LoadOrCreate<FeedAuthorProfileSO>(
                $"{RootFolder}/Author_Student.asset",
                asset => asset.Configure(
                    "최학생",
                    "최",
                    "학생",
                    new Color(0.45f, 0.72f, 0.96f, 1f),
                    CitizenFeedRole.Student,
                    CitizenFeedPersonality.Emotional,
                    0.85f,
                    allEvents,
                    6,
                    22,
                    1.1f,
                    1.1f,
                    1f,
                    new[] { "ㅠㅠ", ":(" },
                    new[] { "#등교", "#하교" }));
            FeedAuthorProfileSO selfEmployed = LoadOrCreate<FeedAuthorProfileSO>(
                $"{RootFolder}/Author_SelfEmployed.asset",
                asset => asset.Configure(
                    "김사장",
                    "김",
                    "자영업자",
                    new Color(0.90f, 0.62f, 0.30f, 1f),
                    CitizenFeedRole.SelfEmployed,
                    CitizenFeedPersonality.Proud,
                    0.9f,
                    allEvents,
                    6,
                    23,
                    1f,
                    0.7f,
                    1.3f,
                    new[] { ":)", "!" },
                    new[] { "#오늘장사", "#상권소식" }));
            FeedAuthorProfileSO realEstateAgent = LoadOrCreate<FeedAuthorProfileSO>(
                $"{RootFolder}/Author_RealEstateAgent.asset",
                asset => asset.Configure(
                    "복덕방김씨",
                    "복",
                    "부동산 중개인",
                    new Color(0.74f, 0.62f, 0.38f, 1f),
                    CitizenFeedRole.RealEstateAgent,
                    CitizenFeedPersonality.Analytical,
                    0.75f,
                    allEvents,
                    8,
                    21,
                    0.8f,
                    0.4f,
                    1f,
                    new[] { "[지역소식]", "[참고]" },
                    new[] { "#지역소식", "#접근성" }));
            FeedAuthorProfileSO civicActivist = LoadOrCreate<FeedAuthorProfileSO>(
                $"{RootFolder}/Author_CivicActivist.asset",
                asset => asset.Configure(
                    "우리동네지킴이",
                    "우",
                    "시민운동가",
                    new Color(0.38f, 0.78f, 0.55f, 1f),
                    CitizenFeedRole.CivicActivist,
                    CitizenFeedPersonality.Meddler,
                    0.9f,
                    allEvents,
                    6,
                    23,
                    1.2f,
                    0.4f,
                    1.3f,
                    new[] { "!", "[제보]" },
                    new[] { "#시민제보", "#안전한도시" }));
            FeedAuthorProfileSO nightResident = LoadOrCreate<FeedAuthorProfileSO>(
                $"{RootFolder}/Author_NightResident.asset",
                asset => asset.Configure(
                    "심야산책",
                    "심",
                    "야간 주민",
                    new Color(0.48f, 0.52f, 0.78f, 1f),
                    CitizenFeedRole.Resident,
                    CitizenFeedPersonality.Observer,
                    0.7f,
                    allEvents,
                    20,
                    5,
                    0.8f,
                    0.8f,
                    1f,
                    new[] { "...", "[심야]" },
                    new[] { "#심야도시", "#동네산책" }));
            FeedAuthorProfileSO anonymousDriver = LoadOrCreate<FeedAuthorProfileSO>(
                $"{RootFolder}/Author_AnonymousDriver.asset",
                asset => asset.Configure(
                    "오늘도막힌다",
                    "오",
                    "익명 운전자",
                    new Color(0.88f, 0.42f, 0.52f, 1f),
                    CitizenFeedRole.Driver,
                    CitizenFeedPersonality.Cynical,
                    0.85f,
                    allEvents,
                    0,
                    24,
                    1.5f,
                    1.5f,
                    0.3f,
                    new[] { ":|", "^^" },
                    new[] { "#오늘도정체", "#초록불은장식" }));

            // LoadOrCreate는 에셋이 이미 있으면 Configure를 다시 부르지 않는다.
            // 그래서 이벤트를 추가해도 옛 프로필의 preferredEvents는 그대로 남고,
            // Supports()가 false를 돌려 신규 이벤트 글이 한 건도 안 나간다.
            // 메뉴 이름이 "Create or Upgrade"인 만큼 여기서 실제로 업그레이드한다.
            UpgradeAuthorsForNewEvents(new[]
            {
                officeWorker, parent, taxiDriver, merchant,
                trafficEnthusiast, deliveryDriver, student, selfEmployed,
                realEstateAgent, civicActivist, nightResident, anonymousDriver
            });

            FeedTemplateCollectionSO congestionStartedTemplates = LoadOrCreateTemplateCollection(
                CitizenFeedEventType.CongestionStarted,
                CreateCongestionStartedTemplates());
            FeedTemplateCollectionSO congestionResolvedTemplates = LoadOrCreateTemplateCollection(
                CitizenFeedEventType.CongestionResolved,
                CreateCongestionResolvedTemplates());
            FeedTemplateCollectionSO signalChangedTemplates = LoadOrCreateTemplateCollection(
                CitizenFeedEventType.SignalChanged,
                CreateSignalChangedTemplates());
            FeedTemplateCollectionSO congestionSlowedTemplates = LoadOrCreateTemplateCollection(
                CitizenFeedEventType.CongestionSlowed,
                CreateCongestionSlowedTemplates());
            FeedTemplateCollectionSO congestionSustainedTemplates = LoadOrCreateTemplateCollection(
                CitizenFeedEventType.CongestionSustained,
                CreateCongestionSustainedTemplates());

            FeedTemplateCollectionSO infrastructurePlacedTemplates = LoadOrCreateTemplateCollection(
                CitizenFeedEventType.InfrastructurePlaced,
                CreateInfrastructurePlacedTemplates());
            FeedTemplateCollectionSO infrastructureRemovedTemplates = LoadOrCreateTemplateCollection(
                CitizenFeedEventType.InfrastructureRemoved,
                CreateInfrastructureRemovedTemplates());
            FeedTemplateCollectionSO notableArrivalTemplates = LoadOrCreateTemplateCollection(
                CitizenFeedEventType.NotableArrival,
                CreateNotableArrivalTemplates());
            FeedTemplateCollectionSO vehicleSurgeTemplates = LoadOrCreateTemplateCollection(
                CitizenFeedEventType.VehicleSurge,
                CreateVehicleSurgeTemplates());

            FeedTemplateCollectionSO flowBurstTemplates = LoadOrCreateTemplateCollection(
                CitizenFeedEventType.FlowBurst,
                CreateFlowBurstTemplates());
            FeedTemplateCollectionSO buildingPlacedTemplates = LoadOrCreateTemplateCollection(
                CitizenFeedEventType.BuildingPlaced,
                CreateBuildingPlacedTemplates());
            FeedTemplateCollectionSO emergencyAlertTemplates = LoadOrCreateTemplateCollection(
                CitizenFeedEventType.EmergencyAlert,
                CreateEmergencyAlertTemplates());
            FeedTemplateCollectionSO emergencyResolvedTemplates = LoadOrCreateTemplateCollection(
                CitizenFeedEventType.EmergencyResolved,
                CreateEmergencyResolvedTemplates());
            FeedTemplateCollectionSO timePeriodTemplates = LoadOrCreateTemplateCollection(
                CitizenFeedEventType.TimePeriodChanged,
                CreateTimePeriodTemplates());

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return new CitizenFeedV1Assets(
                settings,
                new[]
                {
                    congestionStartedRule,
                    congestionResolvedRule,
                    signalChangedRule,
                    congestionSlowedRule,
                    congestionSustainedRule,

                    infrastructurePlacedRule,
                    infrastructureRemovedRule,
                    notableArrivalRule,
                    vehicleSurgeRule,

                    flowBurstRule,
                    buildingPlacedRule,
                    emergencyAlertRule,
                    emergencyResolvedRule,
                    timePeriodRule
                },
                new[]
                {
                    officeWorker,
                    parent,
                    taxiDriver,
                    merchant,
                    trafficEnthusiast,
                    deliveryDriver,
                    student,
                    selfEmployed,
                    realEstateAgent,
                    civicActivist,
                    nightResident,
                    anonymousDriver
                },
                new[]
                {
                    congestionStartedTemplates,
                    congestionResolvedTemplates,
                    signalChangedTemplates,
                    congestionSlowedTemplates,
                    congestionSustainedTemplates,

                    infrastructurePlacedTemplates,
                    infrastructureRemovedTemplates,
                    notableArrivalTemplates,
                    vehicleSurgeTemplates,

                    flowBurstTemplates,
                    buildingPlacedTemplates,
                    emergencyAlertTemplates,
                    emergencyResolvedTemplates,
                    timePeriodTemplates
                });
        }

        private static void ApplyToActiveScene(CitizenFeedV1Assets assets)
        {
            CitizenFeedService[] services = UnityEngine.Object.FindObjectsByType<CitizenFeedService>(
                FindObjectsInactive.Include);
            for (int i = 0; i < services.Length; i++)
            {
                CitizenFeedService service = services[i];
                service.Configure(
                    assets.Settings,
                    assets.Rules,
                    assets.Authors,
                    assets.TemplateCollections);
                EditorUtility.SetDirty(service);
            }

            if (services.Length > 0)
            {
                EditorSceneManager.MarkSceneDirty(services[0].gameObject.scene);
                Debug.Log(
                    $"[CitizenFeedDataGenerator] Updated {services.Length} CitizenFeedService " +
                    "component(s) in the active scene.");
            }
        }

        private static CitizenFeedTemplateEntry[] CreateCongestionStartedTemplates()
        {
            return new[]
            {
                CreateTemplate(
                    "Congestion_Office_01",
                    "{TimePeriod}부터 {Location}에서 꼼짝도 못 하고 있습니다. 오늘도 지각 확정이네요.",
                    CitizenFeedTone.Complaint,
                    1.2f,
                    CitizenFeedRole.OfficeWorker),
                CreateTemplate(
                    "Congestion_Parent_01",
                    "{Location}까지 차가 꽉 찼어요. 아이 이동 시간에는 조금 나아졌으면 좋겠네요.",
                    CitizenFeedTone.Complaint,
                    1f,
                    CitizenFeedRole.Parent),
                CreateTemplate(
                    "Congestion_Taxi_01",
                    "{Location} 진입은 피하세요. 현재 밀도 {DensityPercent}, 우회하는 편이 빠릅니다.",
                    CitizenFeedTone.Information,
                    1.3f,
                    CitizenFeedRole.TaxiDriver),
                CreateTemplate(
                    "Congestion_Merchant_01",
                    "가게 앞 {Location}가 막혀서 납품 차량도 늦고 있습니다. 오늘 장사가 걱정이네요.",
                    CitizenFeedTone.Complaint,
                    1f,
                    CitizenFeedRole.Merchant),
                CreateTemplate(
                    "Congestion_Analyst_01",
                    "{Location}가 {CurrentCongestion} 단계입니다. 차량 밀도 {DensityPercent}, 유입 신호를 확인해야겠네요.",
                    CitizenFeedTone.Information,
                    1.2f,
                    CitizenFeedRole.TrafficEnthusiast),
                CreateTemplate(
                    "Congestion_Question_01",
                    "{Location}는 왜 {Hour}만 되면 항상 막히는 걸까요?",
                    CitizenFeedTone.Question,
                    0.5f,
                    CitizenFeedRole.OfficeWorker,
                    CitizenFeedRole.Parent),
                CreateDetailedTemplate(
                    "Congestion_Delivery_01",
                    "배달지는 바로 앞인데 {Location}에서 길이 멈췄습니다. 음식보다 제가 먼저 식겠네요.",
                    CitizenFeedTone.Humor,
                    0.8f,
                    CitizenFeedCategory.HumorAndChatter,
                    new[] { CitizenFeedRole.DeliveryDriver }),
                CreateDetailedTemplate(
                    "Congestion_Driver_01",
                    "{Location}의 초록불은 오늘도 장식품 역할을 훌륭히 수행 중입니다.",
                    CitizenFeedTone.Cynical,
                    0.45f,
                    CitizenFeedCategory.HumorAndChatter,
                    new[] { CitizenFeedRole.Driver }),
                CreateDetailedTemplate(
                    "Congestion_Student_01",
                    "{TimePeriod}부터 차가 안 움직여요. 걸어간 친구가 먼저 도착하겠는데요.",
                    CitizenFeedTone.Complaint,
                    0.8f,
                    CitizenFeedCategory.CommuteExperience,
                    new[] { CitizenFeedRole.Student })
            };
        }

        /// <summary>
        /// 이어받는 문구. followUpOnly=true라 장부에서 작성자가 나왔을 때만 뽑힌다.
        /// 역할을 비워 누구든 자기가 한 말을 이어받을 수 있게 하고, 가중치를 높게 줘서
        /// 이어받기 상황에선 이쪽이 뽑히게 한다 — 안 그러면 평범한 해결 문구가 나와
        /// 인과관계가 글에 안 드러난다.
        /// </summary>
        private static CitizenFeedTemplateEntry CreateFollowUpTemplate(
            string templateId,
            string text)
        {
            CitizenFeedTemplateEntry entry = new CitizenFeedTemplateEntry();
            entry.Configure(
                templateId,
                text,
                CitizenFeedTone.Praise,
                4f,
                Array.Empty<CitizenFeedRole>(),
                null,
                CitizenFeedCategory.TrafficReport,
                null,
                targetFollowUpOnly: true);
            return entry;
        }

        private static CitizenFeedTemplateEntry[] CreateCongestionResolvedTemplates()
        {
            return new[]
            {
                CreateFollowUpTemplate(
                    "FollowUp_Congestion_01",
                    "제가 저번에 말한 {Location}, 이제 좀 괜찮아졌네요. 신경 써주셔서 고맙습니다."),
                CreateFollowUpTemplate(
                    "FollowUp_Congestion_02",
                    "아까 그렇게 막히던 {Location}이 뚫렸습니다. 말한 보람이 있네요."),
                CreateFollowUpTemplate(
                    "FollowUp_Congestion_03",
                    "{Location} 불평했던 사람인데요, 오늘은 그냥 지나갔습니다. 이런 날도 있군요."),
                CreateTemplate(
                    "Resolved_Office_01",
                    "{Location}가 드디어 움직이네요. 오늘은 제시간에 도착할 수 있겠습니다.",
                    CitizenFeedTone.Praise,
                    1.2f,
                    CitizenFeedRole.OfficeWorker),
                CreateTemplate(
                    "Resolved_Parent_01",
                    "집 근처 도로가 한결 조용해졌어요. 아이와 다니기 훨씬 편해졌네요.",
                    CitizenFeedTone.Praise,
                    1f,
                    CitizenFeedRole.Parent),
                CreateTemplate(
                    "Resolved_Taxi_01",
                    "{Location} 정체가 풀렸습니다. 현재 {CurrentCongestion} 상태로 정상 운행 가능합니다.",
                    CitizenFeedTone.Information,
                    1.3f,
                    CitizenFeedRole.TaxiDriver),
                CreateTemplate(
                    "Resolved_Merchant_01",
                    "도로 흐름이 살아나니 손님도 다시 들어오기 시작하네요.",
                    CitizenFeedTone.Praise,
                    1f,
                    CitizenFeedRole.Merchant),
                CreateTemplate(
                    "Resolved_Analyst_01",
                    "{Location}가 {PreviousCongestion}에서 {CurrentCongestion} 상태로 회복됐습니다. 최근 조정 효과일까요?",
                    CitizenFeedTone.Information,
                    1.2f,
                    CitizenFeedRole.TrafficEnthusiast),
                CreateDetailedTemplate(
                    "Resolved_Delivery_01",
                    "{Location}가 뚫렸습니다. 이번 배달은 제시간에 도착할 수 있겠네요.",
                    CitizenFeedTone.Praise,
                    1f,
                    CitizenFeedCategory.CommuteExperience,
                    new[] { CitizenFeedRole.DeliveryDriver }),
                CreateDetailedTemplate(
                    "Resolved_SelfEmployed_01",
                    "가게로 들어오는 길이 다시 움직입니다. 손님과 납품 차량 모두 한결 편해지겠어요.",
                    CitizenFeedTone.Praise,
                    0.9f,
                    CitizenFeedCategory.EconomyReaction,
                    new[] { CitizenFeedRole.SelfEmployed })
            };
        }

        private static CitizenFeedTemplateEntry[] CreateSignalChangedTemplates()
        {
            return new[]
            {
                CreateTemplate(
                    "Signal_Office_01",
                    "{Location} 신호가 조정됐네요. 출근길이 어떻게 달라질지 지켜보겠습니다.",
                    CitizenFeedTone.Neutral,
                    1f,
                    CitizenFeedRole.OfficeWorker),
                CreateTemplate(
                    "Signal_Parent_01",
                    "{Location} 신호 시간이 바뀌었어요. 아이들이 다니는 시간에도 흐름이 좋아지면 좋겠네요.",
                    CitizenFeedTone.Question,
                    0.9f,
                    CitizenFeedRole.Parent),
                CreateTemplate(
                    "Signal_Taxi_01",
                    "{Location} 설정 변경 확인했습니다. {SignalChange}, 실제 대기열을 지켜봐야겠습니다.",
                    CitizenFeedTone.Information,
                    1.3f,
                    CitizenFeedRole.TaxiDriver),
                CreateTemplate(
                    "Signal_Merchant_01",
                    "가게 앞 신호가 바뀌었네요. 손님들이 들어오기 편해질지 궁금합니다.",
                    CitizenFeedTone.Neutral,
                    0.9f,
                    CitizenFeedRole.Merchant),
                CreateTemplate(
                    "Signal_Analyst_01",
                    "{Location}에 {SignalChange}가 적용됐습니다. 양방향 밀도 변화가 핵심이겠네요.",
                    CitizenFeedTone.Information,
                    1.5f,
                    CitizenFeedRole.TrafficEnthusiast),
                CreateDetailedTemplate(
                    "Signal_Delivery_01",
                    "{Location} 신호 설정이 바뀌었습니다. 배달 동선이 짧아질지 직접 달려보겠습니다.",
                    CitizenFeedTone.Neutral,
                    0.9f,
                    CitizenFeedCategory.CommuteExperience,
                    new[] { CitizenFeedRole.DeliveryDriver }),
                CreateDetailedTemplate(
                    "Signal_Driver_01",
                    "{SignalChange}. 이번에는 초록불이 정말로 차를 보내 주는지 보죠.",
                    CitizenFeedTone.Cynical,
                    0.5f,
                    CitizenFeedCategory.HumorAndChatter,
                    new[] { CitizenFeedRole.Driver })
            };
        }

        private static CitizenFeedTemplateEntry[] CreateCongestionSlowedTemplates()
        {
            return new[]
            {
                CreateDetailedTemplate(
                    "Slow_Office_01",
                    "{Location}의 흐름이 느려지기 시작했습니다. {TimePeriod} 일정은 조금 여유 있게 잡아야겠네요.",
                    CitizenFeedTone.Complaint,
                    1f,
                    CitizenFeedCategory.CommuteExperience,
                    new[] { CitizenFeedRole.OfficeWorker }),
                CreateDetailedTemplate(
                    "Slow_Taxi_01",
                    "{Location} 차량 밀도 {DensityPercent}. 아직 막히진 않았지만 우회 준비가 필요합니다.",
                    CitizenFeedTone.Information,
                    1.2f,
                    CitizenFeedCategory.TrafficReport,
                    new[] { CitizenFeedRole.TaxiDriver, CitizenFeedRole.DeliveryDriver }),
                CreateDetailedTemplate(
                    "Slow_Resident_01",
                    "동네 도로에 차가 하나둘 늘어나네요. 곧 붐빌 시간인가 봅니다.",
                    CitizenFeedTone.Neutral,
                    0.8f,
                    CitizenFeedCategory.LifeInconvenience,
                    new[] { CitizenFeedRole.Resident, CitizenFeedRole.Parent }),
                CreateDetailedTemplate(
                    "Slow_Analyst_01",
                    "{Location}가 원활 단계에서 서행 단계로 바뀌었습니다. 지금 유입량을 지켜볼 필요가 있습니다.",
                    CitizenFeedTone.Information,
                    1.1f,
                    CitizenFeedCategory.TrafficReport,
                    new[] { CitizenFeedRole.TrafficEnthusiast })
            };
        }

        private static CitizenFeedTemplateEntry[] CreateCongestionSustainedTemplates()
        {
            return new[]
            {
                CreateDetailedTemplate(
                    "Sustained_Parent_01",
                    "{Location}가 몇 시간째 막혀 있어요. 매일 이용하는 길이라 더 걱정됩니다.",
                    CitizenFeedTone.Complaint,
                    1.1f,
                    CitizenFeedCategory.LifeInconvenience,
                    new[] { CitizenFeedRole.Parent }),
                CreateDetailedTemplate(
                    "Sustained_Taxi_01",
                    "{Location} 정체가 장시간 이어지고 있습니다. 당분간 이 구간은 피하는 편이 좋겠습니다.",
                    CitizenFeedTone.Information,
                    1.3f,
                    CitizenFeedCategory.TrafficReport,
                    new[] { CitizenFeedRole.TaxiDriver, CitizenFeedRole.DeliveryDriver }),
                CreateDetailedTemplate(
                    "Sustained_Merchant_01",
                    "가게 앞 정체가 풀릴 기미가 없네요. 납품도 손님도 계속 늦어지고 있습니다.",
                    CitizenFeedTone.Complaint,
                    1f,
                    CitizenFeedCategory.EconomyReaction,
                    new[] { CitizenFeedRole.Merchant, CitizenFeedRole.SelfEmployed }),
                CreateDetailedTemplate(
                    "Sustained_Driver_01",
                    "{Location}에서 차보다 계절이 먼저 바뀌겠습니다.",
                    CitizenFeedTone.Humor,
                    0.45f,
                    CitizenFeedCategory.HumorAndChatter,
                    new[] { CitizenFeedRole.Driver }),
                CreateDetailedTemplate(
                    "Sustained_Activist_01",
                    "{Location}의 장기 정체로 주민 불편이 계속되고 있습니다. 지속적인 대책이 필요합니다.",
                    CitizenFeedTone.Complaint,
                    0.9f,
                    CitizenFeedCategory.LifeInconvenience,
                    new[] { CitizenFeedRole.CivicActivist })
            };
        }


        private static CitizenFeedTemplateEntry[] CreateInfrastructurePlacedTemplates()
        {
            return new[]
            {
                CreateDetailedTemplate(
                    "FacilityPlaced_Parent_01",
                    "{Location}에 {Facility}이 생겼어요. 동네 이동이 더 편해질지 기대됩니다.",
                    CitizenFeedTone.Praise,
                    1f,
                    CitizenFeedCategory.InfrastructureReaction,
                    new[] { CitizenFeedRole.Parent, CitizenFeedRole.Resident }),
                CreateDetailedTemplate(
                    "FacilityPlaced_Taxi_01",
                    "{Location}에 새 {Facility} 설치를 확인했습니다. 실제 교통 흐름은 조금 더 지켜봐야겠습니다.",
                    CitizenFeedTone.Information,
                    1.3f,
                    CitizenFeedCategory.InfrastructureReaction,
                    new[] { CitizenFeedRole.TaxiDriver, CitizenFeedRole.DeliveryDriver }),
                CreateDetailedTemplate(
                    "FacilityPlaced_Analyst_01",
                    "{Location}에 {Facility}이 적용됐습니다. 설치 전후 밀도 변화가 중요한 평가 기준이겠네요.",
                    CitizenFeedTone.Information,
                    1.4f,
                    CitizenFeedCategory.InfrastructureReaction,
                    new[] { CitizenFeedRole.TrafficEnthusiast }),
                CreateDetailedTemplate(
                    "FacilityPlaced_Driver_01",
                    "{Location}에 {Facility}이 생겼습니다. 이번에는 길이 정말 빨라질까요?",
                    CitizenFeedTone.Question,
                    0.8f,
                    CitizenFeedCategory.InfrastructureReaction,
                    new[] { CitizenFeedRole.Driver, CitizenFeedRole.OfficeWorker })
            };
        }

        private static CitizenFeedTemplateEntry[] CreateInfrastructureRemovedTemplates()
        {
            return new[]
            {
                CreateDetailedTemplate(
                    "FacilityRemoved_Parent_01",
                    "{Location}의 {Facility}이 없어졌어요. 익숙했던 길이 달라져서 조금 걱정되네요.",
                    CitizenFeedTone.Complaint,
                    1f,
                    CitizenFeedCategory.InfrastructureReaction,
                    new[] { CitizenFeedRole.Parent, CitizenFeedRole.Resident }),
                CreateDetailedTemplate(
                    "FacilityRemoved_Taxi_01",
                    "{Location}의 {Facility}이 철거됐습니다. 당분간 진입 흐름을 주의해서 보겠습니다.",
                    CitizenFeedTone.Information,
                    1.3f,
                    CitizenFeedCategory.InfrastructureReaction,
                    new[] { CitizenFeedRole.TaxiDriver, CitizenFeedRole.DeliveryDriver }),
                CreateDetailedTemplate(
                    "FacilityRemoved_Analyst_01",
                    "{Location}에서 {Facility}이 제거됐습니다. 이전 방식으로 돌아간 뒤의 교통량을 비교해 봐야겠네요.",
                    CitizenFeedTone.Information,
                    1.4f,
                    CitizenFeedCategory.InfrastructureReaction,
                    new[] { CitizenFeedRole.TrafficEnthusiast }),
                CreateDetailedTemplate(
                    "FacilityRemoved_Cynical_01",
                    "어제까지 있던 {Facility}이 사라졌습니다. 이유는 도로만 알고 있겠죠.",
                    CitizenFeedTone.Cynical,
                    0.45f,
                    CitizenFeedCategory.HumorAndChatter,
                    new[] { CitizenFeedRole.Driver })
            };
        }

        private static CitizenFeedTemplateEntry[] CreateNotableArrivalTemplates()
        {
            return new[]
            {
                CreateDetailedTemplate(
                    "Arrival_Office_01",
                    "도시를 {RouteDistance}나 가로질러 무사히 도착했습니다. 오늘 출근도 작은 여행이었네요.",
                    CitizenFeedTone.Neutral,
                    1f,
                    CitizenFeedCategory.CommuteExperience,
                    new[] { CitizenFeedRole.OfficeWorker }),
                CreateDetailedTemplate(
                    "Arrival_Delivery_01",
                    "{RouteDistance} 거리의 배달을 마쳤습니다. 늦지 않게 도착해서 다행입니다.",
                    CitizenFeedTone.Praise,
                    1.3f,
                    CitizenFeedCategory.CommuteExperience,
                    new[] { CitizenFeedRole.DeliveryDriver }),
                CreateDetailedTemplate(
                    "Arrival_Taxi_01",
                    "{Location}까지 장거리 운행 완료했습니다. 멀리 돌아왔지만 목적지에는 제대로 도착했습니다.",
                    CitizenFeedTone.Information,
                    1.2f,
                    CitizenFeedCategory.TrafficReport,
                    new[] { CitizenFeedRole.TaxiDriver }),
                CreateDetailedTemplate(
                    "Arrival_Student_01",
                    "오늘 이동 거리가 {RouteDistance}래요. 학교보다 도로에서 더 오래 지낸 기분입니다.",
                    CitizenFeedTone.Humor,
                    0.7f,
                    CitizenFeedCategory.HumorAndChatter,
                    new[] { CitizenFeedRole.Student }),
                CreateDetailedTemplate(
                    "Arrival_Merchant_01",
                    "먼 길을 온 차량이 제시간에 도착했습니다. 오늘 납품 일정은 문제없겠네요.",
                    CitizenFeedTone.Praise,
                    0.9f,
                    CitizenFeedCategory.EconomyReaction,
                    new[] { CitizenFeedRole.Merchant, CitizenFeedRole.SelfEmployed })
            };
        }

        private static CitizenFeedTemplateEntry[] CreateVehicleSurgeTemplates()
        {
            return new[]
            {
                CreateDetailedTemplate(
                    "VehicleSurge_Taxi_01",
                    "현재 도로에 차량이 {VehicleCount}나 나와 있습니다. 주요 교차로는 미리 우회하는 편이 좋겠습니다.",
                    CitizenFeedTone.Information,
                    1.3f,
                    CitizenFeedCategory.TrafficReport,
                    new[] { CitizenFeedRole.TaxiDriver, CitizenFeedRole.DeliveryDriver }),
                CreateDetailedTemplate(
                    "VehicleSurge_Analyst_01",
                    "활성 차량 수가 {VehicleCount}까지 증가했습니다. 신호 용량이 수요를 감당하는지 확인할 시점입니다.",
                    CitizenFeedTone.Information,
                    1.4f,
                    CitizenFeedCategory.TrafficReport,
                    new[] { CitizenFeedRole.TrafficEnthusiast }),
                CreateDetailedTemplate(
                    "VehicleSurge_Resident_01",
                    "평소보다 도로가 많이 붐비네요. 오늘은 도시 전체가 동시에 나온 것 같습니다.",
                    CitizenFeedTone.Neutral,
                    0.9f,
                    CitizenFeedCategory.LifeInconvenience,
                    new[] { CitizenFeedRole.Resident, CitizenFeedRole.Parent }),
                CreateDetailedTemplate(
                    "VehicleSurge_Driver_01",
                    "도로 위 차량 {VehicleCount}. 오늘도 자동차 주차 시뮬레이터가 시작됐습니다.",
                    CitizenFeedTone.Humor,
                    0.5f,
                    CitizenFeedCategory.HumorAndChatter,
                    new[] { CitizenFeedRole.Driver })
            };
        }

        private static CitizenFeedTemplateEntry[] CreateFlowBurstTemplates()
        {
            return new[]
            {
                CreateTemplate(
                    "FlowBurst_Taxi_01",
                    "{Location} 흐름이 갑자기 확 풀렸습니다. 지금이 지나갈 타이밍이에요.",
                    CitizenFeedTone.Information,
                    1.2f,
                    CitizenFeedRole.TaxiDriver),
                CreateTemplate(
                    "FlowBurst_Enthusiast_01",
                    "{Location} 방금 신호 타이밍이 딱 맞아떨어졌습니다. 이런 순간이 좋아요.",
                    CitizenFeedTone.Praise,
                    1f,
                    CitizenFeedRole.TrafficEnthusiast),
                CreateTemplate(
                    "FlowBurst_Driver_01",
                    "{Location}에서 한 번에 쭉 빠졌습니다. 오늘 운이 좋네요.",
                    CitizenFeedTone.Praise,
                    1f,
                    CitizenFeedRole.Driver),
                CreateTemplate(
                    "FlowBurst_Delivery_01",
                    "{Location} 뚫린 김에 배달 두 건 더 잡았습니다.",
                    CitizenFeedTone.Neutral,
                    0.9f,
                    CitizenFeedRole.DeliveryDriver)
            };
        }

        private static CitizenFeedTemplateEntry[] CreateBuildingPlacedTemplates()
        {
            return new[]
            {
                CreateTemplate(
                    "Placed_Resident_01",
                    "{Spot}에 뭔가 새로 생겼네요. 동네가 조금씩 달라집니다.",
                    CitizenFeedTone.Neutral,
                    1.1f,
                    CitizenFeedRole.Resident),
                CreateTemplate(
                    "Placed_RealEstate_01",
                    "{Spot} 신축 확인했습니다. 주변 유동인구에 영향이 있겠네요.",
                    CitizenFeedTone.Information,
                    1.2f,
                    CitizenFeedRole.RealEstateAgent),
                CreateTemplate(
                    "Placed_Merchant_01",
                    "{Spot}에 건물이 들어섰습니다. 손님이 좀 늘었으면 좋겠는데요.",
                    CitizenFeedTone.Neutral,
                    1f,
                    CitizenFeedRole.Merchant),
                CreateTemplate(
                    "Placed_Activist_01",
                    "{Spot} 공사 끝났네요. 이제 진입로 정리만 되면 좋겠습니다.",
                    CitizenFeedTone.Question,
                    0.9f,
                    CitizenFeedRole.CivicActivist)
            };
        }

        private static CitizenFeedTemplateEntry[] CreateEmergencyAlertTemplates()
        {
            return new[]
            {
                CreateTemplate(
                    "Emergency_Resident_01",
                    "{Spot} 쪽에서 사이렌 소리가 납니다. 무슨 일이죠?",
                    CitizenFeedTone.Question,
                    1.3f,
                    CitizenFeedRole.Resident),
                CreateTemplate(
                    "Emergency_Taxi_01",
                    "{Spot} 구급차 지나갑니다. 길 좀 비켜주세요.",
                    CitizenFeedTone.Information,
                    1.3f,
                    CitizenFeedRole.TaxiDriver),
                CreateTemplate(
                    "Emergency_Parent_01",
                    "{Spot}에 구급차가 갔어요. 별일 아니었으면 좋겠네요.",
                    CitizenFeedTone.Complaint,
                    1.1f,
                    CitizenFeedRole.Parent),
                CreateTemplate(
                    "Emergency_Activist_01",
                    "{Spot} 긴급 상황입니다. 이 구간 진입로가 좁은 게 계속 마음에 걸렸는데요.",
                    CitizenFeedTone.Complaint,
                    1f,
                    CitizenFeedRole.CivicActivist)
            };
        }

        private static CitizenFeedTemplateEntry[] CreateEmergencyResolvedTemplates()
        {
            return new[]
            {
                CreateFollowUpTemplate(
                    "FollowUp_Emergency_01",
                    "아까 {Spot} 사이렌, 잘 마무리됐다고 하네요. 다행입니다."),
                CreateFollowUpTemplate(
                    "FollowUp_Emergency_02",
                    "{Spot} 상황 궁금해했었는데 무사히 끝났답니다. 한숨 놓았어요."),
                CreateTemplate(
                    "EmergencyResolved_Resident_01",
                    "{Spot} 상황은 정리된 것 같습니다. 사이렌 소리가 멎었어요.",
                    CitizenFeedTone.Praise,
                    1f,
                    CitizenFeedRole.Resident),
                CreateTemplate(
                    "EmergencyResolved_Taxi_01",
                    "{Spot} 통제 풀렸습니다. 정상 통행 가능합니다.",
                    CitizenFeedTone.Information,
                    1.2f,
                    CitizenFeedRole.TaxiDriver)
            };
        }

        private static CitizenFeedTemplateEntry[] CreateTimePeriodTemplates()
        {
            // 시간대 훅은 장소가 없다 — {Location}을 쓰면 안 된다.
            return new[]
            {
                CreateDetailedTemplate(
                    "TimePeriod_Office_Morning",
                    "출근길 시작입니다. 오늘은 무사히 갈 수 있을까요.",
                    CitizenFeedTone.Neutral,
                    1.2f,
                    CitizenFeedCategory.CommuteExperience,
                    new[] { CitizenFeedRole.OfficeWorker },
                    null,
                    new[] { CitizenFeedTimePeriod.MorningRush }),
                CreateDetailedTemplate(
                    "TimePeriod_Student_Morning",
                    "아침 등굣길. 오늘은 좀 덜 붐볐으면.",
                    CitizenFeedTone.Neutral,
                    1f,
                    CitizenFeedCategory.CommuteExperience,
                    new[] { CitizenFeedRole.Student },
                    null,
                    new[] { CitizenFeedTimePeriod.MorningRush }),
                CreateDetailedTemplate(
                    "TimePeriod_Driver_Evening",
                    "퇴근 시간입니다. 다들 무사히 들어가세요.",
                    CitizenFeedTone.Neutral,
                    1.1f,
                    CitizenFeedCategory.CommuteExperience,
                    new[] { CitizenFeedRole.Driver },
                    null,
                    new[] { CitizenFeedTimePeriod.EveningRush }),
                CreateDetailedTemplate(
                    "TimePeriod_Merchant_Evening",
                    "퇴근길 손님이 들어올 시간이네요. 슬슬 준비합니다.",
                    CitizenFeedTone.Neutral,
                    1f,
                    CitizenFeedCategory.EconomyReaction,
                    new[] { CitizenFeedRole.Merchant },
                    null,
                    new[] { CitizenFeedTimePeriod.EveningRush })
            };
        }

        private static CitizenFeedTemplateEntry CreateTemplate(
            string templateId,
            string text,
            CitizenFeedTone tone,
            float weight,
            params CitizenFeedRole[] roles)
        {
            CitizenFeedTemplateEntry entry = new CitizenFeedTemplateEntry();
            entry.Configure(templateId, text, tone, weight, roles);
            return entry;
        }

        private static CitizenFeedTemplateEntry CreateDetailedTemplate(
            string templateId,
            string text,
            CitizenFeedTone tone,
            float weight,
            CitizenFeedCategory category,
            CitizenFeedRole[] roles,
            CitizenFeedPersonality[] personalities = null,
            CitizenFeedTimePeriod[] timePeriods = null)
        {
            CitizenFeedTemplateEntry entry = new CitizenFeedTemplateEntry();
            entry.Configure(
                templateId,
                text,
                tone,
                weight,
                roles,
                personalities,
                category,
                timePeriods);
            return entry;
        }

        private static CitizenFeedRole[] AllRoles()
        {
            return (CitizenFeedRole[])Enum.GetValues(typeof(CitizenFeedRole));
        }

        private static CitizenFeedEventType[] AllEvents()
        {
            return (CitizenFeedEventType[])Enum.GetValues(typeof(CitizenFeedEventType));
        }

        private static FeedTemplateCollectionSO LoadOrCreateTemplateCollection(
            CitizenFeedEventType eventType,
            CitizenFeedTemplateEntry[] defaultTemplates)
        {
            string assetPath = $"{RootFolder}/Templates_{eventType}.asset";
            FeedTemplateCollectionSO collection = LoadOrCreate<FeedTemplateCollectionSO>(
                assetPath,
                asset => asset.Configure(eventType, defaultTemplates));
            collection.AddMissingTemplates(defaultTemplates);
            EditorUtility.SetDirty(collection);
            return collection;
        }

        /// <summary>
        /// 기존 프로필에 이번 버전에서 추가된 이벤트 지원을 병합한다.
        /// 나머지 필드(가중치·활동시간·성향)는 손대지 않는다 — 손으로 튜닝했을 수 있다.
        /// </summary>
        private static void UpgradeAuthorsForNewEvents(FeedAuthorProfileSO[] profiles)
        {
            CitizenFeedEventType[] newEvents =
            {
                CitizenFeedEventType.FlowBurst,
                CitizenFeedEventType.BuildingPlaced,
                CitizenFeedEventType.EmergencyAlert,
                CitizenFeedEventType.EmergencyResolved,
                CitizenFeedEventType.TimePeriodChanged
            };

            int upgraded = 0;
            foreach (FeedAuthorProfileSO profile in profiles)
            {
                if (profile == null) continue;
                if (!profile.AddSupportedEvents(newEvents)) continue;
                EditorUtility.SetDirty(profile);
                upgraded++;
            }

            if (upgraded > 0)
            {
                Debug.Log(
                    $"[CitizenFeedDataGenerator] 작성자 프로필 {upgraded}개에 신규 이벤트 지원을 추가했습니다.");
            }
        }

        private static T LoadOrCreate<T>(string assetPath, Action<T> configureNewAsset)
            where T : ScriptableObject
        {
            T existing = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (existing != null)
            {
                return existing;
            }

            T asset = ScriptableObject.CreateInstance<T>();
            configureNewAsset?.Invoke(asset);
            AssetDatabase.CreateAsset(asset, assetPath);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] segments = folderPath.Split('/');
            string currentPath = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string nextPath = $"{currentPath}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, segments[i]);
                }

                currentPath = nextPath;
            }
        }

        // Unity setup: Run Tools > GreenLight > Feed > Create or Upgrade Feed Data without rebaking UI.
    }
}
