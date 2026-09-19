using Microsoft.Data.Sqlite;
using RemoteManager.Core.Devices;
using RemoteManager.Core.Settings;
using RemoteManager.Infrastructure.Storage;

namespace RemoteManager.Infrastructure.Tests;

public sealed class PortableDataTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "RemoteManager.Portable.Tests", Guid.NewGuid().ToString("N"));
    private AppPaths Paths => new("TestApp", Path.Combine(directory, "application"), Path.Combine(directory, "legacy"));

    [Fact]
    public void DataBelongsToExecutableDirectory()
    {
        var paths = Paths;
        Assert.Equal(Path.Combine(directory, "application", "Data"), paths.Root);
        Assert.Equal(Path.Combine(paths.Root, "inventory.db"), paths.Database);
        Assert.Equal(Path.Combine(paths.Root, "settings.json"), paths.Settings);
        Assert.Equal(Path.Combine(paths.Root, "logs"), paths.Logs);
    }

    [Fact]
    public async Task FreshSetupCreatesOnlyPortableData()
    {
        var paths = Paths;
        Assert.False(await PortableDataSetup.PrepareAsync(paths));
        Assert.True(Directory.Exists(paths.Logs));
        Assert.False(Directory.Exists(paths.LegacyRoot));
    }

    [Fact]
    public async Task LegacyImportIncludesWalAndSettingsAndDoesNotRepeat()
    {
        var paths = Paths;
        var database = Path.Combine(paths.LegacyRoot, "inventory.db");
        using var oldRepository = new SqliteDeviceRepository(database);
        await oldRepository.InitializeAsync();
        // Keep a connection open with auto-checkpointing disabled so committed records remain in the WAL.
        using var keepAlive = new SqliteConnection($"Data Source={database};Pooling=False");
        keepAlive.Open();
        using (var command = keepAlive.CreateCommand())
        {
            command.CommandText = "PRAGMA wal_autocheckpoint=0";
            command.ExecuteNonQuery();
        }
        var device = new Device { DisplayName = "Legacy NAS", Hostname = "nas" };
        await oldRepository.SaveAsync(device);
        using (var command = keepAlive.CreateCommand())
        {
            command.CommandText = "UPDATE devices SET name='Legacy NAS (WAL)', document=json_set(document,'$.DisplayName','Legacy NAS (WAL)')";
            command.ExecuteNonQuery();
        }
        Assert.True(File.Exists(database + "-wal"));
        using var oldSettings = new SettingsStore(Path.Combine(paths.LegacyRoot, "settings.json"));
        var preferences = new AppSettings { Theme = AppTheme.Dark, PollingIntervalSeconds = 90 };
        await oldSettings.SaveAsync(preferences);
        Assert.True(await PortableDataSetup.PrepareAsync(paths));
        using var portable = new SqliteDeviceRepository(paths.Database);
        await portable.InitializeAsync();
        Assert.Equal(device.Id, Assert.Single(await portable.ListAsync()).Id);
        Assert.Equal("Legacy NAS (WAL)", Assert.Single(await portable.ListAsync()).DisplayName);
        using var settings = new SettingsStore(paths.Settings);
        Assert.Equal(preferences, await settings.LoadAsync());
        await portable.DeleteAsync(device.Id);
        Assert.False(await PortableDataSetup.PrepareAsync(paths));
        Assert.Empty(await portable.ListAsync());
        Assert.Equal(device.Id, Assert.Single(await oldRepository.ListAsync()).Id);
    }

    [Fact]
    public async Task ExistingPortableFolderNeverImportsLegacyData()
    {
        var paths = Paths;
        Directory.CreateDirectory(paths.LegacyRoot);
        await File.WriteAllTextAsync(Path.Combine(paths.LegacyRoot, "settings.json"), "legacy");
        paths.EnsureCreated();
        Assert.False(await PortableDataSetup.PrepareAsync(paths));
        Assert.False(File.Exists(paths.Settings));
    }

    [Fact]
    public async Task FailedImportPreservesSourceAndDoesNotLeavePartialData()
    {
        var paths = Paths;
        Directory.CreateDirectory(paths.LegacyRoot);
        var database = Path.Combine(paths.LegacyRoot, "inventory.db");
        await File.WriteAllTextAsync(database, "not a SQLite database");
        await Assert.ThrowsAsync<SqliteException>(() => PortableDataSetup.PrepareAsync(paths));
        Assert.False(Directory.Exists(paths.Root));
        Assert.Equal("not a SQLite database", await File.ReadAllTextAsync(database));
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(paths.Root)!, "Data.import-*"));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
