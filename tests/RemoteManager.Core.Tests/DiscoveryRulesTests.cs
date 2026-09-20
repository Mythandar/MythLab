using RemoteManager.Core.Devices;
using RemoteManager.Core.Discovery;

namespace RemoteManager.Core.Tests;

public sealed class DiscoveryRulesTests
{
    [Theory]
    [InlineData("10.23.45.67", "255.255.252.0", "10.23.44.0", "10.23.47.255", 22, 1022UL)]
    [InlineData("172.16.9.20", "255.255.0.0", "172.16.0.0", "172.16.255.255", 16, 65534UL)]
    [InlineData("192.168.8.7", "255.255.255.0", "192.168.8.0", "192.168.8.255", 24, 254UL)]
    [InlineData("10.0.0.1", "255.255.255.254", "10.0.0.0", "10.0.0.1", 31, 2UL)]
    [InlineData("10.0.0.1", "255.255.255.255", "10.0.0.1", "10.0.0.1", 32, 1UL)]
    [InlineData("10.0.0.1", "0.0.0.0", "0.0.0.0", "255.255.255.255", 0, 4294967294UL)]
    public void CalculatesRealSubnetAndBroadcast(string address, string mask, string network, string broadcast,
        int prefix, ulong hostCount)
    {
        var subnet = new Ipv4Subnet(address, mask);
        Assert.Equal(network, Ipv4Subnet.Format(subnet.Network));
        Assert.Equal(broadcast, Ipv4Subnet.Format(subnet.Broadcast));
        Assert.Equal(prefix, subnet.PrefixLength);
        Assert.Equal(hostCount, subnet.HostCount);
    }

    [Theory]
    [InlineData("255.0.255.0")]
    [InlineData("255.255.255.1")]
    [InlineData("255.255.250.0")]
    public void RejectsNoncontiguousMasks(string mask) =>
        Assert.Throws<ArgumentException>(() => new Ipv4Subnet("10.0.0.1", mask));

    [Theory]
    [InlineData("127.1")]
    [InlineData("::1")]
    [InlineData("256.0.0.1")]
    [InlineData("-1.0.0.1")]
    public void RejectsInvalidAddresses(string address) =>
        Assert.Throws<ArgumentException>(() => Ipv4Subnet.Parse(address));

    [Fact]
    public void LeadingZeroOctetsRemainDecimal() => Assert.Equal("10.0.0.1", Ipv4Subnet.Format(Ipv4Subnet.Parse("010.0.0.1")));

    [Fact]
    public void LargeSubnetsRequireAnExplicitSmallerRange()
    {
        var network = new LocalNetworkInterface("test", 1, "Ethernet", "10.0.0.1", "255.0.0.0", "");
        Assert.Throws<ArgumentException>(() => new DiscoveryRequest(network, "10.0.0.1", "10.255.255.254").Validate());
        Assert.Equal(254, new DiscoveryRequest(network, "10.1.7.1", "10.1.7.254").Validate().Count);
    }

    [Theory]
    [InlineData("10.0.1.1", "10.0.1.5")]
    [InlineData("10.0.0.0", "10.0.0.5")]
    [InlineData("10.0.0.1", "10.0.0.255")]
    [InlineData("10.0.0.9", "10.0.0.2")]
    public void RejectsOutOfSubnetBroadcastOrReversedRange(string start, string end)
    {
        var network = new LocalNetworkInterface("test", 1, "Ethernet", "10.0.0.1", "255.255.255.0", "");
        Assert.Throws<ArgumentException>(() => new DiscoveryRequest(network, start, end).Validate());
    }

    [Fact]
    public void DiscoveryDraftIsPrefilledButDoesNotClaimWakeSupport()
    {
        var result = new DiscoveredDevice("10.0.0.4")
        {
            Hostname = "nas.local", MacAddress = "00:11:22:33:44:55", Evidence = DiscoveryEvidence.ArpResolution
        };
        var draft = result.ToDevice();
        Assert.Equal(result.Address, draft.IPv4Address);
        Assert.Equal(result.Hostname, draft.Hostname);
        Assert.Equal(result.MacAddress, draft.MacAddress);
        Assert.Equal(WakeCapability.Disabled, draft.Wake.Capability);
        Assert.Equal(DeviceState.Unknown, draft.LastKnownState);
        Assert.Null(draft.LastSeen);
    }

    [Fact]
    public void CacheAloneNeverMeansOnline()
    {
        var result = new DiscoveredDevice("10.0.0.4") { Evidence = DiscoveryEvidence.NeighborCache };
        Assert.False(result.IsOnline);
        Assert.True((result with { Evidence = result.Evidence | DiscoveryEvidence.PingReply }).IsOnline);
    }
}
