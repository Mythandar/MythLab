using System.Text.Json;
using Microsoft.Data.Sqlite;
using RemoteManager.Core.Devices;

namespace RemoteManager.Infrastructure.Storage;

public sealed class SqliteDeviceRepository(string databasePath) : IDeviceRepository, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool initialized;
    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = databasePath, ForeignKeys = true, Pooling = false, DefaultTimeout = 5
    }.ToString();

    public Task InitializeAsync(CancellationToken cancellationToken = default) => RunAsync(connection =>
    {
        using var version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        var current = Convert.ToInt32(version.ExecuteScalar());
        if (current > 1) throw new InvalidOperationException("The database was created by a newer application version.");
        using var wal = connection.CreateCommand();
        wal.CommandText = "PRAGMA journal_mode=WAL";
        wal.ExecuteNonQuery();
        if (current == 0)
        {
            using var transaction = connection.BeginTransaction();
            using var schema = connection.CreateCommand();
            schema.Transaction = transaction;
            schema.CommandText = """
                CREATE TABLE devices (
                    id TEXT PRIMARY KEY NOT NULL,
                    name TEXT NOT NULL,
                    hostname TEXT NOT NULL COLLATE NOCASE,
                    ipv4 TEXT NOT NULL,
                    mac TEXT NOT NULL,
                    document TEXT NOT NULL CHECK(json_valid(document))
                );
                CREATE UNIQUE INDEX ux_device_mac ON devices(mac) WHERE mac <> '';
                CREATE UNIQUE INDEX ux_device_host ON devices(hostname) WHERE hostname <> '';
                CREATE UNIQUE INDEX ux_device_ip ON devices(ipv4) WHERE ipv4 <> '';
                CREATE TABLE credentials (
                    id TEXT PRIMARY KEY NOT NULL,
                    label TEXT NOT NULL,
                    username TEXT NOT NULL,
                    authentication INTEGER NOT NULL,
                    private_key_path TEXT NOT NULL DEFAULT ''
                );
                CREATE TABLE connection_profiles (
                    id TEXT PRIMARY KEY NOT NULL,
                    device_id TEXT NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
                    credential_id TEXT REFERENCES credentials(id) ON DELETE RESTRICT,
                    document TEXT NOT NULL CHECK(json_valid(document))
                );
                CREATE INDEX ix_profiles_device ON connection_profiles(device_id);
                PRAGMA user_version=1;
                """;
            schema.ExecuteNonQuery();
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
        }
        initialized = true;
        return true;
    }, cancellationToken, requireInitialized: false);

    public Task<IReadOnlyList<Device>> ListAsync(CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<Device>>(connection => ReadDevices(connection), cancellationToken);

    public Task SaveAsync(Device device, CancellationToken cancellationToken = default)
    {
        var normalized = DeviceRules.NormalizeAndValidate(device);
        return RunAsync(connection =>
        {
            // BeginTransaction uses an immediate SQLite transaction; duplicate check and write are atomic.
            using var transaction = connection.BeginTransaction();
            foreach (var existing in ReadDevices(connection, transaction))
            {
                var reason = DeviceRules.MatchReason(normalized, existing);
                if (reason is not null)
                    throw new DuplicateDeviceException($"Another managed device already uses this {reason}. Edit that device if its address changed.");
            }
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO devices(id,name,hostname,ipv4,mac,document) VALUES($id,$name,$host,$ip,$mac,$document)
                ON CONFLICT(id) DO UPDATE SET name=excluded.name,hostname=excluded.hostname,
                ipv4=excluded.ipv4,mac=excluded.mac,document=excluded.document;
                """;
            command.Parameters.AddWithValue("$id", normalized.Id.ToString());
            command.Parameters.AddWithValue("$name", normalized.DisplayName);
            command.Parameters.AddWithValue("$host", normalized.Hostname);
            command.Parameters.AddWithValue("$ip", normalized.IPv4Address);
            command.Parameters.AddWithValue("$mac", normalized.MacAddress);
            command.Parameters.AddWithValue("$document", JsonSerializer.Serialize(normalized));
            command.ExecuteNonQuery();
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return true;
        }, cancellationToken);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => RunAsync(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM devices WHERE id=$id";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.ExecuteNonQuery();
        return true;
    }, cancellationToken);

    private static List<Device> ReadDevices(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT document FROM devices ORDER BY name COLLATE NOCASE, id";
        using var reader = command.ExecuteReader();
        var devices = new List<Device>();
        while (reader.Read())
            devices.Add(JsonSerializer.Deserialize<Device>(reader.GetString(0))
                ?? throw new InvalidDataException("Device record is empty."));
        return devices;
    }

    private async Task<T> RunAsync<T>(Func<SqliteConnection, T> action, CancellationToken cancellationToken,
        bool requireInitialized = true)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (requireInitialized && !initialized) throw new InvalidOperationException("Initialize the repository first.");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();
                cancellationToken.ThrowIfCancellationRequested();
                return action(connection);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public void Dispose() => gate.Dispose();
}
