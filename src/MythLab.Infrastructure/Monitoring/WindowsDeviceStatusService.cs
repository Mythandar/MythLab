using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MythLab.Core.Devices;
using MythLab.Core.Monitoring;
namespace MythLab.Infrastructure.Monitoring;

public sealed class WindowsDeviceStatusService : IDeviceStatusService
{
    public async Task<StatusObservation> CheckAsync(Device device, int timeoutMilliseconds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (timeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        var started = DateTimeOffset.UtcNow;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(timeoutMilliseconds);
        var resolved = false;
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(device.Endpoint, AddressFamily.InterNetwork, budget.Token).ConfigureAwait(false);
            if (addresses.Length == 0) return new(DeviceState.Unknown, started, "The hostname has no IPv4 address.");
            resolved = true;
            // One IPv4 endpoint per observation; no silent fallback to a possibly stale saved IP.
            var address = addresses[0];
            if (device.StatusCheck == StatusCheckKind.Tcp)
            {
                using var tcp = new TcpClient(AddressFamily.InterNetwork);
                await tcp.ConnectAsync(address, device.StatusPort, budget.Token).ConfigureAwait(false);
                return new(DeviceState.Online, started, $"TCP {device.StatusPort} accepted a connection at {address}.");
            }
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, TimeSpan.FromMilliseconds(timeoutMilliseconds),
                cancellationToken: budget.Token).ConfigureAwait(false);
            return reply.Status == IPStatus.Success
                ? new(DeviceState.Online, started, $"ICMP reply from {address}.")
                : new(DeviceState.Offline, started, $"No ICMP reply ({reply.Status}). Ping may be blocked.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(resolved ? DeviceState.Offline : DeviceState.Unknown, started,
                resolved ? "The configured status check timed out." : "Hostname resolution timed out.");
        }
        catch (SocketException ex)
        {
            return new(resolved ? DeviceState.Offline : DeviceState.Unknown, started,
                resolved ? $"TCP check unavailable ({ex.SocketErrorCode}); this does not prove the computer is powered off."
                         : $"Hostname resolution failed ({ex.SocketErrorCode}).");
        }
        catch (PingException)
        {
            return new(DeviceState.Unknown, started, "Windows could not perform the ICMP check. Try a TCP status check.");
        }
    }
}
