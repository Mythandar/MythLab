using MythLab.Core.Connections;
using MythLab.Core.Devices;
namespace MythLab.Core.Tests;
public sealed class ExternalReviewTests
{
    private static ConnectionProfile Profile => new() { DeviceId = Guid.NewGuid(), DisplayName = "Fixture", Kind = ConnectionKind.Custom, ExecutablePath = "./Tools/viewer.exe" };
    [Theory]
    [InlineData("--host\n{host}\n")]
    [InlineData("\n--host\r\n\r\n{host}\r\n  \r\n")]
    public void BlankArgumentLinesAreIgnored(string text) =>
        Assert.Equal(["--host", "{host}"], ExternalConnections.ParseArgumentLines(text));
    [Fact] public void NonEmptyWhitespaceStaysWithinItsArgument() =>
        Assert.Equal(["  literal value  "], ExternalConnections.ParseArgumentLines("  literal value  \n"));
    [Fact] public void EmptyArgumentsCannotBePersisted() =>
        Assert.Throws<ArgumentException>(() => ExternalConnections.Validate(Profile with { Arguments = [""] }));
    [Fact] public void MissingPortRejectedBeforeSave()
    {
        Assert.Throws<ArgumentException>(() => ExternalConnections.Validate(Profile with { Arguments = ["{port}"] }));
        ExternalConnections.Validate(Profile with { Arguments = ["{port}"], Port = 1234 });
        ExternalConnections.Validate(Profile with { Arguments = ["{host}"] });
    }
    [Theory]
    [InlineData(ConnectionKind.RealVnc, 5900)]
    [InlineData(ConnectionKind.Rdp, 3389)]
    public void BuiltinPortsRemainAvailable(ConnectionKind kind, int expected)
    {
        var p = Profile with { Kind = kind, Arguments = kind == ConnectionKind.RealVnc ? ["{port}"] : [] };
        ExternalConnections.Validate(p);
        var request = ExternalConnections.Build(p, new Device { Hostname = "nas.local" }, p.ExecutablePath);
        Assert.Contains(expected.ToString(), request.Arguments[0]);
    }
    [Theory]
    [InlineData("-option")]
    [InlineData("good.local --option")]
    [InlineData("bad;command")]
    [InlineData("bad\nhost")]
    [InlineData("192.168.001.1")]
    [InlineData("a..local")]
    public void NetworkPlaceholdersRejectInvalidValues(string host) =>
        Assert.Throws<ArgumentException>(() => ExternalConnections.Expand("{host}", new Device { Hostname = host, IPv4Address = "10.0.0.1" }, null));
    [Theory]
    [InlineData("{hostname}", "host.local")]
    [InlineData("{host}", "host.local")]
    [InlineData("{ip}", "10.0.0.1")]
    public void ValidNetworkIdentitiesStayData(string template, string expected) =>
        Assert.Equal(expected, ExternalConnections.Expand(template, new Device { Hostname = "host.local", IPv4Address = "10.0.0.1" }, null));
    [Fact] public void InvalidHostnameDoesNotFallBackToSavedIp() =>
        Assert.Throws<ArgumentException>(() => ExternalConnections.Expand("{host}", new Device { Hostname = "-bad", IPv4Address = "10.0.0.1" }, null));
}
