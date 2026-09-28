using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MythLab.Core.Connections;
namespace MythLab.Infrastructure.Connections;

public sealed class WindowsConnectionLauncher(ILogger<WindowsConnectionLauncher> logger) : IConnectionLauncher
{
    internal Func<string, bool> FileExists { get; init; } = File.Exists;
    // Public construction boundary makes argument preservation testable without starting processes.
    public static ProcessStartInfo CreateStartInfo(LaunchRequest request)
    {
        if (request.WebUri is { } uri)
        {
            if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0)
                throw new ArgumentException("Only HTTP/HTTPS URLs without credentials can be opened.");
            return new(uri.AbsoluteUri) { UseShellExecute = true }; // Windows default browser; never a native command string.
        }
        ExternalConnections.ValidateExecutablePath(request.Executable);
        var start = new ProcessStartInfo(request.Executable) { UseShellExecute = false };
        foreach (var argument in request.Arguments) start.ArgumentList.Add(argument);
        return start;
    }
    public Task LaunchAsync(LaunchRequest request, CancellationToken token = default) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var start = CreateStartInfo(request); // validate before touching the filesystem
        if (request.WebUri is null && !FileExists(request.Executable))
            throw new InvalidOperationException("The configured executable is missing. Edit this connection and choose its new location.");
        try
        {
            using var process = Process.Start(start);
            logger.LogInformation("External connection launched; web {IsWeb}", request.WebUri is not null);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            logger.LogWarning("External launch failed; category {FailureCategory}", ex.GetType().Name);
            throw new InvalidOperationException("Windows could not start this connection. Check installation, executable permissions or the default browser.");
        }
    }, token);
}

public sealed class WindowsExecutableLocator : IExecutableLocator
{
    internal Func<string, bool> FileExists { get; init; } = File.Exists;
    internal Func<string, string> FullPath { get; init; } = p => Path.GetFullPath(p, AppContext.BaseDirectory);
    public LauncherAvailability Locate(ConnectionProfile profile)
    {
        try
        {
            var configured = profile.ExecutablePath;
            if (!string.IsNullOrEmpty(configured)) ExternalConnections.ValidateExecutablePath(configured);
            if (profile.Kind == ConnectionKind.Rdp) configured = Path.Combine(Environment.SystemDirectory, "mstsc.exe");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                ExternalConnections.ValidateExecutablePath(configured);
                var full = FullPath(configured);
                ExternalConnections.ValidateExecutablePath(full);
                return FileExists(full) ? new(true, full, "Configured executable available") :
                    new(false, full, "The configured executable is missing. Edit this connection to choose its new location.");
            }
            var candidates = Candidates(profile.Kind);
            foreach (var path in candidates)
            {
                ExternalConnections.ValidateExecutablePath(path);
                if (FileExists(path)) return new(true, path, "Installed executable detected");
            }
            return new(false, "", ExternalConnections.Label(profile.Kind) + " is not installed or detected. Edit this connection and choose an executable.");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        { return new(false, "", "The executable path cannot be used. Edit this connection and choose a valid .exe path."); }
    }
    private static IEnumerable<string> Candidates(ConnectionKind kind)
    {
        if (kind == ConnectionKind.LocalPowerShell)
        {
            yield return Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            yield break;
        }
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) }.Where(s => s.Length > 0).Distinct())
        {
            if (kind == ConnectionKind.RealVnc)
            {
                yield return Path.Combine(root, "RealVNC", "VNC Viewer", "vncviewer.exe");
                yield return Path.Combine(root, "Programs", "RealVNC", "VNC Viewer", "vncviewer.exe");
            }
            if (kind == ConnectionKind.Moonlight)
            {
                yield return Path.Combine(root, "Moonlight Game Streaming", "Moonlight.exe");
                yield return Path.Combine(root, "Moonlight", "Moonlight.exe");
                yield return Path.Combine(root, "Programs", "Moonlight Game Streaming", "Moonlight.exe");
            }
        }
    }
}
