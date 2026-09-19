namespace RemoteManager.Core.Settings;

public enum AppTheme { System, Light, Dark }
public sealed record AppSettings
{
    public AppTheme Theme { get; init; } = AppTheme.System;
    public int PollingIntervalSeconds { get; init; } = 30;
    public int DiscoveryConcurrency { get; init; } = 32;
    public int ProbeTimeoutMilliseconds { get; init; } = 750;
    public void Validate()
    {
        if (!Enum.IsDefined(Theme)) throw new ArgumentException("Select a valid theme.");
        if (PollingIntervalSeconds is < 5 or > 3600) throw new ArgumentException("Polling interval must be 5–3600 seconds.");
        if (DiscoveryConcurrency is < 1 or > 64) throw new ArgumentException("Discovery concurrency must be 1–64.");
        if (ProbeTimeoutMilliseconds is < 100 or > 10000) throw new ArgumentException("Probe timeout must be 100–10000 ms.");
    }
}
