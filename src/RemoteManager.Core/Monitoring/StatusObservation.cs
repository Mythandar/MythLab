using RemoteManager.Core.Devices;
namespace RemoteManager.Core.Monitoring;

public sealed record StatusObservation(DeviceState State, DateTimeOffset CheckedAt, string Detail);
public interface IDeviceStatusService
{
    Task<StatusObservation> CheckAsync(Device device, int timeoutMilliseconds, CancellationToken cancellationToken = default);
}
public static class DeviceTargets
{
    public static bool SameStatus(Device a, Device b) => a.Hostname == b.Hostname && a.IPv4Address == b.IPv4Address &&
        a.MacAddress == b.MacAddress && a.StatusCheck == b.StatusCheck && a.StatusPort == b.StatusPort;
    public static bool SameWake(Device a, Device b) => SameStatus(a, b) &&
        a.Wake with { Capability = WakeCapability.EnabledUnverified } == b.Wake with { Capability = WakeCapability.EnabledUnverified };
}
