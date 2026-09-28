using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MythLab.App.Diagnostics;
using MythLab.App.ViewModels;
using MythLab.Core.Devices;
using MythLab.Infrastructure.Storage;
using Serilog;
using Serilog.Formatting.Json;

namespace MythLab.App;

public partial class App : Application
{
    private ServiceProvider? services;
    private readonly CancellationTokenSource lifetime = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var startupStage = StartupStage.PortableData;
        try
        {
            var paths = new AppPaths(AppIdentity.DataId);
            var imported = await PortableDataSetup.PrepareAsync(paths, lifetime.Token);
            await paths.VerifyWritableAsync(lifetime.Token);
            var recent = new RecentLogSink();
            Log.Logger = new LoggerConfiguration().MinimumLevel.Information()
                .WriteTo.Sink(recent)
                .WriteTo.File(new JsonFormatter(), System.IO.Path.Combine(paths.Logs, "events-.jsonl"),
                    rollingInterval: RollingInterval.Day, fileSizeLimitBytes: 5_000_000,
                    rollOnFileSizeLimit: true, retainedFileCountLimit: 7, shared: true)
                .CreateLogger();
            startupStage = StartupStage.Initialization;
            var collection = new ServiceCollection();
            collection.AddSingleton(paths);
            collection.AddSingleton(recent);
            collection.AddLogging(builder => builder.ClearProviders().AddSerilog(dispose: false));
            collection.AddSingleton<SqliteDeviceRepository>(_ => new SqliteDeviceRepository(paths.Database));
            collection.AddSingleton<IDeviceRepository>(sp => sp.GetRequiredService<SqliteDeviceRepository>());
            collection.AddSingleton<Core.Connections.IConnectionRepository>(sp => sp.GetRequiredService<SqliteDeviceRepository>());
            collection.AddSingleton<Core.Credentials.ICredentialStore>(_ => new Infrastructure.Credentials.WindowsCredentialStore(AppIdentity.DataId));
            collection.AddSingleton(_ => new Infrastructure.Ssh.KnownHostsStore(paths.KnownHosts));
            collection.AddSingleton<Infrastructure.Ssh.SshSessionService>();
            collection.AddSingleton<Core.Connections.IExecutableLocator, Infrastructure.Connections.WindowsExecutableLocator>();
            collection.AddSingleton<Core.Connections.IConnectionLauncher, Infrastructure.Connections.WindowsConnectionLauncher>();
            collection.AddSingleton<Core.Connections.ExternalConnectionService>();
            collection.AddSingleton<ConnectionsViewModel>();
            collection.AddSingleton(_ => new SettingsStore(paths.Settings));
            collection.AddSingleton<Views.DeviceDialogs>();
            collection.AddSingleton<Core.Discovery.ILanProbe, Infrastructure.Discovery.WindowsLanProbe>();
            collection.AddSingleton<Core.Discovery.INetworkDiscoveryService, Infrastructure.Discovery.NetworkDiscoveryService>();
            collection.AddSingleton<DiscoveryViewModel>();
            collection.AddSingleton<Core.Monitoring.IDeviceStatusService, Infrastructure.Monitoring.WindowsDeviceStatusService>();
            collection.AddSingleton<Core.WakeOnLan.IWakePacketTransport, Infrastructure.WakeOnLan.UdpWakePacketTransport>();
            collection.AddSingleton<Core.WakeOnLan.IWakeOnLanService, Infrastructure.WakeOnLan.WakeOnLanService>();
            collection.AddSingleton<Infrastructure.WakeOnLan.WakeDeviceService>();
            collection.AddSingleton<DeviceActivityViewModel>();
            collection.AddSingleton<ShellViewModel>();
            collection.AddSingleton<MainWindow>();
            services = collection.BuildServiceProvider();
            startupStage = StartupStage.Database;
            await services.GetRequiredService<IDeviceRepository>().InitializeAsync(lifetime.Token);
            startupStage = StartupStage.Settings;
            var settings = await services.GetRequiredService<SettingsStore>().LoadAsync(lifetime.Token);
            startupStage = StartupStage.Initialization;
            ThemeManager.Apply(settings.Theme);
            var model = services.GetRequiredService<ShellViewModel>();
            model.ApplySettings(settings);
            await model.LoadAsync(lifetime.Token);
            if (imported) model.Notice = "Previous inventory and settings copied into the portable Data folder. Original files were kept.";
            MainWindow = services.GetRequiredService<MainWindow>();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            Log.Information("Application started; inventory schema {SchemaVersion}", 1);
            MainWindow.Show();
            model.Activity.Start();
        }
        catch (Exception ex)
        {
            // Never log raw exception text or stack traces: configuration may contain sensitive content.
            Log.Error("Startup failed; stage {StartupStage}; category {FailureCategory}", startupStage, ex.GetType().Name);
            MessageBox.Show(StartupDiagnostics.Describe(startupStage, ex),
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
