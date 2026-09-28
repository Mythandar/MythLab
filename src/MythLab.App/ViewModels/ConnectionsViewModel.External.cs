using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MythLab.App.Views;
using MythLab.Core.Connections;
using Microsoft.Extensions.Logging;
namespace MythLab.App.ViewModels;

public sealed record DeviceConnectionAction(Guid DeviceId, ConnectionKind Kind);
public partial class ConnectionsViewModel
{
    private CancellationTokenSource? externalCancellation;
    [ObservableProperty] private bool isLaunching;
    [RelayCommand] private void CancelExternal() => externalCancellation?.Cancel();
    [RelayCommand] private Task AddExternalAsync(DeviceCardViewModel? card) => RunAsync(async () =>
    {
        if (ExternalProfileDialog.Edit(repository, await devices.ListAsync(), null, card?.Id)) await LoadAsync();
    });
    [RelayCommand] private Task EditExternalAsync(DeviceCardViewModel? card) => RunAsync(async () =>
    {
        if (card is null) return;
        var matches = (await repository.ListProfilesAsync()).Where(p => p.DeviceId == card.Id && p.Kind != ConnectionKind.Ssh)
            .OrderBy(p => p.DisplayName).ToArray();
        var profile = ExternalProfileDialog.Choose(matches);
        if (profile is not null && ExternalProfileDialog.Edit(repository, await devices.ListAsync(), profile, card.Id)) await LoadAsync();
    });
    [RelayCommand] private async Task DeviceExternalAsync(DeviceConnectionAction? action)
    {
        if (action is null || IsBusy) return;
        ConnectionProfile? selected = null;
        await RunAsync(async () =>
        {
            var matches = (await repository.ListProfilesAsync()).Where(p => p.DeviceId == action.DeviceId && p.Kind == action.Kind)
                .OrderBy(p => p.DisplayName).ToArray();
            selected = ExternalProfileDialog.Choose(matches);
        });
        if (selected is not null) await LaunchExternalAsync(selected);
    }
    private Task LaunchExternalAsync(ConnectionProfile profile) => RunAsync(async () =>
    {
        if (external is null) throw new InvalidOperationException("External launcher services are unavailable.");
        using var cancellation = new CancellationTokenSource();
        externalCancellation = cancellation;
        IsLaunching = true;
        Message = profile.ReadinessPort is null ? "Opening connection..." : "Checking TCP service readiness...";
        try
        {
            var current = (await repository.ListProfilesAsync(cancellation.Token)).FirstOrDefault(p => p.Id == profile.Id)
                ?? throw new InvalidOperationException("This connection profile was deleted.");
            var device = (await devices.ListAsync(cancellation.Token)).FirstOrDefault(d => d.Id == current.DeviceId)
                ?? throw new InvalidOperationException("This device was deleted.");
            logger.LogInformation("External connection requested; profile {ProfileId}; kind {ConnectionKind}; readiness {ReadinessEnabled}", current.Id, current.Kind, current.ReadinessPort is not null);
            await external.LaunchAsync(current, device, cancellation.Token);
            Message = current.Kind == ConnectionKind.Moonlight && string.IsNullOrWhiteSpace(current.RemoteApplication)
                ? "Moonlight opened. Choose the host and app in its picker."
                : "Connection handed to the external application.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { Message = "Connection launch cancelled."; }
        finally { externalCancellation = null; IsLaunching = false; }
    });
}
