using System.ComponentModel;
using System.Windows;
using RemoteManager.App.ViewModels;

namespace RemoteManager.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel model;
    public MainWindow(ShellViewModel model)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;
        Closing += OnClosing;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Let an in-flight local write finish before disposing the repository.
        if (model.IsBusy) e.Cancel = true;
    }
}
