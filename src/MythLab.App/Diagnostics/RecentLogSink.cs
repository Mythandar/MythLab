using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace MythLab.App.Diagnostics;

public sealed class RecentLogSink : ILogEventSink
{
    private readonly ConcurrentQueue<string> entries = new();
    public void Emit(LogEvent logEvent)
    {
        entries.Enqueue($"{logEvent.Timestamp:HH:mm:ss}  {logEvent.Level}  {logEvent.RenderMessage()}");
        while (entries.Count > 200) entries.TryDequeue(out _);
    }
    public string Snapshot() => string.Join(Environment.NewLine, entries.Reverse());
}
