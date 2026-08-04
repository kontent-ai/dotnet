
namespace Kontent.Ai.Management.Models.Webhooks.Triggers.Language;

/// <summary>
/// Represents a language action.
/// </summary>
public enum LanguageAction
{
    /// <summary>
    /// Language created action.
    /// </summary>
    [JsonStringEnumMemberName("created")]
    Created,

    /// <summary>
    /// Language changed action.
    /// </summary>
    [JsonStringEnumMemberName("changed")]
    Changed,

    /// <summary>
    /// Language deleted action.
    /// </summary>
    [JsonStringEnumMemberName("deleted")]
    Deleted
}