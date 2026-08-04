
namespace Kontent.Ai.Management.Models.CustomApps.Patch;

/// <summary>
/// Represents enum of properties that can be replaced in the custom app.
/// </summary>
public enum CustomAppPropertyName
{
    /// <summary>
    /// The custom app's name.
    /// </summary>
    [JsonStringEnumMemberName("name")]
    Name,

    /// <summary>
    /// The custom app's codename.
    /// </summary>
    [JsonStringEnumMemberName("codename")]
    Codename,

    /// <summary>
    /// The custom app's source url.
    /// </summary>
    [JsonStringEnumMemberName("source_url")]
    SourceUrl,

    /// <summary>
    /// The custom app's config.
    /// </summary>
    [JsonStringEnumMemberName("config")]
    Config,

    /// <summary>
    /// The custom app's allowed_roles.
    /// </summary>
    [JsonStringEnumMemberName("allowed_roles")]
    AllowedRoles,

    /// <summary>
    /// The custom app's display mode.
    /// </summary>
    [JsonStringEnumMemberName("display_mode")]
    DisplayMode
}