using System.Windows;
using Microsoft.Extensions.Logging;
using MythLab.App.ViewModels;
using MythLab.Core.Devices;

namespace MythLab.App.Views;

public sealed class DeviceDialogs(IDeviceRepository repository, ILoggerFactory loggerFactory, Core.Discovery.ILanProbe? networks = null)
{
    public bool AddDiscovered(Core.Discovery.DiscoveredDevice discovery) => Edit(discovery.ToDevice(), isNew: true);

    public bool Edit(Device? device, bool isNew = false)
    {
        var model = new DeviceEditorViewModel(repository, loggerFactory.CreateLogger<DeviceEditorViewModel>(), device, isNew, networks);
        var dialog = new DeviceEditorWindow(model) { Owner = Application.Current.MainWindow };
        return dialog.ShowDialog() == true;
    }

    public bool ConfirmDelete(Device device) => MessageBox.Show(Application.Current.MainWindow,
        $"Delete “{device.DisplayName}” and its connection profiles? This cannot be undone.",
        "Delete device", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
}
