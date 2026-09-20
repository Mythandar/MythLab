using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using RemoteManager.App;
using RemoteManager.App.Diagnostics;
using RemoteManager.App.ViewModels;
using RemoteManager.App.Views;
using RemoteManager.Core.Devices;
using RemoteManager.Core.Discovery;
using RemoteManager.Infrastructure.Discovery;
using RemoteManager.Core.Settings;
using RemoteManager.Infrastructure.Storage;

namespace RemoteManager.SmokeTests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var result = 1;
        var app = new RemoteManager.App.App();
        app.InitializeComponent();
        var errors = new StringBuilder();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(new StringWriter(errors)));
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "RemoteManager.Smoke", Guid.NewGuid().ToString("N"));
            try
            {
                if (args.Contains("--network-smoke")) { result = await NetworkSmoke.RunAsync(); return; }
                using var repository = new SqliteDeviceRepository(Path.Combine(directory, "inventory.db"));
                using var settings = new SettingsStore(Path.Combine(directory, "settings.json"));
                using var loggerFactory = LoggerFactory.Create(_ => { });
                var logger = loggerFactory.CreateLogger<ShellViewModel>();
                var dialogs = new DeviceDialogs(repository, loggerFactory);
                var fakeProbe = new SmokeProbe();
                var discovery = new DiscoveryViewModel(new NetworkDiscoveryService(fakeProbe, loggerFactory.CreateLogger<NetworkDiscoveryService>()),
                    repository, settings, dialogs, loggerFactory.CreateLogger<DiscoveryViewModel>());
                var shell = new ShellViewModel(repository, settings, new RecentLogSink(), logger,
                    new AppPaths(AppIdentity.DataId), dialogs, discovery);
                await repository.InitializeAsync();
                await shell.LoadAsync();
                var main = new MainWindow(shell);
                ThemeManager.Apply(AppTheme.Light);
                await RenderAsync(main, "empty-light.png");
                var editor = new DeviceEditorViewModel(repository, logger);
                var saved = false;
                editor.Saved += (_, _) => saved = true;
                await editor.SaveCommand.ExecuteAsync(null);
                Require(!saved && editor.Error.Length > 0, "Invalid editor input must remain unsaved.");
                editor.DisplayName = "Smoke NAS";
                editor.Hostname = "smoke-nas.local";
                editor.IpAddress = "10.20.30.40";
                editor.MacAddress = "00:11:22:33:44:55";
                editor.Group = "Servers";
                editor.Tags = "lab, storage";
                editor.Notes = "Temporary UI smoke fixture; never written to application data.";
                editor.WakeEnabled = true;
                var editorWindow = new DeviceEditorWindow(editor);
                await RenderAsync(editorWindow, "editor-light.png");
                editorWindow.Close();
                await editor.SaveCommand.ExecuteAsync(null);
                Require(saved && editor.Error.Length == 0, "Editor save must succeed.");
                await shell.LoadAsync();
                Require(shell.Devices.Count == 1, "Saved device must appear in inventory.");
                await RenderAsync(main, "inventory-light.png");
                shell.Search = "unmatched";
                Require(shell.FilteredDevices.IsEmpty, "Search must filter inventory.");
                shell.Search = "storage";
                Require(!shell.FilteredDevices.IsEmpty, "Search must match tags.");
                shell.Search = "";
                var duplicate = new DeviceEditorViewModel(repository, logger)
                    { DisplayName = "Duplicate", Hostname = "SMOKE-NAS.LOCAL" };
                await duplicate.SaveCommand.ExecuteAsync(null);
                Require(duplicate.Error.Contains("already uses"), "Editor must report duplicate.");
                var editing = new DeviceEditorViewModel(repository, logger, shell.Devices[0]) { IpAddress = "10.20.30.41" };
                await editing.SaveCommand.ExecuteAsync(null);
                await shell.LoadAsync();
                Require(shell.Devices[0].IPv4Address == "10.20.30.41", "Address edit must persist.");
                ThemeManager.Apply(AppTheme.Dark);
                await RenderAsync(main, "inventory-dark.png");
                var tabs = Find<TabControl>((DependencyObject)main.Content)!;
                for (var i = 1; i < tabs.Items.Count; i++)
                {
                    tabs.SelectedIndex = i;
                    await RenderAsync(main, $"page-{i}-dark.png");
                }
                shell.Theme = AppTheme.Light;
                shell.PollingInterval = "60";
                await shell.SaveSettingsCommand.ExecuteAsync(null);
                Require((await settings.LoadAsync()).PollingIntervalSeconds == 60, "Settings command must persist.");
                await discovery.RefreshInterfacesCommand.ExecuteAsync(null);
                discovery.StartAddress = "10.20.30.50";
                discovery.EndAddress = "10.20.30.51";
                await discovery.ScanCommand.ExecuteAsync(null);
                Require(discovery.Results.Count == 2, "Discovery UI must show non-ping devices.");
                Require(discovery.Results.All(r => !r.Device.IsOnline), "ARP/cache must not be shown as online.");
                discovery.SelectedResult = discovery.Results[0];
                var draftAccepted = false;
                var acceptNext = true;
                EventManager.RegisterClassHandler(typeof(DeviceEditorWindow), FrameworkElement.LoadedEvent,
                    new RoutedEventHandler(async (sender, _) =>
                    {
                        if (!acceptNext) return;
                        acceptNext = false;
                        var window = (DeviceEditorWindow)sender;
                        var model = (DeviceEditorViewModel)window.DataContext;
                        draftAccepted = model.Title == "Add device" && model.IpAddress == "10.20.30.50" &&
                            model.Hostname == "discovered-nas.local" && !model.WakeEnabled && model.MacAddress == "00:11:22:33:44:50";
                        if (draftAccepted) await model.SaveCommand.ExecuteAsync(null);
                        else window.Close();
                    }));
                await discovery.AddSelectedCommand.ExecuteAsync(null);
                await shell.LoadAsync();
                Require(draftAccepted && shell.Devices.Count == 2, "Discovery add must use a prefilled editor and save into inventory.");
                Require(discovery.Results[0].IsManaged, "Added discovery must be marked managed.");
                var discoveryTabs = Find<TabControl>((DependencyObject)main.Content)!;
                discoveryTabs.SelectedIndex = 1;
                await RenderAsync(main, "discovery-light.png");
                ThemeManager.Apply(AppTheme.Dark);
                await RenderAsync(main, "discovery-dark.png");
                fakeProbe.Block = true;
                var cancelledScan = discovery.ScanCommand.ExecuteAsync(null);
                await fakeProbe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                discovery.ScanCommand.Cancel();
                await cancelledScan.WaitAsync(TimeSpan.FromSeconds(5));
                Require(!discovery.IsScanning && discovery.Message.Contains("cancelled"), "Discovery cancellation must reset the UI.");
                fakeProbe.Block = false;
                foreach (var device in shell.Devices.ToArray()) await repository.DeleteAsync(device.Id);
                await shell.LoadAsync();
                Require(shell.IsEmpty, "Deleted device must disappear.");
                // Exercise the same bound appearance control a user selects, not ThemeManager directly.
                var appearanceTabs = Find<TabControl>((DependencyObject)main.Content)!;
                appearanceTabs.SelectedIndex = 3;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var settingsView = Find<SettingsView>((DependencyObject)main.Content)!;
                var themePicker = Find<ComboBox>(settingsView)!;
                themePicker.SelectedItem = AppTheme.System;
                themePicker.SelectedItem = AppTheme.Light;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var lightForeground = ((SolidColorBrush)main.FindResource("WindowForeground")).Color;
                themePicker.SelectedItem = AppTheme.Dark;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var darkForeground = ((SolidColorBrush)main.FindResource("WindowForeground")).Color;
                Require(shell.Theme == AppTheme.Dark && lightForeground != darkForeground,
                    "Appearance selection must immediately change actual theme brushes without Save.");
                await shell.SaveSettingsCommand.ExecuteAsync(null);
                Require((await settings.LoadAsync()).Theme == AppTheme.Dark, "The selected appearance must persist when saved.");
                themePicker.SelectedItem = AppTheme.System;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Require(shell.Theme == AppTheme.System, "System appearance must be selectable.");
                themePicker.SelectedItem = AppTheme.Light;
                for (var i = 1; i <= 6; i++)
                    await repository.SaveAsync(new Device { DisplayName = $"Layout device {i}", Hostname = $"layout-{i}.local",
                        Group = "Lab", Notes = "Responsive card layout fixture." });
                await shell.LoadAsync();
                appearanceTabs.SelectedIndex = 0;
                main.Width = 920;
                main.Height = 1000;
                await RenderAsync(main, "cards-narrow-light.png");
                var panel = Find<ResponsiveCardsPanel>((DependencyObject)main.Content)!;
                var list = Find<ListBox>((DependencyObject)main.Content)!;
                var scroll = Find<ScrollViewer>(list)!;
                int FirstRowCount() => panel.Children.Cast<UIElement>().Count(child =>
                    Math.Abs(child.TranslatePoint(new Point(), panel).Y - panel.Children[0].TranslatePoint(new Point(), panel).Y) < 1);
                Require(FirstRowCount() == 1 && scroll.ScrollableHeight > 0, "Narrow inventory must use one column and scroll when needed.");
                main.Width = 1600;
                await RenderAsync(main, "cards-wide-light.png");
                Require(FirstRowCount() >= 3, "Wide inventory must flow into at least three columns.");
                Require(scroll.ScrollableHeight < 1, "Fitting cards must not require vertical scrolling.");
                shell.Theme = AppTheme.Dark;
                await RenderAsync(main, "cards-wide-dark.png");
                main.Height = 640;
                await RenderAsync(main, "cards-short-dark.png");
                Require(scroll.ScrollableHeight > 0, "Short windows must still allow scrolling to every card.");
                fakeProbe.Block = true;
                var shutdownScan = discovery.ScanCommand.ExecuteAsync(null);
                main.Close();
                await shutdownScan.WaitAsync(TimeSpan.FromSeconds(5));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Require(!main.IsVisible && !discovery.IsScanning, "Closing must cancel and drain discovery before disposing services.");
                Require(errors.Length == 0, "WPF binding errors: " + errors);
                Console.WriteLine("PASS: WPF views rendered; editor validation/save/edit/duplicate, inventory search/delete, settings, discovery scan/add/cancel, light/dark and bindings verified.");
                result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
                Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        });
        Dispatcher.Run();
        return result;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T target) return target;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (Find<T>(VisualTreeHelper.GetChild(root, i)) is { } match) return match;
        return null;
    }

    private static async Task RenderAsync(Window window, string filename)
    {
        if (!window.IsVisible)
        {
            window.ShowInTaskbar = false;
            window.ShowActivated = false;
            window.Left = -30000;
            window.Top = -30000;
            window.Show();
        }
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(100);
        var element = (FrameworkElement)window.Content;
        element.DataContext = window.DataContext;
        var size = new Size(window.Width - 48, window.Height - 80);
        element.Measure(size);
        element.Arrange(new Rect(size));
        element.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen())
        {
            drawing.DrawRectangle(filename.Contains("dark") ? new SolidColorBrush(Color.FromRgb(32,32,32)) : Brushes.White, null, new Rect(size));
            drawing.DrawRectangle(new VisualBrush(element), null, new Rect(size));
        }
        bitmap.Render(background);
        var folder = Path.GetFullPath("artifacts/ui-smoke");
        Directory.CreateDirectory(folder);
        using var stream = File.Create(Path.Combine(folder, filename));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }
}
