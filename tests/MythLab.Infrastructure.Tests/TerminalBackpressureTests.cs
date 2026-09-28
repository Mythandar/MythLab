using System.Text;
using MythLab.Infrastructure.Ssh;
namespace MythLab.Infrastructure.Tests;

public sealed class TerminalBackpressureTests
{
    [Fact]
    public async Task SlowConsumerPreservesMegabytesOfSplitUtf8AndAnsi()
    {
        var pipe = new BoundedTerminalOutput();
        var pattern = Encoding.UTF8.GetBytes("\x1b[32m\U0001F30D burst \x1b[0m\r\n");
        var expected = Enumerable.Range(0, 4 * 1024 * 1024).Select(i => pattern[i % pattern.Length]).ToArray();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var producer = Task.Run(async () => { await pipe.WriteAsync(expected, timeout.Token); pipe.Complete(); });
        await Task.Delay(100, timeout.Token);
        Assert.False(producer.IsCompleted); // queue is full, producer is waiting, not disconnecting
        using var actual = new MemoryStream();
        var buffer = new byte[4093]; // deliberately split code points and ANSI sequences
        var reads = 0;
        int count;
        while ((count = await pipe.ReadAsync(buffer, timeout.Token)) != 0)
        {
            actual.Write(buffer, 0, count);
            if (++reads % 8 == 0) await Task.Delay(2, timeout.Token);
        }
        await producer;
        Assert.Equal(expected, actual.ToArray());
    }
    [Fact]
    public async Task CancellationReleasesProducerWaitingForCapacity()
    {
        var pipe = new BoundedTerminalOutput();
        using var stop = new CancellationTokenSource();
        var producer = pipe.WriteAsync(new byte[2 * 1024 * 1024], stop.Token).AsTask();
        Assert.False(producer.IsCompleted);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => producer.WaitAsync(TimeSpan.FromSeconds(2)));
        pipe.Complete();
    }
    [Fact]
    public async Task CancellationReleasesEmptyReader()
    {
        var pipe = new BoundedTerminalOutput();
        using var stop = new CancellationTokenSource();
        var read = pipe.ReadAsync(new byte[8192], stop.Token);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(2)));
    }
    [Fact]
    public async Task TransportFailureReleasesFullProducer()
    {
        var pipe = new BoundedTerminalOutput();
        var producer = pipe.WriteAsync(new byte[2 * 1024 * 1024], CancellationToken.None).AsTask();
        pipe.Complete(new IOException("Fixture transport failure"));
        await Assert.ThrowsAsync<System.Threading.Channels.ChannelClosedException>(() => producer.WaitAsync(TimeSpan.FromSeconds(2)));
    }
}
