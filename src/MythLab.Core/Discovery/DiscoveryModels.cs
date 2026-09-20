using MythLab.Core.Devices;

namespace MythLab.Core.Discovery;

public sealed record LocalNetworkInterface(string Id, int Index, string Name, string Address, string Mask, string MacAddress)
{
    public Ipv4Subnet Subnet => new(Address, Mask);
    public string DisplayLabel => $"{Name} — {Address} ({Subnet.Cidr})";
}

[Flags]
public enum DiscoveryEvidence { None = 0, NeighborCache = 1, ArpResolution = 2, PingReply = 4, LocalInterface = 8 }

public sealed record DiscoveredDevice(string Address)
{
    public string MacAddress { get; init; } = "";
    public string Hostname { get; init; } = "";
    public string Vendor { get; init; } = "";
    public DiscoveryEvidence Evidence { get; init; }
    /// <summary>When this scan read evidence; a cache entry may have been learned much earlier.</summary>
    public DateTimeOffset EvidenceCollectedAt { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>When ping replied or the selected local interface was observed during this scan.</summary>
    public DateTimeOffset? LastLiveObservationAt { get; init; }
    public bool IsOnline => (Evidence & (DiscoveryEvidence.PingReply | DiscoveryEvidence.LocalInterface)) != 0;
    public string StateLabel => IsOnline ? "Online" : "Discovered";
    public Device ToDevice() => new()
    {
        DisplayName = Hostname.Length == 0 ? Address : Hostname, Hostname = Hostname,
        IPv4Address = Address, MacAddress = MacAddress,
        LastKnownState = IsOnline ? DeviceState.Online : DeviceState.Unknown,
        LastSeen = IsOnline ? LastLiveObservationAt : null
        // Wake is deliberately Disabled: discovery cannot determine wake support.
    };
}

public sealed record NeighborEntry(int InterfaceIndex, string Address, string MacAddress);
public sealed record DiscoveryUpdate(int Completed, int Total, DiscoveredDevice? Device = null, string? Diagnostic = null);
public sealed record DiscoverySummary(int Scanned, int Found);

public sealed record DiscoveryRequest(LocalNetworkInterface Interface, string StartAddress, string EndAddress,
    int Concurrency = 32, int TimeoutMilliseconds = 750)
{
    public const int MaximumAddresses = 4096;
    public (uint Start, uint End, int Count) Validate()
    {
        if (Concurrency is < 1 or > 64 || TimeoutMilliseconds is < 100 or > 10000)
            throw new ArgumentException("Use concurrency 1–64 and a probe timeout of 100–10000 ms.");
        var start = Ipv4Subnet.Parse(StartAddress);
        var end = Ipv4Subnet.Parse(EndAddress);
        if (end < start) throw new ArgumentException("The end address must not precede the start address.");
        if (!Interface.Subnet.ContainsHost(start) || !Interface.Subnet.ContainsHost(end))
            throw new ArgumentException("Scan addresses must be usable hosts on the selected interface's subnet.");
        var count = (ulong)end - start + 1;
        if (count > MaximumAddresses)
            throw new ArgumentException($"Select a smaller range (at most {MaximumAddresses} addresses per scan).");
        return (start, end, (int)count);
    }
}

public interface INetworkDiscoveryService
{
    Task<IReadOnlyList<LocalNetworkInterface>> GetInterfacesAsync(CancellationToken cancellationToken = default);
    Task<DiscoverySummary> ScanAsync(DiscoveryRequest request, IProgress<DiscoveryUpdate> progress,
        CancellationToken cancellationToken = default);
}

/// <summary>Optional offline OUI data source. No online vendor requests are made by default.</summary>
public interface IMacVendorLookup { string? FindVendor(string normalizedMac); }
