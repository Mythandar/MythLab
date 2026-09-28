using System.Threading.Channels;
namespace MythLab.Infrastructure.Ssh;

// Single producer and consumer; byte chunks never exceed 8 KiB.
internal sealed class BoundedTerminalOutput
{
    internal const int ChunkSize = 8192, Capacity = 64;
    private readonly Channel<byte[]> channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(Capacity)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private byte[]? pending;
    private int offset;
    internal async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        while (!bytes.IsEmpty)
        {
            var count = Math.Min(bytes.Length, ChunkSize);
            await channel.Writer.WriteAsync(bytes[..count].ToArray(), token).ConfigureAwait(false);
            bytes = bytes[count..];
        }
    }
    internal async Task<int> ReadAsync(byte[] buffer, CancellationToken token)
    {
        if (buffer.Length == 0) return 0;
        if (pending is null)
        {
            if (!await channel.Reader.WaitToReadAsync(token).ConfigureAwait(false)) return 0;
            pending = await channel.Reader.ReadAsync(token).ConfigureAwait(false);
            offset = 0;
        }
        var count = Math.Min(buffer.Length, pending.Length - offset);
        pending.AsSpan(offset, count).CopyTo(buffer);
        offset += count;
        if (offset == pending.Length) pending = null;
        return count;
    }
    internal void Complete(Exception? error = null) => channel.Writer.TryComplete(error);
}
