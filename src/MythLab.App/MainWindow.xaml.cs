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
    private bool closeRequested;
    public MainWindow(ShellViewModel model)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;
        Closing += OnClosing;
        model.PropertyChanged += ResumeClose;
        model.Discovery.PropertyChanged += ResumeClose;
        if (model.Connections is not null) model.Connections.PropertyChanged += ResumeClose;
        Closed += (_, _) =>
        {
            model.PropertyChanged -= ResumeClose;
            model.Discovery.PropertyChanged -= ResumeClose;
            if (model.Connections is not null) model.Connections.PropertyChanged -= ResumeClose;
        };
    }

    private void ResumeClose(object? sender, PropertyChangedEventArgs e)
    {
        if (!closeRequested || e.PropertyName != "IsBusy") return;
        if (model.IsBusy || model.Discovery.IsBusy || model.Connections?.IsBusy == true) return;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (!drained && !waitingForScan) Close(); }));
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        // Let a local write finish before disposing the repository.
        if (waitingForScan) { e.Cancel = true; return; }
        if (model.IsBusy || model.Discovery.IsBusy || model.Connections?.IsBusy == true)
        {
            e.Cancel = true;
            closeRequested = true;
            IsEnabled = false; // prevent new main-window actions while the existing write finishes
            model.Notice = "Closing after the current metadata operation finishes...";
            return;
        }
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
