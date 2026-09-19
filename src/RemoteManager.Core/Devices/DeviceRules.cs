using System.Globalization;

namespace RemoteManager.Core.Devices;

public sealed class DeviceValidationException(string message) : Exception(message);
public sealed class DuplicateDeviceException(string message) : Exception(message);

public static class DeviceRules
{
    public static Device NormalizeAndValidate(Device device)
    {
        if (device.Id == Guid.Empty) throw new DeviceValidationException("Device ID cannot be empty.");
        var name = device.DisplayName.Trim();
        var host = device.Hostname.Trim().TrimEnd('.');
        var ip = device.IPv4Address.Trim();
        if (name.Length is < 1 or > 120) throw new DeviceValidationException("Friendly name must contain 1–120 characters.");
        if (host.Length == 0 && ip.Length == 0) throw new DeviceValidationException("Enter a hostname or IPv4 address.");
        if (host.Length > 253 || (host.Length > 0 && Uri.CheckHostName(host) != UriHostNameType.Dns))
            throw new DeviceValidationException("Enter a valid hostname without a scheme, port or path.");
        if (ip.Length > 0) ip = NormalizeIPv4(ip);
        string mac;
        try { mac = string.IsNullOrWhiteSpace(device.MacAddress) ? "" : MacAddress.Normalize(device.MacAddress); }
        catch (ArgumentException ex) { throw new DeviceValidationException(ex.Message); }
        if (!Enum.IsDefined(device.Type) || !Enum.IsDefined(device.LastKnownState) ||
            !Enum.IsDefined(device.StatusCheck) || !Enum.IsDefined(device.Wake.Capability))
            throw new DeviceValidationException("Select a valid device, status and wake type.");
        if (device.Wake.Capability != WakeCapability.Disabled && mac.Length == 0)
            throw new DeviceValidationException("Wake-on-LAN requires a MAC address; support remains unverified.");
        if (device.Wake.Capability != WakeCapability.Disabled && (Convert.FromHexString(mac.Replace(":", ""))[0] & 1) != 0)
            throw new DeviceValidationException("Wake-on-LAN requires a unicast MAC address.");
        var broadcast = device.Wake.BroadcastAddress.Trim();
        if (broadcast.Length > 0) broadcast = NormalizeIPv4(broadcast);
        if (device.Wake.Port is < 1 or > 65535 || device.StatusPort is < 1 or > 65535)
            throw new DeviceValidationException("Ports must be between 1 and 65535.");
        if (device.Wake.PacketCount is < 1 or > 10 || device.Wake.RetryCount is < 0 or > 5 ||
            device.Wake.DelayMilliseconds is < 50 or > 10000)
            throw new DeviceValidationException("Wake: 1–10 packets, 0–5 retries, and 50–10000 ms delay.");
        if (device.Notes.Length > 10000 || device.Group.Length > 100)
            throw new DeviceValidationException("Notes may contain 10000 characters; group may contain 100.");
        var tags = device.Tags.Select(t => t.Trim()).Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (tags.Length > 30 || tags.Any(t => t.Length > 60))
            throw new DeviceValidationException("Use up to 30 tags, each up to 60 characters.");
        return device with { DisplayName = name, Hostname = host, IPv4Address = ip, MacAddress = mac,
            Group = device.Group.Trim(), Tags = tags, Wake = device.Wake with { BroadcastAddress = broadcast } };
    }

    private static string NormalizeIPv4(string value)
    {
        var parts = value.Split('.');
        if (parts.Length != 4 || parts.Any(p => p.Length is < 1 or > 3 || !p.All(char.IsAsciiDigit) || !byte.TryParse(p, out _)))
            throw new DeviceValidationException("Enter a valid dotted IPv4 address.");
        return string.Join(".", parts.Select(p => byte.Parse(p, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)));
    }

    public static string? MatchReason(Device candidate, Device existing)
    {
        if (candidate.Id == existing.Id) return null;
        if (candidate.MacAddress.Length > 0 && existing.MacAddress.Length > 0 &&
            MacAddress.Normalize(candidate.MacAddress) == MacAddress.Normalize(existing.MacAddress)) return "MAC address";
        if (candidate.Hostname.Length > 0 && string.Equals(candidate.Hostname.TrimEnd('.'), existing.Hostname.TrimEnd('.'),
            StringComparison.OrdinalIgnoreCase)) return "hostname";
        if (candidate.IPv4Address.Length > 0 && candidate.IPv4Address == existing.IPv4Address) return "IPv4 address";
        return null;
    }
}
