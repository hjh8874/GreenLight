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
        VehicleSurge = 10
        ,JobChanged = 11
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
