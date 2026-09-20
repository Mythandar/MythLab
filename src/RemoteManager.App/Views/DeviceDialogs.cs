using System.Windows;
using Microsoft.Extensions.Logging;
using RemoteManager.App.ViewModels;
using RemoteManager.Core.Devices;

namespace RemoteManager.App.Views;

public sealed class DeviceDialogs(IDeviceRepository repository, ILoggerFactory loggerFactory)
{
    public bool AddDiscovered(Core.Discovery.DiscoveredDevice discovery) => Edit(discovery.ToDevice(), isNew: true);

    public bool Edit(Device? device, bool isNew = false)
    {
        var model = new DeviceEditorViewModel(repository, loggerFactory.CreateLogger<DeviceEditorViewModel>(), device, isNew);
        var dialog = new DeviceEditorWindow(model) { Owner = Application.Current.MainWindow };
        return dialog.ShowDialog() == true;
    }

    public bool ConfirmDelete(Device device) => MessageBox.Show(Application.Current.MainWindow,
        $"Delete “{device.DisplayName}” and its connection profiles? This cannot be undone.",
        "Delete device", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
}
