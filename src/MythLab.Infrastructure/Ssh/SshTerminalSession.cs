using System.Threading.Channels;
using MythLab.Core.Connections;
using Renci.SshNet;
namespace MythLab.Infrastructure.Ssh;

internal sealed class SshTerminalSession : ITerminalSession
{
    private readonly SshClient client;
    private readonly ShellStream shell;
    private readonly Channel<byte[]> output = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64) { SingleReader = true });
    private readonly object drainGate = new();
    private byte[]? pending;
    private int pendingOffset, disposed;
    private Task? disposal;
    public SshTerminalSession(SshClient client, ShellStream shell)
    {
        this.client = client; this.shell = shell;
        shell.DataReceived += (_, _) => Drain();
        shell.Closed += (_, _) => { Drain(); output.Writer.TryComplete(); };
        shell.ErrorOccurred += (_, _) => output.Writer.TryComplete(new IOException("SSH shell transport failed."));
        Drain();
    }
    private void Drain()
    {
        lock (drainGate)
        {
            if (Volatile.Read(ref disposed) != 0) return;
            try
            {
                while (shell.DataAvailable)
                {
                    var buffer = new byte[8192];
                    var count = shell.Read(buffer, 0, buffer.Length);
                    if (count == 0) break;
                    if (count != buffer.Length) Array.Resize(ref buffer, count);
                    if (!output.Writer.TryWrite(buffer))
                    {
                        output.Writer.TryComplete(new IOException("Terminal output exceeded its bounded queue. Reconnect after reducing output."));
                        _ = DisposeAsync();
                        break;
                    }
                }
            }
            catch (ObjectDisposedException) { output.Writer.TryComplete(); }
        }
    }
    public async Task<int> ReadAsync(byte[] buffer, CancellationToken token)
    {
        if (pending is null)
        {
            if (!await output.Reader.WaitToReadAsync(token).ConfigureAwait(false)) return 0;
            pending = await output.Reader.ReadAsync(token).ConfigureAwait(false);
            pendingOffset = 0;
        }
        var count = Math.Min(buffer.Length, pending.Length - pendingOffset);
        pending.AsSpan(pendingOffset, count).CopyTo(buffer);
        pendingOffset += count;
        if (pendingOffset == pending.Length) pending = null;
        return count;
    }
    public async Task WriteAsync(byte[] data, CancellationToken token)
    {
        await shell.WriteAsync(data, 0, data.Length, token).ConfigureAwait(false);
        await shell.FlushAsync(token).ConfigureAwait(false);
    }
    public Task ResizeAsync(int columns, int rows, CancellationToken token)
    {
        if (columns is < 2 or > 500 || rows is < 1 or > 300) throw new ArgumentException("Invalid terminal size.");
        return Task.Run(() => { token.ThrowIfCancellationRequested(); shell.ChangeWindowSize((uint)columns, (uint)rows, 0, 0); }, token);
    }
    public ValueTask DisposeAsync()
    {
        lock (drainGate)
        {
            if (disposal is not null) return new(disposal);
            Interlocked.Exchange(ref disposed, 1);
            output.Writer.TryComplete();
            disposal = Task.Run(() => { try { shell.Dispose(); } finally { client.Dispose(); } });
            return new(disposal);
        }
    }
}