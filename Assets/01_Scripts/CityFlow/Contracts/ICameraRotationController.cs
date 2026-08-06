namespace CityFlow.Contracts
{
    public interface ICameraRotationController
    {
        bool TryRotateCamera(int stepDirection);
    }
}
