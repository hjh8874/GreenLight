namespace CityFlow.View
{
    /// <summary>
    /// Backward-compatible component for existing school bus Prefab instances.
    /// New Prefabs use BusWorldView directly.
    /// </summary>
    public sealed class SchoolBusWorldView : BusWorldView
    {
        // Unity integration: existing Prefab instances continue to load this legacy type.
    }
}
