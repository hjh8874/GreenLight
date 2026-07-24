using System;
using UnityEngine;

namespace CityFlow.Contracts
{
    public readonly struct ArrivalEvent
    {
        public readonly Vector2Int Destination;
        public readonly int Coins;

        public ArrivalEvent(Vector2Int destination, int coins)
        {
            Destination = destination;
            Coins = coins;
        }
    }

    public readonly struct FlowBurstEvent
    {
        public readonly Vector2Int Tile;
        // 호환을 위해 이름은 유지한다. 코인이 아니라 SFX/쉐이크용 연출 magnitude다.
        public readonly int Reward;

        public FlowBurstEvent(Vector2Int tile, int reward)
        {
            Tile = tile;
            Reward = reward;
        }
    }

    public readonly struct CongestionEvent
    {
        public readonly Vector2Int Tile;
        public readonly CongestionLevel Level;

        public CongestionEvent(Vector2Int tile, CongestionLevel level)
        {
            Tile = tile;
            Level = level;
        }
    }

    public readonly struct StabilityEvent
    {
        public readonly float Stability01;

        public StabilityEvent(float stability01)
        {
            Stability01 = Mathf.Clamp01(stability01);
        }
    }

    public readonly struct PlacedEvent
    {
        public readonly Vector2Int Tile;
        public readonly TileType Type;
        public readonly bool IsRemove;
        public readonly PlacementDirection Direction;

        public PlacedEvent(Vector2Int tile, TileType type, bool isRemove, PlacementDirection direction = PlacementDirection.North)
        {
            Tile = tile;
            Type = type;
            IsRemove = isRemove;
            Direction = direction;
        }
    }

    public readonly struct InfrastructureChangedEvent
    {
        public readonly Vector2Int Tile;
        public readonly bool IsRemove;

        public InfrastructureChangedEvent(Vector2Int tile, bool isRemove)
        {
            Tile = tile;
            IsRemove = isRemove;
        }
    }

    public sealed class SimEventHub
    {
        public event Action<ArrivalEvent> Arrival;
        public event Action<FlowBurstEvent> FlowBurst;
        public event Action<CongestionEvent> CongestionChanged;
        public event Action<StabilityEvent> StabilityChanged;
        public event Action<PlacedEvent> Placed;
        public event Action<InfrastructureChangedEvent> InfrastructureChanged;

        public void Publish(ArrivalEvent e) => Arrival?.Invoke(e);

        public void Publish(FlowBurstEvent e) => FlowBurst?.Invoke(e);

        public void Publish(CongestionEvent e) => CongestionChanged?.Invoke(e);

        public void Publish(StabilityEvent e) => StabilityChanged?.Invoke(e);

        public void Publish(PlacedEvent e) => Placed?.Invoke(e);

        public void Publish(InfrastructureChangedEvent e) => InfrastructureChanged?.Invoke(e);
    }
}
