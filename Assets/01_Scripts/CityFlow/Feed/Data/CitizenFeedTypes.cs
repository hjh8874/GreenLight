namespace CityFlow.Feed
{
    public enum CitizenFeedEventType
    {
        CongestionStarted,
        CongestionResolved,
        SignalChanged,
        CongestionSlowed,
        CongestionSustained,

        InfrastructurePlaced,
        InfrastructureRemoved,
        NotableArrival,
        VehicleSurge
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
