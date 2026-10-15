using System.Reflection;

namespace Collaterale.App;

public static class AppInfo
{
    /// <summary>Product name; the single place to change if the app is renamed (also see packaging scripts).</summary>
    public const string Name = "Collaterale";

    public static string Version { get; } =
        (typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];
}
