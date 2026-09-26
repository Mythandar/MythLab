using MythLab.Core.Connections;
using MythLab.Core.Credentials;
namespace MythLab.Core.Tests;

public sealed class CredentialSaveTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MetadataFailureRestoresBothStores(bool existing, bool failAfterMutation)
    {
        var fixture = new Fixture(existing);
        fixture.Repository.FailSave = call => call == 1;
        fixture.Repository.FailAfter = failAfterMutation;
        var error = await Assert.ThrowsAsync<CredentialSaveException>(() => fixture.Save());
        Assert.False(error.RepairRequired);
        fixture.AssertOriginal();
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SecretFailureRestoresPriorSecretWithoutChangingMetadata(bool existing, bool failAfterMutation)
    {
        var fixture = new Fixture(existing);
        fixture.Store.FailMutation = call => call == 1;
        fixture.Store.FailAfter = failAfterMutation;
        var error = await Assert.ThrowsAsync<CredentialSaveException>(() => fixture.Save());
        Assert.False(error.RepairRequired);
        Assert.Equal(0, fixture.Repository.SaveCalls);
        fixture.AssertOriginal();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticationChangeRollsBackDeletedPassword(bool failSecret)
    {
        var fixture = new Fixture(true);
        fixture.Updated = fixture.Updated with { Authentication = AuthenticationKind.PrivateKey, PrivateKeyPath = "key" };
        if (failSecret) { fixture.Store.FailMutation = call => call == 1; fixture.Store.FailAfter = true; }
        else { fixture.Repository.FailSave = call => call == 1; fixture.Repository.FailAfter = true; }
        var error = await Assert.ThrowsAsync<CredentialSaveException>(() => fixture.Save(false));
        Assert.False(error.RepairRequired);
        fixture.AssertOriginal();
    }
    [Fact]
    public async Task SuccessfulAuthenticationChangesRemoveStaleSecretOrRequireReplacement()
    {
        var fixture = new Fixture(true);
        fixture.Updated = fixture.Updated with { Authentication = AuthenticationKind.PrivateKey, PrivateKeyPath = "key" };
        await fixture.Save(false);
        Assert.Null(fixture.Store.Value);
        fixture.Updated = fixture.Updated with { Authentication = AuthenticationKind.Password };
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Save(false));
        Assert.Equal(AuthenticationKind.PrivateKey, fixture.Repository.Value!.Authentication);
        await fixture.Save();
        Assert.Equal("new-secret", fixture.Store.Value);
        Assert.Equal(AuthenticationKind.Password, fixture.Repository.Value!.Authentication);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RollbackFailureReportsRepairAndStillAttemptsOtherStore(bool existing)
    {
        var fixture = new Fixture(existing);
        fixture.Repository.FailSave = call => call == 1;
        fixture.Repository.FailAfter = true;
        fixture.Store.FailMutation = call => call == 2;
        var error = await Assert.ThrowsAsync<CredentialSaveException>(() => fixture.Save());
        Assert.True(error.RepairRequired);
        Assert.Contains("Manual credential repair", error.Message);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("old-secret", error.ToString());
        Assert.DoesNotContain("new-secret", error.ToString());
        Assert.Equal(fixture.Original, fixture.Repository.Value);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MetadataRollbackFailureDoesNotPreventSecretRestoration(bool existing)
    {
        var fixture = new Fixture(existing);
        fixture.Repository.FailSave = _ => true;
        fixture.Repository.FailAfter = true;
        fixture.Repository.FailDelete = true;
        var error = await Assert.ThrowsAsync<CredentialSaveException>(() => fixture.Save());
        Assert.True(error.RepairRequired);
        Assert.Equal(existing ? "old-secret" : null, fixture.Store.Value);
    }
    [Fact]
    public async Task UnreadablePriorSecretPreventsAllMutation()
    {
        var fixture = new Fixture(true);
        fixture.Store.ReadDenied = true;
        await Assert.ThrowsAsync<CredentialStoreException>(() => fixture.Save());
        Assert.Equal(0, fixture.Repository.SaveCalls);
        Assert.Equal(0, fixture.Store.Mutations);
        fixture.AssertOriginal();
    }
    [Fact]
    public async Task MissingExistingSecretRemainsMissingAfterFailedReplacement()
    {
        var fixture = new Fixture(true);
        fixture.Store.Value = null;
        fixture.Repository.FailSave = call => call == 1;
        await Assert.ThrowsAsync<CredentialSaveException>(() => fixture.Save());
        Assert.Null(fixture.Store.Value);
        Assert.Equal(fixture.Original, fixture.Repository.Value);
    }
    [Fact]
    public async Task CancellationDuringMutationDoesNotInterruptCompensation()
    {
        var fixture = new Fixture(true);
        using var cancel = new CancellationTokenSource();
        fixture.Store.OnMutation = () => cancel.Cancel();
        fixture.Repository.FailSave = call => call == 1;
        var error = await Assert.ThrowsAsync<CredentialSaveException>(() => fixture.Save(token: cancel.Token));
        Assert.False(error.RepairRequired);
        fixture.AssertOriginal();
    }
    [Fact]
    public async Task MetadataOnlyEditDoesNotReadOrReplaceSecret()
    {
        var fixture = new Fixture(true);
        fixture.Store.ReadDenied = true;
        await fixture.Save(false);
        Assert.Equal("old-secret", fixture.Store.Value);
        Assert.Equal(0, fixture.Store.Mutations);
        Assert.Equal(fixture.Updated, fixture.Repository.Value);
    }
    private sealed class Fixture
    {
        public CredentialReference? Original { get; }
        public CredentialReference Updated { get; set; }
        public FakeRepository Repository { get; } = new();
        public FakeStore Store { get; } = new();
        private readonly CredentialSaveService saver;
        public Fixture(bool existing)
        {
            var value = new CredentialReference { DisplayLabel = "Original", Username = "user" };
            Original = existing ? value : null;
            Updated = value with { DisplayLabel = "Edited", Username = "new-user" };
            Repository.Value = Original;
            Store.Value = existing ? "old-secret" : null;
            saver = new(Repository, Store);
        }
        public Task Save(bool replace = true, CancellationToken token = default) => saver.SaveAsync(Updated, replace, replace ? "new-secret" : null, token);
        public void AssertOriginal()
        {
            Assert.Equal(Original, Repository.Value);
            Assert.Equal(Original is null ? null : "old-secret", Store.Value);
        }
    }
    private sealed class FakeRepository : IConnectionRepository
    {
        public CredentialReference? Value;
        public Func<int, bool> FailSave = _ => false;
        public bool FailAfter, FailDelete;
        public int SaveCalls;
        public Task<IReadOnlyList<CredentialReference>> ListCredentialsAsync(CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<CredentialReference>>(Value is null ? [] : [Value]);
        public Task SaveCredentialAsync(CredentialReference value, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var fail = FailSave(++SaveCalls);
            if (FailAfter || !fail) Value = value;
            if (fail) throw new IOException("provider detail new-secret");
            return Task.CompletedTask;
        }
        public Task DeleteCredentialAsync(Guid id, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (FailDelete) throw new IOException("provider detail");
            Value = null; return Task.CompletedTask;
        }
        public Task<IReadOnlyList<ConnectionProfile>> ListProfilesAsync(CancellationToken token = default) => throw new NotSupportedException();
        public Task SaveProfileAsync(ConnectionProfile profile, CancellationToken token = default) => throw new NotSupportedException();
        public Task DeleteProfileAsync(Guid id, CancellationToken token = default) => throw new NotSupportedException();
    }
    private sealed class FakeStore : ICredentialStore
    {
        public string? Value;
        public bool ReadDenied, FailAfter;
        public int Mutations;
        public Action? OnMutation;
        public Func<int, bool> FailMutation = _ => false;
        public Task<SecretStatus> InspectAsync(Guid id, CancellationToken token = default) => Task.FromResult(Value is null ? SecretStatus.Missing : SecretStatus.Available);
        public Task<string?> ReadAsync(Guid id, CancellationToken token = default) => ReadDenied
            ? throw new CredentialStoreException(SecretStatus.AccessDenied) : Task.FromResult(Value);
        public Task WriteAsync(Guid id, string secret, CancellationToken token = default) => Mutate(secret, token);
        public Task DeleteAsync(Guid id, CancellationToken token = default) => Mutate(null, token);
        private Task Mutate(string? value, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            OnMutation?.Invoke();
            var fail = FailMutation(++Mutations);
            if (FailAfter || !fail) Value = value;
            if (fail) throw new IOException("provider detail old-secret");
            return Task.CompletedTask;
        }
    }
}
