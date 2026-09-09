using System.Reflection;

namespace Merchant;

/// <summary>
/// What this build calls itself.
///
/// The number comes from <c>Version</c> in <c>Directory.Build.props</c>, which is the one place it
/// is written down: the release workflow refuses a tag that disagrees with it. The commit is
/// appended by the SDK, so a build made from a checkout says which one it came from and a person
/// reporting a problem can be asked one question instead of three.
/// </summary>
internal static class Build
{
    private static readonly string Informational =
        typeof(Build).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";

    /// <summary>The released version, without the commit: <c>1.4.0</c>.</summary>
    public static string Version { get; } = Informational.Split('+')[0];

    /// <summary>The version and the commit it was built from: <c>1.4.0+a1b2c3d…</c>.</summary>
    public static string FullVersion => Informational;
}
