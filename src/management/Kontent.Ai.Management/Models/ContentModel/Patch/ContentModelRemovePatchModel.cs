namespace Kontent.Ai.Management.Models.ContentModel.Patch;

/// <summary>
/// <c>remove</c> operation. Removes the object at <see cref="ContentModelOperationBaseModel.Path"/>.
/// </summary>
public sealed record ContentModelRemovePatchModel : ContentModelOperationBaseModel
{
    /// <inheritdoc/>
    [JsonPropertyName("op")]
    public override string Op => "remove";
}
