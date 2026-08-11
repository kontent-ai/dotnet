namespace Kontent.Ai.Management.Models.TaxonomyGroups.Patch;

/// <summary>
/// <c>replace</c> operation. Replaces a property of the taxonomy group or one of its terms. The targeted object is identified by <see cref="Reference"/>; <see cref="PropertyName"/> selects which of its properties to replace.
/// </summary>
public sealed record TaxonomyGroupReplacePatchModel : TaxonomyGroupOperationBaseModel
{
    /// <inheritdoc/>
    public override string Op => "replace";

    /// <summary>
    /// Reference to the taxonomy group or term whose property is replaced.
    /// </summary>
    [JsonPropertyName("reference")]
    public required Reference Reference { get; init; }

    /// <summary>
    /// Property to replace. Valid values are <see cref="TaxonomyGroupPropertyName.Codename"/>, <see cref="TaxonomyGroupPropertyName.Name"/>, and <see cref="TaxonomyGroupPropertyName.Terms"/>.
    /// </summary>
    [JsonPropertyName("property_name")]
    public required TaxonomyGroupPropertyName PropertyName { get; init; }

    /// <summary>
    /// New value. Type depends on <see cref="PropertyName"/>: <c>string</c> for <c>codename</c> / <c>name</c>, <c>IEnumerable&lt;TaxonomyTermCreateModel&gt;</c> for <c>terms</c>.
    /// </summary>
    [JsonPropertyName("value")]
    public required object Value { get; init; }
}
