using System.Net;
using System.Net.Sockets;
using RemoteManager.Core.WakeOnLan;
namespace RemoteManager.Infrastructure.WakeOnLan;

public sealed class UdpWakePacketTransport : IWakePacketTransport
{
    public async Task SendAsync(WakeDestination destination, ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Parse(destination.Interface.Address), 0));
        udp.EnableBroadcast = true;
        var sent = await udp.SendAsync(packet, new IPEndPoint(IPAddress.Parse(destination.BroadcastAddress), destination.Port),
            cancellationToken).ConfigureAwait(false);
        if (sent != packet.Length) throw new IOException("The complete wake packet could not be sent.");
    }
}
