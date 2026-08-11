namespace Kontent.Ai.Management.Models.TaxonomyGroups.Patch;

/// <summary>
/// <c>remove</c> operation. Removes the taxonomy term identified by <see cref="Reference"/>.
/// </summary>
public sealed record TaxonomyGroupRemovePatchModel : TaxonomyGroupOperationBaseModel
{
    /// <inheritdoc/>
    public override string Op => "remove";

    /// <summary>
    /// Reference to the taxonomy term to remove.
    /// </summary>
    [JsonPropertyName("reference")]
    public required Reference Reference { get; init; }
}
