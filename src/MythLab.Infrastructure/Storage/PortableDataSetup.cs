using Microsoft.Data.Sqlite;

namespace MythLab.Infrastructure.Storage;

/// <summary>Copies pre-portable inventory once without modifying or deleting the source.</summary>
public static class PortableDataSetup
{
    public static Task<bool> PrepareAsync(AppPaths paths, CancellationToken cancellationToken = default) =>
        Task.Run(() => Prepare(paths, cancellationToken), cancellationToken);

    private static bool Prepare(AppPaths paths, CancellationToken cancellationToken)
    {
        // An existing Data folder always wins, including an intentionally empty one.
        if (Directory.Exists(paths.Root)) { paths.EnsureCreated(); return false; }
        var sourceDatabase = Path.Combine(paths.LegacyRoot, "inventory.db");
        var sourceSettings = Path.Combine(paths.LegacyRoot, "settings.json");
        if (!File.Exists(sourceDatabase) && !File.Exists(sourceSettings))
        {
            paths.EnsureCreated();
            return false;
        }

        var staging = paths.Root + ".import-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            if (File.Exists(sourceDatabase))
            {
                // BackupDatabase includes committed WAL content. A raw .db copy could lose recent edits.
                using var source = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = sourceDatabase, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 5
                }.ToString());
                using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(staging, "inventory.db"), Pooling = false
                }.ToString());
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(sourceSettings)) File.Copy(sourceSettings, Path.Combine(staging, "settings.json"));
            Directory.CreateDirectory(Path.Combine(staging, "logs"));
            cancellationToken.ThrowIfCancellationRequested();
            // Atomic directory rename: never replace an existing portable installation's data.
            Directory.Move(staging, paths.Root);
            return true;
        }
        finally
        {
            // Only the unique staging directory created by this invocation can be removed.
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }
}
