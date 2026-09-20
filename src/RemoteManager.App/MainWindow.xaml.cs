using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using RemoteManager.App.ViewModels;

namespace RemoteManager.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel model;
    private bool waitingForScan;
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
        if (model.IsBusy || model.Discovery.IsBusy || waitingForScan) { e.Cancel = true; return; }
        if (model.Discovery.ScanCommand.ExecutionTask is not { IsCompleted: false } scan) return;
        e.Cancel = true;
        waitingForScan = true;
        model.Discovery.ScanCommand.Cancel();
        try { await scan; }
        catch (OperationCanceledException) { }
        finally
        {
            waitingForScan = false;
            // Defer until the current Closing event has unwound, including synchronous cancellation.
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Close));
        }
    }
}
