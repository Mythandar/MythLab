using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MythLab.Core.Connections;
using MythLab.Core.Credentials;
using MythLab.Core.Devices;
using MythLab.Infrastructure.Credentials;
using MythLab.Infrastructure.Ssh;
using MythLab.Infrastructure.Storage;
namespace MythLab.Infrastructure.Tests;

public sealed class SshFoundationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "MythLab.Tests", Guid.NewGuid().ToString("N"));
    private SqliteDeviceRepository Repository(string name = "inventory.db") => new(Path.Combine(root, name));
    private static CredentialReference Credential => new() { DisplayLabel = "Linux", Username = "fixture" };
    private static Device Device => new() { DisplayName = "Server", Hostname = "fixture.invalid" };
    private static ConnectionProfile Profile(Device device, CredentialReference credential) => new()
    { DeviceId = device.Id, CredentialId = credential.Id, DisplayName = "SSH", Port = 22, TimeoutSeconds = 30 };
    [Fact]
    public async Task ReusableCredentialsAndMultipleProfilesSurviveReopen()
    {
        var credential = Credential; var device = Device;
        using (var repository = Repository())
        {
            await repository.InitializeAsync(); await repository.SaveAsync(device);
            await repository.SaveCredentialAsync(credential);
            await repository.SaveProfileAsync(Profile(device, credential));
            await repository.SaveProfileAsync(Profile(device, credential) with { DisplayName = "Alternate", Port = 2222 });
        }
        using var reopened = Repository();
        await reopened.InitializeAsync();
        Assert.Equal(2, (await reopened.ListProfilesAsync()).Count);
        Assert.Equal(credential, Assert.Single(await reopened.ListCredentialsAsync()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.DeleteCredentialAsync(credential.Id));
        await reopened.DeleteAsync(device.Id);
        Assert.Empty(await reopened.ListProfilesAsync());
        await reopened.DeleteCredentialAsync(credential.Id);
        Assert.Empty(await reopened.ListCredentialsAsync());
    }
    [Fact]
    public async Task CopiedMetadataCanRecreateMissingSecretWithoutChangingProfiles()
    {
        var credential = Credential; var device = Device; var profile = Profile(device, credential);
        using (var repository = Repository())
        {
            await repository.InitializeAsync(); await repository.SaveAsync(device);
            await repository.SaveCredentialAsync(credential); await repository.SaveProfileAsync(profile);
        }
        File.Copy(Path.Combine(root, "inventory.db"), Path.Combine(root, "copied.db"));
        using var copied = Repository("copied.db");
        await copied.InitializeAsync();
        var windowsOnOtherMachine = new FakeSecrets();
        Assert.Equal(SecretStatus.Missing, await windowsOnOtherMachine.InspectAsync(credential.Id));
        await windowsOnOtherMachine.WriteAsync(credential.Id, "dummy-test-secret");
        Assert.Equal(SecretStatus.Available, await windowsOnOtherMachine.InspectAsync(credential.Id));
        Assert.Equal(profile.Id, Assert.Single(await copied.ListProfilesAsync()).Id);
        Assert.Equal(credential.Id, Assert.Single(await copied.ListProfilesAsync()).CredentialId);
        Assert.DoesNotContain("dummy-test-secret", JsonSerializer.Serialize(await copied.ListCredentialsAsync()));
        Assert.DoesNotContain("dummy-test-secret", System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(root, "copied.db"))));
    }
    [Theory]
    [InlineData(SecretStatus.Missing)]
    [InlineData(SecretStatus.AccessDenied)]
    [InlineData(SecretStatus.StoreError)]
    public async Task MissingOrDeniedSecretsStopBeforeSshAndNeverCreateFallback(SecretStatus failure)
    {
        var credential = Credential; var device = Device;
        var secrets = new FakeSecrets { Failure = failure };
        var service = new SshSessionService(secrets, new KnownHostsStore(Path.Combine(root, "known.json")), NullLogger<SshSessionService>.Instance);
        var exception = await Assert.ThrowsAsync<CredentialStoreException>(() =>
            service.ConnectAsync(device, Profile(device, credential), credential, _ => throw new Exception("Must not contact SSH"), CancellationToken.None));
        Assert.Equal(failure, exception.Status);
        Assert.False(Directory.Exists(root));
    }
    [Theory]
    [InlineData(1168, SecretStatus.Missing)]
    [InlineData(5, SecretStatus.AccessDenied)]
    [InlineData(1312, SecretStatus.StoreError)]
    public void NativeCredentialErrorsRemainDistinct(int code, SecretStatus expected) =>
        Assert.Equal(expected, WindowsCredentialStore.Classify(code));
    [Fact]
    public async Task KnownHostsRequireExplicitTrustAndRejectChangedKeys()
    {
        var store = new KnownHostsStore(Path.Combine(root, "ssh", "known-hosts.json"));
        var host = KnownHostsStore.Identify("NAS.Local.", 22, "ssh-ed25519", [1, 2, 3]);
        Assert.Equal(HostTrust.Unknown, KnownHostsStore.Evaluate(await store.ListAsync(), host));
        await store.TrustAsync(host);
        var reopened = new KnownHostsStore(Path.Combine(root, "ssh", "known-hosts.json"));
        Assert.Equal(HostTrust.Trusted, KnownHostsStore.Evaluate(await reopened.ListAsync(), host));
        var changed = KnownHostsStore.Identify("nas.local", 22, "ssh-ed25519", [4, 5, 6]);
        Assert.Equal(HostTrust.Changed, KnownHostsStore.Evaluate(await reopened.ListAsync(), changed));
        await Assert.ThrowsAsync<HostIdentityException>(() => reopened.TrustAsync(changed));
        Assert.Equal(HostTrust.Unknown, KnownHostsStore.Evaluate(await reopened.ListAsync(), changed with { Port = 2222 }));
        await reopened.RemoveAsync(host);
        await reopened.TrustAsync(changed);
        Assert.Equal(changed, Assert.Single(await reopened.ListAsync()));
    }
    [Fact]
    public async Task CorruptKnownHostsFailClosed()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "known.json");
        await File.WriteAllTextAsync(path, "broken");
        await Assert.ThrowsAsync<JsonException>(() => new KnownHostsStore(path).ListAsync());
        foreach (var incomplete in new[] { "null", "[null]", "[{}]" })
        {
            await File.WriteAllTextAsync(path, incomplete);
            await Assert.ThrowsAsync<InvalidDataException>(() => new KnownHostsStore(path).ListAsync());
        }
    }
    [Fact]
    public async Task InvalidOrMissingProfileReferencesAreRejected()
    {
        using var repository = Repository();
        await repository.InitializeAsync();
        var credential = Credential; var device = Device;
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveProfileAsync(Profile(device, credential) with { Port = 0 }));
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => repository.SaveProfileAsync(Profile(device, credential)));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveCredentialAsync(credential with { Username = "" }));
    }
    [Fact]
    public async Task CredentialUpdateKeepsProfileAssociation()
    {
        using var repository = Repository();
        await repository.InitializeAsync();
        var credential = Credential; var device = Device;
        await repository.SaveAsync(device); await repository.SaveCredentialAsync(credential);
        var profile = Profile(device, credential); await repository.SaveProfileAsync(profile);
        await repository.SaveCredentialAsync(credential with { Username = "new-user", DisplayLabel = "New label" });
        Assert.Equal(credential.Id, Assert.Single(await repository.ListProfilesAsync()).CredentialId);
        Assert.Equal("new-user", Assert.Single(await repository.ListCredentialsAsync()).Username);
    }
    private sealed class FakeSecrets : ICredentialStore
    {
        private readonly Dictionary<Guid, string> values = [];
        public SecretStatus? Failure { get; init; }
        public Task<SecretStatus> InspectAsync(Guid id, CancellationToken token = default) =>
            Task.FromResult(Failure ?? (values.ContainsKey(id) ? SecretStatus.Available : SecretStatus.Missing));
        public Task<string?> ReadAsync(Guid id, CancellationToken token = default)
        {
            if (Failure is { } status && status != SecretStatus.Missing) throw new CredentialStoreException(status);
            return Task.FromResult(values.GetValueOrDefault(id));
        }
        public Task WriteAsync(Guid id, string secret, CancellationToken token = default) { values[id] = secret; return Task.CompletedTask; }
        public Task DeleteAsync(Guid id, CancellationToken token = default) { values.Remove(id); return Task.CompletedTask; }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
