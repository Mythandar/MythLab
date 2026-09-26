using System.IO;
using System.Windows;
using System.Windows.Controls;
using MythLab.Core.Connections;
using MythLab.Core.Credentials;
using MythLab.Core.Devices;
namespace MythLab.App.Views;

internal sealed class MetadataEditor : Window
{
    public StackPanel Fields { get; } = new();
    public TextBlock Error { get; } = new() { Margin = new(0, 12, 0, 8), TextWrapping = TextWrapping.Wrap };
    private bool saving;
    public MetadataEditor(string title)
    {
        Title = title; Width = 570; Height = 660; MinWidth = 460; MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Owner = Application.Current.MainWindow;
        var root = new DockPanel { Margin = new(24) };
        DockPanel.SetDock(Error, Dock.Bottom); root.Children.Add(Error);
        root.Children.Add(new ScrollViewer { Content = Fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        Closing += (_, e) => { if (saving) e.Cancel = true; };
    }
    public TextBox TextField(string label, string value)
    {
        Fields.Children.Add(new TextBlock { Text = label });
        var box = new TextBox { Text = value };
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        Fields.Children.Add(box); return box;
    }
    public ComboBox Choice(string label, System.Collections.IEnumerable values, string displayPath, object? selected)
    {
        Fields.Children.Add(new TextBlock { Text = label });
        var box = new ComboBox { ItemsSource = values, DisplayMemberPath = displayPath, SelectedItem = selected };
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        Fields.Children.Add(box); return box;
    }
    public void OnSave(Func<Task> action)
    {
        var save = new Button { Content = "Save", IsDefault = true, Margin = new(0, 16, 0, 0) };
        Fields.Children.Add(save);
        save.Click += async (_, _) =>
        {
            if (saving) return;
            saving = true; save.IsEnabled = false; Error.Text = "";
            try { await action(); saving = false; DialogResult = true; }
            catch (Exception ex)
            {
                Error.Text = ex is ArgumentException or InvalidOperationException or CredentialStoreException
                    ? ex.Message : $"Could not save ({ex.GetType().Name}). Check the selected metadata and portable folder.";
            }
            finally { saving = false; save.IsEnabled = true; }
        };
    }
}

public static class ConnectionDialogs
{
    public static bool EditCredential(IConnectionRepository repository, ICredentialStore secrets, CredentialReference? value)
    {
        var original = value ?? new CredentialReference();
        var window = new MetadataEditor(value is null ? "Add credential" : "Edit / recreate credential");
        var label = window.TextField("Credential label", original.DisplayLabel);
        var user = window.TextField("Username", original.Username);
        var auth = window.Choice("Authentication", Enum.GetValues<AuthenticationKind>(), "", original.Authentication);
        var key = window.TextField("Private-key path (absolute or relative to the executable)", original.PrivateKeyPath);
        var browse = new Button { Content = "Browse key…" };
        browse.Click += (_, _) => { var picker = new Microsoft.Win32.OpenFileDialog(); if (picker.ShowDialog(window) == true) key.Text = picker.FileName; };
        window.Fields.Children.Add(browse);
        window.Fields.Children.Add(new TextBlock { Text = "New password / key passphrase (saved secrets are never displayed)", Margin = new(0, 16, 0, 4) });
        var secret = new PasswordBox { Margin = new(0, 4, 0, 12), Padding = new(8) };
        System.Windows.Automation.AutomationProperties.SetName(secret, "New secret");
        window.Fields.Children.Add(secret);
        var replace = new CheckBox { Content = "Save / replace this secret in Windows Credential Manager", Margin = new(0, 8, 0, 8) };
        secret.PasswordChanged += (_, _) => replace.IsChecked = true;
        window.Fields.Children.Add(replace);
        window.Fields.Children.Add(new TextBlock { Text = "Leave replacement unchecked to keep the existing secret. Unencrypted private keys need no saved passphrase. Credentials stay with this Windows user and machine." });
        window.OnSave(async () =>
        {
            var updated = original with { DisplayLabel = label.Text.Trim(), Username = user.Text.Trim(),
                Authentication = (AuthenticationKind)auth.SelectedItem, PrivateKeyPath = key.Text.Trim() };
            if (string.IsNullOrWhiteSpace(updated.DisplayLabel) || string.IsNullOrWhiteSpace(updated.Username))
                throw new ArgumentException("Enter a label and username.");
            if (updated.Authentication == AuthenticationKind.PrivateKey &&
                !File.Exists(Path.GetFullPath(updated.PrivateKeyPath, AppContext.BaseDirectory)))
                throw new ArgumentException("Select an existing private-key file.");
            if (updated.Authentication == AuthenticationKind.Password && replace.IsChecked != true &&
                (value is null || original.Authentication != updated.Authentication || await secrets.InspectAsync(original.Id) != SecretStatus.Available))
                throw new ArgumentException("Enter a password and enable Save / replace.");
            if (replace.IsChecked == true)
            {
                if (updated.Authentication == AuthenticationKind.Password && secret.Password.Length == 0)
                    throw new ArgumentException("Enter the password.");
                await secrets.WriteAsync(original.Id, secret.Password);
                secret.Clear(); // never read the stored value back into the UI
            }
            else if (original.Authentication != updated.Authentication) await secrets.DeleteAsync(original.Id);
            await repository.SaveCredentialAsync(updated);
        });
        return window.ShowDialog() == true;
    }

    public static bool EditProfile(IConnectionRepository repository, IReadOnlyList<Device> devices,
        IReadOnlyList<CredentialReference> credentials, ConnectionProfile? value)
    {
        var original = value ?? new ConnectionProfile { Kind = ConnectionKind.Ssh, Port = 22, TimeoutSeconds = 30, DisplayName = "SSH" };
        var window = new MetadataEditor(value is null ? "Add SSH connection" : "Edit SSH connection");
        var name = window.TextField("Connection name", original.DisplayName);
        var device = window.Choice("Managed device", devices, nameof(Device.DisplayName),
            devices.FirstOrDefault(d => d.Id == original.DeviceId) ?? devices.FirstOrDefault());
        var credential = window.Choice("Saved credential", credentials, nameof(CredentialReference.DisplayLabel),
            credentials.FirstOrDefault(c => c.Id == original.CredentialId) ?? credentials.FirstOrDefault());
        var port = window.TextField("SSH port", (original.Port ?? 22).ToString());
        var timeout = window.TextField("Connection timeout (seconds, 5–300)", original.TimeoutSeconds.ToString());
        window.Fields.Children.Add(new TextBlock { Text = "The connection uses the device hostname/IP. A device can have several SSH profiles with different credentials or ports." });
        window.OnSave(async () =>
        {
            if (device.SelectedItem is not Device target || credential.SelectedItem is not CredentialReference reference)
                throw new ArgumentException("Add a managed device and credential first.");
            if (!int.TryParse(port.Text, out var number) || !int.TryParse(timeout.Text, out var seconds))
                throw new ArgumentException("Port and timeout must be whole numbers.");
            await repository.SaveProfileAsync(original with { DisplayName = name.Text, DeviceId = target.Id,
                CredentialId = reference.Id, Port = number, TimeoutSeconds = seconds, Kind = ConnectionKind.Ssh });
        });
        return window.ShowDialog() == true;
    }
}
