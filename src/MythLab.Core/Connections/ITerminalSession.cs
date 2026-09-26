namespace MythLab.Core.Connections;
public interface ITerminalSession : IAsyncDisposable
{
    Task<int> ReadAsync(byte[] buffer, CancellationToken token);
    Task WriteAsync(byte[] data, CancellationToken token);
    Task ResizeAsync(int columns, int rows, CancellationToken token);
}
