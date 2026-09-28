using MythLab.Core.Connections;
using Renci.SshNet;
namespace MythLab.Infrastructure.Ssh;

internal sealed class SshTerminalSession : ITerminalSession
{
    private readonly SshClient client;
    private readonly ShellStream shell;
    private readonly BoundedTerminalOutput output = new();
    private readonly CancellationTokenSource stopped = new();
    private readonly object drainGate = new(), disposeGate = new();
    private int disposed;
    private Task? disposal;
    public SshTerminalSession(SshClient client, ShellStream shell)
    {
        this.client = client; this.shell = shell;
        shell.DataReceived += (_, _) => Drain();
        shell.Closed += (_, _) => { Drain(); output.Complete(); };
        shell.ErrorOccurred += (_, _) => output.Complete(new IOException("SSH shell transport failed."));
        _ = Task.Run(Drain); // Initial buffered data must never block a caller/UI thread.
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
                    // SSH.NET invokes DataReceived synchronously on its receive worker.
                    // Waiting here throttles the transport instead of growing ShellStream's
                    // internal buffer. Never dispatch this callback to the WPF thread.
                    output.WriteAsync(buffer, stopped.Token).AsTask().GetAwaiter().GetResult();
                }
            }
            catch (OperationCanceledException) when (stopped.IsCancellationRequested) { output.Complete(); }
            catch (System.Threading.Channels.ChannelClosedException) { }
            catch (ObjectDisposedException) { output.Complete(); }
        }
    }
    public Task<int> ReadAsync(byte[] buffer, CancellationToken token) => output.ReadAsync(buffer, token);
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
        lock (disposeGate)
        {
            if (disposal is not null) return new(disposal);
            Interlocked.Exchange(ref disposed, 1);
            stopped.Cancel();
            output.Complete();
            disposal = Task.Run(() => { try { shell.Dispose(); } finally { client.Dispose(); } });
            return new(disposal);
        }
    }
}