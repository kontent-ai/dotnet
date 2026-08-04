
namespace Kontent.Ai.Management.Models.VariantFilter;

/// <summary>
/// Represents the publishing state of a content item variant.
/// </summary>
public enum VariantFilterPublishingState
{
    /// <summary>
    /// The variant is published.
    /// </summary>
    [JsonStringEnumMemberName("published")]
    Published,

    /// <summary>
    /// The variant is unpublished.
    /// </summary>
    [JsonStringEnumMemberName("unpublished")]
    Unpublished,

    /// <summary>
    /// The variant has not been published yet.
    /// </summary>
    [JsonStringEnumMemberName("not_published_yet")]
    NotPublishedYet
}
