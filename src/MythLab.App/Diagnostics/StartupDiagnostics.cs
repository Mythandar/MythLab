using Microsoft.Data.Sqlite;
namespace MythLab.App.Diagnostics;

public enum StartupStage { PortableData, Database, Settings, Initialization }

/// <summary>Fixed user-facing diagnoses; never renders exception messages, paths or stack traces.</summary>
public static class StartupDiagnostics
{
    public static string Describe(StartupStage stage, Exception error)
    {
        // Database access also occurs during legacy import and inventory loading.
        if (error is SqliteException) stage = StartupStage.Database;
        return stage switch
        {
            StartupStage.PortableData => "The portable Data directory is unavailable or not writable. Keep the application in a writable folder, outside Program Files. Check access to Data and Data/logs beside the executable. No existing data has been reset.",
            StartupStage.Database => "The device database could not be opened or read, or its schema/version is unsupported. Check access to Data/inventory.db and use an application version compatible with that database. No existing data has been reset.",
            StartupStage.Settings => "The settings file is invalid or could not be read. Check Data/settings.json and its permissions, or restore a known-good settings backup. No existing data has been reset.",
            _ => "Application initialization could not be completed. Check Data/logs, if available, for the startup stage and failure category. No existing data has been reset."
        };
    }
}
