namespace MythLab.Core.Credentials;
public enum SecretStatus { Available, Missing, AccessDenied, StoreError }
public sealed class CredentialStoreException(SecretStatus status) : Exception($"Windows credential store: {status}.")
{
    public SecretStatus Status { get; } = status;
}
/// <summary>Secrets cross this boundary only for native authentication or explicit replacement. Never serialize them.</summary>
public interface ICredentialStore
{
    Task<SecretStatus> InspectAsync(Guid id, CancellationToken token = default);
    Task<string?> ReadAsync(Guid id, CancellationToken token = default);
    Task WriteAsync(Guid id, string secret, CancellationToken token = default);
    Task DeleteAsync(Guid id, CancellationToken token = default);
}
