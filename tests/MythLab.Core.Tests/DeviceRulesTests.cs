using System.Text.Json;
using MythLab.Core.Connections;
using MythLab.Core.Credentials;
using MythLab.Core.Devices;
using MythLab.Core.Settings;

namespace MythLab.Core.Tests;

public sealed class DeviceRulesTests
{
    [Theory]
    [InlineData("001122aabbcc")]
    [InlineData("00-11-22-aa-bb-cc")]
    [InlineData(" 00:11:22:AA:BB:CC ")]
    public void NormalizesMac(string value) => Assert.Equal("00:11:22:AA:BB:CC", MacAddress.Normalize(value));

    [Theory]
    [InlineData("")]
    [InlineData("00:11:22:33:44")]
    [InlineData("GG1122334455")]
    [InlineData("00:11-22:33:44:55")]
    [InlineData("001:122:334:455")]
    public void RejectsMalformedMac(string value) => Assert.Throws<ArgumentException>(() => MacAddress.Normalize(value));

    [Fact]
    public void NormalizesMetadataWithoutChangingIdentity()
    {
        var id = Guid.NewGuid();
        var result = DeviceRules.NormalizeAndValidate(new Device { Id = id, DisplayName = " NAS ",
            Hostname = "nas.local.", MacAddress = "001122aabbcc", Tags = [" Lab ", "lab", "", "Storage"] });
        Assert.Equal(id, result.Id);
        Assert.Equal("NAS", result.DisplayName);
        Assert.Equal("nas.local", result.Hostname);
        Assert.Equal(["Lab", "Storage"], result.Tags);
    }

    [Theory]
    [InlineData("127.1")]
    [InlineData("192.168.0.256")]
    [InlineData("https://nas")]
    [InlineData("::1")]
    public void RejectsInvalidIPv4(string value) => Assert.Throws<DeviceValidationException>(() =>
        DeviceRules.NormalizeAndValidate(new Device { DisplayName = "NAS", IPv4Address = value }));

    [Fact]
    public void IPv4OctetsAreAlwaysDecimal() => Assert.Equal("10.0.0.1",
        DeviceRules.NormalizeAndValidate(new Device { DisplayName = "PC", IPv4Address = "010.000.000.001" }).IPv4Address);

    [Fact]
    public void RequiresEndpoint() => Assert.Throws<DeviceValidationException>(() =>
        DeviceRules.NormalizeAndValidate(new Device { DisplayName = "NAS" }));

    [Fact]
    public void WakeRequiresMac() => Assert.Throws<DeviceValidationException>(() =>
        DeviceRules.NormalizeAndValidate(new Device { DisplayName = "PC", Hostname = "pc",
            Wake = new() { Capability = WakeCapability.EnabledUnverified } }));

    [Fact]
    public void DiscoveryDoesNotImplyWakeSupport() => Assert.Equal(WakeCapability.Disabled, new Device().Wake.Capability);

    [Fact]
    public void MatchesMacDespiteDhcpChange()
    {
        var existing = new Device { MacAddress = "00:11:22:33:44:55", IPv4Address = "10.0.0.2" };
        var candidate = new Device { MacAddress = "00-11-22-33-44-55", IPv4Address = "10.0.0.3" };
        Assert.Equal("MAC address", DeviceRules.MatchReason(candidate, existing));
    }

    [Fact]
    public void MatchesHostnameCaseInsensitively() => Assert.Equal("hostname",
        DeviceRules.MatchReason(new Device { Hostname = "NAS.LOCAL." }, new Device { Hostname = "nas.local" }));

    [Fact]
    public void SameIdentityIsNotADuplicate()
    {
        var device = new Device { Hostname = "nas" };
        Assert.Null(DeviceRules.MatchReason(device with { IPv4Address = "10.0.0.99" }, device));
    }

    [Fact]
    public void CurrentIpCollisionIsReported() => Assert.Equal("IPv4 address",
        DeviceRules.MatchReason(new Device { IPv4Address = "10.0.0.2" }, new Device { IPv4Address = "10.0.0.2" }));

    [Fact]
    public void CredentialSerializationContainsOnlyAllowedMetadata()
    {
        var credential = new CredentialReference { DisplayLabel = "Linux", Username = "test",
            Authentication = AuthenticationKind.PrivateKey, PrivateKeyPath = @"C:\keys\id_ed25519" };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(credential));
        Assert.Equal(new[] { "Authentication", "DisplayLabel", "Id", "PrivateKeyPath", "Username" },
            document.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
        var profile = new ConnectionProfile { CredentialId = credential.Id, DeviceId = Guid.NewGuid() };
        using var profileJson = JsonDocument.Parse(JsonSerializer.Serialize(profile));
        Assert.Equal(credential.Id, profileJson.RootElement.GetProperty("CredentialId").GetGuid());
        Assert.Equal(new[] { "Arguments", "CredentialId", "DeviceId", "DisplayName", "ExecutablePath", "Id", "Kind",
            "Port", "ReadinessPort", "RemoteApplication", "TimeoutSeconds", "UrlTemplate", "WakeAndConnect" },
            profileJson.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.DoesNotContain(profileJson.RootElement.EnumerateObject(),
            p => p.Name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                 p.Name.Contains("passphrase", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MultipleProfilesCanReferenceOneDeviceAndCredential()
    {
        var id = Guid.NewGuid();
        var credential = Guid.NewGuid();
        var profiles = new[] {
            new ConnectionProfile { DeviceId = id, Kind = ConnectionKind.Ssh, CredentialId = credential },
            new ConnectionProfile { DeviceId = id, Kind = ConnectionKind.Web } };
        Assert.NotEqual(profiles[0].Id, profiles[1].Id);
        Assert.All(profiles, p => Assert.Equal(id, p.DeviceId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(3601)]
    public void RejectsUnsafePollingInterval(int interval) => Assert.Throws<ArgumentException>(() =>
        new AppSettings { PollingIntervalSeconds = interval }.Validate());
}
