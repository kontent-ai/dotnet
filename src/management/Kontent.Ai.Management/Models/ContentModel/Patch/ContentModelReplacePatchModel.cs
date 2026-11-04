namespace Kontent.Ai.Management.Models.ContentModel.Patch;

/// <summary>
/// <c>replace</c> operation. Replaces the value at <see cref="ContentModelOperationBaseModel.Path"/>. A null <see cref="Value"/> clears the targeted property.
/// </summary>
public sealed record ContentModelReplacePatchModel : ContentModelOperationBaseModel
{
    /// <inheritdoc/>
    [JsonPropertyName("op")]
    public override string Op => "replace";

    /// <summary>
    /// New value at the targeted path. Set to <c>null</c> to clear the property (e.g., remove a previously-configured default value or guidelines).
    /// </summary>
    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required object? Value { get; init; }
}
