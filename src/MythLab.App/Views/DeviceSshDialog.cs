using System.IO;
using System.Windows;
using System.Windows.Controls;
using MythLab.Core.Connections;
using MythLab.Core.Credentials;
using MythLab.Core.Devices;
namespace MythLab.App.Views;

public static class DeviceSshDialog
{
    public static ConnectionProfile? ChooseProfile(Device device, IReadOnlyList<ConnectionProfile> profiles)
    {
        if (profiles.Count <= 1) return profiles.FirstOrDefault();
        var window = new MetadataEditor("Choose SSH connection — " + device.DisplayName) { Height = 360 };
        var choice = window.Choice("SSH profile", profiles, nameof(ConnectionProfile.DisplayName), profiles[0]);
        window.OnSave(() => Task.CompletedTask, "Continue");
        return window.ShowDialog() == true ? (ConnectionProfile)choice.SelectedItem : null;
    }

    public static ConnectionProfile? Edit(Device device, ConnectionProfile? existing, CredentialReference? credential,
        int sharedProfiles, IConnectionRepository repository, CredentialSaveService saver, bool connectAfterSave)
    {
        var profile = existing ?? new ConnectionProfile { DeviceId = device.Id, DisplayName = "SSH", Kind = ConnectionKind.Ssh, Port = 22, TimeoutSeconds = 30 };
        var defaultLabel = device.DisplayName + " — SSH";
        var original = credential ?? new CredentialReference { DisplayLabel = defaultLabel[..Math.Min(120, defaultLabel.Length)] };
        var window = new MetadataEditor((existing is null ? "Set up SSH — " : "Edit SSH — ") + device.DisplayName) { Name = "DeviceSshEditor" };
        var target = window.TextField("Device", device.DisplayName + " — " + device.Endpoint);
        target.IsReadOnly = true;
        var username = window.TextField("Username", original.Username);
        window.Fields.Children.Add(new TextBlock { Text = existing is null ? "Password" : "New password / passphrase (leave blank to keep saved secret)", TextWrapping = TextWrapping.Wrap });
        var password = new PasswordBox { Margin = new(0, 4, 0, 12), Padding = new(8) };
        System.Windows.Automation.AutomationProperties.SetName(password, "SSH secret");
        window.Fields.Children.Add(password);
        if (sharedProfiles > 1)
            window.Fields.Children.Add(new TextBlock { Text = $"This credential is shared by {sharedProfiles} profiles. Username, authentication and secret changes apply to all of them.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) });
        var advancedStart = window.Fields.Children.Count;
        var name = window.TextField("Connection name", profile.DisplayName);
        var port = window.TextField("SSH port", (profile.Port ?? 22).ToString());
        var timeout = window.TextField("Connection timeout (seconds)", profile.TimeoutSeconds.ToString());
        var label = window.TextField("Credential label", original.DisplayLabel);
        var auth = window.Choice("Authentication", Enum.GetValues<AuthenticationKind>(), "", original.Authentication);
        var key = window.TextField("Private-key path", original.PrivateKeyPath);
        var browse = new Button { Content = "Browse key…" };
        browse.Click += (_, _) => { var picker = new Microsoft.Win32.OpenFileDialog(); if (picker.ShowDialog(window) == true) key.Text = picker.FileName; };
        window.Fields.Children.Add(browse);
        var replace = new CheckBox { Content = "Replace saved secret (blank clears a key passphrase)", Margin = new(0, 8, 0, 8) };
        password.PasswordChanged += (_, _) => replace.IsChecked = password.Password.Length > 0;
        window.Fields.Children.Add(replace);
        var advanced = new StackPanel();
        while (window.Fields.Children.Count > advancedStart)
        {
            var child = window.Fields.Children[advancedStart];
            window.Fields.Children.RemoveAt(advancedStart);
            advanced.Children.Add(child);
        }
        window.Fields.Children.Add(new Expander { Header = "Advanced SSH settings", Content = advanced, IsExpanded = original.Authentication == AuthenticationKind.PrivateKey, Margin = new(0, 8, 0, 12) });
        window.Fields.Children.Add(new TextBlock { Text = "Saved to Connections / Credentials. Secrets stay in Windows Credential Manager.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
        ConnectionProfile? saved = null;
        window.OnSave(async () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || name.Text.Trim().Length > 120 ||
                !int.TryParse(port.Text, out var number) || number is < 1 or > 65535 ||
                !int.TryParse(timeout.Text, out var seconds) || seconds is < 5 or > 300)
                throw new ArgumentException("Enter a connection name, port 1–65535 and timeout 5–300 seconds.");
            var updated = original with { DisplayLabel = label.Text.Trim(), Username = username.Text.Trim(),
                Authentication = (AuthenticationKind)auth.SelectedItem, PrivateKeyPath = key.Text.Trim() };
            if (updated.Authentication == AuthenticationKind.PrivateKey &&
                !File.Exists(Path.GetFullPath(updated.PrivateKeyPath, AppContext.BaseDirectory)))
                throw new ArgumentException("Select an existing private-key file.");
            var updatedProfile = profile with { DisplayName = name.Text.Trim(), Port = number, TimeoutSeconds = seconds, CredentialId = updated.Id };
            await saver.SaveAsync(updated, replace.IsChecked == true, replace.IsChecked == true ? password.Password : null);
            try { await repository.SaveProfileAsync(updatedProfile); }
            catch { throw new InvalidOperationException("The credential was saved, but the SSH profile could not be saved. Retry Save, or manage the saved credential under Connections / Credentials."); }
            password.Clear();
            saved = updatedProfile;
        }, connectAfterSave ? "Save & connect" : "Save");
        return window.ShowDialog() == true ? saved : null;
    }
}
