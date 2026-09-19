namespace RemoteManager.Core.Devices;

public enum DeviceState { Unknown, Online, Offline, Waking }
public enum DeviceType { Computer, Server, VirtualMachine, Appliance, Other }
public enum WakeCapability { Disabled, EnabledUnverified, Verified }
public enum StatusCheckKind { Icmp, Tcp }

public sealed record WakeConfiguration
{
    public WakeCapability Capability { get; init; }
    public string BroadcastAddress { get; init; } = "";
    public string InterfaceId { get; init; } = "";
    public int Port { get; init; } = 9;
    public int PacketCount { get; init; } = 3;
    public int DelayMilliseconds { get; init; } = 250;
    public int RetryCount { get; init; } = 1;
}

public sealed record Device
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string DisplayName { get; init; } = "";
    public string Hostname { get; init; } = "";
    public string IPv4Address { get; init; } = "";
    public string MacAddress { get; init; } = "";
    public DeviceType Type { get; init; }
    public string Group { get; init; } = "";
    public string Notes { get; init; } = "";
    public string[] Tags { get; init; } = [];
    public WakeConfiguration Wake { get; init; } = new();
    public DeviceState LastKnownState { get; init; }
    public DateTimeOffset? LastSeen { get; init; }
    public StatusCheckKind StatusCheck { get; init; }
    public int StatusPort { get; init; } = 22;
    public string Endpoint => string.IsNullOrWhiteSpace(Hostname) ? IPv4Address : Hostname;
}
