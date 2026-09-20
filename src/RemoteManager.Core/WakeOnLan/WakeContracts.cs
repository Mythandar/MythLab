using RemoteManager.Core.Devices;
using RemoteManager.Core.Discovery;
using RemoteManager.Core.Monitoring;
namespace RemoteManager.Core.WakeOnLan;

public sealed record WakeDestination(LocalNetworkInterface Interface, string BroadcastAddress, int Port);
public sealed record WakeProgress(DeviceState State, string Detail);
public sealed record WakeSendReport(WakeDestination Destination, int PacketsSent);
public sealed record WakeOutcome(StatusObservation Observation, bool Verified, string Detail);
public interface IWakeOnLanService
{
    Task<WakeSendReport> SendAsync(Device device, IProgress<WakeProgress> progress, CancellationToken cancellationToken = default);
}
public interface IWakePacketTransport
{
    Task SendAsync(WakeDestination destination, ReadOnlyMemory<byte> packet, CancellationToken cancellationToken);
}

public static class WakeRouting
{
    public static WakeDestination Select(Device device, IReadOnlyList<LocalNetworkInterface> interfaces, string? resolvedAddress = null)
    {
        if (device.Wake.Capability == WakeCapability.Disabled) throw new ArgumentException("Enable Wake-on-LAN in the device editor first.");
        var candidates = interfaces.Where(n => n.Subnet.PrefixLength < 31).ToArray();
        if (device.Wake.InterfaceId.Length > 0)
        {
            candidates = candidates.Where(n => n.Id == device.Wake.InterfaceId).ToArray();
            if (candidates.Length == 0) throw new ArgumentException("The configured wake interface is unavailable or has no broadcast subnet. Edit the device and select an active interface.");
        }
        var address = device.IPv4Address.Length > 0 ? device.IPv4Address : resolvedAddress;
        var broadcast = device.Wake.BroadcastAddress;
        if (broadcast.Length > 0 && broadcast != "255.255.255.255")
            candidates = candidates.Where(n => Ipv4Subnet.Format(n.Subnet.Broadcast) == broadcast).ToArray();
        if (candidates.Length > 1 && !string.IsNullOrEmpty(address))
        {
            var matches = candidates.Where(n => n.Subnet.ContainsHost(Ipv4Subnet.Parse(address))).ToArray();
            if (matches.Length > 0) candidates = matches;
        }
        if (device.Wake.InterfaceId.Length == 0 && broadcast.Length == 0 && !string.IsNullOrEmpty(address))
            candidates = candidates.Where(n => n.Subnet.ContainsHost(Ipv4Subnet.Parse(address))).ToArray();
        if (candidates.Length == 0) throw new ArgumentException("No active local interface matches the target subnet or broadcast. Check the IP, interface and broadcast settings.");
        if (candidates.Length != 1) throw new ArgumentException("More than one interface could send this wake packet. Select a preferred interface in the device editor.");
        var chosen = candidates[0];
        return new(chosen, broadcast.Length == 0 ? Ipv4Subnet.Format(chosen.Subnet.Broadcast) : broadcast, device.Wake.Port);
    }
}
