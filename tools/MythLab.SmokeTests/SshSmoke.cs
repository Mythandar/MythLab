using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MythLab.Core.Connections;
using MythLab.Core.Credentials;
using MythLab.Core.Devices;
using MythLab.Infrastructure.Credentials;
using MythLab.Infrastructure.Ssh;
namespace MythLab.SmokeTests;
internal static class SshSmoke
{
    public static async Task RunAsync()
    {
        var root = Path.GetFullPath("artifacts/ssh-spike");
        using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "fixture.json")));
        var store = new WindowsCredentialStore("Homelab.RemoteManager.DisposableSmoke");
        var password = new CredentialReference { Username = "fixture", DisplayLabel = "Disposable smoke" };
        var key = new CredentialReference { Username = "fixture", Authentication = AuthenticationKind.PrivateKey,
            PrivateKeyPath = fixture.RootElement.GetProperty("key").GetString()! };
        var device = new Device { IPv4Address = "127.0.0.1" };
        var profile = new ConnectionProfile { Kind = ConnectionKind.Ssh, CredentialId = password.Id,
            Port = fixture.RootElement.GetProperty("port").GetInt32(), TimeoutSeconds = 10 };
        var hosts = new KnownHostsStore(Path.Combine(root, Guid.NewGuid().ToString("N"), "known-hosts.json"));
        var service = new SshSessionService(store, hosts, NullLogger<SshSessionService>.Instance);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = deadline.Token;
        try
        {
            Require(await store.InspectAsync(password.Id, token) == SecretStatus.Missing, "Fresh secret must be missing");
            await store.WriteAsync(password.Id, "fixture-password", token);
            Require(await store.InspectAsync(password.Id, token) == SecretStatus.Available, "Saved secret unavailable");
            try { await using var rejected = await service.ConnectAsync(device, profile, password, _ => false, token); throw new Exception("Untrusted key accepted"); }
            catch (HostIdentityException) { }
            Require((await hosts.ListAsync(token)).Count == 0, "Rejected key was persisted");
            await using (var session = await service.ConnectAsync(device, profile, password, _ => true, token))
            {
                await session.ResizeAsync(132, 41, token);
                await session.WriteAsync(Encoding.UTF8.GetBytes("size\n"), token);
                var text = ""; var buffer = new byte[8192];
                while (!text.Contains("132x41"))
                {
                    var count = await session.ReadAsync(buffer, token);
                    Require(count > 0, "Shell ended before resize response");
                    text += Encoding.UTF8.GetString(buffer, 0, count);
                }
            }
            await store.WriteAsync(password.Id, "wrong-fixture-password", token);
            try { await using var bad = await service.ConnectAsync(device, profile, password, _ => throw new Exception("Known host prompted"), token); throw new Exception("Bad password accepted"); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("authentication failed")) { }
            await store.WriteAsync(key.Id, "fixture-passphrase", token);
            await using (var session = await service.ConnectAsync(device, profile with { CredentialId = key.Id }, key,
                _ => throw new Exception("Known host prompted"), token))
                Require(await session.ReadAsync(new byte[8192], token) > 0, "Private-key shell failed");
            await File.WriteAllTextAsync(Path.Combine(root, "changed"), "");
            await store.WriteAsync(password.Id, "fixture-password", token);
            try { await using var changed = await service.ConnectAsync(device, profile, password, _ => throw new Exception("Changed key prompted"), token); throw new Exception("Changed key accepted"); }
            catch (HostIdentityException) { }
        }
        finally
        {
            await store.DeleteAsync(password.Id);
            await store.DeleteAsync(key.Id);
            await File.WriteAllTextAsync(Path.Combine(root, "stop"), "");
        }
        Require(await store.InspectAsync(password.Id) == SecretStatus.Missing, "Disposable secret was not deleted");
        Console.WriteLine("PASS: Windows credential CRUD; SSH password/encrypted-key authentication; host trust/rejection/change; shell resize.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
