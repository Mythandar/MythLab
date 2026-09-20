using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using MythLab.Core.Devices;
using MythLab.Core.Discovery;
using MythLab.Core.WakeOnLan;
namespace MythLab.Infrastructure.WakeOnLan;

public sealed class WakeOnLanService(ILanProbe networks, IWakePacketTransport transport, ILogger<WakeOnLanService> logger) : IWakeOnLanService
{
    public async Task<WakeSendReport> SendAsync(Device device, IProgress<WakeProgress> progress, CancellationToken cancellationToken = default)
    {
        device = DeviceRules.NormalizeAndValidate(device);
        if (device.Wake.Capability == WakeCapability.Disabled) throw new ArgumentException("Wake-on-LAN is disabled for this device.");
        var packet = MagicPacket.Create(device.MacAddress);
        var interfaces = await networks.GetInterfacesAsync(cancellationToken).ConfigureAwait(false);
        string? resolved = null;
        if (device.IPv4Address.Length == 0 && device.Hostname.Length > 0 && interfaces.Count > 1)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try { resolved = (await Dns.GetHostAddressesAsync(device.Hostname, AddressFamily.InterNetwork, timeout.Token).ConfigureAwait(false)).FirstOrDefault()?.ToString(); }
            catch (SocketException) { }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        }
        var destination = WakeRouting.Select(device, interfaces, resolved);
        var count = device.Wake.PacketCount * (device.Wake.RetryCount + 1);
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (i > 0)
            {
                var delay = i % device.Wake.PacketCount == 0 ? device.Wake.RetryDelayMilliseconds : device.Wake.DelayMilliseconds;
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            progress.Report(new(DeviceState.Waking, $"Sending wake packet {i + 1}/{count} to {destination.BroadcastAddress}:{destination.Port} via {destination.Interface.Name}."));
            await transport.SendAsync(destination, packet, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Wake packet sent for {DeviceId}; broadcast {Broadcast}; port {Port}; interface {InterfaceId}; packet {Packet}/{Count}",
                device.Id, destination.BroadcastAddress, destination.Port, destination.Interface.Id, i + 1, count);
        }
        return new(destination, count);
    }
}
