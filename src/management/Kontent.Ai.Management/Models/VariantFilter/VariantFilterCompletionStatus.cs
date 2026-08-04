
namespace Kontent.Ai.Management.Models.VariantFilter;

/// <summary>
/// Represents the completion status of a content item variant.
/// </summary>
public enum VariantFilterCompletionStatus
{
    /// <summary>
    /// The variant is unfinished.
    /// </summary>
    [JsonStringEnumMemberName("unfinished")]
    Unfinished,

    /// <summary>
    /// The variant is ready.
    /// </summary>
    [JsonStringEnumMemberName("ready")]
    Ready,

    /// <summary>
    /// The variant is not translated.
    /// </summary>
    [JsonStringEnumMemberName("not_translated")]
    NotTranslated,

    /// <summary>
    /// The variant is all done (completed and translated).
    /// </summary>
    [JsonStringEnumMemberName("all_done")]
    AllDone
}