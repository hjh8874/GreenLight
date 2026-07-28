namespace CityFlow.Contracts
{
    public interface IWorldCoordinateRoot
    {
        void ApplyCoordinateSpace(IWorldCoordinateSpace coordinateSpace);
    }
}

// Unity setup: Implement this contract on the visual world root.
