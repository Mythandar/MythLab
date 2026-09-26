using MythLab.Core.Credentials;
namespace MythLab.Core.Connections;
public interface IConnectionRepository
{
    Task<IReadOnlyList<ConnectionProfile>> ListProfilesAsync(CancellationToken token = default);
    Task<IReadOnlyList<CredentialReference>> ListCredentialsAsync(CancellationToken token = default);
    Task SaveProfileAsync(ConnectionProfile profile, CancellationToken token = default);
    Task SaveCredentialAsync(CredentialReference credential, CancellationToken token = default);
    Task DeleteProfileAsync(Guid id, CancellationToken token = default);
    Task DeleteCredentialAsync(Guid id, CancellationToken token = default);
}
