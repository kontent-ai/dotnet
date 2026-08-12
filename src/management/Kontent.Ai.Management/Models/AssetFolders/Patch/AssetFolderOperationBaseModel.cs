namespace Kontent.Ai.Management.Models.AssetFolders.Patch;

/// <summary>
/// Represents the operation on folders.
/// </summary>
public abstract record AssetFolderOperationBaseModel
{
    /// <summary>
    /// Gets specification of the operation to perform.
    /// </summary>
    [JsonPropertyName("op")]
    public abstract string Op { get; }
}
