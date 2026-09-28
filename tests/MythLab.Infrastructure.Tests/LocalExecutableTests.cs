using Microsoft.Extensions.Logging.Abstractions;
using MythLab.Core.Connections;
using MythLab.Infrastructure.Connections;
namespace MythLab.Infrastructure.Tests;
public sealed class LocalExecutableTests
{
    [Theory]
    [InlineData(@"\\server\share\viewer.exe")]
    [InlineData("//server/share/viewer.exe")]
    [InlineData(@"\/server/share/viewer.exe")]
    [InlineData(@"/\server/share/viewer.exe")]
    [InlineData(@"\\?\UNC\server\share\viewer.exe")]
    [InlineData(@"\\.\UNC\server\share\viewer.exe")]
    [InlineData("file://server/share/viewer.exe")]
    public async Task NetworkPathsNeverReachResolutionOrFilesystem(string path)
    {
        var probes = 0;
        var locator = new WindowsExecutableLocator
        {
            FileExists = _ => { probes++; throw new Exception("Unexpected filesystem probe"); },
            FullPath = _ => { probes++; throw new Exception("Unexpected path resolution"); }
        };
        var p = new ConnectionProfile { DeviceId = Guid.NewGuid(), DisplayName = "Fixture", Kind = ConnectionKind.Custom, ExecutablePath = path };
        Assert.Throws<ArgumentException>(() => ExternalConnections.Validate(p));
        Assert.False(locator.Locate(p).Available);
        Assert.False(locator.Locate(p with { Kind = ConnectionKind.Rdp }).Available);
        var launcher = new WindowsConnectionLauncher(NullLogger<WindowsConnectionLauncher>.Instance)
            { FileExists = _ => { probes++; throw new Exception("Unexpected launch probe"); } };
        await Assert.ThrowsAsync<ArgumentException>(() => launcher.LaunchAsync(new(path, [])));
        Assert.Equal(0, probes);
    }
}
