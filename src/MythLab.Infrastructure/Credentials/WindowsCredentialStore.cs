using System.Runtime.InteropServices;
using System.Text;
using MythLab.Core.Credentials;
namespace MythLab.Infrastructure.Credentials;

public sealed class WindowsCredentialStore(string applicationId) : ICredentialStore
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);
    private string Target(Guid id) => id == Guid.Empty ? throw new ArgumentException("Credential ID is required.") : $"{applicationId}/credentials/{id:D}";
    internal static SecretStatus Classify(int error) => error switch { 1168 => SecretStatus.Missing, 5 => SecretStatus.AccessDenied, _ => SecretStatus.StoreError };
    public Task<SecretStatus> InspectAsync(Guid id, CancellationToken token = default) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!CredRead(Target(id), 1, 0, out var pointer)) return Classify(Marshal.GetLastWin32Error());
        CredFree(pointer);
        return SecretStatus.Available;
    }, token);
    public Task<string?> ReadAsync(Guid id, CancellationToken token = default) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!CredRead(Target(id), 1, 0, out var pointer))
        {
            var status = Classify(Marshal.GetLastWin32Error());
            if (status == SecretStatus.Missing) return null;
            throw new CredentialStoreException(status);
        }
        try
        {
            var native = Marshal.PtrToStructure<Credential>(pointer);
            if (native.BlobSize > 2560 || native.BlobSize % 2 != 0) throw new CredentialStoreException(SecretStatus.StoreError);
            return Marshal.PtrToStringUni(native.Blob, checked((int)native.BlobSize / 2)) ?? "";
        }
        finally { CredFree(pointer); }
    }, token);
    public Task WriteAsync(Guid id, string secret, CancellationToken token = default) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var bytes = Encoding.Unicode.GetBytes(secret);
        if (bytes.Length > 2560) { Array.Clear(bytes); throw new ArgumentException("The secret exceeds Windows Credential Manager's size limit."); }
        var blob = Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential { Type = 1, TargetName = Target(id), Blob = blob, BlobSize = (uint)bytes.Length,
                Persist = 2, UserName = applicationId }; // CRED_PERSIST_LOCAL_MACHINE: this Windows user on this machine.
            if (!CredWrite(ref credential, 0)) throw new CredentialStoreException(Classify(Marshal.GetLastWin32Error()));
        }
        finally
        {
            Array.Clear(bytes);
            for (var i = 0; i < bytes.Length; i++) Marshal.WriteByte(blob, i, 0);
            Marshal.FreeHGlobal(blob);
        }
    }, token);
    public Task DeleteAsync(Guid id, CancellationToken token = default) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!CredDelete(Target(id), 1, 0))
        {
            var status = Classify(Marshal.GetLastWin32Error());
            if (status != SecretStatus.Missing) throw new CredentialStoreException(status);
        }
    }, token);
}
