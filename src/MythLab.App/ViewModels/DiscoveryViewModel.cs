using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MythLab.App.Views;
using MythLab.Core.Devices;
using MythLab.Core.Discovery;
using MythLab.Infrastructure.Storage;

namespace MythLab.App.ViewModels;

public partial class DiscoveryItemViewModel(DiscoveredDevice device) : ObservableObject
{
    [ObservableProperty] private DiscoveredDevice device = device;
    [ObservableProperty] private string managedLabel = "Not managed";
    [ObservableProperty] private bool isManaged;
}

public partial class DiscoveryViewModel(INetworkDiscoveryService discovery, IDeviceRepository repository,
    SettingsStore settingsStore, DeviceDialogs dialogs, ILogger<DiscoveryViewModel> logger) : ObservableObject
{
    private IReadOnlyList<Device> managed = [];
    private readonly Dictionary<string, DiscoveryItemViewModel> byAddress = [];
    private long scanGeneration;
    public ObservableCollection<LocalNetworkInterface> Interfaces { get; } = [];
    public ObservableCollection<DiscoveryItemViewModel> Results { get; } = [];
    [ObservableProperty] private LocalNetworkInterface? selectedInterface;
    [ObservableProperty] private DiscoveryItemViewModel? selectedResult;
    [ObservableProperty] private string startAddress = "";
    [ObservableProperty] private string endAddress = "";
    [ObservableProperty] private string message = "Choose a network interface and scan for devices.";
    [ObservableProperty] private string diagnostic = "";
    [ObservableProperty] private int completed;
    [ObservableProperty] private int total = 1;
    [ObservableProperty] private bool isScanning;
    [ObservableProperty] private bool isBusy;
    public string ResultSummary => $"{Results.Count} discovered";
    public bool CanConfigure => !IsScanning && !IsBusy;
    private bool CanScan() => CanConfigure && SelectedInterface is not null;
    private bool CanRefresh() => CanConfigure;
    private bool CanAdd() => CanConfigure && SelectedResult is { IsManaged: false };
    public event EventHandler? DeviceAdded;

    partial void OnSelectedInterfaceChanged(LocalNetworkInterface? value)
    {
        ScanCommand.NotifyCanExecuteChanged();
        if (value is null) return;
        var subnet = value.Subnet;
        if (subnet.HostCount <= DiscoveryRequest.MaximumAddresses)
        {
            StartAddress = Ipv4Subnet.Format(subnet.FirstHost);
            EndAddress = Ipv4Subnet.Format(subnet.LastHost);
            Message = $"Selected {subnet.Cidr}. Review the range, then scan.";
        }
        else
        {
            StartAddress = value.Address;
            EndAddress = value.Address;
            Message = $"This subnet has {subnet.HostCount:N0} addresses. Enter a range of at most {DiscoveryRequest.MaximumAddresses:N0}; no full-subnet scan is started automatically.";
        }
    }
    partial void OnSelectedResultChanged(DiscoveryItemViewModel? value) => AddSelectedCommand.NotifyCanExecuteChanged();
    partial void OnIsScanningChanged(bool value) => UpdateCommands();
    partial void OnIsBusyChanged(bool value) => UpdateCommands();
    private void UpdateCommands()
    {
        OnPropertyChanged(nameof(CanConfigure));
        ScanCommand.NotifyCanExecuteChanged();
        RefreshInterfacesCommand.NotifyCanExecuteChanged();
        AddSelectedCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshInterfacesAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var previous = SelectedInterface;
            var interfaces = await discovery.GetInterfacesAsync(cancellationToken);
            Interfaces.Clear();
            foreach (var network in interfaces) Interfaces.Add(network);
            SelectedInterface = interfaces.FirstOrDefault(i => i.Id == previous?.Id && i.Address == previous.Address)
                ?? interfaces.FirstOrDefault();
            if (interfaces.Count == 0) Message = "No active IPv4 interface was found. Connect to your LAN and refresh.";
            await RefreshManagedAsync(cancellationToken);
        }
        catch (OperationCanceledException) { Message = "Interface refresh cancelled."; }
        catch (Exception ex)
        {
            logger.LogError("Interface enumeration failed; category {FailureCategory}", ex.GetType().Name);
            Message = "Could not read Windows network interfaces. Check Diagnostics and retry.";
        }
        finally { IsBusy = false; }
    }

    public async Task RefreshManagedAsync(CancellationToken cancellationToken = default)
    {
        managed = await repository.ListAsync(cancellationToken);
        foreach (var row in Results) Match(row);
        AddSelectedCommand.NotifyCanExecuteChanged();
    }

    private void Match(DiscoveryItemViewModel row)
    {
        var candidate = row.Device.ToDevice();
        var match = managed.Select(d => new { Device = d, Reason = DeviceRules.MatchReason(candidate, d) })
            .FirstOrDefault(m => m.Reason is not null);
        row.IsManaged = match is not null;
        row.ManagedLabel = match is null ? "Not managed" : $"Managed: {match.Device.DisplayName} ({match.Reason})";
    }

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanScan))]
    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        if (SelectedInterface is null) return;
        IsScanning = true;
        var generation = ++scanGeneration;
        try
        {
            var settings = await settingsStore.LoadAsync(cancellationToken);
            var request = new DiscoveryRequest(SelectedInterface, StartAddress, EndAddress,
                settings.DiscoveryConcurrency, settings.ProbeTimeoutMilliseconds);
            request.Validate();
            await RefreshManagedAsync(cancellationToken);
            Results.Clear();
            byAddress.Clear();
            SelectedResult = null;
            Completed = 0;
            Diagnostic = "";
            OnPropertyChanged(nameof(ResultSummary));
            Message = "Scanning. Devices can appear even if ping is blocked.";
            var progress = new Progress<DiscoveryUpdate>(update =>
            {
                if (!IsScanning || scanGeneration != generation || cancellationToken.IsCancellationRequested) return;
                Completed = Math.Max(Completed, update.Completed);
                Total = update.Total;
                if (update.Diagnostic is not null) Diagnostic = update.Diagnostic;
                if (update.Device is not { } found) return;
                if (!byAddress.TryGetValue(found.Address, out var row))
                {
                    row = new(found);
                    byAddress.Add(found.Address, row);
                    // Keep numeric address order as asynchronously discovered devices arrive.
                    var position = 0;
                    var ip = Ipv4Subnet.Parse(found.Address);
                    while (position < Results.Count && Ipv4Subnet.Parse(Results[position].Device.Address) < ip) position++;
                    Results.Insert(position, row);
                    OnPropertyChanged(nameof(ResultSummary));
                }
                else row.Device = found;
                Match(row);
            });
            var summary = await discovery.ScanAsync(request, progress, cancellationToken);
            Completed = summary.Scanned;
            Message = $"Scan complete: {summary.Found} devices discovered across {summary.Scanned} addresses.";
        }
        catch (OperationCanceledException) { Message = $"Scan cancelled. Kept {Results.Count} discoveries; remaining hosts were not checked."; }
        catch (ArgumentException ex) { Message = ex.Message; }
        catch (InvalidOperationException ex) { Message = ex.Message; }
        catch (Exception ex)
        {
            logger.LogError("Discovery failed; category {FailureCategory}", ex.GetType().Name);
            Message = "Discovery could not finish. Partial results were kept. Check Diagnostics and refresh interfaces.";
        }
        finally { IsScanning = false; }
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddSelectedAsync()
    {
        if (SelectedResult is not { } row) return;
        IsBusy = true;
        try
        {
            // Recheck persistent inventory in case it changed while the scan was running.
            await RefreshManagedAsync();
            if (row.IsManaged) { Message = "This address, hostname or MAC already matches a managed device. Edit it from My Devices."; return; }
            if (dialogs.AddDiscovered(row.Device))
            {
                await RefreshManagedAsync();
                DeviceAdded?.Invoke(this, EventArgs.Empty);
                Message = "Device added to My Devices. Wake-on-LAN remains disabled unless you explicitly enabled it.";
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Adding discovery failed; category {FailureCategory}", ex.GetType().Name);
            Message = "Could not finish adding the device. Check Diagnostics, then refresh My Devices.";
        }
        finally { IsBusy = false; }
    }
}
