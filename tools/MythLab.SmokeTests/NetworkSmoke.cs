using Microsoft.Extensions.Logging.Abstractions;
using MythLab.Core.Discovery;
using MythLab.Infrastructure.Discovery;

namespace MythLab.SmokeTests;

internal static class NetworkSmoke
{
    // Explicit opt-in only: normal smoke/unit tests never access the LAN.
    public static async Task<int> RunAsync()
    {
        var probe = new WindowsLanProbe(NullLogger<WindowsLanProbe>.Instance);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var interfaces = await probe.GetInterfacesAsync(cancellation.Token);
        var neighbors = await probe.ReadNeighborsAsync(cancellation.Token);
        Console.WriteLine($"Windows IPv4 interfaces: {interfaces.Count}; valid ARP entries: {neighbors.Count}.");
        var target = (from network in interfaces
                      from neighbor in neighbors
                      where neighbor.InterfaceIndex == network.Index &&
                            network.Subnet.ContainsHost(Ipv4Subnet.Parse(neighbor.Address)) && neighbor.Address != network.Address
                      select new { Network = network, Neighbor = neighbor }).FirstOrDefault();
        if (target is null)
        {
            Console.WriteLine("Native enumeration passed; no on-subnet cached neighbor available for the optional single-host probe.");
            return 0;
        }
        var service = new NetworkDiscoveryService(probe, NullLogger<NetworkDiscoveryService>.Instance);
        var progress = new Progress<DiscoveryUpdate>();
        var summary = await service.ScanAsync(new(target.Network, target.Neighbor.Address, target.Neighbor.Address, 1, 750),
            progress, cancellation.Token);
        if (summary.Scanned != 1 || summary.Found != 1) throw new InvalidOperationException("Expected the cached local neighbor to survive discovery.");
        Console.WriteLine("PASS: one cached on-subnet host scanned using real Windows ping/ARP/DNS adapters; cache evidence retained.");
        return 0;
    }
}
