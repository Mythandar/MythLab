using RemoteManager.Infrastructure.Storage;

namespace RemoteManager.Infrastructure.Tests;

public sealed class PortableBoundaryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "RemoteManager.Boundary.Tests", Guid.NewGuid().ToString("N"));
    private AppPaths Paths => new("TestApp", directory, Path.Combine(directory, "old-appdata"));

    [Fact]
    public void AllApplicationPathsRemainUnderData()
    {
        var paths = Paths;
        foreach (var path in new[] { paths.Database, paths.Settings, paths.Logs, paths.KnownHosts, paths.WebView2UserData })
            Assert.StartsWith(paths.Root + Path.DirectorySeparatorChar, path, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(paths.WebView2UserData));
    }

    [Fact]
    public async Task WriteProbeLeavesNoFilesAndDoesNotInitializeBrowser()
    {
        var paths = Paths;
        await paths.VerifyWritableAsync();
        Assert.Empty(Directory.GetFiles(paths.Root, "*", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(paths.WebView2UserData));
        Assert.False(Directory.Exists(paths.LegacyRoot));
    }

    [Fact]
    public async Task InaccessibleLogsLocationFailsInsteadOfFallingBack()
    {
        var paths = Paths;
        Directory.CreateDirectory(paths.Root);
        await File.WriteAllTextAsync(paths.Logs, "occupied by a file");
        await Assert.ThrowsAsync<PortableDataAccessException>(() => paths.VerifyWritableAsync());
        Assert.False(Directory.Exists(paths.LegacyRoot));
        Assert.Equal("occupied by a file", await File.ReadAllTextAsync(paths.Logs));
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
