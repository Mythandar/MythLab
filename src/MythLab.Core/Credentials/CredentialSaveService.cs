using MythLab.Core.Connections;
namespace MythLab.Core.Credentials;

public sealed class CredentialSaveException(bool repairRequired) : InvalidOperationException(repairRequired
    ? "Credential save failed and rollback could not fully restore the previous state. Manual credential repair may be required. Review this credential before connecting."
    : "Credential save failed. The previous metadata and Windows secret were restored.")
{
    public bool RepairRequired { get; } = repairRequired;
}

/// <summary>Compensating updates, not a transaction. Prior secrets exist only in memory.</summary>
public sealed class CredentialSaveService(IConnectionRepository repository, ICredentialStore secrets)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task SaveAsync(CredentialReference updated, bool replaceSecret, string? replacement,
        CancellationToken token = default)
    {
        if (updated.Id == Guid.Empty || string.IsNullOrWhiteSpace(updated.DisplayLabel) || updated.DisplayLabel.Length > 120 ||
            string.IsNullOrWhiteSpace(updated.Username) || updated.Username.Length > 256 || !Enum.IsDefined(updated.Authentication) ||
            updated.Authentication == AuthenticationKind.PrivateKey && string.IsNullOrWhiteSpace(updated.PrivateKeyPath))
            throw new ArgumentException("Enter a valid credential label, username and authentication configuration.");
        if (replaceSecret && (replacement is null || updated.Authentication == AuthenticationKind.Password && replacement.Length == 0))
            throw new ArgumentException("Enter the replacement password or key passphrase.");
        await gate.WaitAsync(token).ConfigureAwait(false);
        string? priorSecret = null;
        try
        {
            var prior = (await repository.ListCredentialsAsync(token).ConfigureAwait(false)).SingleOrDefault(c => c.Id == updated.Id);
            var changeSecret = replaceSecret || prior is not null && prior.Authentication != updated.Authentication;
            // Read before any mutation. A denied/unreadable store must not be treated as missing.
            if (changeSecret || prior is null)
                priorSecret = await secrets.ReadAsync(updated.Id, token).ConfigureAwait(false);
            if (prior is null && priorSecret is not null)
                throw new InvalidOperationException("A Windows secret already exists for this new credential identifier. Manual credential repair may be required.");
            if (updated.Authentication == AuthenticationKind.Password && !replaceSecret &&
                (prior is null || prior.Authentication != updated.Authentication ||
                 await secrets.InspectAsync(updated.Id, token).ConfigureAwait(false) != SecretStatus.Available))
                throw new ArgumentException("Enter a password and enable Save / replace.");
            token.ThrowIfCancellationRequested();
            var secretAttempted = false;
            var metadataAttempted = false;
            try
            {
                // Once mutation begins, finish or compensate without caller cancellation interrupting either store.
                if (changeSecret)
                {
                    secretAttempted = true;
                    if (replaceSecret) await secrets.WriteAsync(updated.Id, replacement!, CancellationToken.None).ConfigureAwait(false);
                    else await secrets.DeleteAsync(updated.Id, CancellationToken.None).ConfigureAwait(false);
                }
                metadataAttempted = true;
                await repository.SaveCredentialAsync(updated, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                var repairRequired = false;
                // Treat throwing writes as possibly committed. Attempt BOTH restorations independently.
                if (secretAttempted)
                {
                    try
                    {
                        if (priorSecret is null) await secrets.DeleteAsync(updated.Id, CancellationToken.None).ConfigureAwait(false);
                        else await secrets.WriteAsync(updated.Id, priorSecret, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch { repairRequired = true; }
                }
                if (metadataAttempted)
                {
                    try
                    {
                        if (prior is null) await repository.DeleteCredentialAsync(updated.Id, CancellationToken.None).ConfigureAwait(false);
                        else await repository.SaveCredentialAsync(prior, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch { repairRequired = true; }
                }
                // Do not retain underlying exceptions: a provider could put secret material in its message.
                throw new CredentialSaveException(repairRequired);
            }
        }
        finally { priorSecret = null; replacement = null; gate.Release(); }
    }
}
