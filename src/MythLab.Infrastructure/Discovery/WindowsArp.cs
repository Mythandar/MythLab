using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using MythLab.Core.Devices;
using MythLab.Core.Discovery;

namespace MythLab.Infrastructure.Discovery;

internal static class WindowsArp
{
    // Native DWORDs are 4-byte aligned on both x86 and x64. Marshal computes table offset/row stride.
    [StructLayout(LayoutKind.Sequential)]
    private struct Row
    {
        public uint Index;
        public uint MacLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] Mac;
        public uint Address;
        public uint Type;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Table { public uint Count; public Row First; }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetIpNetTable(IntPtr table, ref uint size, [MarshalAs(UnmanagedType.Bool)] bool order);
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint SendARP(uint destination, uint source, [Out] byte[] mac, ref uint length);

    public static IReadOnlyList<NeighborEntry> ReadNeighbors()
    {
        uint size = 0;
        var status = GetIpNetTable(IntPtr.Zero, ref size, false);
        if (status == 232) return [];
        if (status != 122) throw new Win32Exception((int)status);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (size > 16 * 1024 * 1024) throw new InvalidDataException("ARP table is unexpectedly large.");
            var allocated = size;
            var pointer = Marshal.AllocHGlobal(checked((int)allocated));
            try
            {
                status = GetIpNetTable(pointer, ref size, false);
                if (status == 122) continue; // Table can grow between calls.
                if (status == 232) return [];
                if (status != 0) throw new Win32Exception((int)status);
                var count = (uint)Marshal.ReadInt32(pointer);
                var offset = Marshal.OffsetOf<Table>(nameof(Table.First)).ToInt32();
                var stride = Marshal.SizeOf<Row>();
                if ((ulong)offset + (ulong)count * (uint)stride > allocated)
                    throw new InvalidDataException("ARP table size is inconsistent.");
                var results = new List<NeighborEntry>();
                for (var i = 0; i < count; i++)
                {
                    var row = Marshal.PtrToStructure<Row>(IntPtr.Add(pointer, checked(offset + i * stride)));
                    if (row.Type is not (3 or 4) || row.MacLength != 6 || !IsUnicast(row.Mac.AsSpan(0, 6))) continue;
                    var address = new IPAddress(BitConverter.GetBytes(row.Address)).ToString();
                    results.Add(new(checked((int)row.Index), address, MacAddress.Normalize(Convert.ToHexString(row.Mac.AsSpan(0, 6)))));
                }
                return results;
            }
            finally { Marshal.FreeHGlobal(pointer); }
        }
        throw new IOException("ARP table changed repeatedly; retry discovery.");
    }

    public static string? Resolve(string source, string destination)
    {
        var bytes = new byte[8];
        uint length = (uint)bytes.Length;
        var status = SendARP(BitConverter.ToUInt32(IPAddress.Parse(destination).GetAddressBytes()),
            BitConverter.ToUInt32(IPAddress.Parse(source).GetAddressBytes()), bytes, ref length);
        return status == 0 && length == 6 && IsUnicast(bytes.AsSpan(0, 6))
            ? MacAddress.Normalize(Convert.ToHexString(bytes.AsSpan(0, 6))) : null;
    }

    private static bool IsUnicast(ReadOnlySpan<byte> mac) => (mac[0] & 1) == 0 && mac.IndexOfAnyExcept((byte)0) >= 0;
}
