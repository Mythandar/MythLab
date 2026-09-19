using System.Reflection;

namespace RemoteManager.App;

public static class AppIdentity
{
    private static string Metadata(string key) => typeof(AppIdentity).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == key).Value!;
    public static string DisplayName => Metadata("AppDisplayName");
    public static string DataId => Metadata("AppDataId");
}
