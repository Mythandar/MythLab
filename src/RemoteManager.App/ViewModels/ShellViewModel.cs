using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteManager.App.Diagnostics;
using RemoteManager.App.Views;
using RemoteManager.Core.Devices;
using RemoteManager.Core.Settings;
using RemoteManager.Infrastructure.Storage;

namespace RemoteManager.App.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly IDeviceRepository repository;
    private readonly SettingsStore settingsStore;
    private readonly RecentLogSink recent;
    private readonly ILogger<ShellViewModel> logger;
    private readonly DeviceDialogs dialogs;
    private AppSettings settings = new();
    public ObservableCollection<Device> Devices { get; } = [];
    public ICollectionView FilteredDevices { get; }
    public Array Themes { get; } = Enum.GetValues<AppTheme>();
    public string DisplayName => AppIdentity.DisplayName;
    public string DataDirectory { get; }
    public DiscoveryViewModel Discovery { get; }
    [ObservableProperty] private string search = "";
    [ObservableProperty] private string notice = "Add a computer, server or appliance to begin.";
    [ObservableProperty] private string diagnostics = "";
    [ObservableProperty] private AppTheme theme;
    [ObservableProperty] private string pollingInterval = "30";
    [ObservableProperty] private string discoveryConcurrency = "32";
    [ObservableProperty] private string probeTimeout = "750";
    [ObservableProperty] private bool isBusy;
    public string InventorySummary => $"{Devices.Count} managed device{(Devices.Count == 1 ? "" : "s")}";
    public bool IsEmpty => Devices.Count == 0;

    public ShellViewModel(IDeviceRepository repository, SettingsStore settingsStore, RecentLogSink recent,
        ILogger<ShellViewModel> logger, AppPaths paths, DeviceDialogs dialogs, DiscoveryViewModel discovery)
    {
        Discovery = discovery;
        Discovery.DeviceAdded += OnDiscoveredDeviceAdded;
        this.repository = repository;
        this.settingsStore = settingsStore;
        this.recent = recent;
        this.logger = logger;
        this.dialogs = dialogs;
        DataDirectory = paths.Root;
        FilteredDevices = CollectionViewSource.GetDefaultView(Devices);
        FilteredDevices.Filter = item => item is Device device &&
            (string.IsNullOrWhiteSpace(Search) || new[] { device.DisplayName, device.Hostname, device.IPv4Address,
                device.MacAddress, device.Group, string.Join(" ", device.Tags) }.Any(value =>
                value.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    partial void OnThemeChanged(AppTheme value) => ThemeManager.Apply(value);
    partial void OnSearchChanged(string value) => FilteredDevices.Refresh();
    partial void OnIsBusyChanged(bool value)
    {
        AddCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        SaveSettingsCommand.NotifyCanExecuteChanged();
    }
    private bool CanMutate() => !IsBusy;
    public void ApplySettings(AppSettings value)
    {
        settings = value;
        Theme = value.Theme;
        PollingInterval = value.PollingIntervalSeconds.ToString();
        DiscoveryConcurrency = value.DiscoveryConcurrency.ToString();
        ProbeTimeout = value.ProbeTimeoutMilliseconds.ToString();
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var devices = await repository.ListAsync(cancellationToken);
        Devices.Clear();
        foreach (var device in devices) Devices.Add(device);
        OnPropertyChanged(nameof(InventorySummary));
        OnPropertyChanged(nameof(IsEmpty));
        await Discovery.RefreshManagedAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task RefreshAsync(CancellationToken cancellationToken) => await WithBusyAsync(async () =>
    {
        await LoadAsync(cancellationToken);
        Notice = "Inventory reloaded.";
    });

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task AddAsync() => await WithBusyAsync(async () =>
    {
        if (dialogs.Edit(null)) { await LoadAsync(); Notice = "Device added."; }
    });

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task EditAsync(Device? device) => await WithBusyAsync(async () =>
    {
        if (device is not null && dialogs.Edit(device)) { await LoadAsync(); Notice = "Device updated."; }
    });

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task DeleteAsync(Device? device) => await WithBusyAsync(async () =>
    {
        if (device is null || !dialogs.ConfirmDelete(device)) return;
        await repository.DeleteAsync(device.Id);
        logger.LogInformation("Device deleted {DeviceId}", device.Id);
        await LoadAsync();
        Notice = "Device deleted.";
    });

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task SaveSettingsAsync() => await WithBusyAsync(async () =>
    {
        if (!int.TryParse(PollingInterval, out var interval) ||
            !int.TryParse(DiscoveryConcurrency, out var concurrency) || !int.TryParse(ProbeTimeout, out var timeout))
            throw new ArgumentException("Enter whole numbers for network settings.");
        var next = settings with { Theme = Theme, PollingIntervalSeconds = interval,
            DiscoveryConcurrency = concurrency, ProbeTimeoutMilliseconds = timeout };
        await settingsStore.SaveAsync(next);
        ApplySettings(next);
        ThemeManager.Apply(Theme);
        logger.LogInformation("Settings saved");
        Notice = "Settings saved. Discovery uses the new values on its next scan; status polling arrives in Milestone C.";
    });

    private async void OnDiscoveredDeviceAdded(object? sender, EventArgs e)
    {
        try { await LoadAsync(); }
        catch (Exception ex)
        {
            logger.LogError("Inventory refresh after discovery failed; category {FailureCategory}", ex.GetType().Name);
            Notice = "Device was saved. Reload My Devices to refresh the list.";
        }
    }

    [RelayCommand]
    private void RefreshDiagnostics() => Diagnostics = recent.Snapshot();

    private async Task WithBusyAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await action(); }
        catch (ArgumentException ex) { Notice = ex.Message; }
        catch (OperationCanceledException) { Notice = "Operation cancelled."; }
        catch (Exception ex)
        {
            logger.LogError("Inventory operation failed; category {FailureCategory}", ex.GetType().Name);
            Notice = "The operation could not finish. Check Diagnostics and data-folder permissions, then retry.";
        }
        finally { IsBusy = false; RefreshDiagnostics(); }
    }
}
