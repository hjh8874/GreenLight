namespace CityFlow.Feed
{
    public enum CitizenFeedEventType
    {
        CongestionStarted = 0,
        CongestionResolved = 1,
        SignalChanged = 2,
        CongestionSlowed = 3,
        CongestionSustained = 4,

        InfrastructurePlaced = 7,
        InfrastructureRemoved = 8,
        NotableArrival = 9,
        VehicleSurge = 10,
        JobChanged = 11,

        // 이미 발행되고 있었으나 피드가 듣지 않던 신호들. 숫자를 새로 잇는다 —
        // 기존 값을 바꾸면 직렬화된 규칙 SO가 조용히 다른 이벤트를 가리킨다.
        FlowBurst = 12,
        BuildingPlaced = 13,
        EmergencyAlert = 14,
        EmergencyResolved = 15,
        TimePeriodChanged = 16
    }

    public enum CitizenFeedRole
    {
        OfficeWorker,
        Parent,
        TaxiDriver,
        Merchant,
        TrafficEnthusiast,
        DeliveryDriver,
        Student,
        SelfEmployed,
        RealEstateAgent,
        CivicActivist,
        Resident,
        Driver
    }

    public enum CitizenFeedPersonality
    {
        Complainer,
        Optimist,
        Analytical,
        Emotional,
        Helpful,
        Observer,
        Humorous,
        Cynical,
        Proud,
        Conspiracy,
        Meddler
    }

    public enum CitizenFeedTone
    {
        Complaint,
        Praise,
        Neutral,
        Information,
        Question,
        Humor,
        Cynical
    }

    public enum CitizenFeedTimePeriod
    {
        Night,
        MorningRush,
        Day,
        EveningRush,
        Evening
    }

    public enum CitizenFeedCategory
    {
        LifeInconvenience,
        TrafficReport,
        InfrastructureReaction,
        CommuteExperience,
        EconomyReaction,
        HumorAndChatter
    }

    public enum CitizenFeedInfrastructureType
    {
        None,
        Signal,
        Roundabout,
        Overpass,
        PriorityRoad
    }
}
