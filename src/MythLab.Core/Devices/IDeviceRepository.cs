namespace MythLab.Core.Devices;

public interface IDeviceRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Device>> ListAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(Device device, CancellationToken cancellationToken = default);
    Task<Device?> ApplyObservationAsync(Device expected, Monitoring.StatusObservation observation, bool verifyWake = false, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
