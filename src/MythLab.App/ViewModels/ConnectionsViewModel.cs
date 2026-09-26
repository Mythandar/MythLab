using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MythLab.App.Views;
using MythLab.Core.Connections;
using MythLab.Core.Credentials;
using MythLab.Core.Devices;
using MythLab.Infrastructure.Ssh;
using MythLab.Infrastructure.Storage;
namespace MythLab.App.ViewModels;

public sealed record CredentialRow(CredentialReference Credential, string Availability)
{
    public string Label => Credential.DisplayLabel;
    public string Username => Credential.Username;
    public AuthenticationKind Authentication => Credential.Authentication;
}
public sealed record ProfileRow(ConnectionProfile Profile, string DeviceName, string CredentialLabel)
{
    public string Name => Profile.DisplayName;
    public int? Port => Profile.Port;
}
public partial class ConnectionsViewModel(IConnectionRepository repository, IDeviceRepository devices,
    ICredentialStore secrets, KnownHostsStore knownHosts, SshSessionService ssh, AppPaths paths,
    ILogger<ConnectionsViewModel> logger) : ObservableObject
{
    public ObservableCollection<CredentialRow> Credentials { get; } = [];
    public ObservableCollection<ProfileRow> Profiles { get; } = [];
    public ObservableCollection<KnownHost> KnownHosts { get; } = [];
    private readonly HashSet<TerminalWindow> terminals = [];
    [ObservableProperty] private CredentialRow? selectedCredential;
    [ObservableProperty] private ProfileRow? selectedProfile;
    [ObservableProperty] private KnownHost? selectedHost;
    [ObservableProperty] private string message = "Create a reusable credential, then add an SSH profile for a managed device.";
    [ObservableProperty] private bool isBusy;
    public event EventHandler? ProfilesChanged;
    public async Task LoadAsync(CancellationToken token = default)
    {
        var savedCredentials = await repository.ListCredentialsAsync(token);
        var savedProfiles = await repository.ListProfilesAsync(token);
        var savedDevices = await devices.ListAsync(token);
        Credentials.Clear();
        foreach (var credential in savedCredentials)
        {
            var availability = await secrets.InspectAsync(credential.Id, token);
            var label = availability == SecretStatus.Missing && credential.Authentication == AuthenticationKind.PrivateKey
                ? "No saved passphrase (optional)" : availability == SecretStatus.Missing ? "Missing credential — enter / recreate" : availability.ToString();
            Credentials.Add(new(credential, label));
        }
        Profiles.Clear();
        foreach (var profile in savedProfiles)
            Profiles.Add(new(profile, savedDevices.FirstOrDefault(d => d.Id == profile.DeviceId)?.DisplayName ?? "Missing device",
                savedCredentials.FirstOrDefault(c => c.Id == profile.CredentialId)?.DisplayLabel ?? "Missing credential"));
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
        KnownHosts.Clear();
        try { foreach (var host in await knownHosts.ListAsync(token)) KnownHosts.Add(host); }
        catch (Exception ex) when (ex is System.IO.IOException or System.Text.Json.JsonException or FormatException)
        { Message = "Known-host records could not be read. SSH remains blocked until Data/ssh/known-hosts.json is repaired."; }
    }
    [RelayCommand] private Task RefreshAsync() => RunAsync(() => LoadAsync());
    [RelayCommand] private Task AddCredentialAsync() => RunAsync(async () =>
    {
        if (ConnectionDialogs.EditCredential(repository, secrets, null)) { await LoadAsync(); Message = "Credential saved securely by Windows."; }
    });
    [RelayCommand] private Task EditCredentialAsync() => RunAsync(async () =>
    {
        if (SelectedCredential is { } row && ConnectionDialogs.EditCredential(repository, secrets, row.Credential))
        { await LoadAsync(); Message = "Credential updated; existing profiles retain their reference."; }
    });
    [RelayCommand] private Task DeleteCredentialAsync() => RunAsync(async () =>
    {
        if (SelectedCredential is not { } row || !Confirm("Delete this credential and its Windows secret?")) return;
        await repository.DeleteCredentialAsync(row.Credential.Id);
        try { await secrets.DeleteAsync(row.Credential.Id); }
        catch { await repository.SaveCredentialAsync(row.Credential); throw; }
        await LoadAsync();
    });
    [RelayCommand] private Task AddProfileAsync() => RunAsync(async () =>
    {
        if (ConnectionDialogs.EditProfile(repository, await devices.ListAsync(), await repository.ListCredentialsAsync(), null)) await LoadAsync();
    });
    [RelayCommand] private Task EditProfileAsync() => RunAsync(async () =>
    {
        if (SelectedProfile is { } row && ConnectionDialogs.EditProfile(repository, await devices.ListAsync(), await repository.ListCredentialsAsync(), row.Profile)) await LoadAsync();
    });
    [RelayCommand] private Task DeleteProfileAsync() => RunAsync(async () =>
    {
        if (SelectedProfile is { } row && Confirm("Delete this connection profile?"))
        { await repository.DeleteProfileAsync(row.Profile.Id); await LoadAsync(); }
    });
    [RelayCommand] private Task RemoveHostAsync() => RunAsync(async () =>
    {
        if (SelectedHost is { } host && Confirm($"Remove trust for {host.Host}:{host.Port}?\n{host.Fingerprint}\nVerify any changed fingerprint before trusting it again."))
        { await knownHosts.RemoveAsync(host); await LoadAsync(); }
    });
    [RelayCommand]
    private void Connect(ConnectionProfile? profile)
    {
        profile ??= SelectedProfile?.Profile;
        if (profile is null || IsBusy) return;
        var id = profile.Id;
        var window = new TerminalWindow(paths, profile.DisplayName + " — " + AppIdentity.DisplayName, async token =>
        {
            var current = (await repository.ListProfilesAsync(token)).FirstOrDefault(p => p.Id == id)
                ?? throw new InvalidOperationException("This connection profile was deleted.");
            var device = (await devices.ListAsync(token)).FirstOrDefault(d => d.Id == current.DeviceId)
                ?? throw new InvalidOperationException("The managed device was deleted.");
            var credential = (await repository.ListCredentialsAsync(token)).FirstOrDefault(c => c.Id == current.CredentialId)
                ?? throw new InvalidOperationException("The credential reference is missing. Edit this profile and select a credential.");
            return await ssh.ConnectAsync(device, current, credential, host =>
                Application.Current.Dispatcher.Invoke(() => MessageBox.Show(Application.Current.MainWindow,
                    $"First connection to {host.Host}:{host.Port}\nAlgorithm: {host.Algorithm}\n{host.Fingerprint}\n\nVerify this fingerprint independently. Trust this SSH server?",
                    "Trust SSH host", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes), token);
        }) { Owner = Application.Current.MainWindow };
        terminals.Add(window);
        window.Closed += (_, _) => terminals.Remove(window);
        window.Show();
    }
    public async Task StopAsync()
    {
        foreach (var window in terminals.ToArray()) await window.CloseSessionAsync();
    }
    private static bool Confirm(string text) => MessageBox.Show(Application.Current.MainWindow, text, AppIdentity.DisplayName,
        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await action(); }
        catch (Exception ex)
        {
            logger.LogWarning("Connection management failed; category {FailureCategory}", ex.GetType().Name);
            Message = ex is ArgumentException or InvalidOperationException or CredentialStoreException ? ex.Message :
                $"Could not finish ({ex.GetType().Name}). Check portable storage and Windows credential access.";
        }
        finally { IsBusy = false; }
    }
}
