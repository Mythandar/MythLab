using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using MythLab.Core.Discovery;
using MythLab.Infrastructure.Discovery;

namespace MythLab.Infrastructure.Tests;

public sealed class DiscoveryServiceTests
{
    private static readonly LocalNetworkInterface Network = new("test", 7, "Ethernet", "10.0.0.1", "255.255.255.0", "00:11:22:33:44:01");
    private static NetworkDiscoveryService Create(FakeProbe probe) => new(probe, NullLogger<NetworkDiscoveryService>.Instance);
    private static DiscoveryRequest Request(string end = "10.0.0.4", int concurrency = 2) => new(Network, "10.0.0.2", end, concurrency);
    private sealed class Recorder : IProgress<DiscoveryUpdate>
    {
        public ConcurrentQueue<DiscoveryUpdate> Updates { get; } = new();
        public void Report(DiscoveryUpdate value) => Updates.Enqueue(value);
        public DiscoveredDevice Last(string address) => Updates.Last(u => u.Device?.Address == address).Device!;
    }

    [Fact]
    public async Task FindsArpAndCachedHostsWithoutPingAndPreservesDnsOnFinalMerge()
    {
        var probe = new FakeProbe
        {
            Neighbors = [new(7, "10.0.0.2", "00:11:22:33:44:02")],
            Macs = new() { ["10.0.0.3"] = "00:11:22:33:44:03" },
            Names = new() { ["10.0.0.2"] = "nas.local" },
            Online = ["10.0.0.4"]
        };
        var progress = new Recorder();
        var summary = await Create(probe).ScanAsync(Request(), progress);
        Assert.Equal(new DiscoverySummary(3, 3), summary);
        Assert.False(progress.Last("10.0.0.2").IsOnline);
        Assert.Equal(DiscoveryEvidence.NeighborCache, progress.Last("10.0.0.2").Evidence);
        Assert.Equal("00:11:22:33:44:02", progress.Last("10.0.0.2").MacAddress);
        Assert.Null(progress.Last("10.0.0.2").LastLiveObservationAt);
        Assert.Null(progress.Last("10.0.0.2").ToDevice().LastSeen);
        Assert.DoesNotContain("10.0.0.2", probe.ArpAddresses);
        Assert.Equal("nas.local", progress.Last("10.0.0.2").Hostname);
        Assert.Equal(DiscoveryEvidence.ArpResolution, progress.Last("10.0.0.3").Evidence);
        Assert.True(progress.Last("10.0.0.4").IsOnline);
    }

    [Fact]
    public async Task LivePingUpgradesCachedMacWithoutResolvingArpAgain()
    {
        var probe = new FakeProbe
        {
            Neighbors = [new(7, "10.0.0.2", "00:11:22:33:44:02")],
            Online = ["10.0.0.2"]
        };
        var progress = new Recorder();
        await Create(probe).ScanAsync(Request("10.0.0.2"), progress);
        var result = progress.Last("10.0.0.2");
        Assert.True(result.IsOnline);
        Assert.Equal(DiscoveryEvidence.NeighborCache | DiscoveryEvidence.PingReply, result.Evidence);
        Assert.Equal("00:11:22:33:44:02", result.MacAddress);
        Assert.DoesNotContain("10.0.0.2", probe.ArpAddresses);
        Assert.NotNull(result.LastLiveObservationAt);
        Assert.Equal(result.LastLiveObservationAt, result.ToDevice().LastSeen);
    }

    [Fact]
    public async Task UnusableCachedMacStillAllowsDirectArpResolution()
    {
        var probe = new FakeProbe
        {
            Neighbors = [new(7, "10.0.0.2", "00:00:00:00:00:00")],
            Macs = new() { ["10.0.0.2"] = "00:11:22:33:44:02" }
        };
        var progress = new Recorder();
        await Create(probe).ScanAsync(Request("10.0.0.2"), progress);
        Assert.Contains("10.0.0.2", probe.ArpAddresses);
        Assert.Equal("00:11:22:33:44:02", progress.Last("10.0.0.2").MacAddress);
        Assert.False(progress.Last("10.0.0.2").IsOnline);
    }

    [Fact]
    public async Task IgnoresNeighborsFromOtherInterfacesOrOutsideRequestedRange()
    {
        var probe = new FakeProbe { Neighbors = [new(8, "10.0.0.2", "00:11:22:33:44:02"),
            new(7, "10.0.0.50", "00:11:22:33:44:50")] };
        var summary = await Create(probe).ScanAsync(Request(), new Recorder());
        Assert.Equal(0, summary.Found);
        Assert.Contains("10.0.0.2", probe.ArpAddresses);
    }

