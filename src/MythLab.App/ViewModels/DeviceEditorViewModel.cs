using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MythLab.Core.Devices;

namespace MythLab.App.ViewModels;

public partial class DeviceEditorViewModel : ObservableObject
{
    private readonly IDeviceRepository repository;
    private readonly ILogger logger;
    private readonly Device original;
    private readonly bool isNew;
    private readonly MythLab.Core.Discovery.ILanProbe? networks;
    public sealed record InterfaceOption(string Id, string Label);
    public System.Collections.ObjectModel.ObservableCollection<InterfaceOption> Interfaces { get; } = [];
    [ObservableProperty] private string interfaceNotice = "";
    public async Task LoadInterfacesAsync()
    {
        if (networks is null) return;
        try
        {
            var found = await networks.GetInterfacesAsync(CancellationToken.None);
            var selected = InterfaceId;
            var options = found.GroupBy(n => n.Id).Select(g => new InterfaceOption(g.Key,
                g.First().Name + " — " + string.Join(", ", g.Select(n => n.Address + "/" + n.Subnet.PrefixLength)))).ToArray();
            // Append before removing the temporary saved-ID item so selection never falls back silently.
            foreach (var option in options.Where(o => Interfaces.All(i => i.Id != o.Id))) Interfaces.Add(option);
            for (var i = 0; i < Interfaces.Count; i++)
                if (options.FirstOrDefault(o => o.Id == Interfaces[i].Id) is { } option) Interfaces[i] = option;
            InterfaceId = selected;
            if (selected.Length > 0 && options.All(o => o.Id != selected))
                InterfaceNotice = "Saved interface is unavailable. Select an active interface or Automatic.";
        }
        catch (Exception ex)
        {
            InterfaceNotice = "Could not enumerate interfaces. Saved selection was retained.";
            logger.LogWarning("Interface enumeration failed; category {FailureCategory}", ex.GetType().Name);
        }
    }
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
    [ObservableProperty] private string retryDelayMilliseconds;
    [ObservableProperty] private string delayMilliseconds;
    [ObservableProperty] private StatusCheckKind statusCheck;
    [ObservableProperty] private string statusPort;
    [ObservableProperty] private string error = "";
    [ObservableProperty] private bool isBusy;
    public Array DeviceTypes { get; } = Enum.GetValues<DeviceType>();
    public Array StatusChecks { get; } = Enum.GetValues<StatusCheckKind>();
    public string Title => (isNew || original.DisplayName.Length == 0) ? "Add device" : "Edit device";
    public event EventHandler? Saved;

    public DeviceEditorViewModel(IDeviceRepository repository, ILogger logger, Device? device = null, bool isNew = false, MythLab.Core.Discovery.ILanProbe? networks = null)
    {
        this.isNew = isNew;
        this.networks = networks;
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
        Interfaces.Add(new("", "Automatic — use the matching local subnet"));
        if (interfaceId.Length > 0) Interfaces.Add(new(interfaceId, "Saved interface: " + interfaceId));
        wakePort = original.Wake.Port.ToString();
        packetCount = original.Wake.PacketCount.ToString();
        retryCount = original.Wake.RetryCount.ToString();
        retryDelayMilliseconds = original.Wake.RetryDelayMilliseconds.ToString();
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
                !int.TryParse(StatusPort, out var checkPort) || !int.TryParse(RetryDelayMilliseconds, out var retryDelay))
                throw new DeviceValidationException("Ports, packet count, retries and delay must be whole numbers.");
            var wake = original.Wake with
            {
                Capability = WakeEnabled ? WakeCapability.EnabledUnverified : WakeCapability.Disabled,
                BroadcastAddress = BroadcastAddress, InterfaceId = InterfaceId.Trim(),
                Port = port, PacketCount = packets, RetryCount = retries, DelayMilliseconds = delay, RetryDelayMilliseconds = retryDelay
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
