using CommunityToolkit.Mvvm.ComponentModel;
using RemoteManager.Core.Devices;
namespace RemoteManager.App.ViewModels;

public sealed class DeviceCardViewModel(Device device) : ObservableObject
{
    public Device Device { get; private set; } = device;
    public Guid Id => Device.Id;
    public string DisplayName => Device.DisplayName;
    public string Endpoint => Device.Endpoint;
    public string IPv4Address => Device.IPv4Address;
    public DeviceType Type => Device.Type;
    public string Group => Device.Group;
    public string Notes => Device.Notes;
    public WakeConfiguration Wake => Device.Wake;
    private DeviceState state = DeviceState.Unknown;
    public DeviceState State { get => state; set => SetProperty(ref state, value); }
    private string detail = "Awaiting a live status check.";
    public string Detail { get => detail; set => SetProperty(ref detail, value); }
    private bool isWaking;
    public bool IsWaking { get => isWaking; set { SetProperty(ref isWaking, value); OnPropertyChanged(nameof(CanWake)); } }
    public bool WakeEnabled => Wake.Capability != WakeCapability.Disabled;
    public bool CanWake => !IsWaking && Wake.Capability != WakeCapability.Disabled;
    public string LastSeen => Device.LastSeen is { } seen ? $"Last seen {seen.ToLocalTime():g}" : "Not yet seen online";
    internal int Generation { get; set; }
    public void Update(Device value)
    {
        Device = value;
        OnPropertyChanged(string.Empty);
    }
}
