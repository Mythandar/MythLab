namespace MythLab.Infrastructure.Storage;

public sealed class PortableDataAccessException(Exception inner) : IOException("The portable Data folder is not writable.", inner);

public sealed class AppPaths
{
    public string Root { get; }
    public string LegacyRoot { get; }
    public string Database => Path.Combine(Root, "inventory.db");
    public string Settings => Path.Combine(Root, "settings.json");
    public string Logs => Path.Combine(Root, "logs");
    public string KnownHosts => Path.Combine(Root, "ssh", "known-hosts.json");
    public string WebView2UserData => Path.Combine(Root, "WebView2");

    public AppPaths(string applicationId, string? executableDirectory = null, string? localApplicationDataDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(applicationId) || applicationId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            applicationId is "." or "..")
            throw new ArgumentException("Application ID must be a directory name.", nameof(applicationId));
        // AppContext.BaseDirectory is the executable's directory even in a single-file publish.
        // Never use the working directory or the bundle's native extraction directory.
        Root = Path.Combine(Path.GetFullPath(executableDirectory ?? AppContext.BaseDirectory), "Data");
        LegacyRoot = Path.Combine(localApplicationDataDirectory ??
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), applicationId);
    }

    public Task VerifyWritableAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        try
        {
            EnsureCreated();
            foreach (var folder in new[] { Root, Logs })
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var probe = new FileStream(Path.Combine(folder, ".write-probe-" + Guid.NewGuid().ToString("N")),
                    FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
                probe.WriteByte(0);
                probe.Flush(flushToDisk: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new PortableDataAccessException(ex); }
    }, cancellationToken);

    public void EnsureCreated() { Directory.CreateDirectory(Root); Directory.CreateDirectory(Logs); }
}
