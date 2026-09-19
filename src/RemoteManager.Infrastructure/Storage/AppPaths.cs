namespace RemoteManager.Infrastructure.Storage;

public sealed class AppPaths
{
    public string Root { get; }
    public string LegacyRoot { get; }
    public string Database => Path.Combine(Root, "inventory.db");
    public string Settings => Path.Combine(Root, "settings.json");
    public string Logs => Path.Combine(Root, "logs");

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

    public void EnsureCreated() { Directory.CreateDirectory(Root); Directory.CreateDirectory(Logs); }
}
