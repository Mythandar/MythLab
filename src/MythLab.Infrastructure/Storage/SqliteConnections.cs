using System.Text.Json;
using Microsoft.Data.Sqlite;
using MythLab.Core.Connections;
using MythLab.Core.Credentials;
namespace MythLab.Infrastructure.Storage;

public sealed partial class SqliteDeviceRepository
{
    public Task<IReadOnlyList<CredentialReference>> ListCredentialsAsync(CancellationToken token = default) =>
        RunAsync<IReadOnlyList<CredentialReference>>(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id,label,username,authentication,private_key_path FROM credentials ORDER BY label";
            using var reader = command.ExecuteReader();
            var result = new List<CredentialReference>();
            while (reader.Read()) result.Add(new() { Id = Guid.Parse(reader.GetString(0)), DisplayLabel = reader.GetString(1),
                Username = reader.GetString(2), Authentication = (AuthenticationKind)reader.GetInt32(3), PrivateKeyPath = reader.GetString(4) });
            return result;
        }, token);
    public Task<IReadOnlyList<ConnectionProfile>> ListProfilesAsync(CancellationToken token = default) =>
        RunAsync<IReadOnlyList<ConnectionProfile>>(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT document FROM connection_profiles ORDER BY id";
            using var reader = command.ExecuteReader();
            var result = new List<ConnectionProfile>();
            while (reader.Read()) result.Add(JsonSerializer.Deserialize<ConnectionProfile>(reader.GetString(0))
                ?? throw new InvalidDataException("Empty connection profile."));
            return result;
        }, token);
    public Task SaveCredentialAsync(CredentialReference credential, CancellationToken token = default)
    {
        if (credential.Id == Guid.Empty || string.IsNullOrWhiteSpace(credential.DisplayLabel) || credential.DisplayLabel.Length > 120 ||
            string.IsNullOrWhiteSpace(credential.Username) || credential.Username.Length > 256 ||
            !Enum.IsDefined(credential.Authentication) ||
            credential.Authentication == AuthenticationKind.PrivateKey && string.IsNullOrWhiteSpace(credential.PrivateKeyPath))
            throw new ArgumentException("Enter a label, username and (for private-key authentication) a key path.");
        return RunAsync(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO credentials(id,label,username,authentication,private_key_path) VALUES($id,$label,$user,$auth,$key)
                ON CONFLICT(id) DO UPDATE SET label=excluded.label,username=excluded.username,
                authentication=excluded.authentication,private_key_path=excluded.private_key_path
                """;
            command.Parameters.AddWithValue("$id", credential.Id.ToString());
            command.Parameters.AddWithValue("$label", credential.DisplayLabel.Trim());
            command.Parameters.AddWithValue("$user", credential.Username.Trim());
            command.Parameters.AddWithValue("$auth", (int)credential.Authentication);
            command.Parameters.AddWithValue("$key", credential.PrivateKeyPath.Trim());
            command.ExecuteNonQuery();
            return true;
        }, token);
    }
    public Task SaveProfileAsync(ConnectionProfile profile, CancellationToken token = default)
    {
        if (profile.Id == Guid.Empty || profile.DeviceId == Guid.Empty || profile.CredentialId is null ||
            profile.CredentialId == Guid.Empty || string.IsNullOrWhiteSpace(profile.DisplayName) ||
            profile.DisplayName.Length > 120 || profile.Kind != ConnectionKind.Ssh ||
            profile.Port is null or < 1 or > 65535 || profile.TimeoutSeconds is < 5 or > 300)
            throw new ArgumentException("SSH needs a name, managed device, credential, port 1–65535 and timeout 5–300 seconds.");
        return RunAsync(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO connection_profiles(id,device_id,credential_id,document) VALUES($id,$device,$credential,$document)
                ON CONFLICT(id) DO UPDATE SET device_id=excluded.device_id,credential_id=excluded.credential_id,document=excluded.document
                """;
            command.Parameters.AddWithValue("$id", profile.Id.ToString());
            command.Parameters.AddWithValue("$device", profile.DeviceId.ToString());
            command.Parameters.AddWithValue("$credential", profile.CredentialId.Value.ToString());
            command.Parameters.AddWithValue("$document", JsonSerializer.Serialize(profile with { DisplayName = profile.DisplayName.Trim() }));
            command.ExecuteNonQuery();
            return true;
        }, token);
    }
    public Task DeleteProfileAsync(Guid id, CancellationToken token = default) => DeleteMetadataAsync("connection_profiles", id, token);
    public Task DeleteCredentialAsync(Guid id, CancellationToken token = default) => DeleteMetadataAsync("credentials", id, token);
    private Task DeleteMetadataAsync(string table, Guid id, CancellationToken token) => RunAsync(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {table} WHERE id=$id"; // table is an internal constant, never user input.
        command.Parameters.AddWithValue("$id", id.ToString());
        try { command.ExecuteNonQuery(); }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        { throw new InvalidOperationException("This credential is used by a connection. Reassign or delete those profiles first."); }
        return true;
    }, token);
}
