using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using MythLab.App.ViewModels;

namespace MythLab.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel model;
    private bool waitingForScan;
    private bool drained;
    public MainWindow(ShellViewModel model)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        // Let a local write finish before disposing the repository.
        if (model.IsBusy || model.Discovery.IsBusy || model.Connections?.IsBusy == true || waitingForScan) { e.Cancel = true; return; }
        if (drained) return;
        var scan = model.Discovery.ScanCommand.ExecutionTask;
        e.Cancel = true;
        waitingForScan = true;
        model.Discovery.ScanCommand.Cancel();
        try { if (model.Connections is not null) await model.Connections.StopAsync(); await model.Activity.StopAsync(); if (scan is not null) await scan; }
        catch (OperationCanceledException) { }
        finally
        {
            drained = true;
            waitingForScan = false;
            // Defer until the current Closing event has unwound, including synchronous cancellation.
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Close));
        }
    }
}
