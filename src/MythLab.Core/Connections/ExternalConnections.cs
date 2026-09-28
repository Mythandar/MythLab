using System.Globalization;
using System.Text.RegularExpressions;
using MythLab.Core.Devices;
using MythLab.Core.Monitoring;
namespace MythLab.Core.Connections;

// Requests contain only non-secret metadata. There is deliberately no credential-store dependency.
public sealed record LaunchRequest(string Executable, IReadOnlyList<string> Arguments, Uri? WebUri = null);
public sealed record LauncherAvailability(bool Available, string Path, string Detail);
public interface IExecutableLocator { LauncherAvailability Locate(ConnectionProfile profile); }
public interface IConnectionLauncher { Task LaunchAsync(LaunchRequest request, CancellationToken token = default); }

public static partial class ExternalConnections
{
    public static string Label(ConnectionKind kind) => kind switch
    {
        ConnectionKind.RealVnc => "VNC", ConnectionKind.LocalPowerShell => "Terminal",
        ConnectionKind.Rdp => "RDP", _ => kind.ToString()
    };
    public static int? DefaultPort(ConnectionKind kind) => kind switch
    { ConnectionKind.Rdp => 3389, ConnectionKind.RealVnc => 5900, _ => null };

    public static void Validate(ConnectionProfile p)
    {
        if (p.Id == Guid.Empty || p.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(p.DisplayName) ||
            p.DisplayName.Length > 120 || !Enum.IsDefined(p.Kind) || p.Port is < 1 or > 65535 ||
            p.ReadinessPort is < 1 or > 65535 || p.TimeoutSeconds is < 5 or > 300)
            throw new ArgumentException("Enter a name, device, valid ports (1-65535) and timeout (5-300 seconds).");
        if (p.Kind == ConnectionKind.Ssh)
        {
            if (p.CredentialId is null || p.CredentialId == Guid.Empty || p.Port is null)
                throw new ArgumentException("SSH needs a saved credential and a port.");
            return;
        }
        if (p.CredentialId is not null)
            throw new ArgumentException("External connections do not use MythLab credentials. Authenticate in the external application.");
        if (p.Arguments is null || p.Arguments.Length > 64 || p.Arguments.Any(a => string.IsNullOrWhiteSpace(a) || a.Length > 4096 || a.Any(char.IsControl)))
            throw new ArgumentException("Use at most 64 non-secret arguments, each on its own line.");
        foreach (var argument in p.Arguments)
        {
            ValidateTemplate(argument);
            if (argument.Contains("{port}", StringComparison.Ordinal) && p.Port is null && DefaultPort(p.Kind) is null)
                throw new ArgumentException("Set a port before using {port} in an argument.");
            if (Regex.IsMatch(argument, @"(?i)^(?:--?|/)?(?:password|passwd|passphrase|secret|token|pin|pw)(?:$|[=: ])") ||
                Regex.IsMatch(argument, @"(?i)^https?://[^/]*@"))
                throw new ArgumentException("Credentials and authentication arguments are not supported. Authenticate in the external application.");
        }
        if (!string.IsNullOrEmpty(p.ExecutablePath))
        {
            ValidateExecutablePath(p.ExecutablePath);
            ValidateProgram(p.Kind, p.ExecutablePath);
        }
        if (p.Kind == ConnectionKind.Custom && string.IsNullOrWhiteSpace(p.ExecutablePath))
            throw new ArgumentException("Choose an executable for this custom profile.");
        if (p.Kind is ConnectionKind.Rdp or ConnectionKind.Web or ConnectionKind.LocalPowerShell && p.Arguments.Length != 0)
            throw new ArgumentException("This connection type does not accept extra command arguments.");
        if (p.Kind == ConnectionKind.Web)
            _ = WebUri(p, new Device { Hostname = "example.invalid", IPv4Address = "192.0.2.1" });
        if (p.Kind == ConnectionKind.Moonlight && (p.RemoteApplication.Length > 256 ||
            p.RemoteApplication.Any(char.IsControl) || p.RemoteApplication.StartsWith('-')))
            throw new ArgumentException("Enter a valid Moonlight app name.");
        if (p.Kind == ConnectionKind.LocalPowerShell && p.ReadinessPort is not null)
            throw new ArgumentException("A local terminal does not need remote readiness checks.");
    }

