using System.Text.Json;
using MythLab.Core.Connections;
using MythLab.Core.Devices;
using MythLab.Core.Monitoring;
namespace MythLab.Core.Tests;

public sealed class ExternalConnectionTests
{
    private static readonly Device Device = new() { DisplayName = "Ignored & calc.exe", Hostname = "nas.local", IPv4Address = "10.20.30.40" };
    private static ConnectionProfile Profile(ConnectionKind kind = ConnectionKind.Custom) => new()
    { DeviceId = Device.Id, DisplayName = "Fixture", Kind = kind, ExecutablePath = @"C:\Program Files\Viewer\viewer.exe", TimeoutSeconds = 5 };

    [Fact] public void TemplatesReplaceHostIpHostnameAndPortWithoutSplitting()
    {
        var p = Profile() with { Port = 1234, Arguments = ["--host", "{host}", "{ip}:{port}", "label={hostname} with spaces"] };
        var result = ExternalConnections.Build(p, Device, p.ExecutablePath);
        Assert.Equal(p.ExecutablePath, result.Executable);
        Assert.Equal(["--host", "nas.local", "10.20.30.40:1234", "label=nas.local with spaces"], result.Arguments);
    }
    [Theory]
    [InlineData("host & calc.exe")]
    [InlineData("host\" --password=anything")]
    [InlineData("$(Start-Process calc.exe); whoami")]
    [InlineData("host | cmd /c calc")]
    public void DeviceDataNeverBecomesAdditionalArguments(string host)
    {
        var p = Profile() with { Arguments = ["--target", "{host}", "literal"] };
        Assert.Throws<ArgumentException>(() => ExternalConnections.Build(p, Device with { Hostname = host }, p.ExecutablePath));
    }
    [Theory]
    [InlineData("{password}")]
    [InlineData("{passphrase}")]
    [InlineData("{credential}")]
    [InlineData("{username}")]
    [InlineData("{unknown}")]
    public void OnlyExplicitNonSecretPlaceholdersAreSupported(string template) =>
        Assert.Throws<ArgumentException>(() => ExternalConnections.Expand(template, Device, 22));
    [Theory]
    [InlineData("file:///C:/secret")]
    [InlineData("javascript:alert(1)")]
    [InlineData("vnc://host")]
    [InlineData("https://user:pass@host/")]
    [InlineData("https://user@host/")]
    public void UnsupportedSchemesAndUserInfoRejected(string url) =>
        Assert.Throws<ArgumentException>(() => ExternalConnections.Validate(Profile(ConnectionKind.Web) with { UrlTemplate = url }));
    [Theory]
    [InlineData("https://{host}/", "https://nas.local/")]
    [InlineData("http://{ip}:{port}/ui", "http://10.20.30.40:8080/ui")]
    [InlineData("https://nas.local/search?q={hostname}", "https://nas.local/search?q=nas.local")]
    public void WebTemplatesProduceValidatedUris(string template, string expected)
    {
        var p = Profile(ConnectionKind.Web) with { UrlTemplate = template, Port = 8080 };
        Assert.Equal(expected, ExternalConnections.Build(p, Device, "").WebUri!.AbsoluteUri);
    }
    [Fact] public void WebPlaceholderCannotIntroduceAuthorityOrQuerySyntax()
    {
        var p = Profile(ConnectionKind.Web) with { UrlTemplate = "https://example.invalid/search?q={host}" };
        Assert.Throws<ArgumentException>(() => ExternalConnections.WebUri(p, Device with { Hostname = "a&password=not-a-secret#fragment" }));
        Assert.Throws<ArgumentException>(() => ExternalConnections.WebUri(p with { UrlTemplate = "https://{host}/" },
            Device with { Hostname = "trusted.invalid@evil.invalid" }));
    }
    [Theory]
    [InlineData(ConnectionKind.Rdp, 3389, "/v:nas.local:3389")]
    [InlineData(ConnectionKind.RealVnc, 5900, "nas.local::5900")]
    public void BuiltinTargetsUseConfiguredPorts(ConnectionKind kind, int port, string expected)
    {
        var p = Profile(kind) with { Port = port };
        Assert.Equal(expected, ExternalConnections.Build(p, Device, p.ExecutablePath).Arguments[0]);
        Assert.Throws<ArgumentException>(() => ExternalConnections.Build(p, Device with { Hostname = "-evil /password:bad" }, p.ExecutablePath));
    }
    [Fact] public void RdpConstructionContainsOnlyNonSecretNativeOptions()
    {
        var p = Profile(ConnectionKind.Rdp) with { Port = 3390, RdpFullscreen = true };
        Assert.Equal(["/v:nas.local:3390", "/f"], ExternalConnections.Build(p, Device, p.ExecutablePath).Arguments);
        Assert.Equal(["/v:nas.local:3390", "/w:1280", "/h:720"], ExternalConnections.Build(p with { RdpFullscreen = false }, Device, p.ExecutablePath).Arguments);
        Assert.Throws<ArgumentException>(() => ExternalConnections.Validate(p with { CredentialId = Guid.NewGuid() }));
    }
    [Fact] public void MoonlightAppIsOneArgumentAndBlankOpensPicker()
    {
        var p = Profile(ConnectionKind.Moonlight) with { RemoteApplication = "Desktop Game & more" };
        Assert.Equal(["stream", "nas.local", "Desktop Game & more"], ExternalConnections.Build(p, Device, p.ExecutablePath).Arguments);
        Assert.Empty(ExternalConnections.Build(p with { RemoteApplication = "" }, Device, p.ExecutablePath).Arguments);
    }
    [Theory]
    [InlineData("cmd.exe")]
    [InlineData("pwsh.exe")]
    [InlineData("powershell.exe")]
    [InlineData("wt.exe")]
    [InlineData("wscript.exe")]
    public void CustomShellInterpretersRejected(string name) =>
        Assert.Throws<ArgumentException>(() => ExternalConnections.Validate(Profile() with { ExecutablePath = @"C:\Tools\" + name }));
    [Fact] public void LocalTerminalAllowsNoDeviceCommands()
    {
        var p = Profile(ConnectionKind.LocalPowerShell) with { ExecutablePath = @"C:\Windows\powershell.exe" };
        Assert.Equal(["-NoLogo"], ExternalConnections.Build(p, Device with { Hostname = "evil; calc" }, p.ExecutablePath).Arguments);
        Assert.Throws<ArgumentException>(() => ExternalConnections.Validate(p with { Arguments = ["-Command", "{host}"] }));
        Assert.Throws<ArgumentException>(() => ExternalConnections.Validate(p with { ReadinessPort = 22 }));
    }
    [Theory]
    [InlineData("viewer.exe")]
    [InlineData("C:\\Tools\\bad.cmd")]
    [InlineData("\"C:\\Program Files\\viewer.exe\"")]
    [InlineData("C:\\Tools\\viewer.exe --password bad")]
    [InlineData("C:\\Tools\\{host}.exe")]
    [InlineData("C:viewer.exe")]
    public void InvalidExecutableConfigurationRejected(string path) =>
        Assert.Throws<ArgumentException>(() => ExternalConnections.Validate(Profile() with { ExecutablePath = path }));
    [Theory]
    [InlineData("--password")]
    [InlineData("-passwd=secret")]
    [InlineData("Password=secret")]
    [InlineData("--token=secret")]
    [InlineData("https://user:pass@host/")]
    public void AuthenticationArgumentsRejectedBeforePersistence(string argument) =>
        Assert.Throws<ArgumentException>(() => ExternalConnections.Validate(Profile() with { Arguments = [argument] }));
    [Fact] public void MetadataHasNoSecretFieldsAndOldEnumValuesStayStable()
    {
        Assert.Equal(5, (int)ConnectionKind.Custom);
        Assert.Equal(6, (int)ConnectionKind.Rdp);
        var json = JsonSerializer.Serialize(Profile(ConnectionKind.Rdp) with { RdpFullscreen = true });
        Assert.DoesNotContain("Password", json); Assert.DoesNotContain("Passphrase", json);
        Assert.Null(JsonSerializer.Deserialize<ConnectionProfile>(json)!.CredentialId);
        var old = JsonSerializer.Deserialize<ConnectionProfile>("{\"Kind\":0,\"Port\":22}");
        Assert.Equal(ConnectionKind.Ssh, old!.Kind); Assert.False(old.RdpFullscreen);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void InvalidReadinessPortRejected(int port) =>
        Assert.Throws<ArgumentException>(() => ExternalConnections.Validate(Profile() with { ReadinessPort = port }));
    [Fact] public async Task ReadinessUsesOneBoundedTcpCheckAndNoWake()
    {
        var fake = new Fakes(); var service = new ExternalConnectionService(fake, fake, fake);
        var p = Profile() with { ReadinessPort = 4567 };
        await service.LaunchAsync(p, Device);
        Assert.Equal(StatusCheckKind.Tcp, fake.Checked!.StatusCheck);
        Assert.Equal(4567, fake.Checked.StatusPort); Assert.Equal(5000, fake.Timeout);
        Assert.Equal(1, fake.Checks); Assert.Single(fake.Launched);
        fake.State = DeviceState.Offline;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LaunchAsync(p, Device));
        Assert.Single(fake.Launched);
    }
    [Fact] public async Task DisabledReadinessAndMissingLauncherDoNotCheckNetwork()
    {
        var fake = new Fakes(); var service = new ExternalConnectionService(fake, fake, fake);
        await service.LaunchAsync(Profile(), Device);
        Assert.Equal(0, fake.Checks);
        fake.Available = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LaunchAsync(Profile() with { ReadinessPort = 22 }, Device));
        Assert.Equal(0, fake.Checks); Assert.Single(fake.Launched);
    }
    [Fact] public async Task CallerCancellationPreventsLaunch()
    {
        var fake = new Fakes(); var service = new ExternalConnectionService(fake, fake, fake);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LaunchAsync(Profile(), Device, cts.Token));
        Assert.Empty(fake.Launched); Assert.Equal(0, fake.Checks);
        fake.CancelDuringCheck = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LaunchAsync(Profile() with { ReadinessPort = 22 }, Device));
        Assert.Empty(fake.Launched);
    }
    private sealed class Fakes : IExecutableLocator, IConnectionLauncher, IDeviceStatusService
    {
        public bool Available = true, CancelDuringCheck;
        public DeviceState State = DeviceState.Online;
        public Device? Checked; public int Timeout, Checks;
        public List<LaunchRequest> Launched = [];
        public LauncherAvailability Locate(ConnectionProfile profile) => new(Available, profile.ExecutablePath, "Install or configure the viewer.");
        public Task LaunchAsync(LaunchRequest request, CancellationToken token = default) { Launched.Add(request); return Task.CompletedTask; }
        public Task<StatusObservation> CheckAsync(Device device, int timeoutMilliseconds, CancellationToken token = default)
        {
            if (CancelDuringCheck) throw new OperationCanceledException();
            Checks++; Checked = device; Timeout = timeoutMilliseconds;
            return Task.FromResult(new StatusObservation(State, DateTimeOffset.UtcNow, "Fixture check result"));
        }
    }
}
