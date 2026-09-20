using RemoteManager.Core.Discovery;
namespace RemoteManager.SmokeTests;
internal sealed class SmokeProbe : ILanProbe
{
    public bool Block { get; set; }
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<IReadOnlyList<LocalNetworkInterface>> GetInterfacesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LocalNetworkInterface>>([new("fake", 1, "Test Ethernet", "10.20.30.1", "255.255.255.0", "00:11:22:33:44:01")]);
    public Task<IReadOnlyList<NeighborEntry>> ReadNeighborsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NeighborEntry>>([new(1, "10.20.30.51", "00:11:22:33:44:51")]);
    public async Task<bool> PingAsync(string address, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        if (Block) { Entered.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellationToken); }
        return false;
    }
    public Task<string?> ResolveMacAsync(LocalNetworkInterface network, string address, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(address == "10.20.30.50" ? "00:11:22:33:44:50" : null);
    public Task<string?> ReverseDnsAsync(string address, int timeoutMilliseconds, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(address == "10.20.30.50" ? "discovered-nas.local" : null);
}