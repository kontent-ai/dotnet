namespace Kontent.Ai.Management.Models.Languages.Patch;

/// <summary>
/// Represents properties that can be modified on the content language.
/// </summary>
public enum LanguagePropertyName
{
    /// <summary>
    /// The language's codename
    /// </summary>
    [JsonStringEnumMemberName("codename")]
    Codename,

    /// <summary>
    /// The language's display name.
    /// </summary>
    [JsonStringEnumMemberName("name")]
    Name,

    /// <summary>
    /// Fall back language.
    /// Language to use when the current language contains no content.
    /// </summary>
    [JsonStringEnumMemberName("fallback_language")]
    FallbackLanguage,

    /// <summary>
    /// A flag determining whether the language is active.
    /// </summary>
    [JsonStringEnumMemberName("is_active")]
    IsActive,
}
