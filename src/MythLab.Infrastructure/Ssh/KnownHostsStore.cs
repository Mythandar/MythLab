using System.Security.Cryptography;
using System.Text.Json;
namespace MythLab.Infrastructure.Ssh;

public sealed record KnownHost(string Host, int Port, string Algorithm, string PublicKey, string Fingerprint);
public enum HostTrust { Unknown, Trusted, Changed }
public sealed class HostIdentityException(string message) : Exception(message);
public sealed class KnownHostsStore(string path)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public static KnownHost Identify(string host, int port, string algorithm, byte[] key) =>
        new(host.Trim().TrimEnd('.').ToLowerInvariant(), port, algorithm, Convert.ToBase64String(key),
            "SHA256:" + Convert.ToBase64String(SHA256.HashData(key)).TrimEnd('='));
    public static HostTrust Evaluate(IEnumerable<KnownHost> known, KnownHost incoming)
    {
        var matches = known.Where(k => k.Host == incoming.Host && k.Port == incoming.Port).ToArray();
        if (matches.Length == 0) return HostTrust.Unknown;
        return matches.Any(k => k.Algorithm == incoming.Algorithm && k.PublicKey == incoming.PublicKey)
            ? HostTrust.Trusted : HostTrust.Changed;
    }
    public async Task<IReadOnlyList<KnownHost>> ListAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { return await ReadAsync(token).ConfigureAwait(false); }
        finally { gate.Release(); }
    }
    private async Task<List<KnownHost>> ReadAsync(CancellationToken token)
    {
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Known-hosts file is too large.");
        var entries = JsonSerializer.Deserialize<List<KnownHost>>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false))
            ?? throw new InvalidDataException("Known-hosts file is invalid.");
        foreach (var entry in entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Host) || string.IsNullOrWhiteSpace(entry.Algorithm) ||
                string.IsNullOrWhiteSpace(entry.PublicKey) || string.IsNullOrWhiteSpace(entry.Fingerprint))
                throw new InvalidDataException("Known-hosts record is incomplete.");
            var identified = Identify(entry.Host, entry.Port, entry.Algorithm, Convert.FromBase64String(entry.PublicKey));
            if (entry.Host != identified.Host || entry.Fingerprint != identified.Fingerprint ||
                entry.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(entry.Algorithm))
                throw new InvalidDataException("Known-hosts record is invalid.");
        }
        return entries;
    }
    public Task TrustAsync(KnownHost host, CancellationToken token = default) => UpdateAsync(entries =>
    {
        var trust = Evaluate(entries, host);
        if (trust == HostTrust.Changed) throw new HostIdentityException("SSH identity changed. Remove the old trusted host explicitly after verifying the change.");
        if (trust == HostTrust.Unknown) entries.Add(host);
    }, token);
    public Task RemoveAsync(KnownHost host, CancellationToken token = default) =>
        UpdateAsync(entries => entries.RemoveAll(h => h == host), token);
    private async Task UpdateAsync(Action<List<KnownHost>> update, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            var entries = await ReadAsync(token).ConfigureAwait(false);
            update(entries);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); gate.Release(); }
    }
}
