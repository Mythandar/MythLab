using System.Text.Json;
using Microsoft.Data.Sqlite;
using MythLab.App.Diagnostics;
using MythLab.Infrastructure.Storage;
namespace MythLab.Infrastructure.Tests;

public sealed class StartupDiagnosticsTests
{
    private const string Sensitive = "sensitive-user-input-must-not-appear";
    [Theory]
    [InlineData(StartupStage.PortableData, "portable Data directory")]
    [InlineData(StartupStage.Database, "schema/version is unsupported")]
    [InlineData(StartupStage.Settings, "settings file is invalid")]
    [InlineData(StartupStage.Initialization, "initialization could not be completed")]
    public void StageDeterminesSafeDiagnosis(StartupStage stage, string expected)
    {
        var message = StartupDiagnostics.Describe(stage, new InvalidOperationException(Sensitive));
        Assert.Contains(expected, message);
        Assert.DoesNotContain(Sensitive, message);
        Assert.DoesNotContain(nameof(InvalidOperationException), message);
    }

    [Theory]
    [InlineData(StartupStage.PortableData)]
    [InlineData(StartupStage.Initialization)]
    public void SqliteFailuresDuringImportOrInventoryLoadRemainDatabaseFailures(StartupStage stage)
    {
        var message = StartupDiagnostics.Describe(stage, new SqliteException(Sensitive, 14));
        Assert.Contains("device database", message);
        Assert.DoesNotContain(Sensitive, message);
    }

    [Fact]
    public void ArbitraryExceptionTextCannotChangeDiagnosis()
    {
        var message = StartupDiagnostics.Describe(StartupStage.Initialization,
            new IOException("Data directory unavailable; settings invalid; database cannot be opened"));
        Assert.Equal(StartupDiagnostics.Describe(StartupStage.Initialization, new IOException(Sensitive)), message);
        Assert.DoesNotContain("portable Data directory", message);
    }

    [Fact]
    public async Task InvalidSettingsGiveSettingsDiagnosisWithoutJsonDetails()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "{ " + Sensitive);
            using var settings = new SettingsStore(path);
            var error = await Assert.ThrowsAsync<JsonException>(() => settings.LoadAsync());
            var message = StartupDiagnostics.Describe(StartupStage.Settings, error);
            Assert.Contains("settings file is invalid", message);
            Assert.DoesNotContain(Sensitive, message);
            Assert.DoesNotContain(path, message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task UnsupportedSchemaGivesDatabaseDiagnosisWithoutMessageMatching()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version=999";
                await command.ExecuteNonQueryAsync();
            }
            using var repository = new SqliteDeviceRepository(path);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.InitializeAsync());
            var message = StartupDiagnostics.Describe(StartupStage.Database, error);
            Assert.Contains("schema/version is unsupported", message);
            Assert.DoesNotContain(error.Message, message);
            Assert.DoesNotContain(path, message);
        }
        finally { File.Delete(path); }
    }
}
