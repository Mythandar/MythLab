using Microsoft.Extensions.Logging;
using MythLab.Core.Connections;
using MythLab.Core.Credentials;
using MythLab.Core.Devices;
using Renci.SshNet;
using Renci.SshNet.Common;
namespace MythLab.Infrastructure.Ssh;

public sealed class SshSessionService(ICredentialStore secrets, KnownHostsStore knownHosts, ILogger<SshSessionService> logger)
{
    internal static HostTrust EvaluateIdentity(IEnumerable<KnownHost> known, KnownHost incoming, KnownHost? reviewed) =>
        reviewed is not null && reviewed != incoming ? HostTrust.Changed : KnownHostsStore.Evaluate(known, incoming);

    private sealed class ReviewRequired(KnownHost host) : Exception
    { public KnownHost Host { get; } = host; }

    public async Task<ITerminalSession> ConnectAsync(Device device, ConnectionProfile profile, CredentialReference credential,
        Func<KnownHost, bool> confirmUnknown, CancellationToken token)
    {
        try { return await ConnectAttemptAsync(device, profile, credential, null, token).ConfigureAwait(false); }
        catch (ReviewRequired review)
        {
            // The untrusted connection is already disposed. No network/auth timer runs
            // while a human reviews identity; caller cancellation remains effective.
            var accepted = await Task.Run(() => confirmUnknown(review.Host), token).WaitAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!accepted) throw new HostIdentityException("The unknown SSH host key was not trusted.");
            return await ConnectAttemptAsync(device, profile, credential, review.Host, token).ConfigureAwait(false);
        }
    }

    private async Task<ITerminalSession> ConnectAttemptAsync(Device device, ConnectionProfile profile, CredentialReference credential,
        KnownHost? reviewed, CancellationToken token)
    {
        if (profile.Kind != ConnectionKind.Ssh || profile.CredentialId != credential.Id || profile.Port is null or < 1 or > 65535 ||
            profile.TimeoutSeconds is < 5 or > 300) throw new ArgumentException("Invalid SSH profile.");
        var known = await knownHosts.ListAsync(token).ConfigureAwait(false);
        var secret = await secrets.ReadAsync(credential.Id, token).ConfigureAwait(false);
        if (credential.Authentication == AuthenticationKind.Password && secret is null)
            throw new CredentialStoreException(SecretStatus.Missing);
        return await Task.Run(async () =>
        {
            PrivateKeyFile? key = null;
            SshClient? client = null;
            KnownHost? approved = null;
            HostTrust? rejected = null;
            KnownHost? needsReview = null;
            try
            {
                AuthenticationMethod auth;
                if (credential.Authentication == AuthenticationKind.PrivateKey)
                {
                    var keyPath = Path.GetFullPath(credential.PrivateKeyPath, AppContext.BaseDirectory);
                    key = string.IsNullOrEmpty(secret) ? new PrivateKeyFile(keyPath) : new PrivateKeyFile(keyPath, secret);
                    auth = new PrivateKeyAuthenticationMethod(credential.Username, key);
                }
                else auth = new PasswordAuthenticationMethod(credential.Username, secret!);
                secret = null;
                var info = new ConnectionInfo(device.Endpoint, profile.Port.Value, credential.Username, auth)
                { Timeout = TimeSpan.FromSeconds(profile.TimeoutSeconds) };
                client = new SshClient(info) { KeepAliveInterval = TimeSpan.FromSeconds(30) };
                client.HostKeyReceived += (_, e) =>
                {
                    var incoming = KnownHostsStore.Identify(device.Endpoint, profile.Port.Value, e.HostKeyName, e.HostKey);
                    var trust = EvaluateIdentity(known, incoming, reviewed);
                    e.CanTrust = !token.IsCancellationRequested && (trust == HostTrust.Trusted ||
                        trust == HostTrust.Unknown && reviewed == incoming);
                    if (trust == HostTrust.Unknown && reviewed is null && !token.IsCancellationRequested) needsReview = incoming;
                    if (e.CanTrust) approved = incoming;
                    else rejected = trust;
                };
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(profile.TimeoutSeconds));
                await client.ConnectAsync(deadline.Token).ConfigureAwait(false);
                if (approved is null) throw new HostIdentityException("No approved SSH identity was received.");
                await knownHosts.TrustAsync(approved, token).ConfigureAwait(false);
                known = [.. known.Where(k => k.Host != approved.Host || k.Port != approved.Port), approved];
                token.ThrowIfCancellationRequested();
                var shell = client.CreateShellStream("xterm-256color", 100, 30, 0, 0, 8192);
                var session = new SshTerminalSession(client, shell);
                client = null; // ownership transfers to the session
                logger.LogInformation("SSH connected; device {DeviceId}; profile {ProfileId}", device.Id, profile.Id);
                return (ITerminalSession)session;
            }
            catch (Exception ex)
            {
                logger.LogWarning("SSH connection failed; profile {ProfileId}; category {FailureCategory}", profile.Id, ex.GetType().Name);
                token.ThrowIfCancellationRequested();
                if (needsReview is not null) throw new ReviewRequired(needsReview);
                if (rejected is not null) throw new HostIdentityException(rejected == HostTrust.Changed
                    ? "SSH host key changed. Connection rejected. Verify the new fingerprint before removing the old trusted host."
                    : "The unknown SSH host key was not trusted.");
                if (ex is SshAuthenticationException) throw new InvalidOperationException("SSH authentication failed. Check the username and replace the saved password/passphrase or key.");
                if (ex is OperationCanceledException && !token.IsCancellationRequested) throw new TimeoutException("SSH connection timed out.");
                throw;
            }
            finally { client?.Dispose(); key?.Dispose(); secret = null; }
        }, token).ConfigureAwait(false);
    }
}
