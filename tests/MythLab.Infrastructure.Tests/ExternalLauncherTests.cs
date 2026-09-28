using Microsoft.Extensions.Logging.Abstractions;
using MythLab.Core.Connections;
using MythLab.Core.Devices;
using MythLab.Infrastructure.Connections;
using MythLab.Infrastructure.Storage;
namespace MythLab.Infrastructure.Tests;

public sealed class ExternalLauncherTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "MythLab.ExternalTests", Guid.NewGuid().ToString("N"));
    [Fact] public void NativeStartInfoPreservesPathAndStructuredArgumentsWithoutShell()
    {
        var request = new LaunchRequest(@"C:\Program Files\Viewer\viewer.exe",
            ["--host", "host\" & calc.exe", "app name", "", @"C:\path with spaces\"]);
        var start = WindowsConnectionLauncher.CreateStartInfo(request);
        Assert.Equal(request.Executable, start.FileName);
        Assert.Equal(request.Arguments, start.ArgumentList);
        Assert.False(start.UseShellExecute); Assert.Equal("", start.Arguments); Assert.Equal("", start.Verb);
        Assert.DoesNotContain(start.Environment.Keys, k => k == "MYTHLAB_PASSWORD");
    }
    [Theory]
    [InlineData("file:///C:/test")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:secret@host/")]
    public void BrowserBoundaryRechecksSchemeAndUserInfo(string uri) =>
        Assert.Throws<ArgumentException>(() => WindowsConnectionLauncher.CreateStartInfo(new("", [], new Uri(uri))));
    [Fact] public void HttpUsesDefaultBrowserWithNoNativeArguments()
    {
        var start = WindowsConnectionLauncher.CreateStartInfo(new("", [], new Uri("https://nas.local:8080/")));
        Assert.True(start.UseShellExecute); Assert.Equal("https://nas.local:8080/", start.FileName);
        Assert.Empty(start.ArgumentList);
    }
    [Fact] public async Task MissingExecutableReportedWithoutAttemptingAProcess()
    {
        var launcher = new WindowsConnectionLauncher(NullLogger<WindowsConnectionLauncher>.Instance);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            launcher.LaunchAsync(new(Path.Combine(root, "missing.exe"), [])));
        Assert.Contains("missing", error.Message);
    }
    [Fact] public void ExplicitMissingPathDoesNotFallBackToDifferentInstalledProgram()
    {
        var profile = new ConnectionProfile { Kind = ConnectionKind.Moonlight, ExecutablePath = Path.Combine(root, "missing.exe") };
        var result = new WindowsExecutableLocator().Locate(profile);
        Assert.False(result.Available); Assert.Equal(profile.ExecutablePath, result.Path);
        Assert.Contains("Edit", result.Detail);
    }
    [Fact] public void RelativeExecutableResolvesBesideApplicationNotWorkingDirectory()
    {
        var result = new WindowsExecutableLocator().Locate(new() { Kind = ConnectionKind.Custom, ExecutablePath = "./Tools/missing.exe" });
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Tools", "missing.exe"), result.Path);
    }
    [Fact] public async Task MultiplePortableExternalProfilesSurviveCopyWithoutLocalExecutables()
    {
        Directory.CreateDirectory(root);
        var originalPath = Path.Combine(root, "original.db");
        var copiedPath = Path.Combine(root, "copied.db");
        var device = new Device { DisplayName = "Fixture", Hostname = "fixture.invalid" };
        var profiles = Enum.GetValues<ConnectionKind>().Where(k => k != ConnectionKind.Ssh).Select(kind => new ConnectionProfile
        {
            DeviceId = device.Id, DisplayName = kind.ToString(), Kind = kind, TimeoutSeconds = 5,
            ExecutablePath = kind == ConnectionKind.LocalPowerShell ? Path.Combine(root, "pwsh.exe") : Path.Combine(root, "missing viewer.exe"),
            UrlTemplate = kind == ConnectionKind.Web ? "https://{host}/" : "",
            ReadinessPort = kind == ConnectionKind.Rdp ? 3390 : null, RdpFullscreen = kind == ConnectionKind.Rdp,
            Port = kind == ConnectionKind.Rdp ? 3390 : null
        }).ToArray();
        using (var repository = new SqliteDeviceRepository(originalPath))
        {
            await repository.InitializeAsync(); await repository.SaveAsync(device);
            foreach (var profile in profiles) await repository.SaveProfileAsync(profile);
        }
        File.Copy(originalPath, copiedPath);
        using var copy = new SqliteDeviceRepository(copiedPath);
        await copy.InitializeAsync();
        var restored = await copy.ListProfilesAsync();
        Assert.Equal(6, restored.Count);
        Assert.All(restored, p => { Assert.Null(p.CredentialId); Assert.Equal(device.Id, p.DeviceId); });
        Assert.Empty(await copy.ListCredentialsAsync());
        foreach (var p in restored.Where(p => p.Kind is not (ConnectionKind.Rdp or ConnectionKind.Web)))
            Assert.False(new WindowsExecutableLocator().Locate(p).Available);
        var rdp = restored.Single(p => p.Kind == ConnectionKind.Rdp);
        Assert.Equal(3390, rdp.ReadinessPort); Assert.True(rdp.RdpFullscreen);
        var custom = restored.Single(p => p.Kind == ConnectionKind.Custom);
        await copy.SaveProfileAsync(custom with { ExecutablePath = Path.Combine(root, "new location.exe") });
        Assert.Equal(custom.Id, (await copy.ListProfilesAsync()).Single(p => p.Kind == ConnectionKind.Custom).Id);
        await copy.DeleteAsync(device.Id);
        Assert.Empty(await copy.ListProfilesAsync());
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
