namespace Kontent.Ai.Management.Models.Webhooks.Triggers.Taxonomy;

/// <summary>
/// Represents taxonomy actions.
/// </summary>
public enum TaxonomyAction
{
    /// <summary>
    /// Taxonomy created action.
    /// </summary>
    [JsonStringEnumMemberName("created")]
    Created,

    /// <summary>
    /// Taxonomy metadata changed action.
    /// </summary>
    [JsonStringEnumMemberName("metadata_changed")]
    MetadataChanged,

    /// <summary>
    /// Taxonomy deleted action.
    /// </summary>
    [JsonStringEnumMemberName("deleted")]
    Deleted,

    /// <summary>
    /// Taxonomy term created action.
    /// </summary>
    [JsonStringEnumMemberName("term_created")]
    TermCreated,

    /// <summary>
    /// Taxonomy term changed action.
    /// </summary>
    [JsonStringEnumMemberName("term_changed")]
    TermChanged,

    /// <summary>
    /// Taxonomy term deleted action.
    /// </summary>
    [JsonStringEnumMemberName("term_deleted")]
    TermDeleted,

    /// <summary>
    /// Taxonomy terms moved action.
    /// </summary>
    [JsonStringEnumMemberName("terms_moved")]
    TermsMoved
}