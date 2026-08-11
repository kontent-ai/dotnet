namespace Kontent.Ai.Management.Models.TaxonomyGroups.Patch;

/// <summary>
/// Base shape for a taxonomy group PATCH operation. Concrete subtypes specialize the verb (<c>addInto</c>, <c>replace</c>, <c>move</c>, <c>remove</c>).
/// </summary>
public abstract record TaxonomyGroupOperationBaseModel
{
    /// <summary>
    /// Operation verb. Pinned by each concrete subtype.
    /// </summary>
    [JsonPropertyName("op")]
    public abstract string Op { get; }
}
