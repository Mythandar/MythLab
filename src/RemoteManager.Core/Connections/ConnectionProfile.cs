namespace RemoteManager.Core.Connections;

public enum ConnectionKind { Ssh, RealVnc, Moonlight, Web, LocalPowerShell, Custom }
public sealed record ConnectionProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid DeviceId { get; init; }
    public string DisplayName { get; init; } = "";
    public ConnectionKind Kind { get; init; }
    public Guid? CredentialId { get; init; }
    public int? Port { get; init; }
    public string ExecutablePath { get; init; } = "";
    public string[] Arguments { get; init; } = [];
    public string UrlTemplate { get; init; } = "";
    public string RemoteApplication { get; init; } = "";
    public bool WakeAndConnect { get; init; }
    public int? ReadinessPort { get; init; }
    public int TimeoutSeconds { get; init; } = 120;
}
