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
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        var token = deadline.Token;
        try
        {
            Require(await store.InspectAsync(password.Id, token) == SecretStatus.Missing, "Fresh secret must be missing");
            await store.WriteAsync(password.Id, "fixture-password", token);
            Require(await store.InspectAsync(password.Id, token) == SecretStatus.Available, "Saved secret unavailable");
            try { await using var rejected = await service.ConnectAsync(device, profile, password, _ => false, token); throw new Exception("Untrusted key accepted"); }
            catch (HostIdentityException) { }
            Require((await hosts.ListAsync(token)).Count == 0, "Rejected key was persisted");
            await using (var session = await service.ConnectAsync(device, profile with { TimeoutSeconds = 5 }, password, _ =>
            {
                // Deliberately exceed the network timeout while reviewing identity.
                Thread.Sleep(TimeSpan.FromSeconds(7));
                return true;
            }, token))
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
            await using (var burst = await service.ConnectAsync(device, profile, password, _ => throw new Exception("Known host prompted"), token))
            {
                var buffer = new byte[8191];
                var greeting = "";
                while (!greeting.Contains("ready\r\n"))
                    greeting += Encoding.UTF8.GetString(buffer, 0, await burst.ReadAsync(buffer, token));
                await burst.WriteAsync(Encoding.UTF8.GetBytes("burst\n"), token);
                await Task.Delay(300, token); // let the bounded queue fill before consuming
                var pattern = Encoding.UTF8.GetBytes("\x1b[32m\U0001F30D burst \x1b[0m\r\n");
                var total = pattern.Length * 300000;
                var received = 0;
                while (received < total)
                {
                    var count = await burst.ReadAsync(buffer, token);
                    Require(count > 0, "Burst disconnected");
                    for (var i = 0; i < count; i++) Require(buffer[i] == pattern[(received + i) % pattern.Length], "Burst bytes reordered/corrupted");
                    received += count;
                    await Task.Delay(2, token);
                }
                Require(received == total, "Burst length mismatch");
                await burst.WriteAsync(Encoding.UTF8.GetBytes("size\n"), token);
                Require(await burst.ReadAsync(buffer, token) > 0, "SSH failed after burst");
                // Leave a second burst unread: disposal must unblock the receive worker.
                await burst.WriteAsync(Encoding.UTF8.GetBytes("burst\n"), token);
                await Task.Delay(300, token);
                await burst.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3), token);
            }
            await store.WriteAsync(password.Id, "wrong-fixture-password", token);
            try { await using var bad = await service.ConnectAsync(device, profile, password, _ => throw new Exception("Known host prompted"), token); throw new Exception("Bad password accepted"); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("authentication failed")) { }
            var failedAuthHosts = new KnownHostsStore(Path.Combine(root, Guid.NewGuid().ToString("N"), "known-hosts.json"));
            var failedAuthService = new SshSessionService(store, failedAuthHosts, NullLogger<SshSessionService>.Instance);
            try { await using var bad = await failedAuthService.ConnectAsync(device, profile, password, _ => true, token); throw new Exception("Bad password accepted"); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("authentication failed")) { }
            Require((await failedAuthHosts.ListAsync(token)).Count == 0, "Approval without authentication persisted trust");
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
        Console.WriteLine("PASS: Windows credential CRUD; SSH password/encrypted-key authentication; host trust/rejection/change; shell resize; delayed host review beyond timeout; 6 MB slow-consumer burst and blocked-output disposal.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
