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
using RemoteManager.Core.Settings;
using RemoteManager.Infrastructure.Storage;

namespace RemoteManager.SmokeTests;

internal static class Program
{
    [STAThread]
    private static int Main()
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
                using var repository = new SqliteDeviceRepository(Path.Combine(directory, "inventory.db"));
                using var settings = new SettingsStore(Path.Combine(directory, "settings.json"));
                using var loggerFactory = LoggerFactory.Create(_ => { });
                var logger = loggerFactory.CreateLogger<ShellViewModel>();
                var shell = new ShellViewModel(repository, settings, new RecentLogSink(), logger,
                    new AppPaths(AppIdentity.DataId), new DeviceDialogs(repository, loggerFactory));
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
                await repository.DeleteAsync(shell.Devices[0].Id);
                await shell.LoadAsync();
                Require(shell.IsEmpty, "Deleted device must disappear.");
                Require(errors.Length == 0, "WPF binding errors: " + errors);
                Console.WriteLine("PASS: WPF views rendered; editor validation/save/edit/duplicate, inventory search/delete, settings, light/dark and bindings verified.");
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