    [Fact]
    public async Task WorkerConcurrencyIsBounded()
    {
        var probe = new FakeProbe { Delay = 15 };
        await Create(probe).ScanAsync(Request("10.0.0.20", 3), new Recorder());
        Assert.InRange(probe.Peak, 2, 3);
        Assert.Equal(19, probe.PingCalls);
    }

    [Fact]
    public async Task CancellationStopsSchedulingAndPropagates()
    {
        var probe = new FakeProbe { Block = true };
        using var cancellation = new CancellationTokenSource();
        var scan = Create(probe).ScanAsync(Request("10.0.0.254", 2), new Recorder(), cancellation.Token);
        await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.InRange(probe.PingCalls, 1, 2);
    }

    [Fact]
    public async Task CacheFailureIsDiagnosticAndOtherEvidenceStillWorks()
    {
        var probe = new FakeProbe { CacheFailure = true, Online = ["10.0.0.2"] };
        var progress = new Recorder();
        var summary = await Create(probe).ScanAsync(Request(), progress);
        Assert.Equal(1, summary.Found);
        Assert.Contains(progress.Updates, u => u.Diagnostic is not null);
    }

    [Fact]
    public async Task LocalInterfaceAppearsWithoutArpOrPing()
    {
        var probe = new FakeProbe();
        var progress = new Recorder();
        await Create(probe).ScanAsync(new(Network, Network.Address, Network.Address), progress);
        Assert.Equal(0, probe.PingCalls);
        Assert.True(progress.Last(Network.Address).IsOnline);
        Assert.Equal(Network.MacAddress, progress.Last(Network.Address).MacAddress);
    }

    [Fact]
    public async Task AdapterChangesPreventScanningStaleNetwork()
    {
        var probe = new FakeProbe { Interfaces = [] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(probe).ScanAsync(Request(), new Recorder()));
        Assert.Equal(0, probe.PingCalls);
    }

    [Fact]
    public async Task InvalidRangeDoesNotTouchNetwork()
    {
        var probe = new FakeProbe();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Create(probe).ScanAsync(new(Network, "10.0.0.1", "10.0.255.254"), new Recorder()));
        Assert.Equal(0, probe.InterfaceCalls);
    }

    private sealed class FakeProbe : ILanProbe
    {
        public IReadOnlyList<LocalNetworkInterface> Interfaces { get; init; } = [Network];
        public IReadOnlyList<NeighborEntry> Neighbors { get; init; } = [];
        public Dictionary<string, string> Macs { get; init; } = [];
        public Dictionary<string, string> Names { get; init; } = [];
        public string[] Online { get; init; } = [];
        public bool CacheFailure { get; init; }
        public bool Block { get; init; }
        public int Delay { get; init; }
        public ConcurrentBag<string> ArpAddresses { get; } = [];
        public int PingCalls;
        public int InterfaceCalls;
        public int Peak;
        private int active;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<LocalNetworkInterface>> GetInterfacesAsync(CancellationToken cancellationToken)
        {
            InterfaceCalls++;
            return Task.FromResult(Interfaces);
        }
        public Task<IReadOnlyList<NeighborEntry>> ReadNeighborsAsync(CancellationToken cancellationToken) =>
            CacheFailure ? Task.FromException<IReadOnlyList<NeighborEntry>>(new IOException("unavailable")) : Task.FromResult(Neighbors);
        public async Task<bool> PingAsync(string address, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref PingCalls);
            var count = Interlocked.Increment(ref active);
            lock (Entered) Peak = Math.Max(Peak, count);
            Entered.TrySetResult();
            try
            {
                if (Block) await Task.Delay(Timeout.Infinite, cancellationToken);
                else if (Delay > 0) await Task.Delay(Delay, cancellationToken);
                return Online.Contains(address);
            }
            finally { Interlocked.Decrement(ref active); }
        }
        public Task<string?> ResolveMacAsync(LocalNetworkInterface network, string address, CancellationToken cancellationToken)
        {
            ArpAddresses.Add(address);
            return Task.FromResult(Macs.GetValueOrDefault(address));
        }
        public Task<string?> ReverseDnsAsync(string address, int timeoutMilliseconds, CancellationToken cancellationToken) =>
            Task.FromResult(Names.GetValueOrDefault(address));
    }
}
