using MythLab.Core.Devices;
using MythLab.Core.Monitoring;
using MythLab.Core.WakeOnLan;
namespace MythLab.Infrastructure.WakeOnLan;

/// <summary>Wake and observe host availability. Connection readiness/launching belongs to Milestone F.</summary>
public sealed class WakeDeviceService(IWakeOnLanService sender, IDeviceStatusService status)
{
    public async Task<WakeOutcome> WakeAsync(Device device, int probeTimeoutMilliseconds, TimeSpan timeout,
        TimeSpan interval, IProgress<WakeProgress> progress, CancellationToken cancellationToken = default)
    {
        if (device.Wake.Capability == WakeCapability.Disabled) throw new ArgumentException("Wake-on-LAN is disabled.");
        if (timeout <= TimeSpan.Zero || interval <= TimeSpan.Zero) throw new ArgumentException("Wake wait and polling intervals must be positive.");
        progress.Report(new(DeviceState.Unknown, "Checking availability before wake…"));
        var before = await status.CheckAsync(device, probeTimeoutMilliseconds, cancellationToken).ConfigureAwait(false);
        if (before.State == DeviceState.Online)
            return new(before, false, "Already Online by the configured check. No packets sent; wake support cannot be verified while already online.");
        await sender.SendAsync(device, progress, cancellationToken).ConfigureAwait(false);
        progress.Report(new(DeviceState.Waking, "Wake packets sent. Waiting for the configured status check to succeed…"));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var latest = before;
        try
        {
            while (true)
            {
                await Task.Delay(interval, deadline.Token).ConfigureAwait(false);
                latest = await status.CheckAsync(device, probeTimeoutMilliseconds, deadline.Token).ConfigureAwait(false);
                if (latest.State == DeviceState.Online)
                {
                    var verified = before.State == DeviceState.Offline;
                    return new(latest, verified, verified
                        ? "Offline → wake → Online observed. Wake configuration verified by observation."
                        : "Now Online. Initial availability was unknown, so wake support remains unverified.");
                }
                progress.Report(new(DeviceState.Waking, $"Still waiting. {latest.Detail}"));
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(latest, false, "Wake wait timed out. Check the MAC, NIC/broadcast, BIOS/NIC wake settings, power state and chosen ICMP/TCP check.");
        }
    }
}
