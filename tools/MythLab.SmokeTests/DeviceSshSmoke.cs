using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using MythLab.App.ViewModels;
using MythLab.App.Views;
using MythLab.Core.Connections;
using MythLab.Infrastructure.Storage;
namespace MythLab.SmokeTests;
internal static class DeviceSshSmoke
{
    public static async Task RunAsync(ConnectionsViewModel connections, SqliteDeviceRepository repository,
        DeviceCardViewModel card, SmokeSecrets secrets)
    {
        foreach (var row in connections.Profiles.Where(p => p.Profile.DeviceId == card.Id).ToArray())
            await repository.DeleteProfileAsync(row.Profile.Id);
        await connections.LoadAsync();
        var initialCredentials = (await repository.ListCredentialsAsync()).Count;
        var stage = 0;
        var active = true;
        var terminals = 0;
        Exception? failure = null;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(async (sender, _) =>
        {
            if (!active || sender is not Window window) return;
            try
            {
                if (window is TerminalWindow terminal)
                {
                    secrets.AllowReads = false; // any accidental authentication fails before network access
                    terminals++;
                    await terminal.CloseSessionAsync();
                    return;
                }
                if (window.Title.StartsWith("Choose SSH connection"))
                {
                    All<Button>(window).Single(b => Equals(b.Content, "Continue")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    return;
                }
                if (window.Name != "DeviceSshEditor") return;
                if (stage == 0) { window.Close(); return; }
                TextBox Field(string label) => All<TextBox>(window).Single(b => AutomationProperties.GetName(b) == label);
                var target = Field("Device");
                Require(target.IsReadOnly && target.Text.Contains(card.Endpoint), "Device and endpoint must be prefilled.");
                var secret = All<PasswordBox>(window).Single();
                Require(secret.Password.Length == 0, "Saved secrets must never appear in editor.");
                if (stage == 1)
                {
                    Field("Username").Text = "quick-user";
                    secret.Password = "disposable-fixture-secret";
                    Field("SSH port").Text = "2222";
                    secrets.AllowReads = true;
                }
                else
                {
                    Require(All<TextBlock>(window).Any(t => t.Text.Contains("shared by 2")), "Shared credential edits need a visible explanation.");
                    Field("Username").Text = "edited-user";
                    Field("SSH port").Text = "2200";
                }
                await Program.RenderAsync(window, stage == 1 ? "device-ssh-setup-light.png" : "device-ssh-edit-light.png");
                All<Button>(window).Single(b => Equals(b.Content, stage == 1 ? "Save & connect" : "Save")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception ex) { failure = ex; window.Close(); }
        }));
        try
        {
            await connections.DeviceSshCommand.ExecuteAsync(card);
            Require((await repository.ListProfilesAsync()).Count == 0 &&
                (await repository.ListCredentialsAsync()).Count == initialCredentials, "Cancelling setup must save nothing.");
            stage = 1;
            await connections.DeviceSshCommand.ExecuteAsync(card);
            if (failure is not null) throw failure;
            await connections.StopAsync();
            var profile = (await repository.ListProfilesAsync()).Single();
            Require(profile.DeviceId == card.Id && profile.Port == 2222 && terminals == 1, "Save & connect must bind this device and open a terminal.");
            var credential = (await repository.ListCredentialsAsync()).Single(c => c.Id == profile.CredentialId);
            Require(credential.Username == "quick-user" && connections.Credentials.Any(c => c.Credential.Id == credential.Id), "Quick credential must appear in Connections.");
            Require(secrets.Values[credential.Id] == "disposable-fixture-secret", "Secret must use credential store.");
            await repository.SaveProfileAsync(profile with { Id = Guid.NewGuid(), DisplayName = "ZZ mirror" });
            stage = 2;
            await connections.EditDeviceSshCommand.ExecuteAsync(card);
            if (failure is not null) throw failure;
            var edited = (await repository.ListProfilesAsync()).Single(p => p.Id == profile.Id);
            Require(edited.Port == 2200 && edited.CredentialId == credential.Id, "Edit must preserve profile and credential IDs.");
            Require((await repository.ListCredentialsAsync()).Single(c => c.Id == credential.Id).Username == "edited-user" &&
                secrets.Values[credential.Id] == "disposable-fixture-secret", "Metadata edit must preserve saved secret.");
            stage = 3;
            await connections.DeviceSshCommand.ExecuteAsync(card);
            await connections.StopAsync();
            if (failure is not null) throw failure;
            Require(terminals == 2, "Configured SSH must connect without a credential editor.");
            Require((await repository.ListCredentialsAsync()).Count == initialCredentials + 1, "Editing/reconnecting must not duplicate credentials.");
            Console.WriteLine("PASS: device SSH setup/cancel/save/connect, advanced port, shared-credential edit, profile selection, and credential-tab synchronization.");
        }
        finally { active = false; secrets.AllowReads = false; }
    }
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in All<T>(child)) yield return found;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
