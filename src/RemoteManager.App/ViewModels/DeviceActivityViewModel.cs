using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteManager.Core.Devices;
using RemoteManager.Core.Monitoring;
using RemoteManager.Core.Settings;
using RemoteManager.Core.WakeOnLan;
using RemoteManager.Infrastructure.WakeOnLan;
namespace RemoteManager.App.ViewModels;

/// <summary>UI-thread coordinator; all network waits yield, with at most eight status probes per sweep.</summary>
public partial class DeviceActivityViewModel(IDeviceRepository repository, IDeviceStatusService status,
    WakeDeviceService wake, ILogger<DeviceActivityViewModel> logger) : ObservableObject
{
    public ObservableCollection<DeviceCardViewModel> Cards { get; } = [];
    public AppSettings Settings { get; set; } = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<Guid, CancellationTokenSource> waking = [];
    private readonly HashSet<Task> operations = [];
    private Task? monitor;
    private bool stopped;
    private bool checking;
    public void Synchronize(IReadOnlyList<Device> devices)
    {
        foreach (var card in Cards.Where(c => devices.All(d => d.Id != c.Id)).ToArray())
        {
            CancelWake(card);
            card.Generation++;
            Cards.Remove(card);
        }
        foreach (var device in devices)
        {
            var card = Cards.FirstOrDefault(c => c.Id == device.Id);
            if (card is null) Cards.Add(new(device));
            else
            {
                if (!DeviceTargets.SameWake(card.Device, device) ||
                    card.Wake.Capability != device.Wake.Capability && device.Wake.Capability == WakeCapability.Disabled)
                {
                    CancelWake(card);
                    card.Generation++;
                    card.State = DeviceState.Unknown;
                    card.Detail = "Configuration changed; awaiting a live status check.";
                }
                card.Update(device);
            }
        }
        // Keep names sorted without replacing cards and losing live operation state.
        foreach (var pair in Cards.OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray().Select((c, i) => (c, i)))
            Cards.Move(Cards.IndexOf(pair.c), pair.i);
    }
    public void Start() => monitor ??= MonitorAsync();
    private async Task MonitorAsync()
    {
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                await CheckNowAsync();
                await Task.Delay(TimeSpan.FromSeconds(Settings.PollingIntervalSeconds), lifetime.Token);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Status monitor failed; category {FailureCategory}", ex.GetType().Name);
                try { await Task.Delay(TimeSpan.FromSeconds(5), lifetime.Token); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
    [RelayCommand]
    private async Task CheckNowAsync()
    {
        if (stopped || checking) return;
        checking = true;
        var task = CheckAllAsync();
        operations.Add(task);
        try { await task; }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { operations.Remove(task); checking = false; }
    }
    private async Task CheckAllAsync()
    {
        foreach (var batch in Cards.ToArray().Chunk(8))
        {
            lifetime.Token.ThrowIfCancellationRequested();
            await Task.WhenAll(batch.Select(CheckOneAsync));
        }
    }
    private async Task CheckOneAsync(DeviceCardViewModel card)
    {
        if (card.IsWaking || !Cards.Contains(card)) return;
        var generation = card.Generation;
        var device = card.Device;
        try
        {
            var observation = await status.CheckAsync(device, Settings.ProbeTimeoutMilliseconds, lifetime.Token);
            if (generation != card.Generation || !Cards.Contains(card)) return;
            await ApplyAsync(card, device, observation, false, generation, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (generation == card.Generation)
            {
                card.State = DeviceState.Unknown;
                card.Detail = $"Status check could not finish ({ex.GetType().Name}). See Diagnostics.";
            }
            logger.LogWarning("Status check failed for {DeviceId}; category {FailureCategory}", device.Id, ex.GetType().Name);
        }
    }
    private async Task ApplyAsync(DeviceCardViewModel card, Device expected, StatusObservation observation,
        bool verify, int generation, CancellationToken token)
    {
        var saved = await repository.ApplyObservationAsync(expected, observation, verify, token);
        if (saved is null || generation != card.Generation || !Cards.Contains(card)) return;
        if (card.State != observation.State)
            logger.LogInformation("Device {DeviceId} status {PreviousState} -> {State}", card.Id, card.State, observation.State);
        card.Update(saved);
        card.State = observation.State;
        card.Detail = observation.Detail;
    }
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task WakeAsync(DeviceCardViewModel? card) => BeginWakeAsync(card, false);
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task TestWakeAsync(DeviceCardViewModel? card) => BeginWakeAsync(card, true);
    private async Task BeginWakeAsync(DeviceCardViewModel? card, bool test)
    {
        if (stopped || card is null || !card.CanWake || !Cards.Contains(card)) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        waking.Add(card.Id, cancellation);
        card.IsWaking = true;
        var generation = ++card.Generation;
        var task = RunWakeAsync(card, generation, test, cancellation.Token);
        operations.Add(task);
        try { await task; }
        finally
        {
            operations.Remove(task);
            waking.Remove(card.Id);
            card.IsWaking = false;
        }
    }
    private async Task RunWakeAsync(DeviceCardViewModel card, int generation, bool test, CancellationToken token)
    {
        var device = card.Device;
        var finished = false;
        var progress = new Progress<WakeProgress>(p =>
        {
            if (finished || generation != card.Generation) return;
            card.State = p.State;
            card.Detail = (test ? "Test Wake: " : "") + p.Detail;
        });
        try
        {
            logger.LogInformation("Wake started for {DeviceId}; test {Test}", card.Id, test);
            var result = await wake.WakeAsync(device, Settings.ProbeTimeoutMilliseconds,
                TimeSpan.FromSeconds(Settings.WakeTimeoutSeconds), TimeSpan.FromSeconds(Settings.WakePollIntervalSeconds), progress, token);
            finished = true;
            if (generation != card.Generation) return;
            await ApplyAsync(card, device, result.Observation, result.Verified, generation, token);
            if (generation == card.Generation) card.Detail = result.Detail;
            logger.LogInformation("Wake finished for {DeviceId}; state {State}; verified {Verified}", card.Id, result.Observation.State, result.Verified);
        }
        catch (OperationCanceledException)
        {
            if (generation == card.Generation) { card.State = DeviceState.Unknown; card.Detail = "Wake cancelled. Packets already sent cannot be recalled."; }
        }
        catch (Exception ex)
        {
            if (generation == card.Generation)
            {
                card.State = DeviceState.Unknown;
                card.Detail = ex is ArgumentException or InvalidOperationException ? ex.Message :
                    $"Wake could not finish ({ex.GetType().Name}). Check the selected network interface and Diagnostics.";
            }
            logger.LogWarning("Wake failed for {DeviceId}; category {FailureCategory}", card.Id, ex.GetType().Name);
        }
        finally { finished = true; }
    }
    [RelayCommand]
    private void CancelWake(DeviceCardViewModel? card)
    {
        if (card is not null && waking.TryGetValue(card.Id, out var cancellation)) cancellation.Cancel();
    }
    public async Task StopAsync()
    {
        stopped = true;
        await lifetime.CancelAsync();
        if (monitor is not null) await monitor;
        await Task.WhenAll(operations.ToArray());
    }
}
