using MythLab.Core.Devices;
namespace MythLab.Core.WakeOnLan;

public static class MagicPacket
{
    public static byte[] Create(string macAddress)
    {
        var mac = Convert.FromHexString(MacAddress.Normalize(macAddress).Replace(":", ""));
        if (mac.Length != 6 || (mac[0] & 1) != 0 || mac.All(b => b == 0))
            throw new ArgumentException("Wake-on-LAN requires a nonzero unicast MAC address.");
        var packet = new byte[102];
        packet.AsSpan(0, 6).Fill(0xFF);
        for (var i = 0; i < 16; i++) mac.CopyTo(packet, 6 + i * 6);
        return packet;
    }
}