    public static string[] ParseArgumentLines(string text) =>
        text.Replace("\r\n", "\n").Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();

    public static void ValidateExecutablePath(string path)
    {
        // Reject UNC, slash variants, device namespaces and root-relative forms
        // lexically, before any path resolution or filesystem probes.
        if (path.StartsWith('\\') || path.StartsWith('/'))
            throw new ArgumentException("External executables must be local files. Network and device paths are not supported.");
        if (path != path.Trim() || path.Any(char.IsControl) || path.IndexOfAny(['"', '<', '>', '|', '*', '?', '{', '}']) >= 0 ||
            !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.Skip(2).Contains(':') ||
            path.Contains(':') && !(path.Length > 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/'))
            throw new ArgumentException("Choose an .exe path, absolute or relative to MythLab. Do not include quotes or arguments.");
        // Do not resolve bare executable names through PATH or the current working directory.
        if (!path.Contains('\\') && !path.Contains('/'))
            throw new ArgumentException("Use an explicit executable path (for example ./Tools/viewer.exe).");

    }

    public static string Expand(string template, Device device, int? port)
    {
        ValidateTemplate(template);
        return Placeholder().Replace(template, m => m.Value switch
        {
            "{ip}" => NetworkIdentity(device.IPv4Address, true), "{host}" => NetworkIdentity(device.Endpoint),
            "{hostname}" => NetworkIdentity(device.Hostname),
            "{port}" => port?.ToString(CultureInfo.InvariantCulture) ?? throw new ArgumentException("Set a port to use {port}."),
            _ => throw new ArgumentException("Unsupported placeholder.")
        });
    }
    private static string Required(string value) => !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl)
        ? value : throw new ArgumentException("This device is missing a required host/IP value.");
    private static void ValidateTemplate(string value)
    {
        if (value.Any(char.IsControl) || Placeholder().Replace(value, "").IndexOfAny(['{', '}']) >= 0)
            throw new ArgumentException("Only {ip}, {host}, {hostname} and {port} placeholders are supported. Never enter secrets.");
    }
    [GeneratedRegex(@"\{(?:ip|host|hostname|port)\}")] private static partial Regex Placeholder();

    public static Uri WebUri(ConnectionProfile profile, Device device)
    {
        // URI escaping prevents placeholder values from introducing userinfo, ports, paths or query syntax.
        ValidateTemplate(profile.UrlTemplate);
        var text = Placeholder().Replace(profile.UrlTemplate, m => Uri.EscapeDataString(Expand(m.Value, device, profile.Port)));
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo.Length != 0 || text.Contains('\\') ||
            text.Any(char.IsControl))
            throw new ArgumentException("Enter an HTTP/HTTPS URL without embedded credentials.");
        return uri;
    }
    private static string NetworkHost(Device device) => NetworkIdentity(device.Endpoint);
    private static string NetworkIdentity(string value, bool ipOnly = false)
    {
        var host = Required(value);
        if (ipOnly || host.All(c => char.IsAsciiDigit(c) || c == '.'))
        {
            var octets = host.Split('.');
            if (octets.Length != 4 || octets.Any(o => o.Length is < 1 or > 3 ||
                o.Length > 1 && o[0] == '0' || !o.All(char.IsAsciiDigit) || !byte.TryParse(o, out _)))
                throw new ArgumentException("The device needs a valid dotted IPv4 address without leading zeros.");
        }
        else if (host.Length > 253 || host.Split('.').Any(label => label.Length is < 1 or > 63 ||
            label.StartsWith('-') || label.EndsWith('-') || label.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))
            throw new ArgumentException("The device needs a valid DNS hostname without options, whitespace or shell syntax.");
        return host;
    }
    private static void ValidateProgram(ConnectionKind kind, string executable)
    {
        var name = Path.GetFileName(executable.Replace('\\', '/'));
        var shell = new[] { "cmd.exe", "powershell.exe", "pwsh.exe", "wt.exe", "wscript.exe", "cscript.exe", "mshta.exe" }
            .Contains(name, StringComparer.OrdinalIgnoreCase);
        if (shell && kind != ConnectionKind.LocalPowerShell)
            throw new ArgumentException("Use the restricted Terminal profile for local shells. Shell command profiles are not supported.");
        if (kind == ConnectionKind.LocalPowerShell &&
            !new[] { "powershell.exe", "pwsh.exe", "wt.exe" }.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Select powershell.exe, pwsh.exe or wt.exe for the local terminal.");
    }
    public static LaunchRequest Build(ConnectionProfile p, Device device, string executable)
    {
        Validate(p);
        if (p.Kind == ConnectionKind.Ssh) throw new ArgumentException("SSH uses the integrated terminal.");
        if (p.Kind == ConnectionKind.Web) return new("", [], WebUri(p, device));
        ValidateExecutablePath(executable);
        ValidateProgram(p.Kind, executable);
        var name = Path.GetFileName(executable.Replace('\\', '/'));
        var args = new List<string>();
        switch (p.Kind)
        {
            case ConnectionKind.Rdp:
                args.Add("/v:" + NetworkHost(device) + ":" + (p.Port ?? 3389).ToString(CultureInfo.InvariantCulture));
                if (p.RdpFullscreen) args.Add("/f");
                else { args.Add("/w:1280"); args.Add("/h:720"); }
                break;
            case ConnectionKind.RealVnc:
                args.Add(NetworkHost(device) + "::" + (p.Port ?? 5900).ToString(CultureInfo.InvariantCulture));
                break;
            case ConnectionKind.Moonlight:
                // Moonlight's stream action needs an app. With no app, open its native picker.
                if (!string.IsNullOrWhiteSpace(p.RemoteApplication))
                    args.AddRange(["stream", NetworkHost(device), p.RemoteApplication]);
                break;
            case ConnectionKind.LocalPowerShell:
                if (!name.Equals("wt.exe", StringComparison.OrdinalIgnoreCase)) args.Add("-NoLogo");
                break;
        }
        args.AddRange(p.Arguments.Select(a => Expand(a, device, p.Port ?? DefaultPort(p.Kind))));
        return new(executable, args.ToArray());
    }
}

public sealed class ExternalConnectionService(IExecutableLocator locator, IConnectionLauncher launcher, IDeviceStatusService status)
{
    public LauncherAvailability Availability(ConnectionProfile profile) => profile.Kind == ConnectionKind.Web
        ? new(true, "", "Default web browser") : locator.Locate(profile);
    public async Task LaunchAsync(ConnectionProfile profile, Device device, CancellationToken token = default)
    {
        ExternalConnections.Validate(profile);
        token.ThrowIfCancellationRequested();
        var availability = await Task.Run(() => Availability(profile), token).ConfigureAwait(false);
        if (!availability.Available) throw new InvalidOperationException(availability.Detail);
        var request = ExternalConnections.Build(profile, device, availability.Path);
        if (profile.ReadinessPort is { } port)
        {
            var result = await status.CheckAsync(device with { StatusCheck = StatusCheckKind.Tcp, StatusPort = port },
                checked(profile.TimeoutSeconds * 1000), token);
            if (result.State != DeviceState.Online)
                throw new InvalidOperationException("The TCP service is not ready. " + result.Detail + " No connection was launched.");
        }
        token.ThrowIfCancellationRequested();
        await launcher.LaunchAsync(request, token);
    }
}
