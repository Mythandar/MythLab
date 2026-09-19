namespace RemoteManager.Core.Credentials;

public enum AuthenticationKind { Password, PrivateKey }
/// <summary>Serializable metadata only. Secrets belong exclusively in Windows secure storage.</summary>
public sealed record CredentialReference
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string DisplayLabel { get; init; } = "";
    public string Username { get; init; } = "";
    public AuthenticationKind Authentication { get; init; }
    public string PrivateKeyPath { get; init; } = "";
}
