using System.Reflection;

namespace RescueTimeStatus;

/// <summary>
/// The application's version, as stamped into the assembly from git at build time
/// (see the <c>SetGitVersion</c> target in RescueTimeStatus.csproj). On a release this is a
/// clean "1.2.3"; on other builds it carries the commit — e.g. "1.2.3-5-g1a2b3c4" or a bare
/// short hash — with a trailing "-dirty" when built from a modified tree.
/// </summary>
public static class AppVersion
{
    /// <summary>The git-derived version, ready to show to the user.</summary>
    public static string Display { get; } = Read();

    private static string Read()
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        string? info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(info))
        {
            // No informational version (e.g. an IDE design-time load): fall back to the numeric one.
            return asm.GetName().Version?.ToString() ?? "unknown";
        }

        // Strip any "+<source-revision>" the SDK might append; our string already carries the hash.
        int plus = info.IndexOf('+');
        return plus >= 0 ? info[..plus] : info;
    }
}
