using System.Text.Json;
using Microsoft.Data.Sqlite;
using MythLab.Core.Devices;
using MythLab.Core.Settings;
using MythLab.Infrastructure.Storage;

namespace MythLab.Infrastructure.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "MythLab.Tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(directory, "inventory.db");

    [Fact]
    public async Task DeviceSurvivesReopenAndAddressEditThenDelete()
    {
        var device = new Device { DisplayName = "NAS", Hostname = "nas", IPv4Address = "10.0.0.2",
            MacAddress = "00:11:22:33:44:55", Group = "Servers", Notes = "Storage", Tags = ["lab"],
            Wake = new() { Capability = WakeCapability.EnabledUnverified, BroadcastAddress = "10.0.0.255" } };
        using (var repository = new SqliteDeviceRepository(Database))
        {
            await repository.InitializeAsync();
            await repository.SaveAsync(device);
        }
        using var reopened = new SqliteDeviceRepository(Database);
        await reopened.InitializeAsync();
        var saved = Assert.Single(await reopened.ListAsync());
        Assert.Equal(JsonSerializer.Serialize(device), JsonSerializer.Serialize(saved));
        await reopened.SaveAsync(saved with { IPv4Address = "10.0.0.20" });
        Assert.Equal(device.Id, Assert.Single(await reopened.ListAsync()).Id);
        Assert.Equal("10.0.0.20", Assert.Single(await reopened.ListAsync()).IPv4Address);
        await reopened.DeleteAsync(device.Id);
        Assert.Empty(await reopened.ListAsync());
    }

    [Fact]
    public async Task DuplicateRejectionDoesNotDamageExistingDevice()
    {
        using var repository = new SqliteDeviceRepository(Database);
        await repository.InitializeAsync();
        await repository.SaveAsync(new Device { DisplayName = "NAS", Hostname = "nas" });
        await Assert.ThrowsAsync<DuplicateDeviceException>(() =>
            repository.SaveAsync(new Device { DisplayName = "Duplicate", Hostname = "NAS" }));
        Assert.Equal("NAS", Assert.Single(await repository.ListAsync()).DisplayName);
    }

    [Fact]
    public async Task ConcurrentDuplicateSavesHaveOneWinner()
    {
        using var repository = new SqliteDeviceRepository(Database);
        await repository.InitializeAsync();
        async Task<bool> Save()
        {
            try { await repository.SaveAsync(new Device { DisplayName = "NAS", Hostname = "nas" }); return true; }
            catch (DuplicateDeviceException) { return false; }
        }
        var results = await Task.WhenAll(Save(), Save());
        Assert.Single(results, x => x);
        Assert.Single(await repository.ListAsync());
    }

    [Fact]
    public async Task CancelledSaveDoesNotCreateRecord()
    {
        using var repository = new SqliteDeviceRepository(Database);
        await repository.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.SaveAsync(new Device { DisplayName = "NAS", Hostname = "nas" }, cancellation.Token));
        Assert.Empty(await repository.ListAsync());
    }

    [Fact]
    public async Task SqlSpecialCharactersRemainData()
    {
        using var repository = new SqliteDeviceRepository(Database);
        await repository.InitializeAsync();
        const string name = "NAS'); DROP TABLE devices; --";
        await repository.SaveAsync(new Device { DisplayName = name, Hostname = "nas" });
        Assert.Equal(name, Assert.Single(await repository.ListAsync()).DisplayName);
    }

    [Fact]
    public async Task MigrationIsIdempotentAndRejectsNewerSchema()
    {
        using var repository = new SqliteDeviceRepository(Database);
        await repository.InitializeAsync();
        await repository.InitializeAsync();
        using (var connection = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version=2";
            command.ExecuteNonQuery();
        }
        using var newer = new SqliteDeviceRepository(Database);
        await Assert.ThrowsAsync<InvalidOperationException>(() => newer.InitializeAsync());
    }

    [Fact]
    public async Task SchemaSeparatesCredentialsAndProfilesAndEnforcesRelations()
    {
        using var repository = new SqliteDeviceRepository(Database);
        await repository.InitializeAsync();
        var device = new Device { DisplayName = "NAS", Hostname = "nas" };
        await repository.SaveAsync(device);
        using var connection = new SqliteConnection($"Data Source={Database};Foreign Keys=True;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO credentials(id,label,username,authentication) VALUES('credential','Linux','user',0);
            INSERT INTO connection_profiles(id,device_id,credential_id,document) VALUES('ssh',$device,'credential','{}');
            INSERT INTO connection_profiles(id,device_id,document) VALUES('web',$device,'{}');
            """;
        command.Parameters.AddWithValue("$device", device.Id.ToString());
        command.ExecuteNonQuery();
        command.CommandText = "DELETE FROM credentials WHERE id='credential'";
        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        await repository.DeleteAsync(device.Id);
        command.CommandText = "SELECT COUNT(*) FROM connection_profiles";
        Assert.Equal(0L, command.ExecuteScalar());
        command.CommandText = "SELECT COUNT(*) FROM credentials";
        Assert.Equal(1L, command.ExecuteScalar());
        command.CommandText = "SELECT name FROM pragma_table_info('credentials')";
        using var reader = command.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read()) columns.Add(reader.GetString(0));
        Assert.Equal(["id", "label", "username", "authentication", "private_key_path"], columns);
    }

    [Fact]
    public async Task SettingsRoundTripAndInvalidSavePreservesPreviousFile()
    {
        using var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        Assert.Equal(new AppSettings(), await store.LoadAsync());
        var expected = new AppSettings { Theme = AppTheme.Dark, PollingIntervalSeconds = 60 };
        await store.SaveAsync(expected);
        Assert.Equal(expected, await store.LoadAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(expected with { PollingIntervalSeconds = 0 }));
        Assert.Equal(expected, await store.LoadAsync());
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task MalformedSettingsAreNotSilentlyReset()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(path, "{ broken");
        using var store = new SettingsStore(path);
        await Assert.ThrowsAsync<JsonException>(() => store.LoadAsync());
        Assert.Equal("{ broken", await File.ReadAllTextAsync(path));
    }

    public void Dispose()
    {
        // A unique test-owned directory, never the user's application data.
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
