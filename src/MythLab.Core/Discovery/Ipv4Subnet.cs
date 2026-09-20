using System.Buffers.Binary;
using System.Globalization;
using System.Net;

namespace MythLab.Core.Discovery;

public sealed class Ipv4Subnet
{
    public uint Network { get; }
    public uint Broadcast { get; }
    public int PrefixLength { get; }
    public uint FirstHost => PrefixLength >= 31 ? Network : Network + 1;
    public uint LastHost => PrefixLength >= 31 ? Broadcast : Broadcast - 1;
    public ulong HostCount => (ulong)LastHost - FirstHost + 1;
    public string Cidr => $"{Format(Network)}/{PrefixLength}";

    public Ipv4Subnet(string address, string mask)
    {
        var ip = Parse(address);
        var bits = Parse(mask);
        var inverse = ~bits;
        if ((inverse & (inverse + 1UL)) != 0)
            throw new ArgumentException("The interface subnet mask is not contiguous.");
        PrefixLength = System.Numerics.BitOperations.PopCount(bits);
        Network = ip & bits;
        Broadcast = Network | inverse;
    }

    public bool ContainsHost(uint address) => address >= FirstHost && address <= LastHost;

    public static uint Parse(string address)
    {
        var parts = address.Trim().Split('.');
        if (parts.Length != 4 || parts.Any(p => p.Length is < 1 or > 3 || !p.All(char.IsAsciiDigit) || !byte.TryParse(p, out _)))
            throw new ArgumentException("Enter a valid dotted IPv4 address.");
        return BinaryPrimitives.ReadUInt32BigEndian(parts.Select(p => byte.Parse(p, CultureInfo.InvariantCulture)).ToArray());
    }

    public static string Format(uint address)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, address);
        return new IPAddress(bytes).ToString();
    }
}
