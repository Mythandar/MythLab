using System.Windows;
using System.Windows.Controls;
using MythLab.Core.Connections;
using MythLab.Core.Devices;
namespace MythLab.App.Views;

public static class ExternalProfileDialog
{
    public static ConnectionProfile? Choose(IReadOnlyList<ConnectionProfile> profiles)
    {
        if (profiles.Count == 1) return profiles[0];
        if (profiles.Count == 0) return null;
        var window = new MetadataEditor("Choose connection");
        var choice = window.Choice("Connection profile", profiles, nameof(ConnectionProfile.DisplayName), profiles[0]);
        window.OnSave(() => Task.CompletedTask, "Continue");
        return window.ShowDialog() == true ? (ConnectionProfile)choice.SelectedItem : null;
    }
    public static bool Edit(IConnectionRepository repository, IReadOnlyList<Device> devices, ConnectionProfile? value, Guid? deviceId = null)
    {
        var original = value ?? new ConnectionProfile { Kind = ConnectionKind.Rdp, DisplayName = "RDP", TimeoutSeconds = 10 };
        var window = new MetadataEditor(value is null ? "Add external connection" : "Edit external connection");
        System.Windows.Automation.AutomationProperties.SetAutomationId(window, "ExternalProfileEditor");
        var device = window.Choice("Managed device", devices, nameof(Device.DisplayName),
            devices.FirstOrDefault(d => d.Id == (deviceId ?? original.DeviceId)) ?? devices.FirstOrDefault());
        device.IsEnabled = deviceId is null;
        var kinds = Enum.GetValues<ConnectionKind>().Where(k => k != ConnectionKind.Ssh).ToArray();
        var kind = window.Choice("Connection type", kinds, "", original.Kind);
        kind.IsEnabled = value is null;
        var name = window.TextField("Connection name", original.DisplayName);
        var port = window.TextField("Target port (blank uses type default)", original.Port?.ToString() ?? "");
        var executable = window.TextField("Executable path (blank detects installed viewer)", original.ExecutablePath);
        var browse = new Button { Content = "Browse executable..." };
        browse.Click += (_, _) =>
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Windows executable|*.exe" };
            if (picker.ShowDialog(window) == true) executable.Text = picker.FileName;
        };
        window.Fields.Children.Add(browse);
        var url = window.TextField("HTTP/HTTPS URL template", string.IsNullOrEmpty(original.UrlTemplate) ? "https://{host}/" : original.UrlTemplate);
        var app = window.TextField("Moonlight app/game (blank opens Moonlight's picker)", original.RemoteApplication);
        var fullscreen = new CheckBox { Content = "RDP fullscreen", IsChecked = original.RdpFullscreen, Margin = new(0,8,0,8) };
        window.Fields.Children.Add(fullscreen);
        var arguments = window.TextField("Non-secret arguments: ONE argument per line, without surrounding quotes", string.Join("\n", original.Arguments));
        arguments.AcceptsReturn = true; arguments.MinHeight = 70; arguments.MaxHeight = 140; arguments.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var readiness = window.TextField("Optional TCP port on this device (blank launches immediately)", original.ReadinessPort?.ToString() ?? "");
        var timeout = window.TextField("Readiness timeout (seconds, 5-300)", original.TimeoutSeconds.ToString());
        var help = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0,8,0,8) };
        window.Fields.Children.Add(help);
        void ShowField(TextBox field, bool show)
        {
            field.IsEnabled = show;
            field.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            window.Fields.Children[window.Fields.Children.IndexOf(field) - 1].Visibility = field.Visibility;
        }
        void Update()
        {
            var selected = (ConnectionKind)kind.SelectedItem;
            ShowField(executable, selected is not (ConnectionKind.Web or ConnectionKind.Rdp));
            browse.IsEnabled = executable.IsEnabled; browse.Visibility = executable.Visibility;
            ShowField(port, selected != ConnectionKind.LocalPowerShell);
            ShowField(url, selected == ConnectionKind.Web);
            ShowField(app, selected == ConnectionKind.Moonlight);
            fullscreen.IsEnabled = selected == ConnectionKind.Rdp;
            fullscreen.Visibility = fullscreen.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
            ShowField(arguments, selected is ConnectionKind.RealVnc or ConnectionKind.Moonlight or ConnectionKind.Custom);
            ShowField(readiness, selected != ConnectionKind.LocalPowerShell);
            help.Text = "Profiles contain only non-secret configuration. Never enter passwords, passphrases, tokens or credentials here. " +
                "Arguments support {ip}, {host}, {hostname}, {port}. Executable paths are absolute or relative to MythLab. Missing launchers can be reconfigured after copying the folder. " +
                (selected == ConnectionKind.Rdp ? "RDP uses Windows authentication, separately from MythLab SSH credentials. Default port: 3389." :
                 selected == ConnectionKind.RealVnc ? "Default VNC port: 5900. Authenticate in RealVNC Viewer." :
                 selected == ConnectionKind.LocalPowerShell ? "Opens powershell.exe, pwsh.exe or wt.exe interactively; no command strings or remote actions." :
                 selected == ConnectionKind.Moonlight ? "Pair the host in Moonlight first. Set an app name to stream directly to this device; otherwise use its native picker." :
                 selected == ConnectionKind.Custom ? "Only configure executables you trust. MythLab passes arguments as data; the selected program decides how to interpret them. Shell interpreters are not supported." : "");
        }
        kind.SelectionChanged += (_, _) => { name.Text = ExternalConnections.Label((ConnectionKind)kind.SelectedItem); port.Text = ""; Update(); };
        Update();
        window.OnSave(async () =>
        {
            if (device.SelectedItem is not Device target) throw new ArgumentException("Add a managed device first.");
            var selected = (ConnectionKind)kind.SelectedItem;
            int? OptionalPort(string text) => string.IsNullOrWhiteSpace(text) ? null : int.TryParse(text, out var n) ? n :
                throw new ArgumentException("Ports must be whole numbers.");
            if (!int.TryParse(timeout.Text, out var seconds)) throw new ArgumentException("Timeout must be a whole number.");
            await repository.SaveProfileAsync(original with
            {
                DeviceId = target.Id, Kind = selected, DisplayName = name.Text.Trim(), CredentialId = null,
                Port = port.IsEnabled ? OptionalPort(port.Text) : null,
                ExecutablePath = executable.IsEnabled ? executable.Text.Trim() : "",
                UrlTemplate = url.IsEnabled ? url.Text.Trim() : "",
                RemoteApplication = app.IsEnabled ? app.Text.Trim() : "",
                RdpFullscreen = fullscreen.IsEnabled && fullscreen.IsChecked == true,
                Arguments = arguments.IsEnabled && arguments.Text.Length > 0 ? arguments.Text.Replace("\r", "").Split('\n') : [],
                ReadinessPort = readiness.IsEnabled ? OptionalPort(readiness.Text) : null, TimeoutSeconds = seconds
            });
        });
        return window.ShowDialog() == true;
    }
}
