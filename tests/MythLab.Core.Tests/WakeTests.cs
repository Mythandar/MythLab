using MythLab.Core.Devices;
using MythLab.Core.Discovery;
using MythLab.Core.WakeOnLan;
namespace MythLab.Core.Tests;
public sealed class WakeTests
{
    private static readonly LocalNetworkInterface Lan = new("lan", 1, "LAN", "10.20.0.2", "255.255.0.0", "");
    private static Device Target => new() { DisplayName = "PC", IPv4Address = "10.20.30.40", MacAddress = "02:11:22:33:44:55", Wake = new() { Capability = WakeCapability.EnabledUnverified } };
    [Fact]
    public void MagicPacketHasExactSynchronizationAndSixteenMacCopies()
    {
        var packet = MagicPacket.Create("02-11-22-33-44-55");
        Assert.Equal(102, packet.Length);
        Assert.All(packet.Take(6), b => Assert.Equal((byte)255, b));
        for (var i = 0; i < 16; i++) Assert.Equal(Convert.FromHexString("021122334455"), packet.Skip(6 + i * 6).Take(6));
    }
    [Theory]
    [InlineData("")]
    [InlineData("00:00:00:00:00:00")]
    [InlineData("FF:FF:FF:FF:FF:FF")]
    [InlineData("01:00:5E:00:00:01")]
    [InlineData("bad")]
    public void InvalidWakeTargetsAreRejected(string mac) => Assert.ThrowsAny<ArgumentException>(() => MagicPacket.Create(mac));
    [Fact]
    public void AutomaticBroadcastUsesRealMask()
    {
        var route = WakeRouting.Select(Target, [Lan, new("other", 2, "Other", "192.168.5.1", "255.255.255.0", "")]);
        Assert.Equal("10.20.255.255", route.BroadcastAddress);
        Assert.Equal("lan", route.Interface.Id);
        Assert.Equal(9, route.Port);
    }
    [Fact]
    public void AmbiguousOrMissingInterfacesFailInsteadOfBroadcastingEverywhere()
    {
        Assert.Throws<ArgumentException>(() => WakeRouting.Select(Target, [Lan, Lan with { Id = "other" }]));
        Assert.Throws<ArgumentException>(() => WakeRouting.Select(Target with { Wake = Target.Wake with { InterfaceId = "missing" } }, [Lan]));
        Assert.Throws<ArgumentException>(() => WakeRouting.Select(Target, []));
        Assert.Throws<ArgumentException>(() => WakeRouting.Select(Target, [Lan with { Mask = "255.255.255.254" }]));
    }
    [Fact]
    public void ExplicitInterfaceAndLimitedBroadcastAreRespected()
    {
        var route = WakeRouting.Select(Target with { Wake = Target.Wake with { InterfaceId = "lan", BroadcastAddress = "255.255.255.255", Port = 7 } }, [Lan, Lan with { Id = "other" }]);
        Assert.Equal("lan", route.Interface.Id);
        Assert.Equal("255.255.255.255", route.BroadcastAddress);
        Assert.Equal(7, route.Port);
    }
    [Fact]
    public void OffSubnetAndNonBroadcastDestinationsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => WakeRouting.Select(Target with { IPv4Address = "172.16.1.2" }, [Lan]));
        Assert.Throws<ArgumentException>(() => WakeRouting.Select(Target with { Wake = Target.Wake with { BroadcastAddress = "10.20.0.100" } }, [Lan]));
        Assert.Throws<ArgumentException>(() => WakeRouting.Select(Target with { Wake = new() }, [Lan]));
    }
}
