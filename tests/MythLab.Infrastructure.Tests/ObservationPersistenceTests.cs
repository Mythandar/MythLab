using MythLab.Core.Devices;
using MythLab.Core.Monitoring;
using MythLab.Infrastructure.Storage;
namespace MythLab.Infrastructure.Tests;
public sealed class ObservationPersistenceTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "MythLab.Tests", Guid.NewGuid().ToString("N"));
    private SqliteDeviceRepository Create() => new(Path.Combine(folder, "inventory.db"));
    private static Device Device => new() { DisplayName = "PC", IPv4Address = "10.0.0.2", MacAddress = "02:11:22:33:44:55", Wake = new() { Capability = WakeCapability.EnabledUnverified } };
    [Fact]
    public async Task ObservationPreservesEditsAndCannotResurrectDeletedDevices()
    {
        using var repo = Create();
        await repo.InitializeAsync();
        var original = Device;
        await repo.SaveAsync(original);
        await repo.SaveAsync(original with { Notes = "Edited during probe" });
        var now = DateTimeOffset.UtcNow;
        var saved = await repo.ApplyObservationAsync(original, new(DeviceState.Online, now, "reply"), true);
        Assert.NotNull(saved);
        Assert.Equal("Edited during probe", saved.Notes);
        Assert.Equal(now, saved.LastSeen);
        Assert.Equal(WakeCapability.Verified, saved.Wake.Capability);
        await repo.SaveAsync(original with { Notes = "Stale editor snapshot" });
        saved = Assert.Single(await repo.ListAsync());
        Assert.Equal(now, saved.LastSeen);
        Assert.Equal(WakeCapability.Verified, saved.Wake.Capability);
        await repo.DeleteAsync(original.Id);
        Assert.Null(await repo.ApplyObservationAsync(original, new(DeviceState.Online, now.AddSeconds(1), "")));
        Assert.Empty(await repo.ListAsync());
    }
    [Fact]
    public async Task LateProbeAndChangedTargetAreRejected()
    {
        using var repo = Create();
        await repo.InitializeAsync();
        var device = Device;
        await repo.SaveAsync(device);
        var now = DateTimeOffset.UtcNow;
        await repo.ApplyObservationAsync(device, new(DeviceState.Online, now, ""));
        Assert.Null(await repo.ApplyObservationAsync(device, new(DeviceState.Offline, now.AddSeconds(-1), "")));
        await repo.SaveAsync(device with { IPv4Address = "10.0.0.3" });
        Assert.Null(await repo.ApplyObservationAsync(device, new(DeviceState.Online, now.AddSeconds(1), ""), true));
        Assert.Equal(DeviceState.Unknown, Assert.Single(await repo.ListAsync()).LastKnownState);
    }
    [Fact]
    public async Task ChangedWakeSettingsCannotBeVerifiedByAnOldOperation()
    {
        using var repo = Create();
        await repo.InitializeAsync();
        var device = Device;
        await repo.SaveAsync(device);
        await repo.SaveAsync(device with { Wake = device.Wake with { Port = 7 } });
        var saved = await repo.ApplyObservationAsync(device, new(DeviceState.Online, DateTimeOffset.UtcNow, ""), true);
        Assert.Equal(WakeCapability.EnabledUnverified, saved!.Wake.Capability);
        Assert.Equal(7, saved.Wake.Port);
        await Assert.ThrowsAsync<ArgumentException>(() => repo.ApplyObservationAsync(device, new(DeviceState.Waking, DateTimeOffset.UtcNow, "")));
    }
    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
}
