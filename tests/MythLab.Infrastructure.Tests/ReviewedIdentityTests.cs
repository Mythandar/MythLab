using MythLab.Infrastructure.Ssh;
namespace MythLab.Infrastructure.Tests;
public sealed class ReviewedIdentityTests
{
    [Fact] public void ReconnectMustMatchReviewedIdentityEvenIfTrustFileChanged()
    {
        var reviewed = KnownHostsStore.Identify("host.local", 22, "ssh-ed25519", [1,2,3]);
        var changed = KnownHostsStore.Identify("host.local", 22, "ssh-ed25519", [4,5,6]);
        Assert.Equal(HostTrust.Changed, SshSessionService.EvaluateIdentity([changed], changed, reviewed));
        Assert.Equal(HostTrust.Unknown, SshSessionService.EvaluateIdentity([], reviewed, reviewed));
        Assert.Equal(HostTrust.Trusted, SshSessionService.EvaluateIdentity([reviewed], reviewed, reviewed));
    }
    [Fact] public void ReviewingNewIdentityDoesNotOverrideExistingChangedKey()
    {
        var old = KnownHostsStore.Identify("host.local", 22, "ssh-ed25519", [1,2,3]);
        var changed = KnownHostsStore.Identify("host.local", 22, "ssh-ed25519", [4,5,6]);
        Assert.Equal(HostTrust.Changed, SshSessionService.EvaluateIdentity([old], changed, changed));
    }
}
