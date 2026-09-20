using System.Windows;
using System.Windows.Controls;
using MythLab.App.ViewModels;

namespace MythLab.App.Views;

public partial class DiscoveryView : UserControl
{
    private bool initialized;
    public DiscoveryView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (initialized || DataContext is not DiscoveryViewModel model) return;
        initialized = true;
        await model.RefreshInterfacesCommand.ExecuteAsync(null);
    }
}
