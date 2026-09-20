namespace MythLab.Core.Discovery;

/// <summary>Boundary for Windows network observations; discovery tests provide fake evidence.</summary>
public interface ILanProbe
{
    Task<IReadOnlyList<LocalNetworkInterface>> GetInterfacesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<NeighborEntry>> ReadNeighborsAsync(CancellationToken cancellationToken);
    Task<bool> PingAsync(string address, int timeoutMilliseconds, CancellationToken cancellationToken);
    Task<string?> ResolveMacAsync(LocalNetworkInterface network, string address, CancellationToken cancellationToken);
    Task<string?> ReverseDnsAsync(string address, int timeoutMilliseconds, CancellationToken cancellationToken);
}
