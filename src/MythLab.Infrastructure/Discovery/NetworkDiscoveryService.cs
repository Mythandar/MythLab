using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MythLab.Core.Devices;
using MythLab.Core.Discovery;

namespace MythLab.Infrastructure.Discovery;

public sealed class NetworkDiscoveryService(ILanProbe probe, ILogger<NetworkDiscoveryService> logger,
    IMacVendorLookup? vendorLookup = null) : INetworkDiscoveryService
{
    public Task<IReadOnlyList<LocalNetworkInterface>> GetInterfacesAsync(CancellationToken cancellationToken = default) =>
        probe.GetInterfacesAsync(cancellationToken);

    public async Task<DiscoverySummary> ScanAsync(DiscoveryRequest request, IProgress<DiscoveryUpdate> progress,
        CancellationToken cancellationToken = default)
    {
        var range = request.Validate();
        // Do not scan a disconnected/readdressed adapter using a stale selection.
        var current = await probe.GetInterfacesAsync(cancellationToken).ConfigureAwait(false);
        if (!current.Any(i => i.Id == request.Interface.Id && i.Index == request.Interface.Index &&
            i.Address == request.Interface.Address && i.Mask == request.Interface.Mask))
            throw new InvalidOperationException("The selected network changed. Refresh interfaces and select it again.");
        var results = new ConcurrentDictionary<string, DiscoveredDevice>();
        var completed = 0;
        logger.LogInformation("Discovery started on interface {InterfaceId}; range {Start} to {End}; addresses {Count}",
            request.Interface.Id, request.StartAddress, request.EndAddress, range.Count);
        progress.Report(new(0, range.Count));
        void Publish(DiscoveredDevice device)
        {
            if (cancellationToken.IsCancellationRequested) return;
            var merged = results.AddOrUpdate(device.Address, device, (_, old) => device with
            {
                Evidence = old.Evidence | device.Evidence,
                Hostname = device.Hostname.Length > 0 ? device.Hostname : old.Hostname,
                MacAddress = device.MacAddress.Length > 0 ? device.MacAddress : old.MacAddress,
                Vendor = device.Vendor.Length > 0 ? device.Vendor : old.Vendor,
                LastLiveObservationAt = device.LastLiveObservationAt ?? old.LastLiveObservationAt
            });
            progress.Report(new(Volatile.Read(ref completed), range.Count, merged));
        }
        async Task MergeNeighbors()
        {
            try
            {
                var neighbors = await probe.ReadNeighborsAsync(cancellationToken).ConfigureAwait(false);
                foreach (var neighbor in neighbors)
                {
                    if (neighbor.InterfaceIndex != request.Interface.Index || !IsUsableMac(neighbor.MacAddress)) continue;
                    var ip = Ipv4Subnet.Parse(neighbor.Address);
                    if (ip < range.Start || ip > range.End) continue;
                    Publish(new(neighbor.Address) { MacAddress = neighbor.MacAddress, Evidence = DiscoveryEvidence.NeighborCache,
                        Vendor = vendorLookup?.FindVendor(neighbor.MacAddress) ?? "" });
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning("Neighbor cache unavailable; category {FailureCategory}", ex.GetType().Name);
                progress.Report(new(Volatile.Read(ref completed), range.Count, Diagnostic:
                    "Windows neighbor-cache lookup is unavailable. Continuing with ping and ARP."));
            }
        }

        try
        {
            await MergeNeighbors().ConfigureAwait(false);
            await Parallel.ForEachAsync(Enumerable.Range(0, range.Count), new ParallelOptions
            {
                MaxDegreeOfParallelism = request.Concurrency, CancellationToken = cancellationToken
            }, async (offset, token) =>
            {
                var address = Ipv4Subnet.Format(range.Start + (uint)offset);
                var local = address == request.Interface.Address;
                var evidence = local ? DiscoveryEvidence.LocalInterface : DiscoveryEvidence.None;
                string? mac = local ? request.Interface.MacAddress : null;
                DateTimeOffset? liveObservedAt = local ? DateTimeOffset.UtcNow : null;
                if (!local)
                {
                    if (await probe.PingAsync(address, request.TimeoutMilliseconds, token).ConfigureAwait(false))
                    {
                        evidence |= DiscoveryEvidence.PingReply;
                        liveObservedAt = DateTimeOffset.UtcNow;
                    }
                    // Cache evidence has already been restricted to this interface and range.
                    // Still ping above; cached MAC data alone never establishes availability.
                    if (results.TryGetValue(address, out var cachedMac) &&
                        IsUsableMac(cachedMac.MacAddress))
                        mac = cachedMac.MacAddress;
                    else
                    {
                        mac = await probe.ResolveMacAsync(request.Interface, address, token).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(mac)) evidence |= DiscoveryEvidence.ArpResolution;
                    }
                }
                if (results.TryGetValue(address, out var cached))
                {
                    evidence |= cached.Evidence;
                    mac ??= cached.MacAddress;
                }
                if (evidence != DiscoveryEvidence.None)
                {
                    // Show network evidence immediately; slow or absent DNS never hides a discovered device.
                    var device = new DiscoveredDevice(address) { MacAddress = mac ?? "", Evidence = evidence,
                        LastLiveObservationAt = liveObservedAt,
                        Vendor = mac is null ? "" : vendorLookup?.FindVendor(mac) ?? "" };
                    Publish(device);
                    var hostname = await probe.ReverseDnsAsync(address, request.TimeoutMilliseconds, token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(hostname)) Publish(device with { Hostname = hostname });
                }
                logger.LogDebug("Discovery address checked {Address}; evidence {Evidence}", address, evidence);
                progress.Report(new(Interlocked.Increment(ref completed), range.Count));
            }).ConfigureAwait(false);
            await MergeNeighbors().ConfigureAwait(false);
            logger.LogInformation("Discovery completed; addresses {Scanned}; found {Found}", completed, results.Count);
            return new(completed, results.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Discovery cancelled; addresses checked {Scanned}; found {Found}", completed, results.Count);
            throw;
        }
    }
    private static bool IsUsableMac(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            var bytes = Convert.FromHexString(MacAddress.Normalize(value).Replace(":", ""));
            return (bytes[0] & 1) == 0 && bytes.Any(b => b != 0);
        }
        catch (ArgumentException) { return false; }
    }
}
