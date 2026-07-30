using CityFlow.View;

namespace CityFlow.DebugTools
{
    /// <summary>
    /// Backward-compatible component for existing city bus Prefab instances.
    /// New Prefabs use BusWorldView directly.
    /// </summary>
    public sealed class CityBusWorldView : BusWorldView
    {
        // Unity integration: existing Prefab instances continue to load this legacy type.
    }
}
