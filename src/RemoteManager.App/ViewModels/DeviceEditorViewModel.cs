using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteManager.Core.Devices;

namespace RemoteManager.App.ViewModels;

public partial class DeviceEditorViewModel : ObservableObject
{
    private readonly IDeviceRepository repository;
    private readonly ILogger logger;
    private readonly Device original;
    [ObservableProperty] private string displayName;
    [ObservableProperty] private string hostname;
    [ObservableProperty] private string ipAddress;
    [ObservableProperty] private string macAddress;
    [ObservableProperty] private DeviceType deviceType;
    [ObservableProperty] private string group;
    [ObservableProperty] private string notes;
    [ObservableProperty] private string tags;
    [ObservableProperty] private bool wakeEnabled;
    [ObservableProperty] private string broadcastAddress;
    [ObservableProperty] private string interfaceId;
    [ObservableProperty] private string wakePort;
    [ObservableProperty] private string packetCount;
    [ObservableProperty] private string retryCount;
    [ObservableProperty] private string delayMilliseconds;
    [ObservableProperty] private StatusCheckKind statusCheck;
    [ObservableProperty] private string statusPort;
    [ObservableProperty] private string error = "";
    [ObservableProperty] private bool isBusy;
    public Array DeviceTypes { get; } = Enum.GetValues<DeviceType>();
    public Array StatusChecks { get; } = Enum.GetValues<StatusCheckKind>();
    public string Title => original.DisplayName.Length == 0 ? "Add device" : "Edit device";
    public event EventHandler? Saved;

    public DeviceEditorViewModel(IDeviceRepository repository, ILogger logger, Device? device = null)
    {
        this.repository = repository;
        this.logger = logger;
        original = device ?? new Device();
        displayName = original.DisplayName;
        hostname = original.Hostname;
        ipAddress = original.IPv4Address;
        macAddress = original.MacAddress;
        deviceType = original.Type;
        group = original.Group;
        notes = original.Notes;
        tags = string.Join(", ", original.Tags);
        wakeEnabled = original.Wake.Capability != WakeCapability.Disabled;
        broadcastAddress = original.Wake.BroadcastAddress;
        interfaceId = original.Wake.InterfaceId;
        wakePort = original.Wake.Port.ToString();
        packetCount = original.Wake.PacketCount.ToString();
        retryCount = original.Wake.RetryCount.ToString();
        delayMilliseconds = original.Wake.DelayMilliseconds.ToString();
        statusCheck = original.StatusCheck;
        statusPort = original.StatusPort.ToString();
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        Error = "";
        IsBusy = true;
        try
        {
            if (!int.TryParse(WakePort, out var port) || !int.TryParse(PacketCount, out var packets) ||
                !int.TryParse(RetryCount, out var retries) || !int.TryParse(DelayMilliseconds, out var delay) ||
                !int.TryParse(StatusPort, out var checkPort))
                throw new DeviceValidationException("Ports, packet count, retries and delay must be whole numbers.");
            var wake = original.Wake with
            {
                Capability = WakeEnabled ? WakeCapability.EnabledUnverified : WakeCapability.Disabled,
                BroadcastAddress = BroadcastAddress, InterfaceId = InterfaceId.Trim(),
                Port = port, PacketCount = packets, RetryCount = retries, DelayMilliseconds = delay
            };
            // Preserve verified status only if all settings and the target MAC are unchanged.
            if (original.Wake.Capability == WakeCapability.Verified && WakeEnabled &&
                wake with { Capability = WakeCapability.Verified } == original.Wake &&
                Core.Devices.MacAddress.Normalize(MacAddress) == original.MacAddress)
                wake = wake with { Capability = WakeCapability.Verified };
            var updated = DeviceRules.NormalizeAndValidate(original with
            {
                DisplayName = DisplayName, Hostname = Hostname, IPv4Address = IpAddress, MacAddress = MacAddress,
                Type = DeviceType, Group = Group, Notes = Notes, Tags = Tags.Split(','),
                Wake = wake, StatusCheck = StatusCheck, StatusPort = checkPort
            });
            await repository.SaveAsync(updated, cancellationToken);
            logger.LogInformation("Device saved {DeviceId}", updated.Id);
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is DeviceValidationException or DuplicateDeviceException or ArgumentException)
        { Error = ex.Message; }
        catch (OperationCanceledException) { Error = "Save cancelled."; }
        catch (Exception ex)
        {
            logger.LogError("Device save failed; category {FailureCategory}", ex.GetType().Name);
            Error = "Could not save the device. Check the data folder and diagnostics, then retry.";
        }
        finally { IsBusy = false; }
    }
}
