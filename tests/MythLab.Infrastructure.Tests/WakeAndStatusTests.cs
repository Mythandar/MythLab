using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using MythLab.Core.Devices;
using MythLab.Core.Discovery;
using MythLab.Core.Monitoring;
using MythLab.Core.WakeOnLan;
using MythLab.Infrastructure.Monitoring;
using MythLab.Infrastructure.WakeOnLan;
namespace MythLab.Infrastructure.Tests;
public sealed class WakeAndStatusTests
{
    private static Device Target => new() { DisplayName = "PC", IPv4Address = "10.2.0.20", MacAddress = "02:11:22:33:44:55",
        Wake = new() { Capability = WakeCapability.EnabledUnverified, PacketCount = 2, RetryCount = 1, DelayMilliseconds = 50, RetryDelayMilliseconds = 50 } };
    private sealed class ProgressSink : IProgress<WakeProgress>
    {
        public List<WakeProgress> Updates { get; } = [];
        public void Report(WakeProgress value) => Updates.Add(value);
    }
    private sealed class Probe : ILanProbe
    {
        public Task<IReadOnlyList<LocalNetworkInterface>> GetInterfacesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LocalNetworkInterface>>([new("lan", 1, "LAN", "10.2.0.1", "255.255.0.0", "")]);
        public Task<IReadOnlyList<NeighborEntry>> ReadNeighborsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> PingAsync(string address, int timeoutMilliseconds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> ResolveMacAsync(LocalNetworkInterface network, string address, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> ReverseDnsAsync(string address, int timeoutMilliseconds, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Transport : IWakePacketTransport
    {
        public List<(WakeDestination Destination, byte[] Packet)> Sent { get; } = [];
        public Action? AfterSend { get; set; }
        public Task SendAsync(WakeDestination destination, ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sent.Add((destination, packet.ToArray()));
            AfterSend?.Invoke();
            return Task.CompletedTask;
        }
    }
    [Fact]
    public async Task SenderUsesConfiguredBurstCountAndDestinationWithoutRealUdp()
    {
        var transport = new Transport();
        var sender = new WakeOnLanService(new Probe(), transport, NullLogger<WakeOnLanService>.Instance);
        var progress = new ProgressSink();
        var result = await sender.SendAsync(Target, progress);
        Assert.Equal(4, result.PacketsSent);
        Assert.Equal(4, transport.Sent.Count);
        Assert.All(transport.Sent, s => { Assert.Equal("10.2.255.255", s.Destination.BroadcastAddress); Assert.Equal(MagicPacket.Create(Target.MacAddress), s.Packet); });
        Assert.Equal(4, progress.Updates.Count);
    }
    [Fact]
    public async Task CancellationStopsSubsequentPackets()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new Transport { AfterSend = cancellation.Cancel };
        var sender = new WakeOnLanService(new Probe(), transport, NullLogger<WakeOnLanService>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.SendAsync(Target, new ProgressSink(), cancellation.Token));
        Assert.Single(transport.Sent);
    }
    private sealed class Status(params DeviceState[] states) : IDeviceStatusService
    {
        private int calls;
        public Task<StatusObservation> CheckAsync(Device device, int timeoutMilliseconds, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new StatusObservation(states[Math.Min(calls++, states.Length - 1)], DateTimeOffset.UtcNow, "Fake availability"));
        }
    }
    private sealed class Sender : IWakeOnLanService
    {
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public Task<WakeSendReport> SendAsync(Device device, IProgress<WakeProgress> progress, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Fail) throw new SocketException();
            return Task.FromResult(new WakeSendReport(new(new("lan", 1, "LAN", "10.2.0.1", "255.255.0.0", ""), "10.2.255.255", 9), 3));
        }
    }
    [Theory]
    [InlineData(DeviceState.Offline, true)]
    [InlineData(DeviceState.Unknown, false)]
    public async Task VerificationRequiresAnOfflineBaseline(DeviceState initial, bool verified)
    {
        var sender = new Sender();
        var result = await new WakeDeviceService(sender, new Status(initial, DeviceState.Online))
            .WakeAsync(Target, 100, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1), new ProgressSink());
        Assert.Equal(verified, result.Verified);
        Assert.Equal(DeviceState.Online, result.Observation.State);
        Assert.Equal(1, sender.Calls);
    }
    [Fact]
    public async Task AlreadyOnlineDoesNotSendOrVerify()
    {
        var sender = new Sender();
        var result = await new WakeDeviceService(sender, new Status(DeviceState.Online))
            .WakeAsync(Target, 100, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1), new ProgressSink());
        Assert.False(result.Verified);
        Assert.Equal(0, sender.Calls);
    }
    [Fact]
    public async Task TimeoutReturnsUsefulFailureWithoutVerification()
    {
        var result = await new WakeDeviceService(new Sender(), new Status(DeviceState.Offline))
            .WakeAsync(Target, 100, TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(5), new ProgressSink());
        Assert.False(result.Verified);
        Assert.Equal(DeviceState.Offline, result.Observation.State);
        Assert.Contains("timed out", result.Detail);
    }
    [Fact]
    public async Task CancellationAndTransportFailurePropagate()
    {
        using var cancellation = new CancellationTokenSource(30);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WakeDeviceService(new Sender(), new Status(DeviceState.Offline))
                .WakeAsync(Target, 100, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(5), new ProgressSink(), cancellation.Token));
        await Assert.ThrowsAsync<SocketException>(() =>
            new WakeDeviceService(new Sender { Fail = true }, new Status(DeviceState.Offline))
                .WakeAsync(Target, 100, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(5), new ProgressSink()));
    }
    [Fact]
    public async Task ResolutionFailuresAndTimeoutsRemainUnknownAndCallerCancellationPropagates()
    {
        var target = Target with { Hostname = "fixture.invalid", IPv4Address = "" };
        var failed = new WindowsDeviceStatusService((_, _) =>
            Task.FromException<IPAddress[]>(new SocketException((int)SocketError.HostNotFound)));
        var failedResult = await failed.CheckAsync(target, 100);
        Assert.Equal(DeviceState.Unknown, failedResult.State);
        Assert.Contains("resolution failed", failedResult.Detail);

        var unresolved = new WindowsDeviceStatusService((_, token) =>
            Task.Delay(Timeout.Infinite, token).ContinueWith(_ => Array.Empty<IPAddress>(), token));
        var timeoutResult = await unresolved.CheckAsync(target, 20);
        Assert.Equal(DeviceState.Unknown, timeoutResult.State);
        Assert.Contains("resolution timed out", timeoutResult.Detail);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unresolved.CheckAsync(target, 100, cancellation.Token));
    }

    [Fact]
    [Trait("Category", "LocalNetwork")]
    public async Task LaterResolvedIpv4CanSucceedWithinOneBudget()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var target = Target with { Hostname = "fixture.invalid", IPv4Address = "",
            StatusCheck = StatusCheckKind.Tcp, StatusPort = ((IPEndPoint)listener.LocalEndpoint).Port };
        var checker = new WindowsDeviceStatusService((_, _) =>
            Task.FromResult(new[] { IPAddress.Parse("127.0.0.2"), IPAddress.Loopback }));
        var result = await checker.CheckAsync(target, 1000);
        Assert.Equal(DeviceState.Online, result.State);
        Assert.Contains("127.0.0.1", result.Detail);
    }

    [Fact]
    [Trait("Category", "LocalNetwork")]
    public async Task TcpStatusWorksAgainstLoopbackOnly()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var target = Target with { IPv4Address = "127.0.0.1", StatusCheck = StatusCheckKind.Tcp, StatusPort = ((IPEndPoint)listener.LocalEndpoint).Port };
        var checker = new WindowsDeviceStatusService();
        var online = await checker.CheckAsync(target, 1000);
        Assert.Equal(DeviceState.Online, online.State);
        listener.Stop();
        var offline = await checker.CheckAsync(target, 1000);
        Assert.Equal(DeviceState.Offline, offline.State);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checker.CheckAsync(target, 100, cancellation.Token));
    }
}
