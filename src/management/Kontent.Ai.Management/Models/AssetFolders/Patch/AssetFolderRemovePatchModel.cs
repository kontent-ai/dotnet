namespace Kontent.Ai.Management.Models.AssetFolders.Patch;

/// <summary>
/// Represents remove operation to perform on the folder.
/// </summary>
public sealed record AssetFolderRemovePatchModel : AssetFolderOperationBaseModel
{
    /// <summary>
    /// Represents remove operation.
    /// </summary>
    public override string Op => "remove";

    /// <summary>
    /// Reference to the folder to remove.
    /// </summary>
    [JsonPropertyName("reference")]
    public required Reference Reference { get; init; }
}
