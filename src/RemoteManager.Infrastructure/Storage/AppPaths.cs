namespace RemoteManager.Infrastructure.Storage;

public sealed class AppPaths
{
    public string Root { get; }
    public string Database => Path.Combine(Root, "inventory.db");
    public string Settings => Path.Combine(Root, "settings.json");
    public string Logs => Path.Combine(Root, "logs");
    public AppPaths(string applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId) || applicationId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            applicationId is "." or "..")
            throw new ArgumentException("Application ID must be a directory name.", nameof(applicationId));
        Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), applicationId);
    }
    public void EnsureCreated() { Directory.CreateDirectory(Root); Directory.CreateDirectory(Logs); }
}
