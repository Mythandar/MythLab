using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using MythLab.App.ViewModels;
using MythLab.Core.Connections;
using MythLab.Core.Devices;
using MythLab.Core.Monitoring;
using MythLab.Infrastructure.Storage;
namespace MythLab.SmokeTests;

internal sealed class ExternalFixture : IExecutableLocator, IConnectionLauncher, IDeviceStatusService
{
    public bool Available = true;
    public bool BlockReadiness;
    public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<LaunchRequest> Requests = [];
    public LauncherAvailability Locate(ConnectionProfile p) => new(Available, p.Kind == ConnectionKind.LocalPowerShell
        ? @"C:\Fixture\pwsh.exe" : @"C:\Fixture with spaces\viewer.exe", Available ? "Fixture available" : "Missing launcher. Edit this connection.");
    public Task LaunchAsync(LaunchRequest request, CancellationToken token = default) { Requests.Add(request); return Task.CompletedTask; }
    public async Task<StatusObservation> CheckAsync(Device device, int timeoutMilliseconds, CancellationToken token = default)
    {
        Entered.TrySetResult();
        if (BlockReadiness) await Task.Delay(Timeout.Infinite, token);
        return new(DeviceState.Online, DateTimeOffset.UtcNow, "Fixture TCP service");
    }
}
internal static class ExternalSmoke
{
    public static async Task RunAsync(ConnectionsViewModel connections, SqliteDeviceRepository repository,
        DeviceCardViewModel card, Window main, ExternalFixture fixture)
    {
        var handling = true; var editing = false; var editorOpened = false; var chooserOpened = false;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (!handling) return;
            var window = (Window)sender;
            if (window.Title == "Choose connection")
            {
                chooserOpened = true;
                var button = CardMenuSmoke.Visuals<Button>(window).Single(b => b.Content?.ToString() == "Continue");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return;
            }
            if (AutomationProperties.GetAutomationId(window) != "ExternalProfileEditor") return;
            editorOpened = true;
            var device = CardMenuSmoke.Visuals<ComboBox>(window).Single(c => AutomationProperties.GetName(c) == "Managed device");
            Require(!device.IsEnabled && device.SelectedItem is Device d && d.Id == card.Id, "External setup/edit must stay device-bound.");
            var name = CardMenuSmoke.Visuals<TextBox>(window).Single(c => AutomationProperties.GetName(c) == "Connection name");
            name.Text = editing ? "Edited fallback RDP" : "Fallback RDP";
            CardMenuSmoke.Visuals<Button>(window).Single(b => b.Content?.ToString() == "Save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        try
        {
            await CardMenuSmoke.InvokeMenuAsync(main, card, "AddExternal");
            Require(editorOpened, "More > Add connection must open the profile editor.");
            var rdp = (await repository.ListProfilesAsync()).Single(p => p.Kind == ConnectionKind.Rdp);
            Require(rdp.CredentialId is null && card.HasRdp && card.HasSshProfiles, "External metadata and SSH must coexist.");
            editing = true; editorOpened = false;
            await CardMenuSmoke.InvokeMenuAsync(main, card, "EditExternal");
            Require(editorOpened && (await repository.ListProfilesAsync()).Single(p => p.Kind == ConnectionKind.Rdp).DisplayName == "Edited fallback RDP",
                "Overflow editing must update the same profile.");
            foreach (var kind in Enum.GetValues<ConnectionKind>().Where(k => k is not (ConnectionKind.Ssh or ConnectionKind.Rdp)))
                await repository.SaveProfileAsync(new()
                {
                    DeviceId = card.Id, Kind = kind, DisplayName = kind.ToString(), TimeoutSeconds = 5,
                    ExecutablePath = kind == ConnectionKind.Custom ? @"C:\Missing tools\custom.exe" : "",
                    UrlTemplate = kind == ConnectionKind.Web ? "https://{host}/" : "",
                    RemoteApplication = kind == ConnectionKind.Moonlight ? "Desktop Game" : ""
                });
            await connections.LoadAsync();
            main.Width = 920;
            await Program.RenderAsync(main, "external-cards-narrow-light.png");
            CardMenuSmoke.CheckMinimumWidth(main);
            foreach (var kind in Enum.GetValues<ConnectionKind>().Where(k => k != ConnectionKind.Ssh))
            {
                var button = CardMenuSmoke.Visuals<Button>(main).Single(b => ReferenceEquals(b.DataContext, card) &&
                    AutomationProperties.GetAutomationId(b) == "Device" + kind);
                Require(button.IsVisible && button.Focusable && button.Command is not null, "Configured external action must be directly reachable.");
                var before = fixture.Requests.Count;
                ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (fixture.Requests.Count == before) await Task.Delay(20, timeout.Token);
                while (connections.IsBusy) await Task.Delay(20, timeout.Token);
            }
            Require(fixture.Requests.Count == 6, "Each external action should launch exactly once through the fake boundary.");
            await repository.SaveProfileAsync(rdp with { Id = Guid.NewGuid(), DisplayName = "Second RDP" });
            await connections.LoadAsync();
            await connections.DeviceExternalCommand.ExecuteAsync(card.RdpAction);
            Require(chooserOpened && fixture.Requests.Count == 7, "Multiple external profiles require a chooser.");
            fixture.Available = false;
            await connections.ConnectCommand.ExecuteAsync(rdp);
            Require(connections.Message.Contains("Missing launcher") && fixture.Requests.Count == 7, "Missing executables must report useful feedback.");
            Require((await repository.ListProfilesAsync()).Count(p => p.Kind == ConnectionKind.Rdp) == 2, "Missing launchers must not remove profiles.");
            fixture.Available = true;
            await repository.SaveProfileAsync(rdp with { ReadinessPort = 3389 });
            fixture.BlockReadiness = true;
            var launch = connections.ConnectCommand.ExecuteAsync(rdp);
            await fixture.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Require(connections.IsLaunching, "Readiness must expose cancellation.");
            connections.CancelExternalCommand.Execute(null);
            await launch.WaitAsync(TimeSpan.FromSeconds(5));
            Require(!connections.IsLaunching && connections.Message.Contains("cancelled") && fixture.Requests.Count == 7,
                "Cancelled readiness must not launch.");
            fixture.BlockReadiness = false;
            await CardMenuSmoke.CheckAsync(main, card, true);
            Console.WriteLine("PASS: External editor/card/menu, six fake launches, multiple profiles, missing launcher, cancellation and minimum-width wrapping.");
        }
        finally { handling = false; }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
