namespace Kontent.Ai.Sync.Abstractions;

/// <summary>
/// Assembly-level attribute allowing authors of libraries built on top of the Kontent.ai Sync SDK
/// to set a custom <c>X-KC-SOURCE</c> tracking header on outgoing requests, for ecosystem analytics.
/// See <see href="https://kontent-ai.github.io/articles/Guidelines-for-Kontent.ai-related-tools.html#analytics"/>
/// for details.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class SyncSourceTrackingHeaderAttribute : Attribute
{
    /// <summary>
    /// Gets the package name to report (e.g. <c>Acme.Kontent.Ai.AwesomeTool</c>).
    /// </summary>
    public string? PackageName { get; }

    /// <summary>
    /// Gets the semantic major version.
    /// </summary>
    public int MajorVersion { get; }

    /// <summary>
    /// Gets the semantic minor version.
    /// </summary>
    public int MinorVersion { get; }

    /// <summary>
    /// Gets the semantic patch version.
    /// </summary>
    public int PatchVersion { get; }

    /// <summary>
    /// Gets the pre-release label (e.g. <c>rc1</c>). When set, it is appended to the version with a hyphen.
    /// </summary>
    public string? PreReleaseLabel { get; }

    /// <summary>
    /// Gets a value indicating whether the package name and version should be read from the decorated assembly.
    /// </summary>
    public bool LoadFromAssembly { get; }

    /// <summary>
    /// Reads both package name (from <see cref="System.Reflection.AssemblyName.Name"/>)
    /// and version (from <see cref="System.Reflection.AssemblyInformationalVersionAttribute"/>)
    /// from the decorated assembly.
    /// </summary>
    public SyncSourceTrackingHeaderAttribute()
    {
        LoadFromAssembly = true;
    }

    /// <summary>
    /// Overrides the package name but reads the version from the decorated assembly.
    /// </summary>
    /// <param name="packageName">The package name to report.</param>
    public SyncSourceTrackingHeaderAttribute(string packageName)
    {
        LoadFromAssembly = true;
        PackageName = packageName;
    }

    /// <summary>
    /// Overrides both the package name and version.
    /// </summary>
    /// <param name="packageName">The package name to report.</param>
    /// <param name="majorVersion">Semantic major version.</param>
    /// <param name="minorVersion">Semantic minor version.</param>
    /// <param name="patchVersion">Semantic patch version.</param>
    /// <param name="preReleaseLabel">Optional pre-release label appended with a hyphen.</param>
    public SyncSourceTrackingHeaderAttribute(
        string packageName,
        int majorVersion,
        int minorVersion,
        int patchVersion,
        string? preReleaseLabel = null)
    {
        LoadFromAssembly = false;
        PackageName = packageName;
        MajorVersion = majorVersion;
        MinorVersion = minorVersion;
        PatchVersion = patchVersion;
        PreReleaseLabel = preReleaseLabel;
    }
}
