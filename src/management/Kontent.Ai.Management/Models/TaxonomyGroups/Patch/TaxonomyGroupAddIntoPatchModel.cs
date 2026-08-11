namespace Kontent.Ai.Management.Models.TaxonomyGroups.Patch;

/// <summary>
/// <c>addInto</c> operation. Inserts a new term into the taxonomy group. <see cref="Reference"/> points at the parent term (or is null to add at the root).
/// </summary>
public sealed record TaxonomyGroupAddIntoPatchModel : TaxonomyGroupOperationBaseModel
{
    /// <inheritdoc/>
    public override string Op => "addInto";

    /// <summary>
    /// Reference to the parent term that receives the new child; omit to add at the root of the taxonomy group.
    /// </summary>
    [JsonPropertyName("reference")]
    public Reference? Reference { get; init; }

    /// <summary>
    /// New term to insert.
    /// </summary>
    [JsonPropertyName("value")]
    public required TaxonomyTermCreateModel Value { get; init; }

    /// <summary>
    /// Position the new term before this sibling. Mutually exclusive with <see cref="After"/>. When both are null the new term is appended.
    /// </summary>
    [JsonPropertyName("before")]
    public Reference? Before { get; init; }

    /// <summary>
    /// Position the new term after this sibling. Mutually exclusive with <see cref="Before"/>. When both are null the new term is appended.
    /// </summary>
    [JsonPropertyName("after")]
    public Reference? After { get; init; }
}
