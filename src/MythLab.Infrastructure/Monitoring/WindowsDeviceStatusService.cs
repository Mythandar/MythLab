using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MythLab.Core.Devices;
using MythLab.Core.Monitoring;

namespace MythLab.Infrastructure.Monitoring;

public sealed class WindowsDeviceStatusService : IDeviceStatusService
{
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> resolveAddresses;

    public WindowsDeviceStatusService() : this((host, token) =>
        Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, token)) { }

    internal WindowsDeviceStatusService(Func<string, CancellationToken, Task<IPAddress[]>> resolveAddresses) =>
        this.resolveAddresses = resolveAddresses;

    public async Task<StatusObservation> CheckAsync(Device device, int timeoutMilliseconds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (timeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        var started = DateTimeOffset.UtcNow;
        var elapsed = Stopwatch.StartNew();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(timeoutMilliseconds);

        IPAddress[] addresses;
        try
        {
            addresses = (await resolveAddresses(device.Endpoint, budget.Token).ConfigureAwait(false))
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork).Distinct().ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            if (budget.IsCancellationRequested)
                return new(DeviceState.Unknown, started, "Hostname resolution timed out.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(DeviceState.Unknown, started, "Hostname resolution timed out.");
        }
        catch (SocketException ex)
        {
            return new(DeviceState.Unknown, started, $"Hostname resolution failed ({ex.SocketErrorCode}).");
        }
        if (addresses.Length == 0)
            return new(DeviceState.Unknown, started, "The hostname has no IPv4 address.");

        var timedOut = false;
        var checkFailed = false;
        var checkError = false;
        string? failure = null;
        for (var i = 0; i < addresses.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = TimeSpan.FromMilliseconds(timeoutMilliseconds) - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero || budget.IsCancellationRequested)
            {
                timedOut = true;
                break;
            }
            // Reserve a fair share of the remaining overall budget for each later address.
            var slice = TimeSpan.FromMilliseconds(Math.Max(1, remaining.TotalMilliseconds / (addresses.Length - i)));
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
            attempt.CancelAfter(slice);
            try
            {
                var address = addresses[i];
                if (device.StatusCheck == StatusCheckKind.Tcp)
                {
                    using var tcp = new TcpClient(AddressFamily.InterNetwork);
                    await tcp.ConnectAsync(address, device.StatusPort, attempt.Token).ConfigureAwait(false);
                    return new(DeviceState.Online, started, $"TCP {device.StatusPort} accepted a connection at {address}.");
                }
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(address, slice, cancellationToken: attempt.Token)
                    .ConfigureAwait(false);
                if (reply.Status == IPStatus.Success)
                    return new(DeviceState.Online, started, $"ICMP reply from {address}.");
                timedOut |= reply.Status == IPStatus.TimedOut;
                checkFailed = true;
                failure = $"No ICMP reply from {address} ({reply.Status}). Ping may be blocked.";
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                timedOut = true;
                if (budget.IsCancellationRequested) break;
            }
            catch (SocketException ex)
            {
                checkFailed = true;
                failure = $"TCP check unavailable at {addresses[i]} ({ex.SocketErrorCode}); this does not prove power-off.";
            }
            catch (PingException)
            {
                checkError = true;
            }
        }

        if (timedOut || budget.IsCancellationRequested)
            return new(DeviceState.Offline, started,
                $"The configured check timed out or did not respond across {addresses.Length} IPv4 address(es).");
        if (checkFailed)
            return new(DeviceState.Offline, started, failure ?? "The configured check did not respond.");
        if (checkError)
            return new(DeviceState.Unknown, started, "Windows could not perform the ICMP check. Try a TCP status check.");
        return new(DeviceState.Unknown, started, "No IPv4 address could be checked.");
    }
}
