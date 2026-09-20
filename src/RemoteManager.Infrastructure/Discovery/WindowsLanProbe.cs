using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using RemoteManager.Core.Devices;
using RemoteManager.Core.Discovery;

namespace RemoteManager.Infrastructure.Discovery;

public sealed class WindowsLanProbe(ILogger<WindowsLanProbe> logger) : ILanProbe
{
    // Native SendARP cannot be cancelled. Keep slots until calls actually return, even after UI cancellation.
    // Static gate also bounds native work across rapid cancel/restart and multiple service instances.
    private static readonly SemaphoreSlim ArpSlots = new(8, 8);

    public Task<IReadOnlyList<LocalNetworkInterface>> GetInterfacesAsync(CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<LocalNetworkInterface>>(() =>
        {
            var results = new List<LocalNetworkInterface>();
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!network.Supports(NetworkInterfaceComponent.IPv4)) continue;
                    var properties = network.GetIPProperties();
                    var addresses = properties.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).ToArray();
                    if (addresses.Length == 0) continue;
                    var ipv4 = properties.GetIPv4Properties();
                    if (ipv4 is null) continue;
                    var physical = network.GetPhysicalAddress().GetAddressBytes();
                    var mac = physical.Length == 6 ? MacAddress.Normalize(Convert.ToHexString(physical)) : "";
                    foreach (var address in addresses)
                        results.Add(new(network.Id, ipv4.Index, network.Name, address.Address.ToString(), address.IPv4Mask.ToString(), mac));
                }
                catch (NetworkInformationException ex)
                {
                    // Adapters can disappear or lose IPv4 while Windows is enumerating them.
                    logger.LogWarning("Skipped unavailable IPv4 adapter {InterfaceId}; error {ErrorCode}", network.Id, ex.ErrorCode);
                }
            }
            return results.OrderBy(n => n.Name).ThenBy(n => Ipv4Subnet.Parse(n.Address)).ToArray();
        }, cancellationToken);

    public Task<IReadOnlyList<NeighborEntry>> ReadNeighborsAsync(CancellationToken cancellationToken) =>
        Task.Run(WindowsArp.ReadNeighbors, cancellationToken);

    public async Task<bool> PingAsync(string address, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(IPAddress.Parse(address), TimeSpan.FromMilliseconds(timeoutMilliseconds),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return reply.Status == IPStatus.Success;
        }
        catch (Exception ex) when (ex is PingException or SocketException)
        {
            logger.LogDebug("ICMP unavailable for {Address}; category {FailureCategory}", address, ex.GetType().Name);
            return false;
        }
    }

    public async Task<string?> ResolveMacAsync(LocalNetworkInterface network, string address, CancellationToken cancellationToken)
    {
        await ArpSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        var pending = Task.Run(() =>
        {
            try { return WindowsArp.Resolve(network.Address, address); }
            catch (Exception ex)
            {
                logger.LogDebug("ARP unavailable for {Address}; category {FailureCategory}", address, ex.GetType().Name);
                return null;
            }
            finally { ArpSlots.Release(); }
        }, CancellationToken.None);
        return await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> ReverseDnsAsync(string address, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMilliseconds);
        try
        {
            var host = await Dns.GetHostEntryAsync(address, AddressFamily.InterNetwork, timeout.Token).ConfigureAwait(false);
            var name = host.HostName.TrimEnd('.');
            if (name.Length > 253 || Uri.CheckHostName(name) != UriHostNameType.Dns) return null;
            logger.LogDebug("Reverse DNS resolved {Address} to {Hostname}", address, name);
            return name;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Reverse DNS timed out for {Address}", address);
            return null;
        }
        catch (SocketException)
        {
            logger.LogDebug("No reverse DNS name for {Address}", address);
            return null;
        }
    }
}
