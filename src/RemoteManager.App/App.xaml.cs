using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RemoteManager.App.Diagnostics;
using RemoteManager.App.ViewModels;
using RemoteManager.Core.Devices;
using RemoteManager.Infrastructure.Storage;
using Serilog;
using Serilog.Formatting.Json;

namespace RemoteManager.App;

public partial class App : Application
{
    private ServiceProvider? services;
    private readonly CancellationTokenSource lifetime = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var paths = new AppPaths(AppIdentity.DataId);
            paths.EnsureCreated();
            var recent = new RecentLogSink();
            Log.Logger = new LoggerConfiguration().MinimumLevel.Information()
                .WriteTo.Sink(recent)
                .WriteTo.File(new JsonFormatter(), System.IO.Path.Combine(paths.Logs, "events-.jsonl"),
                    rollingInterval: RollingInterval.Day, fileSizeLimitBytes: 5_000_000,
                    rollOnFileSizeLimit: true, retainedFileCountLimit: 7, shared: true)
                .CreateLogger();
            var collection = new ServiceCollection();
            collection.AddSingleton(paths);
            collection.AddSingleton(recent);
            collection.AddLogging(builder => builder.ClearProviders().AddSerilog(dispose: false));
            collection.AddSingleton<IDeviceRepository>(_ => new SqliteDeviceRepository(paths.Database));
            collection.AddSingleton(_ => new SettingsStore(paths.Settings));
            collection.AddSingleton<Views.DeviceDialogs>();
            collection.AddSingleton<ShellViewModel>();
            collection.AddSingleton<MainWindow>();
            services = collection.BuildServiceProvider();
            await services.GetRequiredService<IDeviceRepository>().InitializeAsync(lifetime.Token);
            var settings = await services.GetRequiredService<SettingsStore>().LoadAsync(lifetime.Token);
            ThemeManager.Apply(settings.Theme);
            var model = services.GetRequiredService<ShellViewModel>();
            model.ApplySettings(settings);
            await model.LoadAsync(lifetime.Token);
            MainWindow = services.GetRequiredService<MainWindow>();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            Log.Information("Application started; inventory schema {SchemaVersion}", 1);
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            // Exception text can contain user-controlled configuration; log only the category.
            Log.Error("Startup failed; category {FailureCategory}", ex.GetType().Name);
            MessageBox.Show("The application could not open its data. Check data-folder permissions, settings.json, and the diagnostics log. No data has been reset.",
                AppIdentity.DisplayName, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        lifetime.Cancel();
        services?.Dispose();
        lifetime.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
